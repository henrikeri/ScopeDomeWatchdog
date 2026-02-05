# Building and Testing the Nina Plugin

## Prerequisites

- Visual Studio 2022 or VS Code
- .NET 8.0 SDK
- Windows 10/11
- ScopeDomeWatchdog solution open

## Build from Visual Studio

### Option 1: Full Solution Build

1. Open `ScopeDomeWatchdog.sln` in Visual Studio
2. Right-click solution → **Build Solution**
3. Verify all projects build successfully:
   - ✅ ScopeDomeWatchdog.Core
   - ✅ ScopeDomeWatchdog.Tray
   - ✅ ScopeDomeWatchdog.Trigger
   - ✅ ScopeDomeWatchdog.Nina (NEW)

### Option 2: Nina Plugin Only

1. Right-click `ScopeDomeWatchdog.Nina` project
2. Select **Build**
3. Output appears in `ScopeDomeWatchdog.Nina/bin/Release/`

## Build from Command Line

### PowerShell

```powershell
# Navigate to workspace
cd "c:\Users\riise\Documents\DomeCheck\DomeCheck\Workspace"

# Build entire solution
dotnet build ScopeDomeWatchdog.sln -c Release

# Or just the Nina plugin
dotnet build ScopeDomeWatchdog.Nina/ScopeDomeWatchdog.Nina.csproj -c Release

# Verify build output
ls "ScopeDomeWatchdog.Nina/bin/Release/net8.0-windows/"
```

### Output Files

```
ScopeDomeWatchdog.Nina/bin/Release/net8.0-windows/
├── ScopeDomeWatchdog.Nina.dll          ← Main plugin (deploy this)
├── ScopeDomeWatchdog.Nina.pdb          ← Debug symbols (optional)
└── [dependencies]
```

## Install Plugin in NINA

### Automatic Installation Script

```powershell
# Define paths
$SourceDll = "c:\Users\riise\Documents\DomeCheck\DomeCheck\Workspace\ScopeDomeWatchdog.Nina\bin\Release\net8.0-windows\ScopeDomeWatchdog.Nina.dll"
$NinaPluginDir = "$env:APPDATA\NINA\Plugins\ScopeDomeWatchdog"

# Create plugin directory
New-Item -ItemType Directory -Force -Path $NinaPluginDir | Out-Null

# Copy DLL
Copy-Item $SourceDll -Destination $NinaPluginDir -Force
Write-Host "Plugin installed to: $NinaPluginDir"

# Verify installation
if (Test-Path "$NinaPluginDir\ScopeDomeWatchdog.Nina.dll") {
    Write-Host "✅ Installation successful"
} else {
    Write-Host "❌ Installation failed"
}

# Unblock if needed
Unblock-File -Path "$NinaPluginDir\ScopeDomeWatchdog.Nina.dll" -Force
Write-Host "✅ DLL unblocked"
```

### Manual Installation

1. Build the plugin (see sections above)
2. Locate built DLL: `ScopeDomeWatchdog.Nina/bin/Release/net8.0-windows/ScopeDomeWatchdog.Nina.dll`
3. Create directory: `%APPDATA%\NINA\Plugins\ScopeDomeWatchdog\`
4. Copy DLL to that directory
5. Right-click DLL → Properties → Unblock (if security warning)

## Verify Installation

### In NINA

1. Start NINA
2. Tools → Plugin Manager
3. Look for "ScopeDomeWatchdog" plugin
4. Status should show "Loaded"
5. If error, check event log

### In File System

```powershell
# Check if plugin is installed
$PluginPath = "$env:APPDATA\NINA\Plugins\ScopeDomeWatchdog\ScopeDomeWatchdog.Nina.dll"
if (Test-Path $PluginPath) {
    Write-Host "✅ Plugin file exists: $PluginPath"
    $Details = (Get-Item $PluginPath) | Select-Object FullName, Length, LastWriteTime
    $Details
} else {
    Write-Host "❌ Plugin not found at: $PluginPath"
}
```

## Testing the Plugin

### Manual Test in NINA

1. **Start NINA**
   - Restart to load the plugin

2. **Create Test Sequence**
   - File → New Sequence
   - Switch to "Advanced Sequencer" tab
   - Click "Add Row"
   - Category: "ScopeDome"
   - Instruction: "Dome Reconnection Pause"

3. **Verify Instruction Loads**
   - Double-click the instruction row
   - Should open instruction editor
   - Should show "Dome Reconnection Pause" dialog

4. **Test Without Dome Issues**
   - Save sequence as "test_sequence.xml"
   - Start ScopeDomeWatchdog tray app
   - Run the sequence in NINA
   - Should complete without pausing (no reconnection needed)

### Test with Simulated Reconnection

1. **Prepare Watchdog**
   ```powershell
   # In PowerShell, create a test event
   $event = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, "ScopeDome_ReconnectionStarted")
   $event.Set()
   Write-Host "Reconnection event triggered"
   ```

2. **Run Sequence**
   - Start the test sequence in NINA
   - Run the DomeReconnectionPauseInstruction
   - Should detect reconnection and pause
   - Should show progress counter
   - Should wait for completion event

3. **Complete Reconnection**
   ```powershell
   # Signal completion
   $event = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, "ScopeDome_ReconnectionComplete")
   $event.Set()
   Write-Host "Reconnection complete event triggered"
   ```

4. **Verify Resume**
   - Sequence should resume automatically
   - Should show success message
   - Logs should show state transition

### Unit Test Without NINA

```csharp
// Create test program
using ScopeDomeWatchdog.Core.Services;

var service = new NinaPluginService();

// Test 1: Basic signaling
Console.WriteLine("Test 1: Signal and complete");
await service.SignalReconnectionStartAsync();
await Task.Delay(1000);
await service.SignalReconnectionCompleteAsync();
Console.WriteLine("✅ Test 1 passed");

// Test 2: wait for pause
Console.WriteLine("Test 2: Wait for pause");
await service.SignalReconnectionStartAsync();
var paused = await service.WaitForNinaPauseAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"✅ Test 2: Nina paused = {paused}");

// Test 3: Error handling
Console.WriteLine("Test 3: Error handling");
await service.SignalReconnectionFailedAsync("Test error");
Console.WriteLine("✅ Test 3 passed");
```

## Debugging

### Enable Verbose Logging in NINA

1. Tools → Options → Debugging
2. Enable "Verbose Logging"
3. Run sequence
4. Check NINA logs for plugin messages

### Visual Studio Debugging

#### Attach to Running NINA

1. Start NINA normally
2. In Visual Studio: Debug → Attach to Process
3. Find "NINA.exe" or similar
4. Set breakpoints in plugin code
5. Trigger events to hit breakpoints

#### Debug Plugin Directly

1. Create test console application
2. Reference `ScopeDomeWatchdog.Core` project
3. Instantiate `NinaPluginService`
4. Set breakpoints and run

### Event Monitoring

```powershell
# Monitor named events in PowerShell
$events = @(
    "ScopeDome_ReconnectionStarted",
    "ScopeDome_ReconnectionComplete",
    "Nina_PauseRequested",
    "Nina_ResumeRequested"
)

foreach ($eventName in $events) {
    try {
        $e = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, $eventName)
        Write-Host "✅ Event accessible: $eventName"
        $e.Dispose()
    } catch {
        Write-Host "❌ Event not found or error: $eventName"
    }
}
```

## Troubleshooting Build Issues

### "NINA.SDK not found"

```powershell
# Restore NuGet packages
dotnet restore ScopeDomeWatchdog.Nina/ScopeDomeWatchdog.Nina.csproj

# Or in Visual Studio
# Tools → NuGet Package Manager → Restore Packages
```

### Build Fails with DLL Conflicts

```powershell
# Clean and rebuild
dotnet clean ScopeDomeWatchdog.sln
dotnet build ScopeDomeWatchdog.sln -c Release
```

### Plugin Won't Load in NINA

1. Check Windows Event Viewer → Windows Logs → Application
2. Look for exceptions from NINA
3. Verify DLL is unblocked:
   ```powershell
   Unblock-File -Path "$env:APPDATA\NINA\Plugins\ScopeDomeWatchdog\ScopeDomeWatchdog.Nina.dll"
   ```
4. Verify no missing dependencies:
   ```powershell
   # Check if DLL loads
   [Reflection.Assembly]::LoadFile("C:\path\to\ScopeDomeWatchdog.Nina.dll")
   ```

## Release Build

### Prepare for Distribution

```powershell
# Ensure Release build
dotnet build ScopeDomeWatchdog.Nina -c Release

# Create distribution package
$distributionDir = "$PWD\dist\ScopeDomeWatchdog.Nina.1.0.0"
New-Item -ItemType Directory -Force -Path $distributionDir | Out-Null

# Copy plugin and documentation
Copy-Item "ScopeDomeWatchdog.Nina\bin\Release\net8.0-windows\ScopeDomeWatchdog.Nina.dll" $distributionDir
Copy-Item "ScopeDomeWatchdog.Nina\README.md" $distributionDir
Copy-Item "NINA_PLUGIN_IMPLEMENTATION.md" $distributionDir

Write-Host "Distribution ready at: $distributionDir"
```

### Create Installation Instructions

Include in distribution:
1. `README.md` - User guide
2. `ScopeDomeWatchdog.Nina.dll` - Plugin binary
3. Installation script (if desired)

## Continuous Integration (Optional)

### GitHub Actions Example

```yaml
name: Build Nina Plugin

on: [push, pull_request]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build ScopeDomeWatchdog.Nina -c Release
      - uses: actions/upload-artifact@v2
        with:
          name: ScopeDomeWatchdog.Nina
          path: ScopeDomeWatchdog.Nina/bin/Release/net8.0-windows/ScopeDomeWatchdog.Nina.dll
```

## Summary

### Quick Build & Test

```powershell
# 1. Build
dotnet build ScopeDomeWatchdog.Nina -c Release

# 2. Install
$src = "ScopeDomeWatchdog.Nina\bin\Release\net8.0-windows\ScopeDomeWatchdog.Nina.dll"
$dst = "$env:APPDATA\NINA\Plugins\ScopeDomeWatchdog"
New-Item -Type Directory -Force $dst | Out-Null
Copy-Item $src -Destination $dst -Force
Unblock-File "$dst\ScopeDomeWatchdog.Nina.dll"

# 3. Restart NINA and verify

Write-Host "Build and install complete!"
```

---

**Ready to deploy!** Follow the steps above for a working installation.
