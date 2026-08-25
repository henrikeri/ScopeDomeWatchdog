using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ScopeDomeWatchdog.Core.Services;
using ScopeDomeWatchdog.Tray;
using ScopeDomeWatchdog.Tray.Services;
using Xunit;

namespace ScopeDomeWatchdog.Core.Tests;

public sealed class ShellyBlePlugTests
{
    private const string Address = "8C:BF:EA:99:9C:DE";

    [Fact]
    public void UsesShellyRpcGattUuids()
    {
        Assert.Equal(
            Guid.Parse("5f6d4f53-5f52-5043-5f53-56435f49445f"),
            ShellyBlePlug.RpcServiceUuid);
        Assert.Equal(
            Guid.Parse("5f6d4f53-5f52-5043-5f64-6174615f5f5f"),
            ShellyBlePlug.RpcDataUuid);
        Assert.Equal(
            Guid.Parse("5f6d4f53-5f52-5043-5f74-785f63746c5f"),
            ShellyBlePlug.RpcTxControlUuid);
        Assert.Equal(
            Guid.Parse("5f6d4f53-5f52-5043-5f72-785f63746c5f"),
            ShellyBlePlug.RpcRxControlUuid);
    }

    [Fact]
    public void ScanResultsPutShellyDevicesFirstAndDisplayAssignedName()
    {
        var devices = new[]
        {
            new ShellyBleScanResult("00:00:00:00:00:01", "Headphones", -30),
            new ShellyBleScanResult("00:00:00:00:00:02", "ShellyPlugSG3-AABBCC", -70),
            new ShellyBleScanResult(
                "00:00:00:00:00:03",
                "ShellyPlugSG3-DDEEFF",
                -45,
                "Dome power")
        };

        var ordered = WindowsShellyBleConnector.OrderScanResults(devices);

        Assert.Equal(
            new[] { "00:00:00:00:00:03", "00:00:00:00:00:02", "00:00:00:00:00:01" },
            ordered.Select(device => device.Address));
        Assert.Contains("Dome power", ordered[0].DisplayLabel);
        Assert.Contains("ShellyPlugSG3-DDEEFF", ordered[0].DisplayLabel);
        Assert.Contains("-45 dBm", ordered[0].DisplayLabel);
    }

    [Fact]
    public void ScanResultsAreEnrichedWithConfiguredShellyNameByIdentitySuffix()
    {
        var devices = new[]
        {
            new ShellyBleScanResult(
                "8C:BF:EA:01:02:03",
                "ShellyPlugSG3-AABBCC",
                -42),
            new ShellyBleScanResult(
                "00:11:22:33:44:55",
                "Headphones",
                -30)
        };
        var identity = new ShellyDeviceIdentity(
            "112233AABBCC",
            "Dome controller power",
            "shellyplugs3-aabbcc");

        var enriched = SettingsWindow.EnrichWithAssignedName(devices, identity);

        Assert.Equal("Dome controller power", enriched[0].AssignedName);
        Assert.Null(enriched[1].AssignedName);
    }

    [Fact]
    public async Task RpcWritesBigEndianLengthAndCompactJson()
    {
        var connection = CreateGetConnection(123, true);
        using var plug = CreatePlug(new QueueConnector(connection), () => 123);

        Assert.True(await plug.GetOutputAsync(CancellationToken.None));

        Assert.Equal(ShellyBlePlug.RpcTxControlUuid, connection.Writes[0].Uuid);
        Assert.Equal(4, connection.Writes[0].Value.Length);
        var advertisedLength = BinaryPrimitives.ReadUInt32BigEndian(connection.Writes[0].Value);

        Assert.Equal(ShellyBlePlug.RpcDataUuid, connection.Writes[1].Uuid);
        Assert.Equal((uint)connection.Writes[1].Value.Length, advertisedLength);
        var json = Encoding.UTF8.GetString(connection.Writes[1].Value);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain(": ", json, StringComparison.Ordinal);

        using var request = JsonDocument.Parse(json);
        Assert.Equal(123, request.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Switch.GetStatus", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(0, request.RootElement.GetProperty("params").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task RpcAssemblesMultipleDataReadsToAdvertisedLength()
    {
        const int requestId = 50;
        var response = Response(requestId, new { output = true });
        var connection = new FakeBleConnection();
        connection.EnqueueRead(ShellyBlePlug.RpcRxControlUuid, Length(0));
        connection.EnqueueRead(ShellyBlePlug.RpcRxControlUuid, Length(response.Length));
        connection.EnqueueRead(ShellyBlePlug.RpcDataUuid, response[..7]);
        connection.EnqueueRead(ShellyBlePlug.RpcDataUuid, response[7..]);
        using var plug = CreatePlug(new QueueConnector(connection), () => requestId);

        Assert.True(await plug.GetOutputAsync(CancellationToken.None));
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task RpcUsesFirstAdvertisedResponseOnFreshConnection()
    {
        const int requestId = 51;
        var response = Response(requestId, new { output = true });
        var connection = new FakeBleConnection();
        connection.EnqueueRead(ShellyBlePlug.RpcRxControlUuid, Length(response.Length));
        connection.EnqueueRead(ShellyBlePlug.RpcDataUuid, response);
        using var plug = CreatePlug(new QueueConnector(connection), () => requestId);

        Assert.True(await plug.GetOutputAsync(CancellationToken.None));
        Assert.Equal(2, connection.Writes.Count);
    }

    [Fact]
    public async Task MismatchedResponseIdFailsAndDisconnects()
    {
        var connection = CreateGetConnection(999, true);
        using var plug = CreatePlug(new QueueConnector(connection), () => 100);

        var error = await Assert.ThrowsAsync<ShellyBleProtocolException>(
            () => plug.GetOutputAsync(CancellationToken.None));

        Assert.Contains("did not match", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task RpcErrorResponseFailsWithTypedException()
    {
        const int requestId = 101;
        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = requestId,
            error = new { code = 401, message = "not authorized" }
        });
        var connection = CreateSingleRpcConnection(response);
        using var plug = CreatePlug(new QueueConnector(connection), () => requestId);

        var error = await Assert.ThrowsAsync<ShellyBleRpcException>(
            () => plug.GetOutputAsync(CancellationToken.None));

        Assert.Contains("401", error.ErrorJson, StringComparison.Ordinal);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task SetOutputFalseUsesSwitchSetAndNeverToggle()
    {
        var connection = CreateSetConnection(200, false);
        var nextId = 199;
        using var plug = CreatePlug(new QueueConnector(connection), () => Interlocked.Increment(ref nextId));

        await plug.SetOutputAsync(false, CancellationToken.None);

        var requests = DataWrites(connection).Select(ParseRequest).ToList();
        Assert.Equal(new[] { "Switch.Set", "Switch.GetStatus" },
            requests.Select(request => request.RootElement.GetProperty("method").GetString()));
        Assert.DoesNotContain(requests, request =>
            request.RootElement.GetProperty("method").GetString() == "Switch.Toggle");
        Assert.False(requests[0].RootElement.GetProperty("params").GetProperty("on").GetBoolean());
        DisposeAll(requests);
    }

    [Fact]
    public async Task SetOutputTrueVerifiesTrueAndDisconnects()
    {
        var connection = CreateSetConnection(300, true);
        var nextId = 299;
        using var plug = CreatePlug(new QueueConnector(connection), () => Interlocked.Increment(ref nextId));

        await plug.SetOutputAsync(true, CancellationToken.None);

        Assert.True(connection.Disposed);
        using var statusRequest = ParseRequest(DataWrites(connection).Last());
        Assert.Equal("Switch.GetStatus", statusRequest.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task VerificationMismatchFailsAndDisconnects()
    {
        var connection = CreateSetConnection(400, false);
        var nextId = 399;
        using var plug = CreatePlug(new QueueConnector(connection), () => Interlocked.Increment(ref nextId));

        var error = await Assert.ThrowsAsync<ShellyStateVerificationException>(
            () => plug.SetOutputAsync(true, CancellationToken.None));

        Assert.Contains("verification failed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task ConnectionIsDisposedWhenWriteFails()
    {
        var connection = new FakeBleConnection
        {
            WriteError = new ShellyBleProtocolException("write failed")
        };
        using var plug = CreatePlug(new QueueConnector(connection), () => 1);

        await Assert.ThrowsAsync<ShellyBleProtocolException>(
            () => plug.GetOutputAsync(CancellationToken.None));

        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task ConcurrentCallsAreSerialized()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateGetConnection(501, true);
        first.BeforeFirstRead = () => gate.Task;
        var second = CreateGetConnection(502, false);
        var connector = new QueueConnector(first, second);
        var nextId = 500;
        using var plug = CreatePlug(connector, () => Interlocked.Increment(ref nextId));

        var firstCall = plug.GetOutputAsync(CancellationToken.None);
        await connector.FirstConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var secondCall = plug.GetOutputAsync(CancellationToken.None);
        await Task.Delay(50);

        Assert.Equal(1, connector.ConnectCount);
        gate.SetResult();
        Assert.True(await firstCall);
        Assert.False(await secondCall);
        Assert.Equal(1, connector.MaximumActiveConnections);
    }

    [Fact]
    public async Task TransientDiscoveryFailureRetriesOneCompleteConnection()
    {
        var connection = CreateGetConnection(600, true);
        var connector = new QueueConnector(connection)
        {
            ConnectErrors = new Queue<Exception>(new[]
            {
                new ShellyBleDiscoveryException("not found")
            })
        };
        using var plug = CreatePlug(connector, () => 600);

        Assert.True(await plug.GetOutputAsync(CancellationToken.None));
        Assert.Equal(2, connector.ConnectCount);
    }

    [Fact]
    public async Task SecondDiscoveryFailureIsNotRetriedAgain()
    {
        var connector = new QueueConnector(CreateGetConnection(601, true))
        {
            ConnectErrors = new Queue<Exception>(new Exception[]
            {
                new ShellyBleDiscoveryException("first scan timed out"),
                new ShellyBleDiscoveryException("second scan timed out")
            })
        };
        using var plug = CreatePlug(connector, () => 601);

        await Assert.ThrowsAsync<ShellyBleDiscoveryException>(
            () => plug.GetOutputAsync(CancellationToken.None));

        Assert.Equal(2, connector.ConnectCount);
    }

    [Fact]
    public async Task AuthorizationFailureIsNotRetried()
    {
        var connector = new QueueConnector(CreateGetConnection(602, true))
        {
            ConnectErrors = new Queue<Exception>(new Exception[]
            {
                new ShellyBleAuthorizationException("bond is missing")
            })
        };
        using var plug = CreatePlug(connector, () => 602);

        await Assert.ThrowsAsync<ShellyBleAuthorizationException>(
            () => plug.GetOutputAsync(CancellationToken.None));

        Assert.Equal(1, connector.ConnectCount);
    }

    [Fact]
    public async Task TcpSuccessNeverInvokesBle()
    {
        var tcp = new FakeTransport(ShellyTransport.Tcp) { Output = true };
        var ble = new FakeTransport(ShellyTransport.Ble);
        var controller = new ShellySwitchController(tcp, ble);

        var result = await controller.SetOutputAsync(false, CancellationToken.None);

        Assert.Equal(ShellyTransport.Tcp, result.Transport);
        Assert.Equal(new[] { false }, tcp.SetTargets);
        Assert.Empty(ble.SetTargets);
        Assert.Equal(0, ble.GetCalls);
    }

    [Fact]
    public async Task TcpSuccessDoesNotConstructLazyBleFallback()
    {
        var tcp = new FakeTransport(ShellyTransport.Tcp);
        var fallbackFactoryCalled = false;
        using var controller = new ShellySwitchController(
            tcp,
            () =>
            {
                fallbackFactoryCalled = true;
                throw new InvalidOperationException("invalid BLE configuration");
            });

        var result = await controller.SetOutputAsync(true, CancellationToken.None);

        Assert.Equal(ShellyTransport.Tcp, result.Transport);
        Assert.False(fallbackFactoryCalled);
        Assert.Equal(new[] { true }, tcp.SetTargets);
    }

    [Fact]
    public async Task LazyBleConfigurationFailureRetainsTcpFailure()
    {
        var tcpError = new HttpRequestException("connection refused");
        var configurationError = new ArgumentException("BLE address is missing");
        var tcp = new FakeTransport(ShellyTransport.Tcp) { SetError = tcpError };
        using var controller = new ShellySwitchController(
            tcp,
            () => throw configurationError);

        var error = await Assert.ThrowsAsync<ShellyControlException>(
            () => controller.SetOutputAsync(false, CancellationToken.None));

        Assert.Same(tcpError, error.PrimaryError);
        Assert.Same(configurationError, error.FallbackError);
        Assert.Equal(false, error.TargetOutput);
    }

    [Fact]
    public async Task TransientTcpFailureInvokesBleOnceWithSameTarget()
    {
        var tcp = new FakeTransport(ShellyTransport.Tcp)
        {
            SetError = new HttpRequestException("network unreachable")
        };
        var ble = new FakeTransport(ShellyTransport.Ble);
        var controller = new ShellySwitchController(tcp, ble);

        var result = await controller.SetOutputAsync(true, CancellationToken.None);

        Assert.Equal(ShellyTransport.Ble, result.Transport);
        Assert.Equal(new[] { true }, tcp.SetTargets);
        Assert.Equal(new[] { true }, ble.SetTargets);
    }

    [Fact]
    public async Task DeterministicTcpErrorDoesNotInvokeBle()
    {
        var expected = new InvalidOperationException("invalid component id");
        var tcp = new FakeTransport(ShellyTransport.Tcp) { SetError = expected };
        var ble = new FakeTransport(ShellyTransport.Ble);
        var controller = new ShellySwitchController(tcp, ble);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SetOutputAsync(true, CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Empty(ble.SetTargets);
    }

    [Fact]
    public async Task CombinedFailureRetainsBothErrorsAndTarget()
    {
        var tcpError = new HttpRequestException("connection reset");
        var bleError = new ShellyBleConnectionException("adapter unavailable");
        var tcp = new FakeTransport(ShellyTransport.Tcp) { SetError = tcpError };
        var ble = new FakeTransport(ShellyTransport.Ble) { SetError = bleError };
        var controller = new ShellySwitchController(tcp, ble);

        var error = await Assert.ThrowsAsync<ShellyControlException>(
            () => controller.SetOutputAsync(false, CancellationToken.None));

        Assert.Same(tcpError, error.PrimaryError);
        Assert.Same(bleError, error.FallbackError);
        Assert.Equal(false, error.TargetOutput);
    }

    [Fact]
    public async Task CallerCancellationDoesNotInvokeBle()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tcp = new FakeTransport(ShellyTransport.Tcp)
        {
            SetError = new OperationCanceledException(cancellation.Token)
        };
        var ble = new FakeTransport(ShellyTransport.Ble);
        var controller = new ShellySwitchController(tcp, ble);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.SetOutputAsync(true, cancellation.Token));

        Assert.Empty(ble.SetTargets);
    }

    private static ShellyBlePlug CreatePlug(
        IShellyBleConnector connector,
        Func<int> idFactory) =>
        new(
            connector,
            new ShellyBleOptions(
                Address,
                ResponseTimeout: TimeSpan.FromSeconds(1)),
            idFactory,
            (_, _) => Task.CompletedTask);

    private static FakeBleConnection CreateGetConnection(int requestId, bool output) =>
        CreateSingleRpcConnection(Response(requestId, new { output }));

    private static FakeBleConnection CreateSetConnection(int firstRequestId, bool verifiedOutput)
    {
        var connection = new FakeBleConnection();
        EnqueueRpcResponse(connection, Response(firstRequestId, new { was_on = !verifiedOutput }));
        EnqueueRpcResponse(connection, Response(firstRequestId + 1, new { output = verifiedOutput }));
        return connection;
    }

    private static FakeBleConnection CreateSingleRpcConnection(byte[] response)
    {
        var connection = new FakeBleConnection();
        EnqueueRpcResponse(connection, response);
        return connection;
    }

    private static void EnqueueRpcResponse(FakeBleConnection connection, byte[] response)
    {
        connection.EnqueueRead(ShellyBlePlug.RpcRxControlUuid, Length(response.Length));
        connection.EnqueueRead(ShellyBlePlug.RpcDataUuid, response);
    }

    private static byte[] Response(int id, object result) =>
        JsonSerializer.SerializeToUtf8Bytes(new { id, result });

    private static byte[] Length(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        return bytes;
    }

    private static IEnumerable<byte[]> DataWrites(FakeBleConnection connection) =>
        connection.Writes
            .Where(write => write.Uuid == ShellyBlePlug.RpcDataUuid)
            .Select(write => write.Value);

    private static JsonDocument ParseRequest(byte[] bytes) => JsonDocument.Parse(bytes);

    private static void DisposeAll(IEnumerable<JsonDocument> documents)
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }

    private sealed class QueueConnector : IShellyBleConnector
    {
        private readonly ConcurrentQueue<IShellyBleConnection> _connections;
        private int _activeConnections;
        private int _connectCount;
        private int _maximumActiveConnections;

        public QueueConnector(params IShellyBleConnection[] connections)
        {
            _connections = new ConcurrentQueue<IShellyBleConnection>(connections);
        }

        public Queue<Exception> ConnectErrors { get; set; } = new();
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int MaximumActiveConnections => Volatile.Read(ref _maximumActiveConnections);
        public TaskCompletionSource FirstConnectionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IShellyBleConnection> ConnectAsync(
            ShellyBleOptions options,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCount);
            if (ConnectErrors.Count > 0)
            {
                var error = ConnectErrors.Dequeue();
                return Task.FromException<IShellyBleConnection>(error);
            }
            if (!_connections.TryDequeue(out var connection))
            {
                return Task.FromException<IShellyBleConnection>(
                    new InvalidOperationException("No fake BLE connection is available."));
            }

            var active = Interlocked.Increment(ref _activeConnections);
            UpdateMaximum(active);
            FirstConnectionStarted.TrySetResult();
            return Task.FromResult<IShellyBleConnection>(
                new TrackingConnection(connection, () => Interlocked.Decrement(ref _activeConnections)));
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumActiveConnections);
                if (current >= value ||
                    Interlocked.CompareExchange(ref _maximumActiveConnections, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class TrackingConnection : IShellyBleConnection
    {
        private readonly IShellyBleConnection _inner;
        private readonly Action _onDispose;
        private bool _disposed;

        public TrackingConnection(IShellyBleConnection inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public Task WriteAsync(Guid uuid, ReadOnlyMemory<byte> value, CancellationToken token) =>
            _inner.WriteAsync(uuid, value, token);

        public Task<byte[]> ReadAsync(Guid uuid, CancellationToken token) =>
            _inner.ReadAsync(uuid, token);

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                await _inner.DisposeAsync();
            }
            finally
            {
                _onDispose();
            }
        }
    }

    private sealed class FakeBleConnection : IShellyBleConnection
    {
        private readonly Queue<(Guid Uuid, byte[] Value)> _reads = new();
        private bool _firstRead = true;

        public List<(Guid Uuid, byte[] Value)> Writes { get; } = new();
        public Exception? WriteError { get; set; }
        public Func<Task>? BeforeFirstRead { get; set; }
        public bool Disposed { get; private set; }

        public void EnqueueRead(Guid uuid, byte[] value) => _reads.Enqueue((uuid, value));

        public Task WriteAsync(Guid uuid, ReadOnlyMemory<byte> value, CancellationToken token)
        {
            if (WriteError != null)
            {
                return Task.FromException(WriteError);
            }
            Writes.Add((uuid, value.ToArray()));
            return Task.CompletedTask;
        }

        public async Task<byte[]> ReadAsync(Guid uuid, CancellationToken token)
        {
            if (_firstRead)
            {
                _firstRead = false;
                if (BeforeFirstRead != null)
                {
                    await BeforeFirstRead();
                }
            }

            var next = _reads.Dequeue();
            Assert.Equal(next.Uuid, uuid);
            return next.Value;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTransport : IShellySwitchTransport
    {
        public FakeTransport(ShellyTransport transport)
        {
            Transport = transport;
        }

        public ShellyTransport Transport { get; }
        public bool Output { get; set; }
        public Exception? GetError { get; set; }
        public Exception? SetError { get; set; }
        public int GetCalls { get; private set; }
        public List<bool> SetTargets { get; } = new();

        public Task<bool> GetOutputAsync(CancellationToken cancellationToken)
        {
            GetCalls++;
            return GetError == null ? Task.FromResult(Output) : Task.FromException<bool>(GetError);
        }

        public Task SetOutputAsync(bool on, CancellationToken cancellationToken)
        {
            SetTargets.Add(on);
            return SetError == null ? Task.CompletedTask : Task.FromException(SetError);
        }
    }
}
