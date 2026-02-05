namespace ScopeDomeWatchdog.Tray.Models;

public sealed class AscomDeviceItem
{
    public string Name { get; init; } = string.Empty;
    public string ProgId { get; init; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(Name) ? ProgId : $"{Name} ({ProgId})";
}
