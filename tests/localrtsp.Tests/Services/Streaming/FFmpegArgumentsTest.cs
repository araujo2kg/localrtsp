namespace localrtsp.Tests.Services.Streaming
{
    public sealed class FFmpegArgumentsTest
    {

        /// <summary>
        /// Checks the source, encoding, and transport arguments.
        /// </summary>
        [Theory]
        [InlineData(SourceKind.SolidColor, "color=c=0x000000:s=640x480:r=15")]
        [InlineData(SourceKind.TestPattern, "testsrc2=size=640x480:rate=15")]
        [InlineData(SourceKind.Camera, "video=@device_unique_id")]
        public void Publish_ShouldGenerateInteroperableSourceArguments(SourceKind kind, string input)
        {
            // Arrange
            StreamSettings settings = new()
            {
                Source = kind,
                CameraId = "@device_unique_id"
            };

            // Act
            IReadOnlyList<string> args = FFmpegArguments.Publish(settings, Defaults.Url(8555));

            // Assert
            Assert.Contains(input, args);
            Assert.Contains("libx264", args);
            Assert.Contains("yuv420p", args);
            Assert.Equal(Defaults.Url(8555), args[^1]);
            Assert.Contains("tcp", args);
        }

        /// <summary>
        /// Checks that source paths remain a single argument.
        /// </summary>
        [Theory]
        [InlineData(SourceKind.Image, ".png")]
        [InlineData(SourceKind.Video, ".mp4")]
        public void Publish_ShouldKeepUnicodeAndPunctuationPathAsOneArgument(SourceKind kind, string extension)
        {
            // Arrange
            string path = Path.Combine(Path.GetTempPath(), $"localrtsp teste ÃƒÂ§ & $ {Guid.NewGuid():N}{extension}");
            File.WriteAllText(path, "argument-only fixture");

            try
            {
                StreamSettings settings = new()
                {
                    Source = kind,
                    FilePath = path
                };

                // Act
                IReadOnlyList<string> args = FFmpegArguments.Publish(settings, Defaults.Url(8554));

                // Assert
                Assert.Single(args, a => a == path);
                Assert.Contains("-re", args);
                Assert.Contains(kind == SourceKind.Image ? "-loop" : "-stream_loop", args);
                Assert.Contains(args, a => a.Contains("force_original_aspect_ratio=decrease") && a.Contains("pad="));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}