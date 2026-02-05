using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using ScopeDomeWatchdog.Tray.Models;

namespace ScopeDomeWatchdog.Tray;

public partial class SelectAscomDeviceWindow : Window
{
    public AscomDeviceItem? SelectedItem { get; private set; }

    public SelectAscomDeviceWindow(IEnumerable<AscomDeviceItem> items)
    {
        InitializeComponent();
        WindowChromeHelper.ApplyDarkTitleBar(this);
        var itemList = items.ToList();
        DeviceList.ItemsSource = itemList;
        
        // Debug: Log if items are empty
        if (itemList.Count == 0)
        {
            Title = "Select ASCOM Device (No devices found)";
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedItem = DeviceList.SelectedItem as AscomDeviceItem;
        if (SelectedItem == null)
        {
            WpfMessageBox.Show(this, "Select a device.", "ScopeDome Watchdog", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
