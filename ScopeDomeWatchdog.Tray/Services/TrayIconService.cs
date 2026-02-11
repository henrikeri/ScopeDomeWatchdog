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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ScopeDomeWatchdog.Tray.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(Action onOpen, Action onRestartNow, Action onViewLogs, Action onSettings, Action onExit)
    {
        Icon? appIcon = null;
        try
        {
            // Try to load icon from file next to exe
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var iconPath = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "app.ico");
                if (File.Exists(iconPath))
                {
                    appIcon = new Icon(iconPath);
                }
            }
        }
        catch
        {
            // Fall back to default
        }

        appIcon ??= Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);

        _notifyIcon = new NotifyIcon
        {
            Text = "ScopeDome Watchdog",
            Icon = appIcon,
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => onOpen());
        menu.Items.Add("Trigger Restart Now", null, (_, _) => onRestartNow());
        menu.Items.Add("View Logs", null, (_, _) => onViewLogs());
        menu.Items.Add("Settings", null, (_, _) => onSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExit());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => onOpen();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
