using System.Diagnostics;

namespace localrtsp.Tests.Utilities
{
    public sealed class IntegrationTheoryAttribute : TheoryAttribute
    {

        /// <summary>
        /// Initializes the integration test prerequisites.
        /// </summary>
        public IntegrationTheoryAttribute()
        {
            if (Environment.GetEnvironmentVariable("LOCALRTSP_INTEGRATION") != "1")
            {
                Skip = "Opt-in real MediaMTX/FFmpeg test. Build the solution and set LOCALRTSP_INTEGRATION=1.";
            }
        }
    }
}