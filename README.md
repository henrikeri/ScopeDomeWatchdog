# ScopeDomeWatchdog

Two Windows applications in one solution:
- ScopeDomeWatchdog.Tray (WPF tray watchdog)
- ScopeDomeWatchdog.Trigger (CLI trigger)
- ScopeDomeWatchdog.Core (shared logic)

## Configuration
Config is stored per-user at:
%APPDATA%\ScopeDomeWatchdog\config.json

Key settings include:
- Monitor IP, ping interval/timeout, fail threshold
- Shelly IP, switch ID, off/on delays, cooldown
- ASCOM dome/switch ProgID, fan switch index
- Dome HTTP IP, Basic Auth username/password, encoder poll interval, home action mode
- Named mutex and manual trigger event name
- Log directory

The tray app includes a Settings window where you can edit the JSON and select ASCOM devices and sub-switches.

## Run
Build and run the tray app, then use the tray icon menu to open the main window, settings, or trigger a restart.

## Trigger CLI
Usage:
- ScopeDomeWatchdog.Trigger.exe
- ScopeDomeWatchdog.Trigger.exe --eventName "ScopeDomeWatchdog.TriggerRestart"
- ScopeDomeWatchdog.Trigger.exe --config "C:\path\to\config.json"

Exit codes:
- 0 success
- 1 failed to open/create event or load config
- 2 failed to set event

## Publish
Use these commands for single-file, self-contained x64 builds:
- dotnet publish ScopeDomeWatchdog.Tray -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
- dotnet publish ScopeDomeWatchdog.Trigger -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
