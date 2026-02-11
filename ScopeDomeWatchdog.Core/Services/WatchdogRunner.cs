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
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Interop;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class WatchdogRunner : IDisposable
{
    private readonly WatchdogConfig _config;
    private readonly Queue<int> _latency = new();
    private readonly WatchdogStatus _status = new();
    private readonly HealthMetricsTracker _metricsTracker = new();
    private EventWaitHandle _triggerEvent;
    private readonly object _triggerLock = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private Task? _switchCacheTask;
    private DateTime _lastCycleUtc = DateTime.MinValue;
    private DateTime _lastSwitchCacheUtc = DateTime.MinValue;
    private Func<CancellationToken, Task<bool>>? _restartHandler;
    private bool _isRunning = false;
    private INinaPluginService? _ninaService;
    private SwitchStateCacheService? _switchCacheService;
    private volatile bool _restartInProgress = false;

    public event Action<WatchdogStatus>? StatusUpdated;
    public event Action<bool>? RunningStateChanged;
    public event Action<IReadOnlyList<CachedSwitchState>>? SwitchStatesCached;
    public event Action<string>? LogMessage;

    /// <summary>
    /// True when a restart sequence is running - used to pause caching operations.
    /// </summary>
    public bool IsRestartInProgress => _restartInProgress;

    public WatchdogRunner(WatchdogConfig config, Func<CancellationToken, Task<bool>>? restartHandler = null, INinaPluginService? ninaService = null)
    {
        _config = config;
        _restartHandler = restartHandler;
        _ninaService = ninaService;
        _triggerEvent = new EventWaitHandle(false, EventResetMode.ManualReset, _config.TriggerEventName);
    }

    public void SetSwitchCacheService(SwitchStateCacheService service)
    {
        _switchCacheService = service;
        _switchCacheService.LogMessage += msg => LogMessage?.Invoke(msg);
        _switchCacheService.StatesCached += states => SwitchStatesCached?.Invoke(states);
    }

    public SwitchStateCacheService? GetSwitchCacheService() => _switchCacheService;

    public HealthMetricsTracker GetHealthMetrics => _metricsTracker;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _loopTask = Task.Run(LoopAsync);
        _switchCacheTask = Task.Run(SwitchCacheLoopAsync);
        RunningStateChanged?.Invoke(true);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _cts.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(5));
        _switchCacheTask?.Wait(TimeSpan.FromSeconds(5));
        _loopTask = null;
        _switchCacheTask = null;
        RunningStateChanged?.Invoke(false);
    }

    public bool IsRunning => _isRunning;

    public void TriggerManualRestart()
    {
        lock (_triggerLock)
        {
            _triggerEvent.Set();
        }
    }

    public void UpdateTriggerEventName(string eventName)
    {
        lock (_triggerLock)
        {
            var newEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            _triggerEvent.Dispose();
            _triggerEvent = newEvent;
        }
    }

    public void SetRestartHandler(Func<CancellationToken, Task<bool>>? handler)
    {
        _restartHandler = handler;
    }

    private async Task LoopAsync()
    {
        using var ping = new Ping();

        while (!_cts.IsCancellationRequested)
        {
            _status.TotalPings++;
            bool manualRequested;
            lock (_triggerLock)
            {
                manualRequested = _triggerEvent.WaitOne(0);
            }

            bool ok = false;
            int? ms = null;
            try
            {
                var reply = await ping.SendPingAsync(_config.MonitorIp, _config.PingTimeoutMs);
                ok = reply.Status == IPStatus.Success;
                ms = ok ? (int)reply.RoundtripTime : null;
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                _status.OkPings++;
                _status.ConsecutiveFails = 0;
                if (ms.HasValue)
                {
                    _latency.Enqueue(ms.Value);
                    while (_latency.Count > _config.LatencyWindow)
                    {
                        _latency.Dequeue();
                    }
                }
            }
            else
            {
                _status.ConsecutiveFails++;
            }

            // Record metrics for graphing
            _metricsTracker.RecordPing(ms, ok);

            if (manualRequested && _status.ConsecutiveFails < _config.FailsToTrigger)
            {
                _status.ConsecutiveFails = _config.FailsToTrigger;
            }

            _status.LastPingOk = ok;
            _status.LastPingMs = ms;
            _status.ManualTriggerSet = manualRequested;
            _status.AveragePingMs = _latency.Count == 0 ? null : Average(_latency);

            var now = DateTime.Now;
            var inCooldown = (now - _lastCycleUtc).TotalSeconds < _config.CooldownSeconds;
            _status.CooldownRemaining = inCooldown ? TimeSpan.FromSeconds(Math.Max(0, _config.CooldownSeconds - (now - _lastCycleUtc).TotalSeconds)) : null;

            if (_status.ConsecutiveFails >= _config.FailsToTrigger && !inCooldown && _restartHandler != null)
            {
                if (manualRequested)
                {
                    lock (_triggerLock)
                    {
                        try { _triggerEvent.Reset(); } catch { }
                    }
                }

                var success = false;
                try
                {
                    _restartInProgress = true;
                    LogMessage?.Invoke("Restart sequence starting - pausing cache operations");
                    success = await _restartHandler(_cts.Token);
                }
                catch
                {
                    success = false;
                }
                finally
                {
                    _restartInProgress = false;
                    LogMessage?.Invoke("Restart sequence complete - resuming cache operations");
                }

                _lastCycleUtc = DateTime.Now;
                if (success)
                {
                    _status.LastRestartUtc = _lastCycleUtc;
                    _status.ConsecutiveFails = 0;
                }
                else
                {
                    if (manualRequested)
                    {
                        lock (_triggerLock)
                        {
                            try { _triggerEvent.Set(); } catch { }
                        }
                    }
                    _status.ConsecutiveFails = 0;
                }
            }

            StatusUpdated?.Invoke(_status.Clone());

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.PingIntervalSec), _cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Background loop that caches switch states periodically.
    /// </summary>
    private async Task SwitchCacheLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // Skip caching during restart
                if (_restartInProgress)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
                    continue;
                }

                var now = DateTime.UtcNow;
                var elapsed = (now - _lastSwitchCacheUtc).TotalSeconds;
                
                if (_switchCacheService != null && 
                    _config.MonitoredSwitches.Count > 0 &&
                    !string.IsNullOrWhiteSpace(_config.AscomSwitchProgId) &&
                    elapsed >= _config.SwitchCacheIntervalSec)
                {
                    LogMessage?.Invoke($"Caching switch states (interval: {_config.SwitchCacheIntervalSec}s)...");
                    
                    await _switchCacheService.ReadAndCacheStatesAsync(
                        _config.AscomSwitchProgId,
                        _config.MonitoredSwitches,
                        _cts.Token);
                    
                    _lastSwitchCacheUtc = DateTime.UtcNow;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error in switch cache loop: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
            }
        }
    }

    private static double Average(IEnumerable<int> values)
    {
        long sum = 0;
        int count = 0;
        foreach (var v in values)
        {
            sum += v;
            count++;
        }

        return count == 0 ? 0 : sum / (double)count;
    }

    public void Dispose()
    {
        Stop();
        lock (_triggerLock)
        {
            _triggerEvent.Dispose();
        }
        _cts.Dispose();
    }
}
