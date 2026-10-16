namespace localrtsp.Services.FaultSimulation
{
    /// <summary>
    /// Loopback-only fault simulator with bounded sockets and cancellable delays.
    /// </summary>
    public sealed class FaultEndpoint : IAsyncDisposable
    {
        #region Fields

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> _clients = new();
        private readonly TimeSpan _stall;
        private readonly Task _acceptLoop;
        private int _sequence;
        private int _disposed;

        #endregion

        #region Properties and Events

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        #endregion

        #region Constructors

        /// <summary>
        /// Starts a loopback listener with bounded client stalls.
        /// </summary>
        public FaultEndpoint(int port, TimeSpan stall)
        {
            if (stall <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(stall));
            }

            _stall = stall;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start(Defaults.MaximumFaultClients);
            _acceptLoop = AcceptAsync();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Accepts loopback clients up to the configured connection limit.
        /// </summary>
        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);

                    if (_clients.Count >= Defaults.MaximumFaultClients)
                    {
                        client.Dispose();
                        continue;
                    }

                    int id = Interlocked.Increment(ref _sequence);
                    Task task = StallAsync(client);
                    _clients[id] = task;
                    _ = RemoveWhenDoneAsync(id, task);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_stop.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
                // Stopping the listener can close its socket before cancellation is observed.
            }
        }

        /// <summary>
        /// Keeps a client connected without sending RTSP responses.
        /// </summary>
        private async Task StallAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await Task.Delay(_stall, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                }
            }
        }

        /// <summary>
        /// Removes a client from tracking after its delay completes.
        /// </summary>
        private async Task RemoveWhenDoneAsync(int id, Task task)
        {
            await task.ConfigureAwait(false);
            _clients.TryRemove(id, out _);
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await _stop.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            await _acceptLoop.ConfigureAwait(false);
            await Task.WhenAll(_clients.Values).ConfigureAwait(false);
            _stop.Dispose();
        }

        #endregion
    }
}