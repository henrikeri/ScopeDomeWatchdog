// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
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
        private ResourceDictionary? _resources;
        private ResourceDictionary? _optionsResources;

        [ImportingConstructor]
        public ScopeDomeWatchdogPlugin() {
            // Try to auto-detect common tray app locations
            _trayAppPath = DetectTrayAppPath();
            
            // Initialize commands
            BrowseTrayAppCommand = new RelayCommand(BrowseTrayApp);
            LaunchTrayAppCommand = new RelayCommand(LaunchTrayApp);
            RefreshStatusCommand = new RelayCommand(RefreshStatus);
            
            // Load XAML resources
            LoadResources();
        }
        
        private void LoadResources() {
            try {
                _resources = new ResourceDictionary {
                    Source = new Uri("pack://application:,,,/ScopeDomeWatchdog.Nina;component/Views/DomeReconnectionTriggerTemplate.xaml")
                };
                Application.Current.Resources.MergedDictionaries.Add(_resources);
                Logger.Info("[ScopeDomeWatchdog] Loaded trigger template resources");
                
                _optionsResources = new ResourceDictionary {
                    Source = new Uri("pack://application:,,,/ScopeDomeWatchdog.Nina;component/Views/Options.xaml")
                };
                Application.Current.Resources.MergedDictionaries.Add(_optionsResources);
                Logger.Info("[ScopeDomeWatchdog] Loaded options resources");
            }
            catch (Exception ex) {
                Logger.Error($"[ScopeDomeWatchdog] Failed to load resources: {ex.Message}");
            }
        }

        #region Commands
        
        public ICommand BrowseTrayAppCommand { get; }
        public ICommand LaunchTrayAppCommand { get; }
        public ICommand RefreshStatusCommand { get; }
        
        private void BrowseTrayApp(object? parameter) {
            var dialog = new OpenFileDialog {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select ScopeDomeWatchdog.Tray.exe",
                FileName = "ScopeDomeWatchdog.Tray.exe"
            };
            
            if (!string.IsNullOrWhiteSpace(TrayAppPath) && File.Exists(TrayAppPath)) {
                dialog.InitialDirectory = Path.GetDirectoryName(TrayAppPath);
            }
            
            if (dialog.ShowDialog() == true) {
                TrayAppPath = dialog.FileName;
                Logger.Info($"[ScopeDomeWatchdog] Tray app path set to: {TrayAppPath}");
            }
        }
        
        private void RefreshStatus(object? parameter) {
            // Force property change notification to update UI
            RaisePropertyChanged(nameof(IsTrayAppRunning));
        }
        
        #endregion

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
        public void LaunchTrayApp(object? parameter = null)
        {
            try
            {
                if (IsTrayAppRunning)
                {
                    Logger.Info("[ScopeDomeWatchdog] Tray app is already running");
                    RaisePropertyChanged(nameof(IsTrayAppRunning));
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
                
                // Refresh status after a short delay
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
                    Application.Current?.Dispatcher?.Invoke(() => RaisePropertyChanged(nameof(IsTrayAppRunning)));
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
        
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
        }
    }
}
