using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScopeDomeWatchdog.Core.Models;

namespace ScopeDomeWatchdog.Core.Services;

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetDefaultConfigDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ScopeDomeWatchdog");
    }

    public static string GetDefaultConfigPath()
    {
        return Path.Combine(GetDefaultConfigDirectory(), "config.json");
    }

    public static string GetDefaultLogDirectory()
    {
        return Path.Combine(GetDefaultConfigDirectory(), "Logs");
    }

    public static WatchdogConfig LoadOrCreate(string? configPath = null)
    {
        var path = string.IsNullOrWhiteSpace(configPath) ? GetDefaultConfigPath() : configPath;
        if (!File.Exists(path))
        {
            var cfg = CreateDefault();
            Save(cfg, path);
            return cfg;
        }

        return Load(path);
    }

    public static WatchdogConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<WatchdogConfig>(json, JsonOptions) ?? CreateDefault();
        if (string.IsNullOrWhiteSpace(cfg.RestartLogDirectory))
        {
            cfg.RestartLogDirectory = GetDefaultLogDirectory();
        }
        return cfg;
    }

    public static void Save(WatchdogConfig config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GetDefaultConfigDirectory());
        if (string.IsNullOrWhiteSpace(config.RestartLogDirectory))
        {
            config.RestartLogDirectory = GetDefaultLogDirectory();
        }
        Directory.CreateDirectory(config.RestartLogDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static WatchdogConfig CreateDefault()
    {
        return new WatchdogConfig
        {
            RestartLogDirectory = GetDefaultLogDirectory()
        };
    }
}
