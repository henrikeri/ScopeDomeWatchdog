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
