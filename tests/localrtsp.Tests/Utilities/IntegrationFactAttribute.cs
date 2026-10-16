using System.Diagnostics;

namespace localrtsp.Tests.Utilities
{
    public sealed class IntegrationFactAttribute : FactAttribute
    {

        /// <summary>
        /// Initializes the integration test prerequisites.
        /// </summary>
        public IntegrationFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LOCALRTSP_INTEGRATION") != "1")
            {
                Skip = "Opt-in real MediaMTX/FFmpeg test. Build the solution and set LOCALRTSP_INTEGRATION=1.";
            }
        }
    }
}