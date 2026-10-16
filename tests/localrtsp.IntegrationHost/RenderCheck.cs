using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using localrtsp.Infrastructure;
using localrtsp.Infrastructure.Processes;
using localrtsp.Models;
using localrtsp.Services;
using localrtsp.Services.Dependencies;
using localrtsp.Services.Desktop;
using localrtsp.Services.FaultSimulation;
using localrtsp.Services.Streaming;
using localrtsp.ViewModels;
using localrtsp.Views;

namespace localrtsp.IntegrationHost
{
    internal static class RenderCheck
    {

        /// <summary>
        /// Checks UI bindings and saves a headless window rendering.
        /// </summary>
        public static int Run(string output)
        {
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            SessionLog log = new();
            ProcessRunner runner = new(log);
            DependencyService dependencies = new(runner, log);
            SessionCoordinator sessions = new(new StreamSessionFactory(dependencies, runner, log), log);
            MainWindow window = new(initializeSession: false);
            MainViewModel viewModel = new(sessions, dependencies, new SettingsStore(), log, new DesktopInteractions(window))
            {
                Message = "Stopped. URL is inactive.",
                Status = "Stopped"
            };
            window.DataContext = viewModel;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBox url = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == Defaults.Url(Defaults.FirstPort));

            if (!url.IsReadOnly)
            {
                throw new InvalidOperationException("URL field must be read-only.");
            }

            if (!window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Start") && button.IsEnabled))
            {
                throw new InvalidOperationException("Start must be available while stopped.");
            }

            viewModel.SourceIndex = (int)SourceKind.Unresponsive;
            Dispatcher.UIThread.RunJobs();

            if (!window.GetVisualDescendants().OfType<NumericUpDown>().Single().IsEffectivelyVisible)
            {
                throw new InvalidOperationException("Fault-specific controls did not become visible.");
            }

            viewModel.SourceIndex = 0;
            Dispatcher.UIThread.RunJobs();

            using WriteableBitmap? previous = window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();

            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            frame.Save(output);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine("PASS: bindings, source visibility, stopped commands, and rendered frame: " + output);

            return 0;
        }
    }
}