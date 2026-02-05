using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("ScopeDomeWatchdog")]
[assembly: AssemblyDescription("NINA plugin for ScopeDome reconnection automation")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("DomeCheck Contributors")]
[assembly: AssemblyProduct("ScopeDomeWatchdog NINA Plugin")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("9A4F5E2D-7C1A-4D6E-8F3B-2E5C9A1B7D4F")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// NINA Plugin Metadata (REQUIRED for plugin discovery)
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1001")]

// Plugin Description
[assembly: AssemblyMetadata("ShortDescription", "Automatically pauses NINA sequencer during ScopeDome reconnection events. Integrates with ScopeDomeWatchdog to prevent imaging failures during dome power cycling and ASCOM reconnection.")]
[assembly: AssemblyMetadata("LongDescription", @"ScopeDomeWatchdog NINA Plugin provides seamless integration between the ScopeDomeWatchdog monitoring application and NINA's Advanced Sequencer.

Features:
• Automatic Sequencer Pause/Resume - Sequencer automatically pauses when dome reconnection is detected
• Real-time Status Updates - Shows reconnection progress and elapsed time in NINA UI
• Timeout Protection - Fails gracefully if reconnection exceeds 5 minutes
• Zero Configuration - Works automatically when installed alongside ScopeDomeWatchdog Tray app
• IPC Communication - Uses Windows Named Events for reliable inter-process signaling

Installation:
1. Ensure ScopeDomeWatchdog Tray application is installed and running
2. Install this plugin to NINA's plugin directory
3. Add 'Dome Reconnection Pause' instruction to your sequences at strategic points

Requirements:
• ScopeDomeWatchdog Tray application (monitors dome and manages reconnection)
• ASCOM-compatible dome controller
• Shelly Smart Relay (for power cycling dome)")]

// License Information
[assembly: AssemblyMetadata("License", "MIT")]
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]

// Repository and Documentation
[assembly: AssemblyMetadata("Repository", "https://github.com/yourusername/ScopeDomeWatchdog")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/yourusername/ScopeDomeWatchdog")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/yourusername/ScopeDomeWatchdog/releases")]

// Search Tags
[assembly: AssemblyMetadata("Tags", "dome,scopedome,reconnection,automation,watchdog,ascom")]

// Featured Image URL (optional - replace with actual image URL when available)
// [assembly: AssemblyMetadata("FeaturedImageURL", "https://example.com/scopedome-plugin-icon.png")]
// [assembly: AssemblyMetadata("ScreenshotURL", "https://example.com/scopedome-plugin-screenshot.png")]
