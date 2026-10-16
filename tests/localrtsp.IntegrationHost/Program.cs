using localrtsp.Infrastructure;
using localrtsp.Infrastructure.Processes;
using localrtsp.Models;
using localrtsp.Services;
using localrtsp.Services.Dependencies;
using localrtsp.Services.Desktop;
using localrtsp.Services.FaultSimulation;
using localrtsp.Services.Streaming;

namespace localrtsp.IntegrationHost
{
    internal static class Program
    {

        /// <summary>
        /// Starts the desktop app or activates its existing instance.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--render")
            {
                return RenderCheck.Run(args[1]);
            }

            SessionLog log = new();
            ProcessRunner runner = new(log);
            DependencyService dependencies = new(runner, log);

            await using SessionCoordinator coordinator = new(new StreamSessionFactory(dependencies, runner, log), log);
            StreamSettings settings = new()
            {
                FFmpegPath = args[0],
                MediaMtxPath = args[1],
                Source = Enum.Parse<SourceKind>(args[2]),
                FilePath = args.Length > 3 ? args[3] : ""
            };
            await coordinator.StartAsync(settings);

            if (coordinator.Snapshot.State != SessionState.Streaming)
            {
                Console.Error.WriteLine(log.Text);
                Console.Error.WriteLine(coordinator.Snapshot.Message);

                return 1;
            }

            Console.WriteLine(coordinator.Snapshot.Url);
            Console.WriteLine(string.Join(",", System.Text.RegularExpressions.Regex.Matches(log.Text, @"Started owned process (\d+)").Select(match => match.Groups[1].Value)));
            await Console.In.ReadLineAsync();
            await coordinator.StopAsync();

            return 0;
        }
    }
}