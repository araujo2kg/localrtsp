using System.Diagnostics;

namespace localrtsp.Tests.Utilities
{
    internal static class StreamingTestHost
    {
        #region Properties and Events

        internal static string Repository => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

        internal static string FFmpeg => DependencyService.FindExecutable("ffmpeg", Environment.GetEnvironmentVariable("LOCALRTSP_FFMPEG") ?? Path.Combine(Repository, "tools", "ffmpeg", "bin", "ffmpeg.exe"));

        internal static string Server => DependencyService.FindExecutable("mediamtx", Environment.GetEnvironmentVariable("LOCALRTSP_MEDIAMTX") ?? Path.Combine(Repository, "tools", "mediamtx", "mediamtx.exe"));

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the integration host with redirected input and diagnostics.
        /// </summary>
        internal static Process StartHost(SourceKind source, string file)
        {
            string configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";
            ProcessStartInfo info = new("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = Repository
            };

            foreach (string argument in new[]
            {
                Path.Combine(Repository, "tests", "localrtsp.IntegrationHost", "bin", configuration, "net10.0", "localrtsp.IntegrationHost.dll"),
                FFmpeg,
                Server,
                source.ToString(),
                file
            })
            {
                info.ArgumentList.Add(argument);
            }

            return Process.Start(info)!;
        }

        /// <summary>
        /// Checks whether an owned process is still running.
        /// </summary>
        internal static bool IsAlive(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);

                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks that server sockets bind only to the loopback address.
        /// </summary>
        internal static async Task AssertLoopbackOnlyAsync(ProcessRunner runner, int serverPid)
        {
            string netstat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netstat.exe");

            foreach (string protocol in new[]
            {
                "tcp",
                "udp"
            })
            {
                ProcessResult table = await runner.RunAsync(netstat, new[] { "-ano", "-p", protocol }, CancellationToken.None);
                Assert.Equal(0, table.ExitCode);
                string[][] ownedRows = table.Output.Split('\n')
                    .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(row => row.Length >= 4 && row[^1] == serverPid.ToString())
                    .ToArray();

                if (protocol == "udp")
                {
                    Assert.Empty(ownedRows);
                }
                else
                {
                    Assert.NotEmpty(ownedRows);
                    Assert.All(ownedRows, row => Assert.StartsWith("127.0.0.1:", row[1]));
                }
            }
        }

        #endregion
    }
}