using System.Net.Http;
using ScopeDomeWatchdog.Core.Services;
using ScopeDomeWatchdog.Tray.Services;
using Xunit;

namespace ScopeDomeWatchdog.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShellyBleHardwareCollection
{
    public const string Name = "Shelly BLE hardware";
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class BleHardwareFactAttribute : FactAttribute
{
    public BleHardwareFactAttribute(
        bool expectAuthorizationFailure = false,
        bool requiresTcp = false)
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_BLE_HARDWARE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var address = Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_BLE_ADDRESS");
        var configuredForAuthorizationFailure = string.Equals(
            Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_BLE_EXPECT_AUTH_FAILURE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!enabled || string.IsNullOrWhiteSpace(address))
        {
            Skip = "Set SCOPEDOME_SHELLY_BLE_HARDWARE=true and provide a BLE address.";
        }
        else if (configuredForAuthorizationFailure != expectAuthorizationFailure)
        {
            Skip = expectAuthorizationFailure
                ? "Requires SCOPEDOME_SHELLY_BLE_EXPECT_AUTH_FAILURE=true."
                : "Skipped while the explicit authorization-failure test is enabled.";
        }
        else if (requiresTcp &&
                 string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_IP")))
        {
            Skip = "Set SCOPEDOME_SHELLY_IP to run the TCP-primary test.";
        }
    }
}

[Collection(ShellyBleHardwareCollection.Name)]
[Trait("Category", "Hardware")]
public sealed class ShellyBleHardwareTests
{
    [BleHardwareFact]
    public async Task ScanListsConfiguredBleAddress()
    {
        _ = TryGetHardwareOptions(out var options);
        var scanner = new WindowsShellyBleConnector();

        var devices = await scanner.ScanAsync(TimeSpan.FromSeconds(8), CancellationToken.None);

        Assert.Contains(
            devices,
            device => NormalizeAddress(device.Address) == NormalizeAddress(options.Address));
    }

    [BleHardwareFact]
    public async Task BleFallbackTurnsOffAndOnAndRestoresOriginalState()
    {
        _ = TryGetHardwareOptions(out var options);

        using var ble = new ShellyBlePlug(new WindowsShellyBleConnector(), options);
        var original = await ble.GetOutputAsync(CancellationToken.None);
        try
        {
            var controller = new ShellySwitchController(new UnavailableTcpTransport(), ble);
            var off = await controller.SetOutputAsync(false, CancellationToken.None);
            Assert.Equal(ShellyTransport.Ble, off.Transport);
            Assert.False(await ble.GetOutputAsync(CancellationToken.None));

            var on = await controller.SetOutputAsync(true, CancellationToken.None);
            Assert.Equal(ShellyTransport.Ble, on.Transport);
            Assert.True(await ble.GetOutputAsync(CancellationToken.None));
        }
        finally
        {
            await ble.SetOutputAsync(original, CancellationToken.None);
        }
    }

    [BleHardwareFact(requiresTcp: true)]
    public async Task HealthyTcpDoesNotStartBleDiscovery()
    {
        _ = TryGetHardwareOptions(out var options);
        var ip = Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_IP");

        using var client = new ShellyClient(TimeSpan.FromSeconds(5));
        var primary = new ShellyHttpSwitchTransport(client, ip!, options.SwitchId);
        var connector = new CountingConnector(new WindowsShellyBleConnector());
        using var fallback = new ShellyBlePlug(connector, options);
        var current = await primary.GetOutputAsync(CancellationToken.None);
        var controller = new ShellySwitchController(primary, fallback);

        var result = await controller.SetOutputAsync(current, CancellationToken.None);

        Assert.Equal(ShellyTransport.Tcp, result.Transport);
        Assert.Equal(0, connector.ConnectCount);
    }

    [BleHardwareFact(expectAuthorizationFailure: true)]
    public async Task MissingBondReportsProvisioningRequirement()
    {
        _ = TryGetHardwareOptions(out var options);

        using var ble = new ShellyBlePlug(new WindowsShellyBleConnector(), options);
        var error = await Assert.ThrowsAsync<ShellyBleAuthorizationException>(
            () => ble.GetOutputAsync(CancellationToken.None));

        Assert.Contains("bond", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RPC over Bluetooth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHardwareOptions(out ShellyBleOptions options)
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_BLE_HARDWARE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var address = Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_BLE_ADDRESS");
        var switchIdText = Environment.GetEnvironmentVariable("SCOPEDOME_SHELLY_SWITCH_ID");
        _ = int.TryParse(switchIdText, out var switchId);
        options = new ShellyBleOptions(address ?? string.Empty, switchId);
        return enabled && !string.IsNullOrWhiteSpace(address);
    }

    private static string NormalizeAddress(string address) =>
        new(address.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed class UnavailableTcpTransport : IShellySwitchTransport
    {
        public ShellyTransport Transport => ShellyTransport.Tcp;

        public Task<bool> GetOutputAsync(CancellationToken cancellationToken) =>
            Task.FromException<bool>(new HttpRequestException("TCP deliberately unavailable"));

        public Task SetOutputAsync(bool on, CancellationToken cancellationToken) =>
            Task.FromException(new HttpRequestException("TCP deliberately unavailable"));
    }

    private sealed class CountingConnector : IShellyBleConnector
    {
        private readonly IShellyBleConnector _inner;

        public CountingConnector(IShellyBleConnector inner)
        {
            _inner = inner;
        }

        public int ConnectCount { get; private set; }

        public Task<IShellyBleConnection> ConnectAsync(
            ShellyBleOptions options,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            return _inner.ConnectAsync(options, cancellationToken);
        }
    }
}
