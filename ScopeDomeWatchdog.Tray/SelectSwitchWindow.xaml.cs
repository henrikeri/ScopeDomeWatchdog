using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using ScopeDomeWatchdog.Core.Services;

namespace ScopeDomeWatchdog.Tray;

public partial class SelectSwitchWindow : Window
{
    public AscomSwitchInfo? SelectedSwitch { get; private set; }

    public SelectSwitchWindow(IEnumerable<AscomSwitchInfo> items)
    {
        InitializeComponent();
        SwitchGrid.ItemsSource = items.ToList();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSwitch = SwitchGrid.SelectedItem as AscomSwitchInfo;
        if (SelectedSwitch == null)
        {
            WpfMessageBox.Show(this, "Select a switch.", "ScopeDome Watchdog", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
