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
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;

namespace ScopeDomeWatchdog.Tray;

/// <summary>
/// View model for a switch item with selection support.
/// </summary>
public class SwitchSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool? CanWrite { get; init; }
    public bool? State { get; init; }
    public double? Value { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class SelectSwitchWindow : Window
{
    private readonly List<SwitchSelectionItem> _items;
    
    /// <summary>
    /// Returns the list of selected switches.
    /// </summary>
    public IReadOnlyList<MonitoredSwitch> SelectedSwitches { get; private set; } = new List<MonitoredSwitch>();

    public SelectSwitchWindow(IEnumerable<AscomSwitchInfo> items, IEnumerable<MonitoredSwitch>? currentlySelected = null)
    {
        InitializeComponent();
        WindowChromeHelper.ApplyDarkTitleBar(this);
        
        var selectedIndices = currentlySelected?.Select(s => s.Index).ToHashSet() ?? new HashSet<int>();
        
        _items = items.Select(i => new SwitchSelectionItem
        {
            Index = i.Index,
            Name = i.Name,
            CanWrite = i.CanWrite,
            State = i.State,
            Value = i.Value,
            IsSelected = selectedIndices.Contains(i.Index)
        }).ToList();
        
        SwitchGrid.ItemsSource = _items;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSwitches = _items
            .Where(i => i.IsSelected)
            .Select(i => new MonitoredSwitch { Index = i.Index, Name = i.Name })
            .ToList();

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
