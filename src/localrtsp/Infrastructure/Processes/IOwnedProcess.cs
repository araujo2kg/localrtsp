namespace localrtsp.Infrastructure.Processes
{
    public interface IOwnedProcess : IAsyncDisposable
    {
        /// <summary>
        /// Completes after the process exits and its output is drained.
        /// </summary>
        Task<int> Completion
        {
            get;
        }

        /// <summary>
        /// Contains the bounded diagnostic transcript.
        /// </summary>
        string Output
        {
            get;
        }

        int Id
        {
            get;
        }

        /// <summary>
        /// Stops the active operation and releases its owned resources.
        /// </summary>
        Task StopAsync();
    }
}