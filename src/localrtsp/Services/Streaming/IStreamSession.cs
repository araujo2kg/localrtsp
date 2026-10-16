namespace localrtsp.Services.Streaming
{
    public interface IStreamSession : IAsyncDisposable
    {
        /// <summary>
        /// Provides the loopback endpoint for this session.
        /// </summary>
        string Url
        {
            get;
        }

        /// <summary>
        /// Reports why the session ended.
        /// </summary>
        Task<string> Completion
        {
            get;
        }
    }
}