using System;
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

namespace ScopeDomeWatchdog.Tray;

public partial class SettingsWindow : Window
{
    private readonly string _configPath;
    private readonly StaTaskRunner _staRunner = new("StaSettings");
    private readonly AscomProfileService _profileService = new();
    private readonly AscomSwitchEnumerator _switchEnumerator;
    private WatchdogConfig? _config;

    public SettingsWindow(string configPath)
    {
        _configPath = configPath;
        _switchEnumerator = new AscomSwitchEnumerator(_staRunner);
        InitializeComponent();
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
        SwitchIdBox.Text = _config.SwitchId.ToString("D");
        OffSecondsBox.Text = _config.OffSeconds.ToString("D");

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
            _config.SwitchId = int.Parse(SwitchIdBox.Text);
            _config.OffSeconds = int.Parse(OffSecondsBox.Text);

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

    private void UpdateAscomTexts()
    {
        DomeProgIdText.Text = _config?.AscomDomeProgId ?? "(none)";
        SwitchProgIdText.Text = _config?.AscomSwitchProgId ?? "(none)";
        var fanLabel = string.IsNullOrWhiteSpace(_config?.FanSwitchName) 
            ? $"{_config?.FanSwitchIndex}" 
            : $"{_config?.FanSwitchIndex} ({_config.FanSwitchName})";
        SubSwitchText.Text = fanLabel;
        var shellyLabel = $"ID {_config?.SwitchId}";
        ShellyRelayText.Text = shellyLabel;
    }

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
            var window = new SelectSwitchWindow(switches);
            window.Owner = this;
            if (window.ShowDialog() == true && window.SelectedSwitch != null)
            {
                if (_config != null)
                {
                    _config.FanSwitchIndex = window.SelectedSwitch.Index;
                    _config.FanSwitchName = window.SelectedSwitch.Name;
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
        var ip = PlugIpBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            WpfMessageBox.Show(this, "Please enter a Shelly device IP address first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var client = new ShellyClient(TimeSpan.FromSeconds(5));
            var relays = await client.EnumerateRelaysAsync(ip, CancellationToken.None);

            if (relays.Count == 0)
            {
                WpfMessageBox.Show(this, "No Shelly relays found at this IP address", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    protected override void OnClosed(EventArgs e)
    {
        _staRunner.Dispose();
        base.OnClosed(e);
    }
}

