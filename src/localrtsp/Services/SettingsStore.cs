namespace localrtsp.Services
{
    public sealed class SettingsStore
    {
        #region Fields

        private readonly string _directory;

        #endregion

        #region Constructors

        /// <summary>
        /// Selects the directory used for saved settings.
        /// </summary>
        public SettingsStore(string? directory = null)
        {
            _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "localrtsp");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads saved settings or defaults when no valid settings exist.
        /// </summary>
        public async Task<StreamSettings> LoadAsync(CancellationToken token)
        {
            string path = Path.Combine(_directory, "settings.json");

            if (!File.Exists(path))
            {
                return new StreamSettings();
            }

            try
            {
                string json = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);

                return JsonSerializer.Deserialize<StreamSettings>(json) ?? new StreamSettings();
            }
            catch (JsonException)
            {
                return new StreamSettings();
            }
        }

        /// <summary>
        /// Replaces saved settings after writing a temporary file.
        /// </summary>
        public async Task SaveAsync(StreamSettings settings, CancellationToken token)
        {
            Directory.CreateDirectory(_directory);
            string temporary = Path.Combine(_directory, "settings.json.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            File.Move(temporary, Path.Combine(_directory, "settings.json"), true);
        }

        #endregion
    }
}