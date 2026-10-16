namespace localrtsp.Tests.Services.Dependencies
{
    public sealed class DependencyServiceTest
    {

        /// <summary>
        /// Checks camera labels and stable identifiers for duplicate device names.
        /// </summary>
        [Fact]
        public void ParseCameras_ShouldDisambiguateVideoDevicesUsingAlternativeIdentifiers()
        {
            // Arrange
            string output = """
                [dshow] "USB Camera" (video)
                [dshow] Alternative name "@device_1"
                [dshow] "Microphone" (audio)
                [dshow] Alternative name "@audio_1"
                [dshow] "USB Camera" (video)
                [dshow] Alternative name "@device_2"
                """;

            // Act
            IReadOnlyList<CameraDevice> cameras = DependencyService.ParseCameras(output);

            // Assert
            Assert.Equal(2, cameras.Count);
            Assert.Equal("@device_1", cameras[0].Id);
            Assert.Equal("@device_2", cameras[1].Id);
            Assert.NotEqual(cameras[0].Name, cameras[1].Name);
        }

        /// <summary>
        /// Checks that an invalid explicit tool path prevents fallback.
        /// </summary>
        [Fact]
        public void DependencyDiscovery_ShouldRejectAnExplicitMissingPathInsteadOfFallingBack()
        {
            // Arrange
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => DependencyService.FindExecutable("ffmpeg", path));
        }
    }
}