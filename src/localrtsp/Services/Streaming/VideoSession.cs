using System.Net.Http;

namespace localrtsp.Services.Streaming
{
    internal sealed class VideoSession : IStreamSession
    {
        #region Fields

        private static readonly TimeSpan _controlRequestTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _readinessPollInterval = TimeSpan.FromMilliseconds(100);
        private readonly IProcessRunner _runner;
        private readonly SessionLog _log;
        private readonly string _directory;
        private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = _controlRequestTimeout
        };
        private IOwnedProcess? _server;
        private IOwnedProcess? _publisher;
        private readonly CancellationTokenSource _monitorStop = new();
        private Task<string> _completion = Task.FromResult("Not started.");
        private bool _disposed;

        #endregion

        #region Properties and Events

        public string Url
        {
            get;
        }

        public Task<string> Completion => _completion;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates the session state and temporary configuration path.
        /// </summary>
        private VideoSession(int port, IProcessRunner runner, SessionLog log)
        {
            Url = Defaults.Url(port);
            _runner = runner;
            _log = log;
            _directory = Path.Combine(SessionFiles.Root, $"session-{Environment.ProcessId}-{Guid.NewGuid():N}");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the server and publisher and waits for a usable stream.
        /// </summary>
        public static async Task<IStreamSession> CreateAsync(
            StreamSettings settings,
            int port,
            ToolPaths paths,
            IProcessRunner runner,
            SessionLog log,
            CancellationToken token)
        {
            VideoSession session = new(port, runner, log);

            try
            {
                Directory.CreateDirectory(session._directory);

                TcpListener reservation = new(IPAddress.Loopback, 0);
                reservation.Start();
                int apiPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
                reservation.Stop();

                string configurationPath = Path.Combine(session._directory, "mediamtx.yml");
                await File.WriteAllTextAsync(configurationPath, MediaMtxConfiguration.Create(port, apiPort), token).ConfigureAwait(false);
                session._server = runner.Start(paths.MediaMtx, new[] { configurationPath }, "MediaMTX");

                string controlApiUrl = $"http://127.0.0.1:{apiPort}/v3/paths/list";
                await session.WaitReadyAsync(controlApiUrl, false, token).ConfigureAwait(false);

                session._publisher = runner.Start(paths.FFmpeg, FFmpegArguments.Publish(settings, session.Url), "FFmpeg");
                await session.WaitReadyAsync(controlApiUrl, true, token).ConfigureAwait(false);
                session._completion = session.WatchAsync(settings);

                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Waits for the server or published path to become ready.
        /// </summary>
        private async Task WaitReadyAsync(string controlApiUrl, bool requirePublishedPath, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (_server!.Completion.IsCompleted)
                {
                    await _server.Completion.ConfigureAwait(false);

                    if (IsBindConflict(_server.Output))
                    {
                        throw new PortConflictException();
                    }

                    throw new InvalidOperationException("MediaMTX could not start. Inspect diagnostic logs and verify its version.");
                }

                if (_publisher?.Completion.IsCompleted == true)
                {
                    throw new InvalidOperationException("The source could not publish video. Check the file or camera access and inspect diagnostic logs.");
                }

                try
                {
                    using HttpResponseMessage response = await _http.GetAsync(controlApiUrl, token).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        if (!requirePublishedPath)
                        {
                            return;
                        }

                        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));

                        if (json.RootElement.GetProperty("items").EnumerateArray().Any(path => path.GetProperty("name").GetString() == "camera" && path.GetProperty("ready").GetBoolean()))
                        {
                            return;
                        }
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                }

                await Task.Delay(_readinessPollInterval, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Detects a port conflict in server diagnostics.
        /// </summary>
        private static bool IsBindConflict(string text)
        {
            return text.Contains("address already in use", StringComparison.OrdinalIgnoreCase) || text.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reports normal playback completion or unexpected process exit.
        /// </summary>
        private async Task<string> WatchAsync(StreamSettings settings)
        {
            try
            {
                // Process exit is authoritative; readiness already verified the published path.
                Task<int> exited = await Task.WhenAny(_server!.Completion, _publisher!.Completion).WaitAsync(_monitorStop.Token).ConfigureAwait(false);
                int code = await exited.ConfigureAwait(false);

                if (exited == _publisher.Completion && settings.Source == SourceKind.Video && !settings.Loop && code == 0)
                {
                    return "Playback completed.";
                }

                return "The stream stopped unexpectedly. Check the source and diagnostic logs, then retry.";
            }
            catch (OperationCanceledException) when (_monitorStop.IsCancellationRequested)
            {
                return "Stopped.";
            }
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _monitorStop.CancelAsync().ConfigureAwait(false);

            try
            {
                if (_publisher != null)
                {
                    await _publisher.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (_server != null)
                    {
                        await _server.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _http.Dispose();
                    _monitorStop.Dispose();
                    SessionFiles.Delete(_directory);
                }
            }
        }

        #endregion
    }
}