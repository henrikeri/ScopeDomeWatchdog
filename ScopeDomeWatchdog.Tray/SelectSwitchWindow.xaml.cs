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
