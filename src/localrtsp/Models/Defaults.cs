namespace localrtsp.Models
{
    public static class Defaults
    {
        #region Constants

        public const int FrameWidth = 640;
        public const int FrameHeight = 480;
        public const int FramesPerSecond = 15;
        public const string FrameColor = "#000000";
        public const int StallSeconds = 45;
        public const int MinimumStallSeconds = 1;
        public const int MaximumStallSeconds = 300;
        public const int MaximumLogLineLength = 4096;
        public const int FirstPort = 8554;
        public const int LastPort = 8574;
        public const int MaximumFaultClients = 16;
        public const int MaximumLogLines = 250;

        #endregion

        #region Fields

        public static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(60);
        public static readonly Resolution[] Resolutions =
        {
            new(640, 480),
            new(1280, 720),
            new(1920, 1080)
        };
        public static readonly int[] FrameRates =
        {
            5,
            15,
            30
        };
        public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

        #endregion

        #region Public Methods

        /// <summary>
        /// Builds the loopback RTSP URL for the selected port.
        /// </summary>
        public static string Url(int port)
        {
            return $"rtsp://127.0.0.1:{port}/camera";
        }

        #endregion
    }
}