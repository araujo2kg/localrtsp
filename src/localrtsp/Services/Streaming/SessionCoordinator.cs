namespace localrtsp.Services.Streaming
{
    /// <summary>
    /// Serializes session transitions. Stop/close cancel startup before waiting for its gate.
    /// </summary>
    public sealed class SessionCoordinator : IAsyncDisposable
    {
        #region Fields

        private readonly ISessionFactory _factory;
        private readonly SessionLog _log;
        private readonly SemaphoreSlim _gate = new(1);
        private readonly object _sync = new();
        private CancellationTokenSource? _startup;
        private IStreamSession? _session;
        private bool _closing;
        private SessionSnapshot _snapshot = new(SessionState.Stopped, Defaults.Url(Defaults.FirstPort), "Stopped. URL is inactive.");

        #endregion

        #region Properties and Events

        public event Action<SessionSnapshot>? Changed;

        /// <summary>
        /// Returns the latest immutable session state.
        /// </summary>
        public SessionSnapshot Snapshot => Volatile.Read(ref _snapshot);

        #endregion

        #region Constructors

        /// <summary>
        /// Creates the coordinator for serialized session transitions.
        /// </summary>
        public SessionCoordinator(ISessionFactory factory, SessionLog log)
        {
            _factory = factory;
            _log = log;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a session using the selected settings.
        /// </summary>
        public async Task StartAsync(StreamSettings settings, CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            CancellationTokenSource startup;
            lock (_sync)
            {
                if (_closing)
                {
                    _gate.Release();

                    return;
                }

                _startup = startup = CancellationTokenSource.CreateLinkedTokenSource(token);
                startup.CancelAfter(Defaults.TransitionTimeout);
            }

            try
            {
                // Validation failure must preserve an existing stream and its effective settings.
                await _factory.ValidateAsync(settings, startup.Token).ConfigureAwait(false);
                await StopCurrentAsync().ConfigureAwait(false);
                Publish(new(SessionState.Starting, Snapshot.Url, "Starting local endpoint..."));
                _session = await _factory.StartAsync(settings, startup.Token).ConfigureAwait(false);
                Publish(new(settings.Source == SourceKind.Unresponsive ? SessionState.SimulatingFault : SessionState.Streaming, _session.Url, settings.Source == SourceKind.Unresponsive ? "Fault simulation active - no RTSP responses." : "Streaming on this machine only.", settings));
                _ = ObserveAsync(_session);
            }
            catch (OperationCanceledException) when (startup.IsCancellationRequested)
            {
                await StopCurrentAsync().ConfigureAwait(false);
                Publish(new(SessionState.Stopped, Snapshot.Url, token.IsCancellationRequested ? "Startup canceled." : "Startup canceled or timed out. Retry after checking dependencies and source."));
            }
            catch (Exception ex)
            {
                _log.Write("Session", ex.Message);

                if (_session == null)
                {
                    Publish(new(SessionState.Error, Snapshot.Url, ex.Message));
                }
                else
                {
                    Publish(Snapshot with
                    {
                        Message = "Pending settings were not applied: " + ex.Message
                    });
                }
            }
            finally
            {
                lock (_sync)
                {
                    _startup = null;
                    startup.Dispose();
                }

                _gate.Release();
            }
        }

        /// <summary>
        /// Stops the active operation and releases its owned resources.
        /// </summary>
        public async Task StopAsync()
        {
            lock (_sync)
            {
                _startup?.Cancel();
            }

            await _gate.WaitAsync().ConfigureAwait(false);

            try
            {
                Publish(Snapshot with
                {
                    State = SessionState.Stopping,
                    Message = "Releasing local endpoint..."
                });
                await StopCurrentAsync().ConfigureAwait(false);
                Publish(new(SessionState.Stopped, Snapshot.Url, "Stopped. URL is inactive."));
            }
            finally
            {
                _gate.Release();
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Handles completion of the current session without affecting replacements.
        /// </summary>
        private async Task ObserveAsync(IStreamSession observed)
        {
            try
            {
                string reason = await observed.Completion.ConfigureAwait(false);
                await _gate.WaitAsync().ConfigureAwait(false);

                try
                {
                    if (!ReferenceEquals(_session, observed))
                    {
                        return;
                    }

                    await StopCurrentAsync().ConfigureAwait(false);
                    Publish(new(reason == "Playback completed." ? SessionState.Stopped : SessionState.Error, Snapshot.Url, reason));
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex)
            {
                _log.Write("Monitor", ex.Message);
            }
        }

        /// <summary>
        /// Detaches and disposes the current session.
        /// </summary>
        private async Task StopCurrentAsync()
        {
            IStreamSession? previous = _session;
            _session = null;

            if (previous != null)
            {
                await previous.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the session snapshot and notifies subscribers.
        /// </summary>
        private void Publish(SessionSnapshot value)
        {
            Volatile.Write(ref _snapshot, value);
            Changed?.Invoke(value);
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                _closing = true;
                _startup?.Cancel();
            }

            await StopAsync().ConfigureAwait(false);
        }

        #endregion
    }
}