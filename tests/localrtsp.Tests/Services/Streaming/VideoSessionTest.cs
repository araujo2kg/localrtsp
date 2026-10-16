using System.Diagnostics;

namespace localrtsp.Tests.Services.Streaming
{
    [Collection("Real streaming")]
    public sealed class VideoSessionTest
    {

        /// <summary>
        /// Checks real frame decoding and port release for each video source.
        /// </summary>
        [IntegrationTheory]
        [InlineData(SourceKind.TestPattern)]
        [InlineData(SourceKind.Image)]
        [InlineData(SourceKind.Video)]
        public async Task LocalStream_ShouldDecodeARealFrameAndReleasePortOnStop(SourceKind source)
        {
            // Arrange
            string fixture = Path.Combine(Path.GetTempPath(), "localrtsp ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â§ & fixture " + Guid.NewGuid().ToString("N") + (source == SourceKind.Image ? ".png" : ".mp4"));
            ProcessRunner runner = new(new SessionLog());

            if (source != SourceKind.TestPattern)
            {
                List<string> args = new()
                {
                    "-hide_banner",
                    "-v",
                    "error",
                    "-f",
                    "lavfi",
                    "-i",
                    "testsrc2=size=160x120:rate=5"
                };

                if (source == SourceKind.Image)
                {
                    args.AddRange(new[] { "-frames:v", "1" });
                }
                else
                {
                    args.AddRange(new[] { "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p" });
                }

                args.Add(fixture);
                Assert.Equal(0, (await runner.RunAsync(StreamingTestHost.FFmpeg, args, CancellationToken.None)).ExitCode);
            }

            using Process host = StreamingTestHost.StartHost(source, fixture);
            Task<string> errors = host.StandardError.ReadToEndAsync();

            try
            {
                string? url = await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.StartsWith("rtsp://127.0.0.1:", url);
                string? ids = await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
                int[] children = ids!.Split(',').Select(int.Parse).ToArray();
                await StreamingTestHost.AssertLoopbackOnlyAsync(runner, children[0]);

                // Act
                ProcessResult decoded = await runner.RunAsync(StreamingTestHost.FFmpeg, new[]
                {
                    "-hide_banner", "-nostdin", "-v", "error",
                    "-rtsp_transport", "tcp",
                    "-i", url!,
                    "-frames:v", "1",
                    "-an", "-f", "md5", "-"
                }, CancellationToken.None);
                await host.StandardInput.WriteLineAsync();
                await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

                // Assert
                Assert.Equal(0, decoded.ExitCode);
                Assert.Contains("MD5=", decoded.Output);
                Assert.Equal(0, host.ExitCode);
                Assert.All(children, pid => Assert.False(StreamingTestHost.IsAlive(pid)));
                TcpListener replacement = new(IPAddress.Loopback, new Uri(url!).Port);
                replacement.Start();
                replacement.Stop();
            }
            finally
            {
                if (!host.HasExited)
                {
                    host.Kill(true);
                    await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }

                string errorText = await errors.WaitAsync(TimeSpan.FromSeconds(5));

                if (host.ExitCode != 0)
                {
                    Console.WriteLine(errorText);
                }

                if (File.Exists(fixture))
                {
                    File.Delete(fixture);
                }
            }
        }
    }
}