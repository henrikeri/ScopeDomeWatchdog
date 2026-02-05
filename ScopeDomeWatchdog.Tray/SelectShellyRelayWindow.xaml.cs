using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using ScopeDomeWatchdog.Tray.Models;

namespace ScopeDomeWatchdog.Tray;

public partial class SelectShellyRelayWindow : Window
{
    public ShellyRelayItem? SelectedRelay { get; private set; }

    public SelectShellyRelayWindow(IEnumerable<ShellyRelayItem> items)
    {
        InitializeComponent();
        WindowChromeHelper.ApplyDarkTitleBar(this);
        RelayList.ItemsSource = items.ToList();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedRelay = RelayList.SelectedItem as ShellyRelayItem;
        if (SelectedRelay == null)
        {
            WpfMessageBox.Show(this, "Select a relay.", "ScopeDome Watchdog", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
