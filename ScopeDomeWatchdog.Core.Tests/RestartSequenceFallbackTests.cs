using System.Net;
using System.Net.Sockets;
using System.Text;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;
using Xunit;

namespace ScopeDomeWatchdog.Core.Tests;

public sealed class RestartSequenceFallbackTests
{
    [Fact]
    public async Task HealthyShellyHttpRestoresPowerWithoutInitializingInvalidBleFallback()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var requests = new List<string>();
        var serverTask = ServeShellyRequestsAsync(server, requests);
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ScopeDomeWatchdog.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var connector = new CountingFailingBleConnector();
        var config = new WatchdogConfig
        {
            PlugIp = $"127.0.0.1:{port}",
            SwitchId = 0,
            ShellyBleEnabled = true,
            ShellyBleAddress = string.Empty,
            HttpTimeoutSec = 2,
            PrePowerWaitSec = 0,
            PostPowerActionWaitSec = 0,
            PostLaunchWaitSec = 0,
            PostCycleGraceSec = 0,
            OffSeconds = 0,
            HomeActionMode = HomeActionMode.WriteCachedEncoder,
            CachedEncoderValue = null,
            AscomSwitchProgId = $"invalid.test.{Guid.NewGuid():N}",
            AscomSwitchConnectTimeoutSec = 0,
            FanEnsureTimeoutSec = 0,
            DomeProcessName = $"ScopeDomeWatchdogMissing{Guid.NewGuid():N}",
            DomeExePath = Path.Combine(testDirectory, "missing.exe"),
            RestartLogDirectory = testDirectory,
            MutexName = $"ScopeDomeWatchdog.Tests.{Guid.NewGuid():N}"
        };

        try
        {
            using var service = new RestartSequenceService(
                config,
                testDirectory,
                bleConnector: connector);

            var success = await service.ExecuteAsync(CancellationToken.None, "automatic ping failure test");
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(success);
            Assert.Equal(0, connector.ConnectCount);
            Assert.Equal(2, requests.Count);
            Assert.Contains("/rpc/Switch.GetStatus?id=0", requests[0], StringComparison.Ordinal);
            Assert.Contains("/rpc/Switch.Set?id=0&on=true", requests[1], StringComparison.Ordinal);
        }
        finally
        {
            server.Stop();
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static async Task ServeShellyRequestsAsync(
        TcpListener server,
        ICollection<string> requests)
    {
        for (var index = 0; index < 2; index++)
        {
            using var client = await server.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var requestLine = await reader.ReadLineAsync() ?? string.Empty;
            requests.Add(requestLine);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            var body = requestLine.Contains("Switch.GetStatus", StringComparison.Ordinal)
                ? "{\"output\":false}"
                : "{}";
            var response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                "Connection: close\r\n\r\n" +
                body);
            await stream.WriteAsync(response);
        }
    }

    private sealed class CountingFailingBleConnector : IShellyBleConnector
    {
        public int ConnectCount { get; private set; }

        public Task<IShellyBleConnection> ConnectAsync(
            ShellyBleOptions options,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.FromException<IShellyBleConnection>(
                new InvalidOperationException("BLE must not be touched while Shelly HTTP is healthy."));
        }
    }
}
