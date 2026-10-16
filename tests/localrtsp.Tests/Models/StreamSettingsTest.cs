namespace localrtsp.Tests.Models
{
    public sealed class StreamSettingsTest
    {

        /// <summary>
        /// Checks that invalid frame colors are rejected.
        /// </summary>
        [Theory]
        [InlineData("not-a-color")]
        [InlineData("#000000:malicious-filter")]
        [InlineData("#123")]
        public void Validate_ShouldRejectInvalidColor(string color)
        {
            // Arrange
            StreamSettings settings = new()
            {
                Color = color
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(settings.Validate);
        }

        /// <summary>
        /// Checks validation of missing files and unsupported output settings.
        /// </summary>
        [Fact]
        public void Validate_ShouldRejectMissingSourceAndUnsupportedSettings()
        {
            // Arrange
            StreamSettings file = new()
            {
                Source = SourceKind.Video,
                FilePath = "missing-source.mp4"
            };
            StreamSettings resolution = new()
            {
                Width = 1
            };
            StreamSettings fps = new()
            {
                Fps = 999
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(file.Validate);
            Assert.Throws<ArgumentException>(resolution.Validate);
            Assert.Throws<ArgumentException>(fps.Validate);
        }
    }
}