using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("ScopeDomeWatchdog")]
[assembly: AssemblyDescription("Intelligent ScopeDome monitoring and reconnection automation for NINA")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Henrik Erevik Riise")]
[assembly: AssemblyProduct("ScopeDomeWatchdog NINA Plugin")]
[assembly: AssemblyCopyright("Copyright © 2026 Henrik Erevik Riise")]
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
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

// NINA Plugin Metadata (REQUIRED for plugin discovery)
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1001")]

// Plugin Description
[assembly: AssemblyMetadata("ShortDescription", "Never lose another image to dome reconnections. Automatically pauses your sequence during ScopeDome power cycles and resumes when ready.")]
[assembly: AssemblyMetadata("LongDescription", @"ScopeDomeWatchdog keeps your imaging sessions running smoothly by intelligently handling dome reconnections without manual intervention.

🎯 Key Features:
• Smart Sequencer Control - Automatically pauses imaging during dome reconnection and resumes when stable
• Configurable Timeout - Set your own threshold for graceful failure if reconnection takes too long
• Real-time Status - See reconnection progress directly in NINA's status bar
• Zero-Touch Operation - Works seamlessly with ScopeDomeWatchdog tray app via inter-process communication
• Reliable IPC - Uses Windows Named Events for bulletproof cross-process signaling

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
[assembly: AssemblyMetadata("Repository", "https://github.com/henrikeri/ScopeDomeWatchdog")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/henrikeri/ScopeDomeWatchdog")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/henrikeri/ScopeDomeWatchdog/releases")]

// Search Tags
[assembly: AssemblyMetadata("Tags", "dome,scopedome,reconnection,automation,watchdog,ascom")]

// Featured Image URL (optional - replace with actual image URL when available)
// [assembly: AssemblyMetadata("FeaturedImageURL", "https://example.com/scopedome-plugin-icon.png")]
// [assembly: AssemblyMetadata("ScreenshotURL", "https://example.com/scopedome-plugin-screenshot.png")]
