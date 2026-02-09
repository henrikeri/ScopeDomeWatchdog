using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfMessageBox = System.Windows.MessageBox;
using ScopeDomeWatchdog.Core.Interop;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;

namespace ScopeDomeWatchdog.Tray;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly WatchdogRunner _runner;
    private readonly WatchdogConfig _config;
    private readonly string _configPath;
    private readonly RestartSequenceService _restartService;
    private readonly StaTaskRunner _staRunner;
    private readonly AscomConnectionTester _ascomTester;
    private readonly Action<WatchdogConfig>? _onConfigUpdated;
    private FileSystemWatcher? _logFileWatcher;
    private readonly DispatcherTimer _logRefreshTimer;
    private long _lastLogPosition = 0;

    public bool IsExitRequested { get; set; }

    public MainWindow(WatchdogRunner runner, WatchdogConfig config, string configPath, RestartSequenceService restartService, Action<WatchdogConfig>? onConfigUpdated = null)
    {
        _runner = runner;
        _config = config;
        _configPath = configPath;
        _restartService = restartService;
        _onConfigUpdated = onConfigUpdated;
        _staRunner = new StaTaskRunner("StaTester");
        _ascomTester = new AscomConnectionTester(_staRunner);

        InitializeComponent();
        WindowChromeHelper.ApplyDarkTitleBar(this);
        UpdateConfigSummary();
        RefreshLogs();
        UpdateResetStatistics();

        _runner.StatusUpdated += status => Dispatcher.Invoke(() => UpdateStatus(status));
        _runner.RunningStateChanged += isRunning => Dispatcher.Invoke(() => UpdateRunningState(isRunning));
        _runner.SwitchStatesCached += states => Dispatcher.Invoke(() => UpdateSwitchCacheDisplay(states));
        UpdateRunningState(_runner.IsRunning);

        // Initialize live log viewer
        _logRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _logRefreshTimer.Tick += (s, e) => UpdateLiveLog();
        _logRefreshTimer.Start();

        SetupLogFileWatcher();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _config.RestartLogDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    public void OpenSettings()
    {
        var settings = new SettingsWindow(_configPath, _restartService);
        settings.Owner = this;
        settings.ShowDialog();

        var refreshed = ConfigService.LoadOrCreate(_configPath);
        var oldEventName = _config.TriggerEventName;
        _config.MonitorIp = refreshed.MonitorIp;
        _config.PingIntervalSec = refreshed.PingIntervalSec;
        _config.PingTimeoutMs = refreshed.PingTimeoutMs;
        _config.FailsToTrigger = refreshed.FailsToTrigger;
        _config.LatencyWindow = refreshed.LatencyWindow;
        _config.PlugIp = refreshed.PlugIp;
        _config.SwitchId = refreshed.SwitchId;
        _config.OffSeconds = refreshed.OffSeconds;
        _config.CooldownSeconds = refreshed.CooldownSeconds;
        _config.PostCycleGraceSec = refreshed.PostCycleGraceSec;
        _config.PrePowerWaitSec = refreshed.PrePowerWaitSec;
        _config.PostPowerActionWaitSec = refreshed.PostPowerActionWaitSec;
        _config.PostLaunchWaitSec = refreshed.PostLaunchWaitSec;
        _config.DomeProcessName = refreshed.DomeProcessName;
        _config.DomeExePath = refreshed.DomeExePath;
        _config.AscomDomeProgId = refreshed.AscomDomeProgId;
        _config.AscomDomeConnectTimeoutSec = refreshed.AscomDomeConnectTimeoutSec;
        _config.AscomDomeConnectRetrySec = refreshed.AscomDomeConnectRetrySec;
        _config.FindHomeTimeoutSec = refreshed.FindHomeTimeoutSec;
        _config.FindHomePollMs = refreshed.FindHomePollMs;
        _config.AscomSwitchProgId = refreshed.AscomSwitchProgId;
        _config.MonitoredSwitches.Clear();
        _config.MonitoredSwitches.AddRange(refreshed.MonitoredSwitches);
        _config.SwitchCacheIntervalSec = refreshed.SwitchCacheIntervalSec;
#pragma warning disable CS0618 // Type or member is obsolete
        _config.FanSwitchIndex = refreshed.FanSwitchIndex;
#pragma warning restore CS0618
        _config.AscomSwitchConnectTimeoutSec = refreshed.AscomSwitchConnectTimeoutSec;
        _config.AscomSwitchConnectRetrySec = refreshed.AscomSwitchConnectRetrySec;
        _config.FanEnsureTimeoutSec = refreshed.FanEnsureTimeoutSec;
        _config.MutexName = refreshed.MutexName;
        _config.TriggerEventName = refreshed.TriggerEventName;
        _config.HttpTimeoutSec = refreshed.HttpTimeoutSec;
        _config.RestartLogDirectory = refreshed.RestartLogDirectory;
        _config.StartMinimizedToTray = refreshed.StartMinimizedToTray;
#pragma warning disable CS0618 // Type or member is obsolete
        _config.FanSwitchName = refreshed.FanSwitchName;
#pragma warning restore CS0618
        _config.DomeHttpIp = refreshed.DomeHttpIp;
        _config.DomeHttpUsername = refreshed.DomeHttpUsername;
        _config.DomeHttpPassword = refreshed.DomeHttpPassword;
        _config.EncoderPollSeconds = refreshed.EncoderPollSeconds;
        _config.HomeActionMode = refreshed.HomeActionMode;
        _config.CachedEncoderValue = refreshed.CachedEncoderValue;
        _config.CachedEncoderUtc = refreshed.CachedEncoderUtc;

        if (!string.Equals(oldEventName, _config.TriggerEventName, StringComparison.Ordinal))
        {
            _runner.UpdateTriggerEventName(_config.TriggerEventName);
        }

        _onConfigUpdated?.Invoke(_config);

        UpdateConfigSummary();
    }

    private void UpdateConfigSummary()
    {
        var switchesLabel = _config.MonitoredSwitches.Count > 0 
            ? $"{_config.MonitoredSwitches.Count} switches" 
            : "none";
        ConfigText.Text = $"Monitor IP: {_config.MonitorIp} | Shelly IP: {_config.PlugIp} | Dome ProgID: {_config.AscomDomeProgId} | Switch ProgID: {_config.AscomSwitchProgId} | Monitored: {switchesLabel} | Home Action: {_config.HomeActionMode}";
    }

    private void UpdateStatus(WatchdogStatus status)
    {
        var avg = status.AveragePingMs.HasValue ? $"{status.AveragePingMs:0.0}ms" : "n/a";
        var last = status.LastPingMs.HasValue ? $"{status.LastPingMs}ms" : "--";
        var okPct = status.TotalPings == 0 ? 0 : Math.Round(100.0 * status.OkPings / status.TotalPings, 1);

        StatusText.Text = $"Ping: {(status.LastPingOk ? "OK" : "FAIL")} | Last: {last} | Avg: {avg} | Consecutive fails: {status.ConsecutiveFails} | OK: {status.OkPings}/{status.TotalPings} ({okPct}%)";
        CooldownText.Text = status.CooldownRemaining.HasValue ? $"Cooldown remaining: {status.CooldownRemaining.Value.TotalSeconds:0}s" : "Cooldown: ready";
        LastRestartText.Text = status.LastRestartUtc.HasValue ? $"Last restart: {status.LastRestartUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "Last restart: never";
        var uptime = DateTime.Now - status.StartTimeUtc;
        UptimeText.Text = $"Uptime: {uptime.ToString(@"dd\.hh\:mm\:ss")}";

        if (_config.CachedEncoderValue.HasValue)
        {
            var ts = _config.CachedEncoderUtc.HasValue ? _config.CachedEncoderUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "unknown time";
            EncoderCacheText.Text = $"Cached encoder: {_config.CachedEncoderValue.Value} ({ts})";
        }
        else
        {
            EncoderCacheText.Text = "Cached encoder: n/a";
        }

        UpdateResetStatistics();

        UpdateResetStatistics();
    }

    private void UpdateResetStatistics()
    {
        try
        {
            var historyService = _restartService.GetHistoryService;
            var totalResets = historyService.GetTotalRestarts();
            TotalResetsText.Text = $"Total resets: {totalResets}";

            var recentHistory = historyService.GetRecentHistory(1);
            if (recentHistory.Count > 0)
            {
                var lastReset = recentHistory[0];
                var elapsed = DateTime.UtcNow - lastReset.StartTimeUtc;
                TimeSinceLastResetText.Text = $"Last reset: {FormatElapsedTime(elapsed)} ago";
            }
            else
            {
                TimeSinceLastResetText.Text = "Last reset: never";
            }
        }
        catch
        {
            TotalResetsText.Text = "Total resets: --";
            TimeSinceLastResetText.Text = "Last reset: --";
        }
    }

    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"{(int)elapsed.TotalSeconds}s";
    }

    public void RefreshEncoderDisplay()
    {
        if (_config.CachedEncoderValue.HasValue)
        {
            var ts = _config.CachedEncoderUtc.HasValue ? _config.CachedEncoderUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "unknown time";
            EncoderCacheText.Text = $"Cached encoder: {_config.CachedEncoderValue.Value} ({ts})";
        }
        else
        {
            EncoderCacheText.Text = "Cached encoder: n/a";
        }
    }

    private void UpdateRunningState(bool isRunning)
    {
        StartStopButton.Content = isRunning ? "Stop Watchdog" : "Start Watchdog";
        StartStopButton.Background = isRunning ? System.Windows.Media.Brushes.Crimson : System.Windows.Media.Brushes.Green;
        StatusIndicator.Text = isRunning ? "Status: Running" : "Status: Stopped";
        StatusIndicator.Foreground = isRunning ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Orange;
    }

    private void RefreshLogs()
    {
        try
        {
            var dir = new DirectoryInfo(_config.RestartLogDirectory);
            if (!dir.Exists)
            {
                LogsList.Items.Clear();
                return;
            }

            var files = dir.GetFiles("ScopeDomeRestart_*.log")
                .OrderByDescending(f => f.CreationTime)
                .Take(10)
                .ToList();

            LogsList.Items.Clear();
            foreach (var file in files)
            {
                LogsList.Items.Add($"{file.CreationTime:yyyy-MM-dd HH:mm:ss} - {file.Name}");
            }
        }
        catch
        {
            LogsList.Items.Clear();
        }
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runner.IsRunning)
        {
            _runner.Stop();
        }
        else
        {
            _runner.Start();
        }
    }

    private void RefreshLogsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogs();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void TestAscomButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunAscomTestAsync();
    }

    private async Task RunAscomTestAsync()
    {
        TestAscomButton.IsEnabled = false;
        try
        {
            var result = await _ascomTester.TestAsync(_config.AscomDomeProgId, _config.AscomSwitchProgId, 30, CancellationToken.None);
            WpfMessageBox.Show(this, result, "ASCOM Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "ASCOM Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestAscomButton.IsEnabled = true;
        }
    }

    public void OpenHealthDashboard()
    {
        var historyService = _restartService.GetHistoryService;
        var dashboard = new HealthDashboardWindow(_runner, historyService);
        dashboard.Owner = this;
        dashboard.ShowDialog();
    }

    protected override void OnClosed(EventArgs e)
    {
        _logRefreshTimer?.Stop();
        _logFileWatcher?.Dispose();
        _staRunner.Dispose();
        base.OnClosed(e);
    }

    private void SetupLogFileWatcher()
    {
        try
        {
            var logDir = _config.RestartLogDirectory;
            Directory.CreateDirectory(logDir);

            _logFileWatcher = new FileSystemWatcher(logDir)
            {
                Filter = "ScopeDomeWatchdog.log",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _logFileWatcher.Changed += (s, e) => Dispatcher.BeginInvoke(() => UpdateLiveLog());
        }
        catch
        {
            // If watcher fails, timer will still work
        }
    }

    private void UpdateLiveLog()
    {
        try
        {
            var logPath = Path.Combine(_config.RestartLogDirectory, "ScopeDomeWatchdog.log");
            if (!File.Exists(logPath))
            {
                return;
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= _lastLogPosition)
            {
                // File was truncated or cleared
                if (stream.Length < _lastLogPosition)
                {
                    LiveLogTextBox.Text = "";
                    _lastLogPosition = 0;
                }
                return;
            }

            stream.Seek(_lastLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var newContent = reader.ReadToEnd();
            
            LiveLogTextBox.AppendText(newContent);
            _lastLogPosition = stream.Position;

            // Auto-scroll to bottom
            LiveLogScroller.ScrollToBottom();

            // Limit log size in memory (keep last 10000 lines)
            var lines = LiveLogTextBox.Text.Split('\n');
            if (lines.Length > 10000)
            {
                LiveLogTextBox.Text = string.Join('\n', lines.Skip(lines.Length - 10000));
            }
        }
        catch
        {
            // Ignore file access errors (file might be locked)
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(_config.RestartLogDirectory, "ScopeDomeWatchdog.log");
            if (File.Exists(logPath))
            {
                File.WriteAllText(logPath, "");
                LiveLogTextBox.Text = "";
                _lastLogPosition = 0;
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"Failed to clear log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateSwitchCacheDisplay(IReadOnlyList<ScopeDomeWatchdog.Core.Services.CachedSwitchState> states)
    {
        if (states == null || states.Count == 0)
        {
            SwitchCacheText.Text = "Switch Cache: n/a";
            return;
        }

        var stateStrings = states.Select(s => $"{s.Name}: {(s.State == true ? "ON" : s.State == false ? "OFF" : "?")}");
        SwitchCacheText.Text = $"Switches: {string.Join(" | ", stateStrings)}";
    }
}