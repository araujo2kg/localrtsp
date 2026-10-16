namespace localrtsp.Tests.Services.FaultSimulation
{
    public sealed class FaultEndpointTest
    {
        #region Tests

        /// <summary>
        /// Checks that stopping disconnects fault clients and releases the port.
        /// </summary>
        [Fact]
        public async Task Stop_ShouldDisconnectClientsAndReleasePort()
        {
            // Arrange
            FaultEndpoint endpoint = new(0, TimeSpan.FromSeconds(45));
            int port = endpoint.Port;

            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, port);

            // Act
            await endpoint.DisposeAsync();

            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
            int read = await client.GetStream().ReadAsync(new byte[1], deadline.Token);
            TcpListener replacement = new(IPAddress.Loopback, port);
            replacement.Start();
            replacement.Stop();

            // Assert
            Assert.Equal(0, read);
        }

        /// <summary>
        /// Checks the client limit and repeated disposal.
        /// </summary>
        [Fact]
        public async Task FaultEndpoint_ShouldBoundConcurrentClientsAndAllowIdempotentDisposal()
        {
            // Arrange
            FaultEndpoint endpoint = new(0, TimeSpan.FromSeconds(45));
            List<TcpClient> clients = new();

            try
            {
                for (int index = 0; index <= Defaults.MaximumFaultClients; index++)
                {
                    TcpClient client = new();
                    clients.Add(client);
                    await client.ConnectAsync(IPAddress.Loopback, endpoint.Port);
                }

                // Act
                using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
                int read = await clients[^1].GetStream().ReadAsync(new byte[1], deadline.Token);
                await endpoint.DisposeAsync();
                await endpoint.DisposeAsync();

                // Assert
                Assert.Equal(0, read);
            }
            finally
            {
                foreach (TcpClient client in clients)
                {
                    client.Dispose();
                }

                await endpoint.DisposeAsync();
            }
        }

        /// <summary>
        /// Checks that stalled clients receive no data before the connection closes.
        /// </summary>
        [Fact]
        public async Task Stall_ShouldSendNoResponseAndCloseAfterDeadline()
        {
            // Arrange
            await using FaultEndpoint endpoint = new(0, TimeSpan.FromMilliseconds(200));

            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, endpoint.Port);

            // Act
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
            int read = await client.GetStream().ReadAsync(new byte[8], deadline.Token);

            // Assert
            Assert.Equal(0, read);
        }

        #endregion
    }
}