# ScopeDome Watchdog

Automated recovery system for ScopeDome observatory domes. Monitors dome connectivity, 
performs power-cycle recovery via Shelly smart plugs, and integrates with N.I.N.A. for 
seamless imaging session continuity.

## Components

- **ScopeDomeWatchdog.Tray** - WPF system tray application with watchdog monitoring
- **ScopeDomeWatchdog.Nina** - N.I.N.A. plugin for sequence pause/resume during reconnection
- **ScopeDomeWatchdog.Trigger** - CLI tool for external triggering
- **ScopeDomeWatchdog.Core** - Shared logic library

## Features

- Ping-based dome connectivity monitoring
- Automatic power-cycle recovery via Shelly smart plugs
- ASCOM dome driver integration (connect, FindHome, encoder caching)
- ASCOM switch control for fan management during recovery
- N.I.N.A. integration: pauses sequence mid-exposure during reconnection, resumes automatically
- Real-time log viewer with dark theme UI
- Configurable timeouts and cooldown periods

## Configuration

Config stored at: `%APPDATA%\ScopeDomeWatchdog\config.json`

Key settings:
- Monitor IP, ping interval/timeout, fail threshold
- Shelly IP, switch ID, off/on delays, cooldown
- ASCOM dome/switch ProgID, fan switch index
- Dome HTTP IP, Basic Auth credentials, encoder poll interval
- Home action mode (AutoHome or WriteCachedEncoder)

## N.I.N.A. Plugin

The **Dome Reconnection Trigger** integrates with N.I.N.A. (Nighttime Imaging 'N' Astronomy) to 
automatically pause your imaging sequence when the dome loses connectivity and resumes when 
recovery completes.

### How It Works

1. The watchdog tray app detects dome connectivity loss
2. Signals the NINA plugin via Windows named events (`Global\ScopeDome_ReconnectionStarted`)
3. Plugin cancels the running sequence using `CancelAdvancedSequence()`
4. Watchdog performs power-cycle recovery and ASCOM reconnection
5. Signals completion via `Global\ScopeDome_ReconnectionComplete`
6. Plugin resumes sequence from same position using `StartAdvancedSequence(true)`

### Installation

Copy the plugin build output to your NINA plugins folder:

```
%LOCALAPPDATA%\NINA\Plugins\3.0.0\ScopeDomeWatchdog\
```

Required files:
- `ScopeDomeWatchdog.Nina.dll`

### Usage

1. Open N.I.N.A. and go to **Options > Plugins**
2. Enable **ScopeDome Watchdog**
3. In your sequence, go to **Global Triggers**
4. Add **Dome Reconnection Trigger** from the ScopeDomeWatchdog category
5. The trigger will automatically monitor for reconnection events

### Compatibility

- N.I.N.A. 3.2 or later
- Plugin requires the watchdog tray app to be running
- ASCOM v7 or later

### Requirements
- Shelly power plug for power cycling


## Build

```bash
dotnet build -c Release
```

## Publish (self-contained)

```bash
dotnet publish ScopeDomeWatchdog.Tray -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
dotnet publish ScopeDomeWatchdog.Trigger -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
```

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0-or-later)**.

You are free to use, modify, and distribute this software under the terms of the GPL v3. 
See the [LICENSE](LICENSE) file for the complete license text.

### License Notes

- The majority of the codebase is licensed under GPL v3
- Certain files in the N.I.N.A. plugin (`Triggers/DomeReconnectionTrigger.cs` and 
  `Containers/TriggerInstructionContainer.cs`) retain their MPL-2.0 headers as they 
  contain code derived from the ["When" plugin](https://github.com/isbeorn/nina.plugin.when) 
  by Stefan Berg
- MPL-2.0 is compatible with GPL v3 under the terms of both licenses

For detailed information about third-party components and their licenses, see 
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Copyright

Copyright (C) 2026

This program comes with ABSOLUTELY NO WARRANTY. This is free software, and you are 
welcome to redistribute it under certain conditions. See the LICENSE file for details.

