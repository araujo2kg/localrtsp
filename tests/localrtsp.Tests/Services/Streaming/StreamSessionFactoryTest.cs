namespace localrtsp.Tests.Services.Streaming
{
    public sealed class StreamSessionFactoryTest
    {
        #region Tests

        /// <summary>
        /// Checks that configuration errors release resources without retrying ports.
        /// </summary>
        [Fact]
        public async Task ConfigurationFailure_ShouldDisposePartialSessionWithoutRetryingOtherPorts()
        {
            // Arrange
            Runner runner = new();
            StreamSessionFactory factory = new(new Dependencies(), runner, new SessionLog());

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => factory.StartAsync(new StreamSettings(), CancellationToken.None));
            Assert.Single(runner.Processes);
            Assert.Equal(1, runner.Processes[0].DisposeCount);
        }

        /// <summary>
        /// Checks that port retries stay bounded and release every failed attempt.
        /// </summary>
        [Fact]
        public async Task BindConflict_ShouldRetryOnlyWithinDocumentedRangeAndDisposeEveryAttempt()
        {
            // Arrange
            Runner runner = new()
            {
                Error = "bind: address already in use"
            };
            StreamSessionFactory factory = new(new Dependencies(), runner, new SessionLog());

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => factory.StartAsync(new StreamSettings(), CancellationToken.None));
            Assert.Equal(Defaults.LastPort - Defaults.FirstPort + 1, runner.Processes.Count);
            Assert.All(runner.Processes, process => Assert.Equal(1, process.DisposeCount));
        }

        #endregion

        #region Nested Types

        private sealed class Dependencies : IDependencyService
        {
            #region Public Methods

            /// <summary>
            /// Returns fake executable paths without starting tools.
            /// </summary>
            public Task<ToolPaths> ResolveAsync(StreamSettings settings, CancellationToken token)
            {
                return Task.FromResult(new ToolPaths("fake-ffmpeg", "fake-mediamtx"));
            }

            /// <summary>
            /// Completes fake source validation without accessing hardware.
            /// </summary>
            public Task ValidateSourceAsync(StreamSettings settings, string ffmpeg, CancellationToken token)
            {
                return Task.CompletedTask;
            }

            /// <summary>
            /// Returns an empty fake camera list.
            /// </summary>
            public Task<IReadOnlyList<CameraDevice>> GetCamerasAsync(string path, CancellationToken token)
            {
                return Task.FromResult<IReadOnlyList<CameraDevice>>(Array.Empty<CameraDevice>());
            }

            #endregion
        }

        private sealed class FailedProcess : IOwnedProcess
        {
            #region Properties and Events

            public string Output { get; init; } = "Invalid configuration.";

            public int Id => 0;

            public Task<int> Completion => Task.FromResult(1);

            public int DisposeCount
            {
                get; private set;
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Completes the fake stop operation.
            /// </summary>
            public Task StopAsync()
            {
                return Task.CompletedTask;
            }

            #endregion

            #region Disposal

            /// <summary>
            /// Records disposal of the fake process.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                DisposeCount++;

                return ValueTask.CompletedTask;
            }

            #endregion
        }

        private sealed class Runner : IProcessRunner
        {
            #region Properties and Events

            public string Error { get; init; } = "Invalid configuration.";

            public List<FailedProcess> Processes { get; } = new();

            #endregion

            #region Public Methods

            /// <summary>
            /// Records a failed fake process for cleanup assertions.
            /// </summary>
            public IOwnedProcess Start(string executable, IReadOnlyList<string> arguments, string component)
            {
                FailedProcess process = new()
                {
                    Output = Error
                };
                Processes.Add(process);

                return process;
            }

            /// <summary>
            /// Rejects probes that this startup test does not expect.
            /// </summary>
            public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
            {
                throw new NotSupportedException();
            }

            #endregion
        }

        #endregion
    }
}