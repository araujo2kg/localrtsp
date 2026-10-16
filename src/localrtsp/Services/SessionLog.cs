namespace localrtsp.Services
{
    /// <summary>
    /// Thread-safe bounded diagnostics. Subscribers must marshal UI updates.
    /// </summary>
    public sealed class SessionLog
    {
        #region Fields

        private readonly object _sync = new();
        private readonly Queue<string> _lines = new();

        #endregion

        #region Properties and Events

        public event Action? Changed;
        public string Text
        {
            get
            {
                lock (_sync)
                {
                    return string.Join(Environment.NewLine, _lines);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Appends timestamped diagnostics while enforcing log limits.
        /// </summary>
        public void Write(string component, string message)
        {
            lock (_sync)
            {
                foreach (string line in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    _lines.Enqueue($"{DateTime.Now:HH:mm:ss} [{component}] {line.TrimEnd()[..Math.Min(line.TrimEnd().Length, Defaults.MaximumLogLineLength)]}");

                    while (_lines.Count > Defaults.MaximumLogLines)
                    {
                        _lines.Dequeue();
                    }
                }
            }

            Changed?.Invoke();
        }

        #endregion
    }
}