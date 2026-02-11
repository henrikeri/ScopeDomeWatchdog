# Third-Party Notices

ScopeDome Watchdog incorporates code and libraries from third-party sources. 
This document provides information about these components and their licenses.

## N.I.N.A. (Nighttime Imaging 'N' Astronomy)

**Project:** N.I.N.A.  
**Website:** https://nighttime-imaging.eu/  
**License:** Mozilla Public License 2.0 (MPL-2.0)  
**Components Used:**
- NINA.Plugin (v3.2.0.1018-nightly)
- NINA.Sequencer (v3.2.0.1018-nightly)
- NINA.WPF.Base (v3.2.0.1018-nightly)

N.I.N.A. is an open-source application for astrophotography. ScopeDome Watchdog 
includes a plugin that integrates with N.I.N.A. to provide automated pause/resume 
functionality during dome reconnection events.

The N.I.N.A. plugin packages are licensed under MPL-2.0, which is compatible with 
GPL v3. The MPL-2.0 license text can be found at:  
https://www.mozilla.org/en-US/MPL/2.0/

---

## .NET Runtime and Base Class Libraries

**Project:** .NET  
**Website:** https://dotnet.microsoft.com/  
**License:** MIT License  
**Components Used:**
- .NET 8.0 Runtime and SDK
- Windows Presentation Foundation (WPF)
- Windows Forms
- System Libraries (HttpClient, etc.)

.NET is developed by Microsoft and the .NET Foundation. The project is open-source 
and licensed under the MIT License. The MIT License text can be found at:  
https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

---

## ASCOM Platform

**Project:** ASCOM Platform  
**Website:** https://ascom-standards.org/  
**License:** Creative Commons Attribution-ShareAlike 3.0 Unported License (CC BY-SA 3.0)

ScopeDome Watchdog interacts with ASCOM-compliant drivers through COM interfaces 
defined by the ASCOM Platform standards. The ASCOM Platform provides standardized 
interfaces for astronomy hardware control.

Note: While the ASCOM Platform standards and documentation are under CC BY-SA 3.0, 
individual ASCOM drivers may have their own licenses. ScopeDome Watchdog does not 
include any ASCOM driver code; it only uses the standard COM interfaces to 
communicate with separately installed drivers.

ASCOM Platform information: https://ascom-standards.org/About/Index.htm

---

## "When" N.I.N.A. Plugin

**Project:** When - Conditional Trigger Plugin for N.I.N.A.  
**Author:** Stefan Berg (isbeorn86+NINA@googlemail.com)  
**Repository:** https://github.com/isbeorn/nina.plugin.when  
**License:** Mozilla Public License 2.0 (MPL-2.0)

The ScopeDome Watchdog N.I.N.A. plugin includes code patterns and architectural 
approaches inspired by and derived from the "When" plugin for N.I.N.A. Specifically:

- **ConditionWatchdog Pattern:** The approach for monitoring conditions and responding 
  to state changes in N.I.N.A. sequences
- **Sequence Interrupt/Restart Logic:** The mechanism for pausing and resuming N.I.N.A. 
  imaging sequences
- **TriggerInstructionContainer:** Container pattern inspired by the IfContainer from 
  the When plugin

The following files in this project contain code derived from or inspired by the When plugin:
- `ScopeDomeWatchdog.Nina/Triggers/DomeReconnectionTrigger.cs` (MPL-2.0)
- `ScopeDomeWatchdog.Nina/Containers/TriggerInstructionContainer.cs` (MPL-2.0)

These files are licensed under MPL-2.0 to comply with the original license terms, 
and this is compatible with the GPL v3 license used for the rest of the project.

The MPL-2.0 license text can be found at: https://www.mozilla.org/en-US/MPL/2.0/

**Attribution and Thanks:** We are grateful to Stefan Berg and the N.I.N.A. contributors 
for their excellent work on the When plugin, which provided the foundation for our 
trigger system.

---

## Additional Information

### License Compatibility

This project is licensed under GNU General Public License v3.0 (GPL-3.0-or-later). 
All third-party components used in this project have licenses that are compatible 
with GPL v3:

- **MPL-2.0 (N.I.N.A.):** Compatible with GPL v3 per GPLv3 section 13 and MPL-2.0 section 3.3
- **MIT (.NET):** Permissive license, compatible with GPL v3
- **CC BY-SA 3.0 (ASCOM standards):** Applied to documentation/standards, not code included in this project

### Source Code Attribution

The source code in this repository was developed specifically for ScopeDome Watchdog 
and is licensed under GPL v3, except where otherwise noted in file headers or 
documentation.

For questions about licensing or third-party components, please open an issue on 
the project repository.

---

Last Updated: February 11, 2026
