using System;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class WatchdogStatus
{
    public DateTime StartTimeUtc { get; set; } = DateTime.Now;
    public bool LastPingOk { get; set; }
    public int? LastPingMs { get; set; }
    public double? AveragePingMs { get; set; }
    public int ConsecutiveFails { get; set; }
    public int TotalPings { get; set; }
    public int OkPings { get; set; }
    public DateTime? LastRestartUtc { get; set; }
    public bool ManualTriggerSet { get; set; }
    public TimeSpan? CooldownRemaining { get; set; }

    public WatchdogStatus Clone()
    {
        return (WatchdogStatus)MemberwiseClone();
    }
}
