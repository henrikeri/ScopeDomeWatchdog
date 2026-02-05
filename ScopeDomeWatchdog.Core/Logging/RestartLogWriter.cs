using System;
using System.IO;

namespace ScopeDomeWatchdog.Core.Logging;

public sealed class RestartLogWriter
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;

    public RestartLogWriter(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, "ScopeDomeWatchdog.log");
    }

    public string LogFilePath => _logFilePath;

    public void WriteLine(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        File.AppendAllText(_logFilePath, line + Environment.NewLine);
    }
}
