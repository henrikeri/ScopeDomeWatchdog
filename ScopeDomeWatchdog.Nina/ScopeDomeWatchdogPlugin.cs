using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using NINA.Plugin;
using NINA.Plugin.Interfaces;

namespace ScopeDomeWatchdog.Nina {
    /// <summary>
    /// Main plugin manifest for ScopeDomeWatchdog NINA plugin.
    /// Inherits from PluginBase which automatically handles plugin metadata from AssemblyInfo.cs
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class ScopeDomeWatchdogPlugin : PluginBase, INotifyPropertyChanged {
        
        [ImportingConstructor]
        public ScopeDomeWatchdogPlugin() {
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
