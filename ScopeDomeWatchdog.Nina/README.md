# ScopeDomeWatchdog Nina Plugin Integration

This plugin enables seamless integration between the ScopeDomeWatchdog dome controller and NINA's advanced sequencer. When the watchdog detects a dome connection issue and triggers a reconnection, the plugin automatically pauses the imaging sequence, waits for the reconnection to complete, and then resumes the sequence.

## Features

- **Automatic Pause/Resume**: Pauses the NINA sequencer when dome reconnection is needed
- **Real-time Status Updates**: Provides progress feedback during dome reconnection
- **Timeout Protection**: Automatically fails gracefully if reconnection takes too long (5 minutes)
- **Error Handling**: Notifies NINA if reconnection fails
- **Event-based Communication**: Uses Windows event handles for reliable IPC between applications

## Installation

### Prerequisites

- NINA 3.0 or later
- .NET 8.0 runtime
- ScopeDomeWatchdog 1.0 or later running on the same machine

### Installation Steps

1. Build the `ScopeDomeWatchdog.Nina` project in Release mode
2. Locate the output DLL: `ScopeDomeWatchdog.Nina.dll`
3. Copy it to your NINA plugins directory:
   ```
   %APPDATA%\NINA\Plugins\ScopeDomeWatchdog\
   ```
   (Create the directory if it doesn't exist)
4. Restart NINA
5. The plugin should appear in NINA's plugin list

## Usage in Advanced Sequencer

### Adding the Instruction

1. Open the NINA Advanced Sequencer
2. Create a new instruction in your sequence
3. Under the "ScopeDome" category, select "Dome Reconnection Pause"
4. This instruction can be placed anywhere in your sequence where you want to monitor dome status

### How It Works

When the instruction executes:

1. **Check Status**: The instruction checks if dome reconnection is in progress
2. **Wait & Pause**: If reconnection is detected, the sequencer pauses automatically
3. **Monitor Progress**: Shows progress indicators while waiting (updates every 500ms)
4. **Resume**: Once reconnection completes successfully, the sequencer resumes automatically
5. **Timeout**: If reconnection doesn't complete within 5 minutes, the instruction fails with an error

### Example Sequence Flow

```
1. Capture Images
2. [Dome Reconnection Pause] <- INSERT INSTRUCTION HERE
3. Calibrate Focus
4. Resume Imaging
```

## Communication Architecture

The plugin and watchdog communicate via Windows Named Events:

### Event Names

- `ScopeDome_ReconnectionStarted`: Signals that dome reconnection has begun
- `ScopeDome_ReconnectionComplete`: Signals that reconnection succeeded
- `Nina_PauseRequested`: Nina signals the watchdog it's paused
- `Nina_ResumeRequested`: Nina requests resumption after reconnection

### Timeout Behavior

- **Plugin Timeout**: 5 minutes (300 seconds)
- If reconnection exceeds this, the instruction fails and reports an error
- Can be customized in `DomeReconnectionPauseInstruction.cs` by changing `RECONNECTION_TIMEOUT_SEC`

## Configuration

### In ScopeDomeWatchdog Tray App

No additional configuration needed - the plugin service is automatically initialized when the app starts.

### In NINA

The instruction has no user-configurable parameters. It automatically:
- Detects dome reconnection status
- Manages pause/resume coordination
- Reports progress to NINA

## Troubleshooting

### Plugin Not Appearing in NINA

1. Verify the DLL is in the correct plugins directory
2. Check that the DLL is not blocked (Right-click → Properties → Unblock)
3. Ensure the plugin can access the NINA SDK assemblies
4. Check NINA's plugin manager for error messages

### Sequencer Not Pausing When Dome Reconnects

1. Verify ScopeDomeWatchdog is running
2. Check that the watchdog is detecting the network issue
3. Confirm the instruction is placed at the correct point in your sequence
4. Review logs in NINA for error messages

### Timeout During Reconnection

1. Check ScopeDomeWatchdog logs to see if reconnection is actually completing
2. The timeout can be increased by modifying `RECONNECTION_TIMEOUT_SEC` in the source
3. Verify network connectivity to all required devices (dome, power switch, ASCOM services)

## API Reference

### INinaPluginService Interface

```csharp
public interface INinaPluginService
{
    // Properties
    bool IsReconnectingDome { get; }
    bool IsNinaPaused { get; }

    // Methods
    Task SignalReconnectionStartAsync();
    Task SignalReconnectionCompleteAsync();
    Task SignalReconnectionFailedAsync(string reason);
    Task<bool> WaitForNinaPauseAsync(TimeSpan timeout);
    Task SignalNinaResumeAsync();

    // Events
    event EventHandler<DomeReconnectionStateChangedEventArgs>? ReconnectionStateChanged;
}
```

## Development

### Building from Source

```bash
dotnet build ScopeDomeWatchdog.Nina.csproj
```

### Project Structure

- `ScopeDomeWatchdog.Nina.csproj`: Main plugin project file
- `ScopeDomeWatchdogPlugin.cs`: Plugin entry point and metadata
- `SequenceItems/DomeReconnectionPauseInstruction.cs`: The sequencer instruction
- `ViewModels/`: UI view models (if adding configuration UI)

### Extending the Plugin

To add more instructions or features:

1. Create new classes in `SequenceItems/` directory
2. Export them with the `[Export(typeof(ISequenceItem))]` attribute
3. Implement the `ISequenceItem` interface
4. Rebuild and test in NINA

## Logging

The plugin logs all status changes to NINA's notification system:

- **Information**: Status checks, pause/resume signals
- **Success**: Dome reconnection complete
- **Warning**: Timeout or cancellation
- **Error**: Reconnection failures

Check NINA's notification history for detailed logs.

## Performance Considerations

- **Memory**: Minimal - uses only event handles for IPC
- **CPU**: Negligible - blocking wait on events during pause
- **Disk**: No logging beyond NINA's standard notification system
- **Network**: No network overhead beyond existing dome communication

## Limitations

Current version limitations:

- Single dome unit per machine
- Fixed 5-minute timeout
- No GUI configuration in NINA settings yet
- Requires both apps to be on the same machine

## Future Enhancements

Planned improvements:

- [ ] Configurable timeout via NINA settings UI
- [ ] Support for multiple domes
- [ ] Detailed statistical logging
- [ ] Network-based communication for remote operation
- [ ] Integration with NINA's alert system

## Support

For issues or questions:

1. Check the troubleshooting section above
2. Review logs in both ScopeDomeWatchdog and NINA
3. File an issue on the project repository
4. Include logs and error messages when reporting issues

## License

This plugin is part of the ScopeDomeWatchdog project and follows the same license.

---

**Version**: 1.0.0  
**Last Updated**: February 5, 2026  
**Compatibility**: NINA 3.0+, .NET 8.0+
