# Nina Plugin - Quick Start Guide

## 30-Second Overview

✅ **What was built**: A NINA advanced sequencer plugin that automatically pauses your imaging sequence when dome reconnection is needed, then resumes when complete.

✅ **How it works**: When ScopeDomeWatchdog detects a dome connection failure, it signals NINA to pause. After reconnecting the dome, it signals NINA to resume automatically.

✅ **Status**: Production-ready and fully integrated

## Quick Build & Deploy

### Step 1: Build the Plugin

```powershell
cd "c:\Users\riise\Documents\DomeCheck\DomeCheck\Workspace"
dotnet build ScopeDomeWatchdog.Nina -c Release
```

### Step 2: Copy to NINA

```powershell
# Create plugin directory if needed
$ninaPluginsDir = "$env:APPDATA\NINA\Plugins\ScopeDomeWatchdog"
New-Item -ItemType Directory -Force -Path $ninaPluginsDir

# Copy built plugin
Copy-Item "ScopeDomeWatchdog.Nina\bin\Release\net8.0-windows\ScopeDomeWatchdog.Nina.dll" `
         -Destination $ninaPluginsDir
```

### Step 3: Use in NINA

1. Restart NINA
2. Open Advanced Sequencer
3. Add instruction → Category: "ScopeDome" → "Dome Reconnection Pause"
4. Place in sequence where you want monitoring
5. Save and run

## Files Created

### New Project
- `ScopeDomeWatchdog.Nina/` - Complete Nina plugin project

### Core Service
- `ScopeDomeWatchdog.Core/Services/NinaPluginService.cs` - IPC service

### Documentation
- `NINA_PLUGIN_IMPLEMENTATION.md` - Full technical documentation
- `ScopeDomeWatchdog.Nina/README.md` - User guide
- `ScopeDomeWatchdog.Nina/INTEGRATION_EXAMPLES.cs` - Code examples

### Modified Files
- `ScopeDomeWatchdog.sln` - Added Nina project
- `ScopeDomeWatchdog.Core/Services/RestartSequenceService.cs` - Added Nina signaling
- `ScopeDomeWatchdog.Core/Services/WatchdogRunner.cs` - Added Nina service injection
- `ScopeDomeWatchdog.Tray/App.xaml.cs` - Instantiate and manage Nina service

## Key Features

### Automatic Pause/Resume
- ✅ Detects dome reconnection status automatically
- ✅ Pauses sequencer during reconnection
- ✅ Resumes when complete

### Real-time Feedback
- ✅ Progress counter showing elapsed time
- ✅ Status messages and notifications
- ✅ Error reporting with details

### Robust Error Handling
- ✅ 5-minute timeout protection
- ✅ Graceful failure reporting
- ✅ User-friendly error messages

### Zero Configuration
- ✅ Works out of the box
- ✅ No NINA settings needed
- ✅ No watchdog configuration needed

## Communication Method

Uses **Windows Named Events** (no network required):
- `ScopeDome_ReconnectionStarted`
- `ScopeDome_ReconnectionComplete`
- `Nina_PauseRequested`
- `Nina_ResumeRequested`

## Requirements

- NINA 3.0 or later
- .NET 8.0 runtime
- Windows 10/11
- ScopeDomeWatchdog running

## Troubleshooting

### Plugin not visible in NINA?
1. Unblock the DLL:
   ```powershell
   Unblock-File -Path "$env:APPDATA\NINA\Plugins\ScopeDomeWatchdog\ScopeDomeWatchdog.Nina.dll"
   ```
2. Restart NINA
3. Check NINA plugin manager for errors

### Plugin doesn't pause sequencer?
1. Verify ScopeDomeWatchdog is running
2. Verify watchdog monitoring is enabled
3. Check instruction is in correct sequence location
4. Review NINA notification history

## Architecture

```
NINA Sequencer
  ↓
[DomeReconnectionPauseInstruction]
  ↓ (Windows Named Events)
[NinaPluginService] ← shared IPC service
  ↑ (Windows Named Events)
RestartSequenceService (in watchdog)
  ↓
Dome Reconnection Logic
```

## API Usage Example

```csharp
// In your code
var ninaService = new NinaPluginService();

// Signal reconnection start
await ninaService.SignalReconnectionStartAsync();

// Do dome reconnection work...

// Signal completion
await ninaService.SignalReconnectionCompleteAsync();
```

## Next Steps

### For Users
1. Build and deploy plugin (steps above)
2. Add instruction to your sequences
3. Run normal imaging session
4. Test by manually stopping dome process

### For Developers
1. Review `NINA_PLUGIN_IMPLEMENTATION.md` for full architecture
2. Check `INTEGRATION_EXAMPLES.cs` for code patterns
3. Extend instruction classes as needed
4. Add NINA settings UI (future enhancement)

## Support Resources

- **User Guide**: See `ScopeDomeWatchdog.Nina/README.md`
- **Technical Docs**: See `NINA_PLUGIN_IMPLEMENTATION.md`
- **Code Examples**: See `ScopeDomeWatchdog.Nina/INTEGRATION_EXAMPLES.cs`
- **API Reference**: See `NinaPluginService.cs` interface

## Performance

- **Memory**: Minimal (only event handles)
- **CPU**: Negligible (blocking wait during pause)
- **Network**: None (local only)
- **Startup Impact**: None (lazy initialization)

## Testing

Quick test without NINA:

```csharp
// Run the examples
await NinaIntegrationExample.Example_BasicFlow();
await NinaIntegrationExample.Example_MonitoringNinaPause();
await NinaIntegrationExample.Example_ErrorHandling();
```

---

**Ready to use!** Build the solution and deploy as shown above.

For detailed information, see the full documentation files.
