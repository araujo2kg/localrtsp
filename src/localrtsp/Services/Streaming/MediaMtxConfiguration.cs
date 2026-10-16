using System.Globalization;

namespace localrtsp.Services.Streaming
{
    internal static class MediaMtxConfiguration
    {
        /// <summary>
        /// Builds the server configuration from the embedded template.
        /// </summary>
        internal static string Create(int rtspPort, int controlApiPort)
        {
            using Stream template = typeof(MediaMtxConfiguration).Assembly
                .GetManifestResourceStream("localrtsp.Assets.mediamtx.yml")
                ?? throw new InvalidOperationException("The MediaMTX configuration template is missing.");

            using StreamReader reader = new(template);

            return reader.ReadToEnd()
                .Replace("{rtspPort}", rtspPort.ToString(CultureInfo.InvariantCulture))
                .Replace("{controlApiPort}", controlApiPort.ToString(CultureInfo.InvariantCulture));
        }
    }
}