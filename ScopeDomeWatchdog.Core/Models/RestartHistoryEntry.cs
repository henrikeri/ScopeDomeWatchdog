namespace ScopeDomeWatchdog.Core.Models;

public sealed class RestartHistoryEntry
{
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public bool Success { get; set; }
    public string? TriggerReason { get; set; }
    public string? ErrorMessage { get; set; }

    public TimeSpan? Duration => EndTimeUtc.HasValue ? EndTimeUtc.Value - StartTimeUtc : null;
    public string DurationDisplay => Duration.HasValue ? $"{Duration.Value.TotalSeconds:0}s" : "in progress";
}
