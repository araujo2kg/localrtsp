using Microsoft.Win32.SafeHandles;

namespace localrtsp.Infrastructure.Processes
{
    /// <summary>
    /// Owns process trees, concurrently drains output, and bounds one-shot probes.
    /// </summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        #region Constants

        private const int _maximumProbeOutputLines = 2000;

        #endregion

        #region Fields

        private readonly SessionLog _log;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a process runner with session diagnostics.
        /// </summary>
        public ProcessRunner(SessionLog log)
        {
            _log = log;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a child process owned by this application.
        /// </summary>
        public IOwnedProcess Start(string executable, IReadOnlyList<string> arguments, string component)
        {
            OwnedProcess process = new(executable, arguments, line => _log.Write(component, line));
            _log.Write(component, "Started owned process " + process.Id);

            return process;
        }

        /// <summary>
        /// Runs an operation with cancellation and bounded diagnostics.
        /// </summary>
        public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
        {
            List<string> output = new();

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Defaults.ProbeTimeout);
            await using OwnedProcess process = new(executable, arguments, line =>
            {
                lock (output)
                {
                    if (output.Count < _maximumProbeOutputLines)
                    {
                        output.Add(line);
                    }
                }
            });
            int exitCode = await process.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
            lock (output)
            {
                return new ProcessResult(exitCode, string.Join("\n", output));
            }
        }

        #endregion

        #region Nested Types

        private sealed class OwnedProcess : IOwnedProcess
        {
            #region Fields

            private readonly Process _process;
            private readonly SafeFileHandle _job;
            private readonly StreamReader _output;
            private readonly StreamReader _error;
            private readonly StreamWriter _input;
            private readonly Task _outputTask;
            private readonly Task _errorTask;
            private readonly SemaphoreSlim _stopGate = new(1);
            private bool _disposed;
            private readonly SessionLog _transcript = new();

            #endregion

            #region Properties and Events

            public string Output => _transcript.Text;

            public int Id => _process.Id;

            public Task<int> Completion
            {
                get;
            }

            #endregion

            #region Constructors

            /// <summary>
            /// Starts an owned process and drains its output streams.
            /// </summary>
            public OwnedProcess(string executable, IReadOnlyList<string> arguments, Action<string> output)
            {
                (_process, _job, _output, _error, _input) = NativeProcess.Start(executable, arguments);
                _outputTask = Task.Run(() => DrainAsync(_output, line =>
                {
                    _transcript.Write("stdout", line);
                    output(line);
                }));
                _errorTask = Task.Run(() => DrainAsync(_error, line =>
                {
                    _transcript.Write("stderr", line);
                    output(line);
                }));
                Completion = CompleteAsync();
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Stops the active operation and releases its owned resources.
            /// </summary>
            public async Task StopAsync()
            {
                await _stopGate.WaitAsync().ConfigureAwait(false);

                try
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        if (!_process.HasExited)
                        {
                            await _input.WriteLineAsync("q").ConfigureAwait(false);
                        }

                        await Completion.WaitAsync(Defaults.ShutdownTimeout).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
                    {
                        // Closing the job below terminates children that did not exit gracefully.
                    }
                    finally
                    {
                        _job.Dispose();
                    }

                    try
                    {
                        await Completion.WaitAsync(Defaults.ShutdownTimeout).ConfigureAwait(false);
                    }
                    finally
                    {
                        _disposed = true;
                        _input.Dispose();
                        _output.Dispose();
                        _error.Dispose();
                        _process.Dispose();
                    }
                }
                finally
                {
                    _stopGate.Release();
                }
            }

            #endregion

            #region Private Methods

            /// <summary>
            /// Reads process output until its stream closes.
            /// </summary>
            private static async Task DrainAsync(StreamReader reader, Action<string> output)
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
                {
                    output(line);
                }
            }

            /// <summary>
            /// Waits for process exit and both output readers.
            /// </summary>
            private async Task<int> CompleteAsync()
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(_outputTask, _errorTask).ConfigureAwait(false);

                return _process.ExitCode;
            }

            #endregion

            #region Disposal

            /// <summary>
            /// Releases the resources owned by this instance.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await StopAsync().ConfigureAwait(false);
            }

            #endregion
        }

        #endregion
    }
}