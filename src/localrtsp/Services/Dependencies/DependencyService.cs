using System.Text.RegularExpressions;

namespace localrtsp.Services.Dependencies
{
    public sealed class DependencyService : IDependencyService
    {
        #region Fields

        private readonly IProcessRunner _runner;
        private readonly SessionLog _log;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates the tool discovery and validation service.
        /// </summary>
        public DependencyService(IProcessRunner runner, SessionLog log)
        {
            _runner = runner;
            _log = log;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Finds a configured, bundled, or PATH-provided executable.
        /// </summary>
        public static string FindExecutable(string name, string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (File.Exists(explicitPath))
                {
                    return Path.GetFullPath(explicitPath);
                }

                throw new FileNotFoundException($"The configured {name} path does not exist. Update Dependencies.");
            }

            string[] directories = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools"),
                Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin"),
                Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"),
                Path.Combine(AppContext.BaseDirectory, "tools", "mediamtx"),
                Path.Combine(Environment.CurrentDirectory, "tools", "ffmpeg", "bin"),
                Path.Combine(Environment.CurrentDirectory, "tools", "ffmpeg"),
                Path.Combine(Environment.CurrentDirectory, "tools", "mediamtx")
            }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)).ToArray();

            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string path = Path.Combine(directory.Trim('"'), name + ".exe");

                if (File.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            throw new FileNotFoundException($"{name}.exe was not found. Run scripts/setup-tools.ps1 or select an executable in Dependencies.");
        }

        /// <summary>
        /// Locates and verifies the required streaming tools.
        /// </summary>
        public async Task<ToolPaths> ResolveAsync(StreamSettings settings, CancellationToken token)
        {
            string ffmpeg = FindExecutable("ffmpeg", settings.FFmpegPath);
            string mediaMtx = FindExecutable("mediamtx", settings.MediaMtxPath);
            ProcessResult version = await _runner.RunAsync(ffmpeg, new[] { "-version" }, token).ConfigureAwait(false);

            if (version.ExitCode != 0 || !version.Output.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected FFmpeg executable could not be verified.");
            }

            ProcessResult encoders = await _runner.RunAsync(ffmpeg, new[] { "-hide_banner", "-encoders" }, token).ConfigureAwait(false);

            if (encoders.ExitCode != 0 || !encoders.Output.Contains("libx264"))
            {
                throw new InvalidOperationException("This FFmpeg build lacks libx264. Select a GPL build with H.264 encoding support.");
            }

            ProcessResult serverVersion = await _runner.RunAsync(mediaMtx, new[] { "--version" }, token).ConfigureAwait(false);
            Match versionMatch = Regex.Match(serverVersion.Output, @"v?(\d+\.\d+\.\d+)");

            if (serverVersion.ExitCode != 0 || !Version.TryParse(versionMatch.Groups[1].Value, out Version? server) || server.Major != 1 || server < new Version(1, 21, 2))
            {
                throw new InvalidOperationException("MediaMTX 1.21.2 or newer 1.x is required. Use the pinned setup script.");
            }

            _log.Write("Tools", version.Output.Split('\n')[0]);
            _log.Write("Tools", "MediaMTX " + serverVersion.Output.Trim());

            return new ToolPaths(ffmpeg, mediaMtx);
        }

        /// <summary>
        /// Checks file decoding or availability of the selected camera.
        /// </summary>
        public async Task ValidateSourceAsync(StreamSettings settings, string ffmpeg, CancellationToken token)
        {
            settings.Validate();

            if (settings.Source is SourceKind.Image or SourceKind.Video)
            {
                ProcessResult probe = await _runner.RunAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-v", "error", "-i", settings.FilePath, "-map", "0:v:0", "-frames:v", "1", "-an", "-f", "null", "-" }, token).ConfigureAwait(false);

                if (probe.ExitCode != 0)
                {
                    throw new InvalidOperationException("The selected file could not be decoded as video/image. Select another file.");
                }
            }

            if (settings.Source == SourceKind.Camera)
            {
                IReadOnlyList<CameraDevice> devices = await GetCamerasAsync(ffmpeg, token).ConfigureAwait(false);

                if (!devices.Any(d => d.Id == settings.CameraId))
                {
                    throw new InvalidOperationException("The selected camera is unavailable. Refresh the list and select a device.");
                }
            }
        }

        /// <summary>
        /// Lists local DirectShow cameras without opening a video feed.
        /// </summary>
        public async Task<IReadOnlyList<CameraDevice>> GetCamerasAsync(string explicitFFmpeg, CancellationToken token)
        {
            string ffmpeg = FindExecutable("ffmpeg", explicitFFmpeg);

            // DirectShow intentionally exits nonzero after listing devices.
            ProcessResult result = await _runner.RunAsync(ffmpeg, new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" }, token).ConfigureAwait(false);

            return ParseCameras(result.Output);
        }

        /// <summary>
        /// Extracts camera names and stable identifiers from DirectShow output.
        /// </summary>
        public static IReadOnlyList<CameraDevice> ParseCameras(string output)
        {
            List<CameraDevice> cameras = new();
            string? name = null;

            foreach (string line in output.Split('\n'))
            {
                Match device = Regex.Match(line, "\"([^\"]+)\" \\(video\\)");

                if (device.Success)
                {
                    name = device.Groups[1].Value;
                    continue;
                }

                if (line.Contains("(audio)"))
                {
                    name = null;
                }

                Match alternative = Regex.Match(line, "Alternative name \"([^\"]+)\"");

                if (name != null && alternative.Success)
                {
                    string label = name;
                    int count = cameras.Count(c => c.Name == name || c.Name.StartsWith(name + " ("));

                    if (count > 0)
                    {
                        label += $" ({count + 1})";
                    }

                    cameras.Add(new CameraDevice(label, alternative.Groups[1].Value));
                    name = null;
                }
            }

            return cameras;
        }

        #endregion
    }
}