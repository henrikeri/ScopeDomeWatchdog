using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;

namespace ScopeDomeWatchdog.Nina {
    /// <summary>
    /// Main plugin manifest for ScopeDomeWatchdog NINA plugin.
    /// Inherits from PluginBase which automatically handles plugin metadata from AssemblyInfo.cs
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class ScopeDomeWatchdogPlugin : PluginBase, INotifyPropertyChanged {
        
        private string _trayAppPath = string.Empty;

        [ImportingConstructor]
        public ScopeDomeWatchdogPlugin() {
            // Try to auto-detect common tray app locations
            _trayAppPath = DetectTrayAppPath();
        }

        /// <summary>
        /// Path to the ScopeDome Watchdog Tray application executable.
        /// </summary>
        [Category("Tray App")]
        [DisplayName("Tray App Path")]
        [Description("Full path to ScopeDomeWatchdog.Tray.exe. Used to launch the monitor app if not running.")]
        public string TrayAppPath
        {
            get => _trayAppPath;
            set
            {
                if (_trayAppPath != value)
                {
                    _trayAppPath = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Check if the tray application is currently running.
        /// </summary>
        [Category("Tray App")]
        [DisplayName("Tray App Running")]
        [Description("Indicates whether ScopeDomeWatchdog.Tray is currently running.")]
        public bool IsTrayAppRunning
        {
            get
            {
                try
                {
                    return Process.GetProcessesByName("ScopeDomeWatchdog.Tray").Any();
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Launches the tray application if it's not already running.
        /// </summary>
        public void LaunchTrayApp()
        {
            try
            {
                if (IsTrayAppRunning)
                {
                    Logger.Info("[ScopeDomeWatchdog] Tray app is already running");
                    return;
                }

                if (string.IsNullOrWhiteSpace(TrayAppPath) || !File.Exists(TrayAppPath))
                {
                    Logger.Error($"[ScopeDomeWatchdog] Tray app path not found: {TrayAppPath}");
                    return;
                }

                Logger.Info($"[ScopeDomeWatchdog] Launching tray app: {TrayAppPath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = TrayAppPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(TrayAppPath)
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[ScopeDomeWatchdog] Failed to launch tray app: {ex.Message}");
            }
        }

        private string DetectTrayAppPath()
        {
            // Common installation paths
            var searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "N.I.N.A", "ScopeDomeWatchdog.Tray.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ScopeDomeWatchdog", "ScopeDomeWatchdog.Tray.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScopeDomeWatchdog", "ScopeDomeWatchdog.Tray.exe"),
                @"C:\Program Files\ScopeDomeWatchdog\ScopeDomeWatchdog.Tray.exe"
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    Logger.Info($"[ScopeDomeWatchdog] Auto-detected tray app at: {path}");
                    return path;
                }
            }

            Logger.Warning("[ScopeDomeWatchdog] Could not auto-detect tray app path. Please set manually in plugin options.");
            return string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
