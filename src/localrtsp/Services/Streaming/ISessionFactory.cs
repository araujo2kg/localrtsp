namespace localrtsp.Services.Streaming
{
    public interface ISessionFactory
    {
        /// <summary>
        /// Checks the source and required tools before replacing a session.
        /// </summary>
        Task ValidateAsync(StreamSettings settings, CancellationToken token);

        /// <summary>
        /// Starts a session using the selected settings.
        /// </summary>
        Task<IStreamSession> StartAsync(StreamSettings settings, CancellationToken token);
    }
}