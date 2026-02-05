using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScopeDomeWatchdog.Core.Models;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class RestartHistoryService
{
    private readonly string _historyPath;
    private readonly object _sync = new();
    private List<RestartHistoryEntry>? _cachedHistory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RestartHistoryService(string configDirectory)
    {
        _historyPath = Path.Combine(configDirectory, "restart_history.json");
    }

    public void LogRestartStart(string? reason = null)
    {
        lock (_sync)
        {
            var history = LoadHistory();
            var entry = new RestartHistoryEntry
            {
                StartTimeUtc = DateTime.UtcNow,
                TriggerReason = reason
            };
            history.Add(entry);
            SaveHistory(history);
            _cachedHistory = history;
        }
    }

    public void LogRestartEnd(bool success, string? errorMessage = null)
    {
        lock (_sync)
        {
            var history = LoadHistory();
            if (history.Count > 0)
            {
                var lastEntry = history.Last();
                if (!lastEntry.EndTimeUtc.HasValue)
                {
                    lastEntry.EndTimeUtc = DateTime.UtcNow;
                    lastEntry.Success = success;
                    lastEntry.ErrorMessage = errorMessage;
                    SaveHistory(history);
                    _cachedHistory = history;
                }
            }
        }
    }

    public List<RestartHistoryEntry> GetHistory()
    {
        lock (_sync)
        {
            return LoadHistory();
        }
    }

    public List<RestartHistoryEntry> GetRecentHistory(int count = 50)
    {
        lock (_sync)
        {
            var history = LoadHistory();
            return history.OrderByDescending(h => h.StartTimeUtc).Take(count).ToList();
        }
    }

    public int GetTotalRestarts()
    {
        lock (_sync)
        {
            return LoadHistory().Count;
        }
    }

    public int GetSuccessfulRestarts()
    {
        lock (_sync)
        {
            return LoadHistory().Count(h => h.Success && h.EndTimeUtc.HasValue);
        }
    }

    public double GetSuccessRate()
    {
        lock (_sync)
        {
            var history = LoadHistory();
            var completed = history.Where(h => h.EndTimeUtc.HasValue).ToList();
            if (completed.Count == 0) return 0;
            return completed.Count(h => h.Success) / (double)completed.Count;
        }
    }

    private List<RestartHistoryEntry> LoadHistory()
    {
        if (_cachedHistory != null)
            return new List<RestartHistoryEntry>(_cachedHistory);

        if (!File.Exists(_historyPath))
            return new List<RestartHistoryEntry>();

        try
        {
            var json = File.ReadAllText(_historyPath);
            var history = JsonSerializer.Deserialize<List<RestartHistoryEntry>>(json, JsonOptions);
            return history ?? new List<RestartHistoryEntry>();
        }
        catch
        {
            return new List<RestartHistoryEntry>();
        }
    }

    private void SaveHistory(List<RestartHistoryEntry> history)
    {
        try
        {
            var directory = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(history, JsonOptions);
            File.WriteAllText(_historyPath, json);
        }
        catch
        {
            // Swallow errors to avoid disrupting restart sequence
        }
    }
}
