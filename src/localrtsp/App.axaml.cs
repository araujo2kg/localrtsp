using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using localrtsp.ViewModels;
using localrtsp.Views;

namespace localrtsp
{
    public sealed partial class App : Application
    {
        #region Properties and Events

        internal static SingleInstance? Instance
        {
            get; set;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads the application resources.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Creates the main window and wires application services.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                SessionFiles.CleanStale();
                SessionLog log = new();
                ProcessRunner runner = new(log);
                DependencyService dependencies = new(runner, log);
                SessionCoordinator sessions = new(new StreamSessionFactory(dependencies, runner, log), log);
                MainWindow window = new();
                MainViewModel viewModel = new(sessions, dependencies, new SettingsStore(), log, new DesktopInteractions(window));
                window.DataContext = viewModel;
                desktop.MainWindow = window;
                Instance?.Listen(() => Dispatcher.UIThread.Post(() =>
                {
                    window.Show();
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                    window.Activate();
                }));
            }

            base.OnFrameworkInitializationCompleted();
        }

        #endregion
    }
}