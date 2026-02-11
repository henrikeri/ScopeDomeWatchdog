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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Interop;
using ScopeDomeWatchdog.Core.Models;

namespace ScopeDomeWatchdog.Core.Services;

/// <summary>
/// Represents the cached state of a single switch.
/// </summary>
public sealed class CachedSwitchState
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool? State { get; init; }
    public double? Value { get; init; }
    public DateTime CachedAtUtc { get; init; }
}

/// <summary>
/// Service that periodically reads and caches switch states,
/// and can restore them after dome reconnection.
/// </summary>
public sealed class SwitchStateCacheService
{
    private readonly StaTaskRunner _staRunner;
    private readonly object _lock = new();
    private Dictionary<int, CachedSwitchState> _cachedStates = new();

    public event Action<IReadOnlyList<CachedSwitchState>>? StatesCached;
    public event Action<string>? LogMessage;

    public SwitchStateCacheService(StaTaskRunner staRunner)
    {
        _staRunner = staRunner;
    }

    /// <summary>
    /// Gets a copy of the currently cached switch states.
    /// </summary>
    public IReadOnlyList<CachedSwitchState> GetCachedStates()
    {
        lock (_lock)
        {
            return new List<CachedSwitchState>(_cachedStates.Values);
        }
    }

    /// <summary>
    /// Reads and caches the current state of the specified switches.
    /// </summary>
    public Task<IReadOnlyList<CachedSwitchState>> ReadAndCacheStatesAsync(
        string progId,
        IEnumerable<MonitoredSwitch> switches,
        CancellationToken cancellationToken)
    {
        return _staRunner.RunAsync(() =>
        {
            var results = new List<CachedSwitchState>();
            object? sw = null;
            try
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: true);
                sw = Activator.CreateInstance(type!);
                if (sw == null)
                {
                    Log("Failed to create ASCOM Switch instance");
                    return (IReadOnlyList<CachedSwitchState>)results;
                }

                sw.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, sw, new object[] { true });

                foreach (var monitoredSwitch in switches)
                {
                    try
                    {
                        bool? state = null;
                        double? value = null;

                        try
                        {
                            state = (bool)sw.GetType().InvokeMember("GetSwitch",
                                System.Reflection.BindingFlags.InvokeMethod, null, sw, new object[] { monitoredSwitch.Index })!;
                        }
                        catch { }

                        try
                        {
                            value = (double)sw.GetType().InvokeMember("GetSwitchValue",
                                System.Reflection.BindingFlags.InvokeMethod, null, sw, new object[] { monitoredSwitch.Index })!;
                        }
                        catch { }

                        var cached = new CachedSwitchState
                        {
                            Index = monitoredSwitch.Index,
                            Name = monitoredSwitch.Name,
                            State = state,
                            Value = value,
                            CachedAtUtc = DateTime.UtcNow
                        };

                        results.Add(cached);
                        Log($"Cached switch {monitoredSwitch.Index} ({monitoredSwitch.Name}): State={state}, Value={value}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to read switch {monitoredSwitch.Index}: {ex.Message}");
                    }
                }

                // Update the cache
                lock (_lock)
                {
                    foreach (var cached in results)
                    {
                        _cachedStates[cached.Index] = cached;
                    }
                }

                StatesCached?.Invoke(results);
                return (IReadOnlyList<CachedSwitchState>)results;
            }
            catch (Exception ex)
            {
                Log($"Error reading switch states: {ex.Message}");
                return (IReadOnlyList<CachedSwitchState>)results;
            }
            finally
            {
                if (sw != null)
                {
                    try { sw.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, sw, new object[] { false }); } catch { }
                    try { Marshal.FinalReleaseComObject(sw); } catch { }
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Restores the cached switch states.
    /// </summary>
    public Task<bool> RestoreCachedStatesAsync(
        string progId,
        IEnumerable<CachedSwitchState> states,
        CancellationToken cancellationToken)
    {
        return _staRunner.RunAsync(() =>
        {
            object? sw = null;
            try
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: true);
                sw = Activator.CreateInstance(type!);
                if (sw == null)
                {
                    Log("Failed to create ASCOM Switch instance for restore");
                    return false;
                }

                sw.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, sw, new object[] { true });

                foreach (var cached in states)
                {
                    try
                    {
                        // Try to restore the state
                        bool setOk = false;

                        if (cached.State.HasValue)
                        {
                            try
                            {
                                sw.GetType().InvokeMember("SetSwitch",
                                    System.Reflection.BindingFlags.InvokeMethod, null, sw,
                                    new object[] { cached.Index, cached.State.Value });
                                setOk = true;
                                Log($"Restored switch {cached.Index} ({cached.Name}) State={cached.State.Value}");
                            }
                            catch (Exception ex)
                            {
                                Log($"SetSwitch({cached.Index}, {cached.State.Value}) failed: {ex.Message}");
                            }
                        }

                        if (cached.Value.HasValue && !setOk)
                        {
                            try
                            {
                                sw.GetType().InvokeMember("SetSwitchValue",
                                    System.Reflection.BindingFlags.InvokeMethod, null, sw,
                                    new object[] { cached.Index, cached.Value.Value });
                                Log($"Restored switch {cached.Index} ({cached.Name}) Value={cached.Value.Value}");
                            }
                            catch (Exception ex)
                            {
                                Log($"SetSwitchValue({cached.Index}, {cached.Value.Value}) failed: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to restore switch {cached.Index}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error restoring switch states: {ex.Message}");
                return false;
            }
            finally
            {
                if (sw != null)
                {
                    try { sw.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, sw, new object[] { false }); } catch { }
                    try { Marshal.FinalReleaseComObject(sw); } catch { }
                }
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Clears all cached states.
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cachedStates.Clear();
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(message);
    }
}
