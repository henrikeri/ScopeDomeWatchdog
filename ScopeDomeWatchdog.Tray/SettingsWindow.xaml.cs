// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using Microsoft.VisualBasic;
using ScopeDomeWatchdog.Core.Interop;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;
using ScopeDomeWatchdog.Tray.Models;
using ScopeDomeWatchdog.Tray.Services;

namespace ScopeDomeWatchdog.Tray;

public partial class SettingsWindow : Window
{
    private static readonly TimeSpan BleScanDuration = TimeSpan.FromSeconds(8);
    private readonly string _configPath;
    private readonly StaTaskRunner _staRunner = new("StaSettings");
    private readonly AscomProfileService _profileService = new();
    private readonly AscomSwitchEnumerator _switchEnumerator;
    private readonly RestartSequenceService? _restartService;
    private readonly IShellyBleScanner _bleScanner;
    private CancellationTokenSource? _bleScanCancellation;
    private WatchdogConfig? _config;

    public SettingsWindow(
        string configPath,
        RestartSequenceService? restartService = null,
        IShellyBleScanner? bleScanner = null)
    {
        _configPath = configPath;
        _restartService = restartService;
        _bleScanner = bleScanner ?? new WindowsShellyBleConnector();
        _switchEnumerator = new AscomSwitchEnumerator(_staRunner);
        InitializeComponent();
        WindowChromeHelper.ApplyDarkTitleBar(this);
        LoadConfig();
    }

    private void LoadConfig()
    {
        _config = ConfigService.LoadOrCreate(_configPath);
        PopulateFields();
        UpdateAscomTexts();
    }

    private void PopulateFields()
    {
        if (_config == null) return;

        // Monitor
        MonitorIpBox.Text = _config.MonitorIp;
        PingIntervalBox.Text = _config.PingIntervalSec.ToString("D");
        PingTimeoutBox.Text = _config.PingTimeoutMs.ToString("D");
        FailsToTriggerBox.Text = _config.FailsToTrigger.ToString("D");

        // Shelly
        PlugIpBox.Text = _config.PlugIp;
        FallbackPlugIpBox.Text = _config.FallbackPlugIp;
        SwitchIdBox.Text = _config.SwitchId.ToString("D");
        OffSecondsBox.Text = _config.OffSeconds.ToString("D");
        ShellyBleEnabledBox.IsChecked = _config.ShellyBleEnabled;
        ShellyBleAddressBox.Text = _config.ShellyBleAddress;
        ShellyBleNamePrefixBox.Text = _config.ShellyBleExpectedNamePrefix;
        ShellyBleDiscoveryTimeoutBox.Text = _config.ShellyBleDiscoveryTimeoutSec.ToString("D");
        ShellyBleConnectTimeoutBox.Text = _config.ShellyBleConnectTimeoutSec.ToString("D");
        ShellyBleResponseTimeoutBox.Text = _config.ShellyBleResponseTimeoutSec.ToString("D");

        // Timing
        CooldownBox.Text = _config.CooldownSeconds.ToString("D");
        PostCycleGraceBox.Text = _config.PostCycleGraceSec.ToString("D");
        PrePowerWaitBox.Text = _config.PrePowerWaitSec.ToString("D");
        PostPowerActionWaitBox.Text = _config.PostPowerActionWaitSec.ToString("D");
        PostLaunchWaitBox.Text = _config.PostLaunchWaitSec.ToString("D");
        HttpTimeoutBox.Text = _config.HttpTimeoutSec.ToString("D");

        // Dome Process
        DomeProcessNameBox.Text = _config.DomeProcessName;
        DomeExePathBox.Text = _config.DomeExePath;
        DomeConnectTimeoutBox.Text = _config.AscomDomeConnectTimeoutSec.ToString("D");
        FindHomeTimeoutBox.Text = _config.FindHomeTimeoutSec.ToString("D");
        AscomRemoteRestartEnabledCheckBox.IsChecked = _config.AscomRemoteRestartEnabled;
        AscomRemoteBaseUrlBox.Text = _config.AscomRemoteBaseUrl;
        AscomRemoteDomeDeviceNumberBox.Text = _config.AscomRemoteDomeDeviceNumber.ToString("D");
        AscomRemoteRestartTimeoutBox.Text = _config.AscomRemoteRestartTimeoutSec.ToString("D");

        // Dome HTTP / Encoder
        DomeHttpIpBox.Text = _config.DomeHttpIp;
        DomeHttpUsernameBox.Text = _config.DomeHttpUsername;
        DomeHttpPasswordBox.Password = _config.DomeHttpPassword;
        EncoderPollMinutesBox.Text = _config.EncoderPollSeconds.ToString("D");
        
        if (_config.HomeActionMode == HomeActionMode.WriteCachedEncoder)
        {
            HomeActionWriteEncoder.IsChecked = true;
        }
        else
        {
            HomeActionAutoHome.IsChecked = true;
        }

        // Sync & Logging
        MutexNameBox.Text = _config.MutexName;
        TriggerEventNameBox.Text = _config.TriggerEventName;
        LogDirectoryBox.Text = _config.RestartLogDirectory;
    }

    private WatchdogConfig GetCurrentConfig()
    {
        if (_config == null)
            throw new InvalidOperationException("Config not loaded");

        try
        {
            _config.MonitorIp = MonitorIpBox.Text.Trim();
            _config.PingIntervalSec = int.Parse(PingIntervalBox.Text);
            _config.PingTimeoutMs = int.Parse(PingTimeoutBox.Text);
            _config.FailsToTrigger = int.Parse(FailsToTriggerBox.Text);

            _config.PlugIp = PlugIpBox.Text.Trim();
            _config.FallbackPlugIp = FallbackPlugIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(_config.PlugIp))
            {
                throw new FormatException("Plug IP 1 is required.");
            }
            _config.SwitchId = int.Parse(SwitchIdBox.Text);
            _config.OffSeconds = int.Parse(OffSecondsBox.Text);
            _config.ShellyBleEnabled = ShellyBleEnabledBox.IsChecked == true;
            _config.ShellyBleAddress = ShellyBleAddressBox.Text.Trim();
            _config.ShellyBleExpectedNamePrefix = ShellyBleNamePrefixBox.Text.Trim();
            _config.ShellyBleDiscoveryTimeoutSec = ParsePositiveSeconds(
                ShellyBleDiscoveryTimeoutBox.Text,
                "BLE discovery timeout");
            _config.ShellyBleConnectTimeoutSec = ParsePositiveSeconds(
                ShellyBleConnectTimeoutBox.Text,
                "BLE connect timeout");
            _config.ShellyBleResponseTimeoutSec = ParsePositiveSeconds(
                ShellyBleResponseTimeoutBox.Text,
                "BLE RPC timeout");
            if (_config.ShellyBleEnabled && string.IsNullOrWhiteSpace(_config.ShellyBleAddress))
            {
                throw new FormatException("BLE address is required when BLE fallback is enabled.");
            }
            if (_config.ShellyBleEnabled && !IsValidBleAddress(_config.ShellyBleAddress))
            {
                throw new FormatException(
                    "BLE address must contain twelve hexadecimal digits, for example " +
                    "8C:BF:EA:99:9C:DE.");
            }

            _config.CooldownSeconds = int.Parse(CooldownBox.Text);
            _config.PostCycleGraceSec = int.Parse(PostCycleGraceBox.Text);
            _config.PrePowerWaitSec = int.Parse(PrePowerWaitBox.Text);
            _config.PostPowerActionWaitSec = int.Parse(PostPowerActionWaitBox.Text);
            _config.PostLaunchWaitSec = int.Parse(PostLaunchWaitBox.Text);
            _config.HttpTimeoutSec = int.Parse(HttpTimeoutBox.Text);

            _config.DomeProcessName = DomeProcessNameBox.Text.Trim();
            _config.DomeExePath = DomeExePathBox.Text.Trim();
            _config.AscomDomeConnectTimeoutSec = int.Parse(DomeConnectTimeoutBox.Text);
            _config.FindHomeTimeoutSec = int.Parse(FindHomeTimeoutBox.Text);
            _config.AscomRemoteRestartEnabled = AscomRemoteRestartEnabledCheckBox.IsChecked == true;
            _config.AscomRemoteBaseUrl = AscomRemoteBaseUrlBox.Text.Trim();
            _config.AscomRemoteDomeDeviceNumber = int.Parse(AscomRemoteDomeDeviceNumberBox.Text);
            _config.AscomRemoteRestartTimeoutSec = ParsePositiveSeconds(
                AscomRemoteRestartTimeoutBox.Text,
                "ASCOM Remote reload timeout");
            if (_config.AscomRemoteDomeDeviceNumber < 0)
            {
                throw new FormatException("Alpaca Dome Device Number cannot be negative.");
            }
            if (_config.AscomRemoteRestartEnabled &&
                (!Uri.TryCreate(_config.AscomRemoteBaseUrl, UriKind.Absolute, out var remoteUri) ||
                 remoteUri.Scheme is not ("http" or "https")))
            {
                throw new FormatException(
                    "ASCOM Remote URL must be an absolute HTTP or HTTPS URL.");
            }

            _config.DomeHttpIp = DomeHttpIpBox.Text.Trim();
            _config.DomeHttpUsername = DomeHttpUsernameBox.Text.Trim();
            _config.DomeHttpPassword = DomeHttpPasswordBox.Password;
            _config.EncoderPollSeconds = int.Parse(EncoderPollMinutesBox.Text);
            _config.HomeActionMode = HomeActionWriteEncoder.IsChecked == true ? HomeActionMode.WriteCachedEncoder : HomeActionMode.AutoHome;

            _config.MutexName = MutexNameBox.Text.Trim();
            _config.TriggerEventName = TriggerEventNameBox.Text.Trim();
            _config.RestartLogDirectory = LogDirectoryBox.Text.Trim();

            return _config;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"Invalid field values: {ex.Message}", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private static int ParsePositiveSeconds(string text, string fieldName)
    {
        var value = int.Parse(text);
        if (value <= 0)
        {
            throw new FormatException($"{fieldName} must be greater than zero.");
        }

        return value;
    }

    private static bool IsValidBleAddress(string address)
    {
        var compact = address.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();
        return compact.Length == 12 &&
               ulong.TryParse(
                   compact,
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out _);
    }

    private void UpdateAscomTexts()
    {
        DomeProgIdText.Text = _config?.AscomDomeProgId ?? "(none)";
        SwitchProgIdText.Text = _config?.AscomSwitchProgId ?? "(none)";
        
        // Display monitored switches
        if (_config?.MonitoredSwitches != null && _config.MonitoredSwitches.Count > 0)
        {
            var labels = _config.MonitoredSwitches
                .Select(s => string.IsNullOrWhiteSpace(s.Name) ? $"#{s.Index}" : $"#{s.Index} ({s.Name})")
                .ToList();
            SubSwitchText.Text = string.Join(", ", labels);
        }
        else
        {
            SubSwitchText.Text = "(none selected)";
        }
        
        var shellyLabel = $"ID {_config?.SwitchId}";
        ShellyRelayText.Text = shellyLabel;
    }

    private async void ScanBleDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bleScanCancellation != null)
        {
            return;
        }

        _bleScanCancellation = new CancellationTokenSource();
        var cancellationToken = _bleScanCancellation.Token;
        ScanBleDevicesButton.IsEnabled = false;
        ShellyBleDevicesBox.IsEnabled = false;
        ShellyBleDevicesBox.ItemsSource = null;
        ShellyBleScanStatusText.Text =
            $"Scanning for nearby BLE devices for {BleScanDuration.TotalSeconds:0} seconds...";

        try
        {
            var identityTask = TryGetConfiguredShellyIdentityAsync(cancellationToken);
            var devices = await _bleScanner.ScanAsync(BleScanDuration, cancellationToken);
            var identity = await identityTask;
            var enrichedDevices = EnrichWithAssignedName(devices, identity);

            ShellyBleDevicesBox.ItemsSource = enrichedDevices;
            ShellyBleDevicesBox.IsEnabled = enrichedDevices.Count > 0;
            if (enrichedDevices.Count == 0)
            {
                ShellyBleScanStatusText.Text =
                    "No BLE devices were found. Confirm Bluetooth is enabled and the Shelly is advertising.";
            }
            else
            {
                var assignedName = identity?.AssignedName;
                var nameDetails = string.IsNullOrWhiteSpace(assignedName)
                    ? string.Empty
                    : enrichedDevices.Any(device =>
                        string.Equals(device.AssignedName, assignedName, StringComparison.Ordinal))
                        ? $" The configured Shelly is shown as '{assignedName}'."
                        : $" Configured Shelly name: '{assignedName}' (no certain BLE match).";
                ShellyBleScanStatusText.Text =
                    $"Found {enrichedDevices.Count} BLE device(s). Select the Shelly to use.{nameDetails}";
            }
        }
        catch (OperationCanceledException)
        {
            ShellyBleScanStatusText.Text = "BLE scan cancelled.";
        }
        catch (Exception ex)
        {
            ShellyBleScanStatusText.Text = "BLE scan failed.";
            WpfMessageBox.Show(
                this,
                $"Unable to scan for BLE devices: {ex.Message}",
                "BLE Scan Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _bleScanCancellation.Dispose();
            _bleScanCancellation = null;
            ScanBleDevicesButton.IsEnabled = true;
        }
    }

    private void ShellyBleDevicesBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ShellyBleDevicesBox.SelectedItem is not ShellyBleScanResult selected)
        {
            return;
        }

        ShellyBleAddressBox.Text = selected.Address;
        ShellyBleNamePrefixBox.Text = selected.AdvertisedName;
        ShellyBleEnabledBox.IsChecked = true;
        ShellyBleScanStatusText.Text =
            $"Selected {selected.DisplayLabel}. The BLE address has been filled in.";
    }

    private async Task<ShellyDeviceIdentity?> TryGetConfiguredShellyIdentityAsync(
        CancellationToken cancellationToken)
    {
        if (_config == null)
        {
            return null;
        }

        foreach (var ip in GetEnteredPlugIpAddresses())
        {
            try
            {
                var timeoutSeconds = Math.Clamp(_config.HttpTimeoutSec, 1, 10);
                using var client = new ShellyClient(TimeSpan.FromSeconds(timeoutSeconds));
                return await client.GetDeviceIdentityAsync(ip, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Try the next configured Wi-Fi address. BLE scanning remains usable if both fail.
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetEnteredPlugIpAddresses()
    {
        var addresses = new List<string>();
        foreach (var address in new[] { PlugIpBox.Text, FallbackPlugIpBox.Text })
        {
            var trimmed = address.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) &&
                !addresses.Any(existing =>
                    string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                addresses.Add(trimmed);
            }
        }

        return addresses;
    }

    internal static IReadOnlyList<ShellyBleScanResult> EnrichWithAssignedName(
        IReadOnlyList<ShellyBleScanResult> devices,
        ShellyDeviceIdentity? identity)
    {
        if (identity == null || string.IsNullOrWhiteSpace(identity.AssignedName))
        {
            return devices;
        }

        var mac = NormalizeIdentifier(identity.MacAddress);
        var macSuffix = mac.Length >= 6 ? mac[^6..] : mac;
        var deviceIdSuffix = NormalizeIdentifier(
            identity.DeviceId?.Split('-').LastOrDefault() ?? string.Empty);
        var matches = devices
            .Where(device =>
            {
                var address = NormalizeIdentifier(device.Address);
                var advertisedName = NormalizeIdentifier(device.AdvertisedName);
                return address == mac ||
                       (!string.IsNullOrEmpty(macSuffix) && advertisedName.EndsWith(macSuffix)) ||
                       (deviceIdSuffix.Length >= 6 && advertisedName.EndsWith(deviceIdSuffix));
            })
            .ToList();

        if (matches.Count != 1)
        {
            return devices;
        }

        var matchedAddress = matches[0].Address;
        return devices
            .Select(device =>
                string.Equals(device.Address, matchedAddress, StringComparison.OrdinalIgnoreCase)
                    ? device with { AssignedName = identity.AssignedName }
                    : device)
            .ToList();
    }

    private static string NormalizeIdentifier(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private void SelectDomeButton_Click(object sender, RoutedEventArgs e)
    {
        var devices = _profileService.GetRegisteredDevices("Dome")
            .Select(d => new AscomDeviceItem { Name = d.Name, ProgId = d.ProgId })
            .ToList();

        if (devices.Count == 0)
        {
            WpfMessageBox.Show(this, "No ASCOM Dome devices found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new SelectAscomDeviceWindow(devices);
        window.Owner = this;
        if (window.ShowDialog() == true && window.SelectedItem != null)
        {
            if (_config != null)
            {
                _config.AscomDomeProgId = window.SelectedItem.ProgId;
                UpdateAscomTexts();
            }
        }
    }

    private void SelectSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        var devices = _profileService.GetRegisteredDevices("Switch")
            .Select(d => new AscomDeviceItem { Name = d.Name, ProgId = d.ProgId })
            .ToList();

        if (devices.Count == 0)
        {
            WpfMessageBox.Show(this, "No ASCOM Switch devices found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new SelectAscomDeviceWindow(devices);
        window.Owner = this;
        if (window.ShowDialog() == true && window.SelectedItem != null)
        {
            if (_config != null)
            {
                _config.AscomSwitchProgId = window.SelectedItem.ProgId;
                UpdateAscomTexts();
            }
        }
    }

    private async void SelectSubSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_config?.AscomSwitchProgId))
        {
            WpfMessageBox.Show(this, "Please select a Switch Driver first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var switches = await _switchEnumerator.GetSwitchesAsync(_config.AscomSwitchProgId, CancellationToken.None);
            var window = new SelectSwitchWindow(switches, _config.MonitoredSwitches);
            window.Owner = this;
            if (window.ShowDialog() == true)
            {
                if (_config != null)
                {
                    _config.MonitoredSwitches.Clear();
                    _config.MonitoredSwitches.AddRange(window.SelectedSwitches);
                    UpdateAscomTexts();
                }
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DetectShellyRelaysButton_Click(object sender, RoutedEventArgs e)
    {
        var plugIpAddresses = GetEnteredPlugIpAddresses();
        if (plugIpAddresses.Count == 0)
        {
            WpfMessageBox.Show(this, "Please enter a Shelly device IP address first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var client = new ShellyClient(TimeSpan.FromSeconds(5));
            List<(int id, string name)>? relays = null;
            foreach (var ip in plugIpAddresses)
            {
                try
                {
                    var detected = await client.EnumerateRelaysAsync(ip, CancellationToken.None);
                    if (detected.Count > 0)
                    {
                        relays = detected;
                        break;
                    }
                }
                catch
                {
                    // Continue with Plug IP 2 when Plug IP 1 is unavailable.
                }
            }

            if (relays == null || relays.Count == 0)
            {
                WpfMessageBox.Show(this, "No Shelly relays found at either configured IP address", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var relayItems = relays.Select(r => new ShellyRelayItem { Id = r.id, Name = r.name, IsAvailable = true }).ToList();
            var window = new SelectShellyRelayWindow(relayItems);
            window.Owner = this;
            if (window.ShowDialog() == true && window.SelectedRelay != null)
            {
                if (_config != null)
                {
                    _config.SwitchId = window.SelectedRelay.Id;
                    SwitchIdBox.Text = _config.SwitchId.ToString("D");
                    UpdateAscomTexts();
                }
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"Error detecting Shelly relays: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        LoadConfig();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetCurrentConfig();
            ConfigService.Save(config, _configPath);
            _restartService?.ApplyConnectionSettings(config);
            WpfMessageBox.Show(this, "Configuration saved successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            // Error already shown in GetCurrentConfig
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ClearRestartHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_restartService == null)
        {
            WpfMessageBox.Show(this, "Restart history service not available", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var result = WpfMessageBox.Show(this, "Clear all restart history? This cannot be undone.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var historyService = _restartService.GetHistoryService;
            historyService.ClearHistory();
            WpfMessageBox.Show(this, "Restart history cleared successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"Failed to clear history: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _bleScanCancellation?.Cancel();
        _staRunner.Dispose();
        base.OnClosed(e);
    }
}
