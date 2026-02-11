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
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Services;

/// <summary>
/// Service for managing communication between the watchdog and Nina plugin.
/// Handles pause/resume signaling and status queries.
/// </summary>
public interface INinaPluginService
{
    event EventHandler<DomeReconnectionStateChangedEventArgs>? ReconnectionStateChanged;

    /// <summary>
    /// Gets the current dome reconnection state.
    /// </summary>
    /// <returns>True if dome is currently being reconnected</returns>
    bool IsReconnectingDome { get; }

    /// <summary>
    /// Gets whether Nina is currently paused by the plugin.
    /// </summary>
    bool IsNinaPaused { get; }

    /// <summary>
    /// Signals that dome reconnection has started.
    /// </summary>
    Task SignalReconnectionStartAsync();

    /// <summary>
    /// Signals that dome reconnection has completed successfully.
    /// </summary>
    Task SignalReconnectionCompleteAsync();

    /// <summary>
    /// Signals that dome reconnection has failed.
    /// </summary>
    Task SignalReconnectionFailedAsync(string reason);

    /// <summary>
    /// Waits for Nina to pause, with optional timeout.
    /// </summary>
    Task<bool> WaitForNinaPauseAsync(TimeSpan timeout);

    /// <summary>
    /// Signals Nina to resume from pause.
    /// </summary>
    Task SignalNinaResumeAsync();
}

public class DomeReconnectionStateChangedEventArgs : EventArgs
{
    public bool IsReconnecting { get; set; }
    public string? Reason { get; set; }
}

public sealed class NinaPluginService : INinaPluginService, IDisposable
{
    private volatile bool _isReconnectingDome;
    private volatile bool _isNinaPaused;
    private readonly EventWaitHandle _reconnectionStartedEvent;
    private readonly EventWaitHandle _reconnectionCompleteEvent;
    private readonly EventWaitHandle _ninaPausedEvent;
    private readonly EventWaitHandle _ninaResumeEvent;
    private string? _lastReconnectionReason;

    public event EventHandler<DomeReconnectionStateChangedEventArgs>? ReconnectionStateChanged;

    public bool IsReconnectingDome => _isReconnectingDome;
    public bool IsNinaPaused => _isNinaPaused;

    public NinaPluginService()
    {
        _reconnectionStartedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Global\ScopeDome_ReconnectionStarted");
        _reconnectionCompleteEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Global\ScopeDome_ReconnectionComplete");
        _ninaPausedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Global\Nina_PauseRequested");
        _ninaResumeEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Global\Nina_ResumeRequested");
        
        // Ensure events start in the correct state
        _reconnectionStartedEvent.Reset();
        _reconnectionCompleteEvent.Reset();
        _ninaPausedEvent.Reset();
        _ninaResumeEvent.Reset();
        
        Console.WriteLine("[NinaPluginService] Initialized - events created and reset");
    }

    public Task SignalReconnectionStartAsync()
    {
        return Task.Run(() =>
        {
            _isReconnectingDome = true;
            _reconnectionCompleteEvent.Reset();
            _reconnectionStartedEvent.Set();
            Console.WriteLine("[NinaPluginService] SIGNALED: ReconnectionStarted event SET");
            OnReconnectionStateChanged(true, "Dome reconnection started");
        });
    }

    public Task SignalReconnectionCompleteAsync()
    {
        return Task.Run(() =>
        {
            _isReconnectingDome = false;
            _reconnectionStartedEvent.Reset();
            _reconnectionCompleteEvent.Set();
            Console.WriteLine("[NinaPluginService] SIGNALED: ReconnectionComplete event SET");
            OnReconnectionStateChanged(false, "Dome reconnection complete");
        });
    }

    public Task SignalReconnectionFailedAsync(string reason)
    {
        return Task.Run(() =>
        {
            _isReconnectingDome = false;
            _lastReconnectionReason = reason;
            _reconnectionStartedEvent.Reset();
            _reconnectionCompleteEvent.Set();
            OnReconnectionStateChanged(false, $"Dome reconnection failed: {reason}");
        });
    }

    public Task<bool> WaitForNinaPauseAsync(TimeSpan timeout)
    {
        return Task.Run(() =>
        {
            var result = _ninaPausedEvent.WaitOne(timeout);
            if (result)
            {
                _isNinaPaused = true;
            }
            return result;
        });
    }

    public Task SignalNinaResumeAsync()
    {
        return Task.Run(() =>
        {
            _isNinaPaused = false;
            _ninaPausedEvent.Reset();
            _ninaResumeEvent.Set();
        });
    }

    private void OnReconnectionStateChanged(bool isReconnecting, string reason)
    {
        ReconnectionStateChanged?.Invoke(this, new DomeReconnectionStateChangedEventArgs
        {
            IsReconnecting = isReconnecting,
            Reason = reason
        });
    }

    public void Dispose()
    {
        _reconnectionStartedEvent?.Dispose();
        _reconnectionCompleteEvent?.Dispose();
        _ninaPausedEvent?.Dispose();
        _ninaResumeEvent?.Dispose();
    }
}
