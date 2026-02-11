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
