using System.Diagnostics;

namespace localrtsp.Tests.Infrastructure.Processes
{
    [Collection("Real streaming")]
    public sealed class NativeProcessTest
    {

        /// <summary>
        /// Checks port fallback and child cleanup after forced parent termination.
        /// </summary>
        [IntegrationFact]
        public async Task ParentTermination_ShouldKillOwnedChildrenAndFallbackFromOccupiedPort()
        {
            // Arrange
            TcpListener occupied = new(IPAddress.Loopback, Defaults.FirstPort);
            occupied.Start();

            using Process host = StreamingTestHost.StartHost(SourceKind.TestPattern, "");
            Task<string> errors = host.StandardError.ReadToEndAsync();

            try
            {
                string? url = await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.StartsWith("rtsp://127.0.0.1:", url);
                string? ids = await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
                int[] children = ids!.Split(',').Select(int.Parse).ToArray();
                Assert.NotEmpty(children);
                Assert.NotEqual(Defaults.FirstPort, new Uri(url!).Port);

                // Act
                host.Kill(); // Deliberately kill only the parent; Job Objects must release children.
                await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

                foreach (int pid in children)
                {
                    if (!StreamingTestHost.IsAlive(pid))
                    {
                        continue;
                    }

                    using Process child = Process.GetProcessById(pid);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }

                // Assert
                Assert.All(children, pid => Assert.False(StreamingTestHost.IsAlive(pid)));
                TcpListener replacement = new(IPAddress.Loopback, new Uri(url!).Port);
                replacement.Start();
                replacement.Stop();
            }
            finally
            {
                occupied.Stop();

                if (!host.HasExited)
                {
                    host.Kill(true);
                    await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }

                string errorText = await errors.WaitAsync(TimeSpan.FromSeconds(5));

                if (host.ExitCode != 0 && !string.IsNullOrWhiteSpace(errorText))
                {
                    Console.WriteLine(errorText);
                }
            }
        }
    }
}