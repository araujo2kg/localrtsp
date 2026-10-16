namespace localrtsp.Infrastructure
{
    /// <summary>
    /// A named event activates the existing per-user application instance.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        #region Fields

        private readonly EventWaitHandle _signal;
        private RegisteredWaitHandle? _wait;

        #endregion

        #region Properties and Events

        public bool IsPrimary
        {
            get;
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates the per-user application activation signal.
        /// </summary>
        public SingleInstance()
        {
            string user = Environment.UserDomainName + "-" + Environment.UserName;
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, "localrtsp.Activate." + user, out bool created);
            IsPrimary = created;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Signals the existing application instance to activate its window.
        /// </summary>
        public void ActivatePrimary()
        {
            _signal.Set();
        }

        /// <summary>
        /// Registers the callback for activation requests.
        /// </summary>
        public void Listen(Action activate)
        {
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => activate(), null, Timeout.Infinite, false);
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Releases the activation listener and signal.
        /// </summary>
        public void Dispose()
        {
            _wait?.Unregister(null);
            _signal.Dispose();
        }

        #endregion
    }
}