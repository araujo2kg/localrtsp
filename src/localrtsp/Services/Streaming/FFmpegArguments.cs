namespace localrtsp.Services.Streaming
{
    /// <summary>
    /// Builds FFmpeg arguments without shell command strings.
    /// </summary>
    public static class FFmpegArguments
    {
        private const int _keyframeIntervalSeconds = 2;

        /// <summary>
        /// Builds the source, encoding, and transport arguments for a stream.
        /// </summary>
        public static IReadOnlyList<string> Publish(StreamSettings settings, string url)
        {
            settings.Validate();

            List<string> arguments = new()
            {
                "-hide_banner",
                "-loglevel", "warning",
                "-nostats"
            };

            switch (settings.Source)
            {
                case SourceKind.SolidColor:
                    arguments.AddRange(new[]
                    {
                        "-re", "-f", "lavfi",
                        "-i", $"color=c=0x{settings.Color[1..]}:s={settings.Width}x{settings.Height}:r={settings.Fps}"
                    });
                    break;

                case SourceKind.TestPattern:
                    arguments.AddRange(new[]
                    {
                        "-re", "-f", "lavfi",
                        "-i", $"testsrc2=size={settings.Width}x{settings.Height}:rate={settings.Fps}"
                    });
                    break;

                case SourceKind.Image:
                    arguments.AddRange(new[]
                    {
                        "-re", "-loop", "1",
                        "-framerate", settings.Fps.ToString(),
                        "-i", settings.FilePath
                    });
                    break;

                case SourceKind.Video:
                    arguments.Add("-re");

                    if (settings.Loop)
                    {
                        arguments.AddRange(new[] { "-stream_loop", "-1" });
                    }

                    arguments.AddRange(new[] { "-i", settings.FilePath });
                    break;

                case SourceKind.Camera:
                    // Negotiate capture with DirectShow and normalize the output separately.
                    arguments.AddRange(new[] { "-f", "dshow", "-i", $"video={settings.CameraId}" });
                    break;

                default:
                    throw new ArgumentException("Fault simulation does not publish video.");
            }

            string outputFilter = $"scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=decrease,"
                + $"pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={settings.Fps}";

            arguments.AddRange(new[]
            {
                "-map", "0:v:0",
                "-an",
                "-vf", outputFilter,
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-preset", "ultrafast",
                "-tune", "zerolatency",
                "-g", (settings.Fps * _keyframeIntervalSeconds).ToString(),
                "-keyint_min", settings.Fps.ToString(),
                "-sc_threshold", "0",
                "-f", "rtsp",
                "-rtsp_transport", "tcp",
                url
            });

            return arguments;
        }
    }
}