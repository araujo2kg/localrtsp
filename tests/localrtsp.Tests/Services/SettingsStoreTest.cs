namespace localrtsp.Tests.Services
{
    public sealed class SettingsStoreTest
    {

        /// <summary>
        /// Checks that saved missing sources remain available for correction.
        /// </summary>
        [Fact]
        public async Task Load_ShouldPreserveMissingSourceForUserCorrection()
        {
            // Arrange
            string directory = Path.Combine(Path.GetTempPath(), "localrtsp-settings-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new(directory);
            StreamSettings settings = new()
            {
                Source = SourceKind.Video,
                FilePath = "missing video.mp4",
                Fps = 5
            };

            try
            {
                await store.SaveAsync(settings, CancellationToken.None);

                // Act
                StreamSettings restored = await store.LoadAsync(CancellationToken.None);

                // Assert
                Assert.Equal(settings, restored);
                Assert.Throws<ArgumentException>(restored.Validate);
            }
            finally
            {
                File.Delete(Path.Combine(directory, "settings.json"));
                Directory.Delete(directory);
            }
        }
    }
}