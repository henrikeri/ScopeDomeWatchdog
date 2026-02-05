using System;

namespace ScopeDomeWatchdog.Core.Models;

public sealed class WatchdogConfig
{
    public string MonitorIp { get; set; } = "192.168.6.100";
    public int PingIntervalSec { get; set; } = 1;
    public int PingTimeoutMs { get; set; } = 900;
    public int FailsToTrigger { get; set; } = 5;
    public int LatencyWindow { get; set; } = 60;

    public string PlugIp { get; set; } = "192.168.61.45";
    public int SwitchId { get; set; } = 0;
    public int OffSeconds { get; set; } = 15;

    public int CooldownSeconds { get; set; } = 120;
    public int PostCycleGraceSec { get; set; } = 30;

    public int PrePowerWaitSec { get; set; } = 30;
    public int PostPowerActionWaitSec { get; set; } = 30;
    public int PostLaunchWaitSec { get; set; } = 30;

    public string DomeProcessName { get; set; } = "ASCOM.ScopeDomeUSBDome";
    public string DomeExePath { get; set; } = "C:\\ScopeDome\\Driver_LS\\ASCOM.ScopeDomeUSBDome.exe";

    public string AscomDomeProgId { get; set; } = "ASCOM.ScopeDomeUSBDome.DomeLS";
    public int AscomDomeConnectTimeoutSec { get; set; } = 180;
    public int AscomDomeConnectRetrySec { get; set; } = 5;
    public int FindHomeTimeoutSec { get; set; } = 900;
    public int FindHomePollMs { get; set; } = 500;

    public string AscomSwitchProgId { get; set; } = "ASCOM.ScopeDomeUSBDome.DomeLS.Switch";
    public int FanSwitchIndex { get; set; } = 18;
    public string? FanSwitchName { get; set; }
    public int AscomSwitchConnectTimeoutSec { get; set; } = 60;
    public int AscomSwitchConnectRetrySec { get; set; } = 3;
    public int FanEnsureTimeoutSec { get; set; } = 30;

    public string MutexName { get; set; } = "ScopeDomePowerCycleLock";
    public string TriggerEventName { get; set; } = "ScopeDomeWatchdog.TriggerRestart";

    public int HttpTimeoutSec { get; set; } = 5;
    public string RestartLogDirectory { get; set; } = string.Empty;

    public string DomeHttpIp { get; set; } = "192.168.6.100";
    public string DomeHttpUsername { get; set; } = "scopedome";
    public string DomeHttpPassword { get; set; } = "default";
    public int EncoderPollSeconds { get; set; } = 300;
    public HomeActionMode HomeActionMode { get; set; } = HomeActionMode.AutoHome;
    public int? CachedEncoderValue { get; set; }
    public DateTime? CachedEncoderUtc { get; set; }

    public bool StartMinimizedToTray { get; set; } = true;
}

public enum HomeActionMode
{
    AutoHome = 0,
    WriteCachedEncoder = 1
}
