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
