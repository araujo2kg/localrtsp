namespace localrtsp.Tests.Services.Streaming
{
    public sealed class SessionCoordinatorTest
    {
        #region Tests

        /// <summary>
        /// Checks that closing cancels startup and prevents queued restarts.
        /// </summary>
        [Fact]
        public async Task CloseDuringStartup_ShouldCancelWorkAndPreventQueuedRestart()
        {
            // Arrange
            TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            FakeFactory factory = new()
            {
                Startup = async token =>
                {
                    started.SetResult(true);
                    await Task.Delay(Timeout.Infinite, token);

                    return new FakeSession();
                }
            };
            SessionCoordinator coordinator = new(factory, new SessionLog());
            Task startup = coordinator.StartAsync(new StreamSettings());
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Act
            Task restart = coordinator.StartAsync(new StreamSettings());
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAll(startup, restart);

            // Assert
            Assert.Equal(SessionState.Stopped, coordinator.Snapshot.State);
            Assert.Empty(factory.Sessions);
        }

        /// <summary>
        /// Checks that repeated start and stop releases every session once.
        /// </summary>
        [Fact]
        public async Task RepeatedStartStop_ShouldReleaseEachSessionExactlyOnce()
        {
            // Arrange
            FakeFactory factory = new();

            await using SessionCoordinator coordinator = new(factory, new SessionLog());

            // Act
            for (int iteration = 0; iteration < 4; iteration++)
            {
                await coordinator.StartAsync(new StreamSettings());
                await coordinator.StopAsync();
            }

            // Assert
            Assert.Equal(4, factory.Sessions.Count);
            Assert.All(factory.Sessions, session => Assert.Equal(1, session.DisposeCount));
        }

        /// <summary>
        /// Checks that restart releases the old stream and updates the URL.
        /// </summary>
        [Fact]
        public async Task Restart_ShouldReleaseOldSessionAndExposeEffectiveUrl()
        {
            // Arrange
            FakeFactory factory = new();

            await using SessionCoordinator coordinator = new(factory, new SessionLog());
            await coordinator.StartAsync(new StreamSettings());

            // Act
            await coordinator.StartAsync(new StreamSettings { Source = SourceKind.TestPattern });

            // Assert
            Assert.Equal(1, factory.Sessions[0].DisposeCount);
            Assert.Equal(0, factory.Sessions[1].DisposeCount);
            Assert.Equal(Defaults.Url(8560), coordinator.Snapshot.Url);
            Assert.Equal(SourceKind.TestPattern, coordinator.Snapshot.ActiveSettings!.Source);
        }

        /// <summary>
        /// Checks that invalid pending settings preserve the running stream.
        /// </summary>
        [Fact]
        public async Task InvalidPendingSettings_ShouldPreserveExistingSession()
        {
            // Arrange
            FakeFactory factory = new();

            await using SessionCoordinator coordinator = new(factory, new SessionLog());
            await coordinator.StartAsync(new StreamSettings());
            factory.Validation = _ => throw new ArgumentException("Invalid pending source.");

            // Act
            await coordinator.StartAsync(new StreamSettings { Source = SourceKind.Camera });

            // Assert
            Assert.Equal(SessionState.Streaming, coordinator.Snapshot.State);
            Assert.Equal(0, factory.Sessions[0].DisposeCount);
            Assert.Contains("not applied", coordinator.Snapshot.Message);
        }

        /// <summary>
        /// Checks that Stop cancels startup and can be called again.
        /// </summary>
        [Fact]
        public async Task Stop_ShouldCancelPendingStartupAndBeIdempotent()
        {
            // Arrange
            TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            FakeFactory factory = new()
            {
                Startup = async token =>
                {
                    started.SetResult(true);
                    await Task.Delay(Timeout.Infinite, token);

                    return new FakeSession();
                }
            };

            await using SessionCoordinator coordinator = new(factory, new SessionLog());
            Task startup = coordinator.StartAsync(new StreamSettings());
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Act
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await startup;
            await coordinator.StopAsync();

            // Assert
            Assert.Equal(SessionState.Stopped, coordinator.Snapshot.State);
            Assert.Null(coordinator.Snapshot.ActiveSettings);
        }

        /// <summary>
        /// Checks that closing releases the stream and rejects later starts.
        /// </summary>
        [Fact]
        public async Task Close_ShouldReleaseStreamAndPreventLaterStartup()
        {
            // Arrange
            FakeFactory factory = new();
            SessionCoordinator coordinator = new(factory, new SessionLog());
            await coordinator.StartAsync(new StreamSettings());

            // Act
            await coordinator.DisposeAsync();
            await coordinator.StartAsync(new StreamSettings());
            await coordinator.DisposeAsync();

            // Assert
            Assert.Single(factory.Sessions);
            Assert.Equal(1, factory.Sessions[0].DisposeCount);
            Assert.Equal(SessionState.Stopped, coordinator.Snapshot.State);
        }

        /// <summary>
        /// Checks that unexpected completion releases the session and reports failure.
        /// </summary>
        [Fact]
        public async Task UnexpectedExit_ShouldReleaseSessionAndReportFailure()
        {
            // Arrange
            FakeFactory factory = new();

            await using SessionCoordinator coordinator = new(factory, new SessionLog());
            TaskCompletionSource<bool> failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.Changed += snapshot =>
            {
                if (snapshot.State == SessionState.Error)
                {
                    failed.TrySetResult(true);
                }
            };
            await coordinator.StartAsync(new StreamSettings());

            // Act
            factory.Sessions[0].Complete("Source disconnected.");
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Assert
            Assert.Equal(1, factory.Sessions[0].DisposeCount);
            Assert.Null(coordinator.Snapshot.ActiveSettings);
        }

        /// <summary>
        /// Checks that a failed replacement leaves no active session.
        /// </summary>
        [Fact]
        public async Task FailedReplacement_ShouldLeaveApplicationStoppedAndRecoverable()
        {
            // Arrange
            FakeFactory factory = new();

            await using SessionCoordinator coordinator = new(factory, new SessionLog());
            await coordinator.StartAsync(new StreamSettings());
            factory.Startup = _ => throw new IOException("Publisher failed.");

            // Act
            await coordinator.StartAsync(new StreamSettings());

            // Assert
            Assert.Equal(1, factory.Sessions[0].DisposeCount);
            Assert.Equal(SessionState.Error, coordinator.Snapshot.State);
            Assert.Null(coordinator.Snapshot.ActiveSettings);
        }

        #endregion

        #region Nested Types

        private sealed class FakeSession : IStreamSession
        {
            #region Fields

            private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            #endregion

            #region Properties and Events

            public string Url => Defaults.Url(8560);

            public Task<string> Completion => _completion.Task;

            public int DisposeCount
            {
                get; private set;
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Signals that the fake session has finished.
            /// </summary>
            public void Complete(string message)
            {
                _completion.TrySetResult(message);
            }

            #endregion

            #region Disposal

            /// <summary>
            /// Records disposal and completes the fake session.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                DisposeCount++;
                Complete("Stopped.");

                return ValueTask.CompletedTask;
            }

            #endregion
        }

        private sealed class FakeFactory : ISessionFactory
        {
            #region Properties and Events

            public Func<CancellationToken, Task>? Validation
            {
                get; set;
            }

            public Func<CancellationToken, Task<IStreamSession>>? Startup
            {
                get; set;
            }

            public List<FakeSession> Sessions { get; } = new();

            #endregion

            #region Public Methods

            /// <summary>
            /// Runs the optional fake validation callback.
            /// </summary>
            public Task ValidateAsync(StreamSettings settings, CancellationToken token)
            {
                return Validation?.Invoke(token) ?? Task.CompletedTask;
            }

            /// <summary>
            /// Runs the startup callback or creates a tracked fake session.
            /// </summary>
            public Task<IStreamSession> StartAsync(StreamSettings settings, CancellationToken token)
            {
                if (Startup != null)
                {
                    return Startup(token);
                }

                FakeSession session = new();
                Sessions.Add(session);

                return Task.FromResult<IStreamSession>(session);
            }

            #endregion
        }

        #endregion
    }
}