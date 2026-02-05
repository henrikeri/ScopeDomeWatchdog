namespace ScopeDomeWatchdog.Core.Services;

public sealed class AscomSwitchInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool? CanWrite { get; init; }
    public bool? State { get; init; }
    public double? Value { get; init; }
}
