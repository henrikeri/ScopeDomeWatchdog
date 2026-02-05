namespace ScopeDomeWatchdog.Tray.Models;

public sealed class ShellyRelayItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }

    public string Display => $"Relay {Id}: {Name}" + (IsAvailable ? "" : " (unavailable)");
}
