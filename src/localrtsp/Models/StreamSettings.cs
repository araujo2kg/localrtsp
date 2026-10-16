namespace localrtsp.Models
{
    public sealed record StreamSettings
    {
        private static readonly string[] _supportedImageExtensions = { ".png", ".jpg", ".jpeg" };

        #region Properties and Events

        /// <summary>
        /// Selects the source used to generate the stream.
        /// </summary>
        public SourceKind Source
        {
            get; init;
        }

        public int Width { get; init; } = Defaults.FrameWidth;

        public int Height { get; init; } = Defaults.FrameHeight;

        public int Fps { get; init; } = Defaults.FramesPerSecond;

        public string Color { get; init; } = Defaults.FrameColor;

        public string FilePath { get; init; } = "";

        /// <summary>
        /// Identifies the DirectShow camera independently of its display name.
        /// </summary>
        public string CameraId { get; init; } = "";

        /// <summary>
        /// Repeats video playback instead of stopping at the end.
        /// </summary>
        public bool Loop { get; init; } = true;

        /// <summary>
        /// Sets how long fault clients wait without RTSP replies.
        /// </summary>
        public int StallSeconds { get; init; } = Defaults.StallSeconds;

        /// <summary>
        /// Overrides tool discovery when an executable path is supplied.
        /// </summary>
        public string FFmpegPath { get; init; } = "";

        public string MediaMtxPath { get; init; } = "";

        #endregion

        #region Public Methods

        /// <summary>
        /// Rejects unsupported or missing source settings.
        /// </summary>
        public void Validate()
        {
            if (!Enum.IsDefined(Source))
            {
                throw new ArgumentException("Select a supported source.");
            }

            if (!Defaults.Resolutions.Any(r => r.Width == Width && r.Height == Height))
            {
                throw new ArgumentException("Select a supported resolution.");
            }

            if (!Defaults.FrameRates.Contains(Fps))
            {
                throw new ArgumentException("Select a supported frame rate.");
            }

            if (StallSeconds is < Defaults.MinimumStallSeconds or > Defaults.MaximumStallSeconds)
            {
                throw new ArgumentException("Stall duration must be between 1 and 300 seconds.");
            }

            if (Source == SourceKind.SolidColor && !System.Text.RegularExpressions.Regex.IsMatch(Color, "^#[0-9a-fA-F]{6}$"))
            {
                throw new ArgumentException("Enter a color as #RRGGBB, for example #000000.");
            }

            if (Source is SourceKind.Image or SourceKind.Video && !File.Exists(FilePath))
            {
                throw new ArgumentException("Select an existing, readable source file.");
            }

            if (Source == SourceKind.Image && !_supportedImageExtensions.Contains(Path.GetExtension(FilePath).ToLowerInvariant()))
            {
                throw new ArgumentException("Select a PNG or JPEG image.");
            }

            if (Source == SourceKind.Camera && string.IsNullOrWhiteSpace(CameraId))
            {
                throw new ArgumentException("Select a camera. Use Refresh if the list is empty.");
            }
        }

        #endregion
    }
}