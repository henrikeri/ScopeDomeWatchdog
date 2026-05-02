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
    private Func<string, CancellationToken, Task<bool>>? _restartHandler;
    private bool _isRunning = false;
    private INinaPluginService? _ninaService;
    private SwitchStateCacheService? _switchCacheService;
    private volatile bool _restartInProgress = false;
    private bool _wasFailing = false;
    private DateTime _lastFailureLogUtc = DateTime.MinValue;
    private DateTime _lastSuppressedRestartLogUtc = DateTime.MinValue;
    private bool _manualTriggerLogged = false;

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
        _restartHandler = restartHandler == null ? null : (_, ct) => restartHandler(ct);
        _ninaService = ninaService;
        _triggerEvent = new EventWaitHandle(false, EventResetMode.ManualReset, _config.TriggerEventName);
    }

    public void SetSwitchCacheService(SwitchStateCacheService service)
    {
        _switchCacheService = service;
        _switchCacheService.LogMessage += Log;
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
        Log($"Watchdog monitoring started: monitor={_config.MonitorIp}, interval={_config.PingIntervalSec}s, timeout={_config.PingTimeoutMs}ms, triggerAfter={_config.FailsToTrigger} failed ping(s)");
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
        Log("Watchdog monitoring stopped.");
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
        _restartHandler = handler == null ? null : (_, ct) => handler(ct);
    }

    public void SetRestartHandler(Func<string, CancellationToken, Task<bool>>? handler)
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
            string? failureReason = null;
            try
            {
                var reply = await ping.SendPingAsync(_config.MonitorIp, _config.PingTimeoutMs);
                ok = reply.Status == IPStatus.Success;
                ms = ok ? (int)reply.RoundtripTime : null;
                if (!ok)
                {
                    failureReason = $"ping status {reply.Status}";
                }
            }
            catch (Exception ex)
            {
                ok = false;
                failureReason = ex.Message;
            }

            if (ok)
            {
                if (_wasFailing)
                {
                    Log($"Monitor recovered: ping to {_config.MonitorIp} OK after {_status.ConsecutiveFails} failed ping(s). Last latency={ms}ms");
                }

                _wasFailing = false;
                _lastFailureLogUtc = DateTime.MinValue;
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
                LogFailureIfNeeded(failureReason);
            }

            // Record metrics for graphing
            _metricsTracker.RecordPing(ms, ok);

            if (manualRequested && _status.ConsecutiveFails < _config.FailsToTrigger)
            {
                if (!_manualTriggerLogged)
                {
                    Log($"Manual restart requested via trigger event '{_config.TriggerEventName}'.");
                    _manualTriggerLogged = true;
                }

                _status.ConsecutiveFails = _config.FailsToTrigger;
            }
            else if (!manualRequested)
            {
                _manualTriggerLogged = false;
            }

            _status.LastPingOk = ok;
            _status.LastPingMs = ms;
            _status.ManualTriggerSet = manualRequested;
            _status.AveragePingMs = _latency.Count == 0 ? null : Average(_latency);

            var now = DateTime.Now;
            var inCooldown = (now - _lastCycleUtc).TotalSeconds < _config.CooldownSeconds;
            _status.CooldownRemaining = inCooldown ? TimeSpan.FromSeconds(Math.Max(0, _config.CooldownSeconds - (now - _lastCycleUtc).TotalSeconds)) : null;

            if (_status.ConsecutiveFails >= _config.FailsToTrigger)
            {
                var triggerReason = manualRequested
                    ? $"manual trigger event '{_config.TriggerEventName}'"
                    : $"{_status.ConsecutiveFails} consecutive failed ping(s) to {_config.MonitorIp}";

                if (inCooldown)
                {
                    LogSuppressedRestartIfNeeded($"Restart suppressed during cooldown: {triggerReason}. Remaining cooldown={_status.CooldownRemaining?.TotalSeconds:0}s");
                    StatusUpdated?.Invoke(_status.Clone());
                    await DelayUntilNextPingAsync();
                    continue;
                }

                if (_restartHandler == null)
                {
                    LogSuppressedRestartIfNeeded($"Restart suppressed: no restart handler is configured. Trigger reason={triggerReason}");
                    StatusUpdated?.Invoke(_status.Clone());
                    await DelayUntilNextPingAsync();
                    continue;
                }

                Log($"Restart sequence requested: {triggerReason}.");

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
                    Log("Restart sequence starting - pausing cache operations");
                    success = await _restartHandler(triggerReason, _cts.Token);
                }
                catch (Exception ex)
                {
                    Log("Restart handler failed: " + ex.Message);
                    success = false;
                }
                finally
                {
                    _restartInProgress = false;
                    Log("Restart sequence complete - resuming cache operations");
                }

                _lastCycleUtc = DateTime.Now;
                if (success)
                {
                    Log("Restart sequence finished successfully.");
                    _status.LastRestartUtc = _lastCycleUtc;
                    _status.ConsecutiveFails = 0;
                    _wasFailing = false;
                    _lastFailureLogUtc = DateTime.MinValue;
                }
                else
                {
                    Log("Restart sequence finished with errors.");
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

            await DelayUntilNextPingAsync();
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
                    Log($"Caching switch states (interval: {_config.SwitchCacheIntervalSec}s)...");
                    
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
                Log($"Error in switch cache loop: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
            }
        }
    }

    private void LogFailureIfNeeded(string? failureReason)
    {
        var nowUtc = DateTime.UtcNow;
        var isFirstFailure = !_wasFailing;
        var reachedThreshold = _status.ConsecutiveFails == _config.FailsToTrigger;
        var shouldRepeat = _wasFailing && (nowUtc - _lastFailureLogUtc).TotalSeconds >= 60;

        if (!isFirstFailure && !reachedThreshold && !shouldRepeat)
        {
            return;
        }

        _wasFailing = true;
        _lastFailureLogUtc = nowUtc;

        var reason = string.IsNullOrWhiteSpace(failureReason) ? "no reply" : failureReason;
        Log($"Monitor failure: ping to {_config.MonitorIp} failed ({reason}). Consecutive failures={_status.ConsecutiveFails}/{_config.FailsToTrigger}");
    }

    private void LogSuppressedRestartIfNeeded(string message)
    {
        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastSuppressedRestartLogUtc).TotalSeconds < 60)
        {
            return;
        }

        _lastSuppressedRestartLogUtc = nowUtc;
        Log(message);
    }

    private async Task DelayUntilNextPingAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.PingIntervalSec), _cts.Token);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void Log(string message)
    {
        var handlers = LogMessage;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(message);
            }
            catch
            {
                // Logging must never stop the watchdog loop.
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
