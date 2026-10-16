using Avalonia;

namespace localrtsp
{
    internal static class Program
    {
        /// <summary>
        /// Starts the desktop app or activates its existing instance.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("localrtsp currently supports Windows x64.");
            }

            using SingleInstance instance = new();

            if (!instance.IsPrimary)
            {
                instance.ActivatePrimary();

                return;
            }

            App.Instance = instance;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// Configures Avalonia for the current desktop platform.
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect();
        }
    }
}