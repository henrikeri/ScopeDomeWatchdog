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
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;

namespace ScopeDomeWatchdog.Tray;

public partial class HealthDashboardWindow : Window
{
    private readonly WatchdogRunner _runner;
    private readonly RestartHistoryService _historyService;
    private readonly HealthMetricsTracker _metricsTracker;
    private readonly ObservableCollection<RestartHistoryViewModel> _historyItems = new();

    public HealthDashboardWindow(WatchdogRunner runner, RestartHistoryService historyService)
    {
        _runner = runner;
        _historyService = historyService;
        _metricsTracker = runner.GetHealthMetrics;
        InitializeComponent();
        WindowChromeHelper.ApplyDarkTitleBar(this);
        RestartHistoryGrid.ItemsSource = _historyItems;
        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        // Update restart history
        var history = _historyService.GetRecentHistory(50).OrderByDescending(h => h.StartTimeUtc).ToList();
        _historyItems.Clear();
        foreach (var entry in history)
        {
            _historyItems.Add(new RestartHistoryViewModel(entry));
        }

        // Update key metrics
        var totalRestarts = _historyService.GetTotalRestarts();
        var successRate = _historyService.GetSuccessRate();
        var (avgLatency, _, _) = _metricsTracker.GetLatencyStats();
        var pingSuccessRate = _metricsTracker.GetSuccessRate();

        TotalRestartsText.Text = totalRestarts.ToString();
        SuccessRateText.Text = $"{successRate * 100:0.0}%";
        PingSuccessRateText.Text = $"{pingSuccessRate * 100:0.0}%";
        AvgLatencyText.Text = avgLatency.HasValue ? $"{avgLatency:0.0}ms" : "n/a";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshMetrics();
    }

    public sealed class RestartHistoryViewModel
    {
        private readonly RestartHistoryEntry _entry;

        public RestartHistoryViewModel(RestartHistoryEntry entry)
        {
            _entry = entry;
        }

        public string StartTimeDisplay => _entry.StartTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        public string DurationDisplay => _entry.DurationDisplay;
        public string StatusDisplay => _entry.Success ? "SUCCESS" : "FAILED";
        public string TriggerReason => _entry.TriggerReason ?? "unknown";
    }
}
