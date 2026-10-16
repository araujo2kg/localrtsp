namespace localrtsp.Services.Streaming
{
    public sealed class StreamSessionFactory : ISessionFactory
    {
        #region Fields

        private readonly IDependencyService _dependencies;
        private readonly IProcessRunner _runner;
        private readonly SessionLog _log;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a session factory with tool validation and process services.
        /// </summary>
        public StreamSessionFactory(IDependencyService dependencies, IProcessRunner runner, SessionLog log)
        {
            _dependencies = dependencies;
            _runner = runner;
            _log = log;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks the source and required tools before replacing a session.
        /// </summary>
        public async Task ValidateAsync(StreamSettings settings, CancellationToken token)
        {
            settings.Validate();

            if (settings.Source == SourceKind.Unresponsive)
            {
                return;
            }

            ToolPaths paths = await _dependencies.ResolveAsync(settings, token).ConfigureAwait(false);
            await _dependencies.ValidateSourceAsync(settings, paths.FFmpeg, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a session using the selected settings.
        /// </summary>
        public async Task<IStreamSession> StartAsync(StreamSettings settings, CancellationToken token)
        {
            ToolPaths? paths = settings.Source == SourceKind.Unresponsive ? null : await _dependencies.ResolveAsync(settings, token).ConfigureAwait(false);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Defaults.StartupTimeout);

            for (int port = Defaults.FirstPort; port <= Defaults.LastPort; port++)
            {
                timeout.Token.ThrowIfCancellationRequested();

                try
                {
                    if (settings.Source == SourceKind.Unresponsive)
                    {
                        return new FaultSession(new FaultEndpoint(port, TimeSpan.FromSeconds(settings.StallSeconds)));
                    }

                    return await VideoSession.CreateAsync(settings, port, paths!, _runner, _log, timeout.Token).ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                }
                catch (PortConflictException)
                {
                }
            }

            throw new InvalidOperationException($"No RTSP port is available between {Defaults.FirstPort} and {Defaults.LastPort}. Stop another local stream and retry.");
        }

        #endregion

    }
}