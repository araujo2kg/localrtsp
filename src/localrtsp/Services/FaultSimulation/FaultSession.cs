namespace localrtsp.Services.FaultSimulation
{
    internal sealed class FaultSession : IStreamSession
    {
        #region Fields

        private readonly FaultEndpoint _endpoint;
        private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Properties and Events

        public string Url
        {
            get;
        }

        public Task<string> Completion => _completion.Task;

        #endregion

        #region Constructors

        /// <summary>
        /// Wraps a fault listener as a stream session.
        /// </summary>
        public FaultSession(FaultEndpoint endpoint)
        {
            _endpoint = endpoint;
            Url = Defaults.Url(endpoint.Port);
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await _endpoint.DisposeAsync().ConfigureAwait(false);
            _completion.TrySetResult("Stopped.");
        }

        #endregion
    }
}