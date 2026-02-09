using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Interop;
using ScopeDomeWatchdog.Core.Logging;
using ScopeDomeWatchdog.Core.Models;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class RestartSequenceService : IDisposable
{
    private readonly WatchdogConfig _config;
    private readonly StaTaskRunner _staRunner;
    private readonly RestartHistoryService _historyService;
    private readonly INinaPluginService? _ninaService;
    private SwitchStateCacheService? _switchCacheService;

    public RestartSequenceService(WatchdogConfig config, string? configDirectory = null, INinaPluginService? ninaService = null)
    {
        _config = config;
        _staRunner = new StaTaskRunner();
        var historyDir = configDirectory ?? ConfigService.GetDefaultConfigDirectory();
        _historyService = new RestartHistoryService(historyDir);
        _ninaService = ninaService;
    }

    public void SetSwitchCacheService(SwitchStateCacheService service)
    {
        _switchCacheService = service;
    }

    public RestartHistoryService GetHistoryService => _historyService;

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        using var mutex = new Mutex(false, _config.MutexName);
        var acquired = false;
        var shellyClient = new ShellyClient(TimeSpan.FromSeconds(_config.HttpTimeoutSec));
        var logWriter = new RestartLogWriter(_config.RestartLogDirectory);
        try
        {
            acquired = mutex.WaitOne(0);
            if (!acquired)
            {
                return false;
            }

            _historyService.LogRestartStart("Watchdog triggered");
            Log(logWriter, "=== RESTART SEQUENCE BEGIN ===");

            // Signal Nina that dome reconnection is starting
            if (_ninaService != null)
            {
                await _ninaService.SignalReconnectionStartAsync();
                Log(logWriter, "Nina plugin notified: reconnection started");
            }

            StopDomeProcessBestEffort(logWriter);
            Log(logWriter, $"Waiting {_config.PrePowerWaitSec}s...");
            await DelaySeconds(_config.PrePowerWaitSec, cancellationToken);

            var isOn = await shellyClient.GetSwitchOutputAsync(_config.PlugIp, _config.SwitchId, cancellationToken);
            Log(logWriter, $"Plug state before action: output={isOn}");

            if (!isOn)
            {
                Log(logWriter, "Already OFF -> turning ON only (no cycle)");
                await shellyClient.SetSwitchAsync(_config.PlugIp, _config.SwitchId, true, cancellationToken);
            }
            else
            {
                Log(logWriter, "Cycling plug: OFF -> wait -> ON");
                await shellyClient.SetSwitchAsync(_config.PlugIp, _config.SwitchId, false, cancellationToken);
                await DelaySeconds(_config.OffSeconds, cancellationToken);
                await shellyClient.SetSwitchAsync(_config.PlugIp, _config.SwitchId, true, cancellationToken);
            }

            Log(logWriter, $"Waiting {_config.PostPowerActionWaitSec}s after power action...");
            await DelaySeconds(_config.PostPowerActionWaitSec, cancellationToken);

            if (_config.HomeActionMode == HomeActionMode.WriteCachedEncoder)
            {
                await TryWriteCachedEncoderAsync(logWriter, cancellationToken);
            }

            StartDomeProcessBestEffort(logWriter);
            Log(logWriter, $"Waiting {_config.PostLaunchWaitSec}s after launch...");
            await DelaySeconds(_config.PostLaunchWaitSec, cancellationToken);

            await RunAscomSequenceAsync(logWriter, cancellationToken);

            Log(logWriter, "=== RESTART SEQUENCE END ===");

            if (_config.PostCycleGraceSec > 0)
            {
                Log(logWriter, $"Post-cycle grace: waiting {_config.PostCycleGraceSec}s");
                await DelaySeconds(_config.PostCycleGraceSec, cancellationToken);
            }

            // Signal Nina that reconnection is complete
            if (_ninaService != null)
            {
                await _ninaService.SignalReconnectionCompleteAsync();
                Log(logWriter, "Nina plugin notified: reconnection complete");
            }

            _historyService.LogRestartEnd(true);
            return true;
        }
        catch (Exception ex)
        {
            Log(logWriter, "ERROR during restart: " + ex.Message);
            
            // Signal Nina that reconnection failed
            if (_ninaService != null)
            {
                await _ninaService.SignalReconnectionFailedAsync(ex.Message);
                Log(logWriter, "Nina plugin notified: reconnection failed");
            }

            _historyService.LogRestartEnd(false, ex.Message);
            return false;
        }
        finally
        {
            if (acquired)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private void StopDomeProcessBestEffort(RestartLogWriter logWriter)
    {
        try
        {
            var processes = Process.GetProcessesByName(_config.DomeProcessName);
            if (processes.Length == 0)
            {
                Log(logWriter, $"Process not running: {_config.DomeProcessName}.exe");
                return;
            }

            Log(logWriter, $"Killing process: {_config.DomeProcessName}.exe");
            foreach (var proc in processes)
            {
                try { proc.Kill(true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log(logWriter, "Warning: failed to stop process: " + ex.Message);
        }
    }

    private void StartDomeProcessBestEffort(RestartLogWriter logWriter)
    {
        try
        {
            if (File.Exists(_config.DomeExePath))
            {
                Log(logWriter, $"Launching: {_config.DomeExePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = _config.DomeExePath,
                    UseShellExecute = true
                });
            }
            else
            {
                Log(logWriter, $"Warning: EXE not found: {_config.DomeExePath}");
            }
        }
        catch (Exception ex)
        {
            Log(logWriter, "Warning: failed to launch EXE: " + ex.Message);
        }
    }

    private async Task RunAscomSequenceAsync(RestartLogWriter logWriter, CancellationToken cancellationToken)
    {
        var overallTimeoutSec = _config.AscomDomeConnectTimeoutSec + _config.FindHomeTimeoutSec +
                                _config.AscomSwitchConnectTimeoutSec + _config.FanEnsureTimeoutSec + 30;

        var task = _staRunner.RunAsync(() =>
        {
            if (_config.HomeActionMode == HomeActionMode.AutoHome)
            {
                Log(logWriter, $"ASCOM: connecting to dome driver '{_config.AscomDomeProgId}'");
                var dome = ConnectAscomObject(_config.AscomDomeProgId, _config.AscomDomeConnectTimeoutSec, _config.AscomDomeConnectRetrySec, logWriter);
                try
                {
                    Log(logWriter, "ASCOM: connected; starting FindHome and waiting for completion...");
                    InvokeFindHomeAndWait(dome, _config.FindHomeTimeoutSec, _config.FindHomePollMs, logWriter);
                    Log(logWriter, "ASCOM: FindHome complete.");
                }
                finally
                {
                    DisconnectAndRelease(dome);
                }
            }
            else
            {
                Log(logWriter, "ASCOM: skipping FindHome (HomeActionMode=WriteCachedEncoder)");
            }

            try
            {
                Log(logWriter, $"ASCOM: connecting to switch driver '{_config.AscomSwitchProgId}'");
                var sw = ConnectAscomObject(_config.AscomSwitchProgId, _config.AscomSwitchConnectTimeoutSec, _config.AscomSwitchConnectRetrySec, logWriter);
                try
                {
                    // Restore cached switch states if available
                    var cachedStates = _switchCacheService?.GetCachedStates();
                    if (cachedStates != null && cachedStates.Count > 0)
                    {
                        Log(logWriter, $"Restoring {cachedStates.Count} cached switch states...");
                        RestoreCachedSwitchStates(sw, cachedStates, logWriter);
                    }
                    else
                    {
                        // Fallback to legacy single switch behavior
#pragma warning disable CS0618 // Type or member is obsolete
                        Log(logWriter, $"No cached states, using legacy FanSwitch index {_config.FanSwitchIndex}");
                        EnsureFanOn(sw, _config.FanSwitchIndex, _config.FanEnsureTimeoutSec, logWriter);
#pragma warning restore CS0618
                    }
                }
                finally
                {
                    DisconnectAndRelease(sw);
                }
            }
            catch (Exception ex)
            {
                Log(logWriter, "Warning: failed to ensure FanOnOff: " + ex.Message);
            }

            return true;
        }, cancellationToken);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(overallTimeoutSec), cancellationToken);
        var completed = await Task.WhenAny(task, timeoutTask);
        if (completed == timeoutTask)
        {
            throw new TimeoutException("ASCOM operations timed out.");
        }

        await task;
    }

    private object ConnectAscomObject(string progId, int timeoutSec, int retrySec, RestartLogWriter logWriter)
    {
        var sw = Stopwatch.StartNew();
        Exception? lastErr = null;

        while (true)
        {
            object? obj = null;
            try
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: true);
                obj = Activator.CreateInstance(type!);
                if (obj == null)
                {
                    throw new InvalidOperationException("Failed to create COM instance.");
                }

                obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, obj, new object[] { true });
                var connected = (bool)obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.GetProperty, null, obj, Array.Empty<object>())!;
                if (!connected)
                {
                    throw new InvalidOperationException("Connected remained false");
                }

                return obj;
            }
            catch (Exception ex)
            {
                lastErr = ex;
                if (obj != null)
                {
                    try { obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, obj, new object[] { false }); } catch { }
                    DisconnectAndRelease(obj);
                }

                Log(logWriter, $"ASCOM connect attempt failed for '{progId}': {ex.Message}");

                if (sw.Elapsed.TotalSeconds >= timeoutSec)
                {
                    throw new TimeoutException($"Timeout connecting to ASCOM '{progId}' after {timeoutSec}s. Last error: {lastErr?.Message}");
                }

                Thread.Sleep(TimeSpan.FromSeconds(retrySec));
            }
        }
    }

    private void InvokeFindHomeAndWait(object dome, int timeoutSec, int pollMs, RestartLogWriter logWriter)
    {
        var atHomeSupported = true;

        try
        {
            var canFindHome = (bool)dome.GetType().InvokeMember("CanFindHome", System.Reflection.BindingFlags.GetProperty, null, dome, Array.Empty<object>())!;
            if (!canFindHome)
            {
                throw new InvalidOperationException("Driver reports CanFindHome=false");
            }
        }
        catch (Exception ex)
        {
            Log(logWriter, $"Warning: couldn't verify CanFindHome ({ex.Message}); attempting FindHome anyway.");
        }

        try
        {
            var atHome = (bool)dome.GetType().InvokeMember("AtHome", System.Reflection.BindingFlags.GetProperty, null, dome, Array.Empty<object>())!;
            if (atHome)
            {
                Log(logWriter, "Dome already at home (AtHome=true); skipping FindHome.");
                return;
            }
        }
        catch (Exception ex)
        {
            atHomeSupported = false;
            Log(logWriter, $"Warning: couldn't read AtHome ({ex.Message}); will fall back to waiting for Slewing=false.");
        }

        Log(logWriter, "Triggering ASCOM FindHome()...");
        dome.GetType().InvokeMember("FindHome", System.Reflection.BindingFlags.InvokeMethod, null, dome, Array.Empty<object>());

        var sw = Stopwatch.StartNew();
        var lastLog = 0;

        while (true)
        {
            Thread.Sleep(pollMs);

            bool? slewing = null;
            bool? atHome = null;
            double? az = null;

            try { slewing = (bool)dome.GetType().InvokeMember("Slewing", System.Reflection.BindingFlags.GetProperty, null, dome, Array.Empty<object>())!; } catch { }
            if (atHomeSupported)
            {
                try { atHome = (bool)dome.GetType().InvokeMember("AtHome", System.Reflection.BindingFlags.GetProperty, null, dome, Array.Empty<object>())!; } catch { atHomeSupported = false; }
            }
            try { az = (double)dome.GetType().InvokeMember("Azimuth", System.Reflection.BindingFlags.GetProperty, null, dome, Array.Empty<object>())!; } catch { }

            if (atHomeSupported && atHome == true)
            {
                Log(logWriter, $"Home found (AtHome=true). Elapsed {sw.Elapsed.TotalSeconds:n1}s. Azimuth={az}");
                return;
            }

            if (!atHomeSupported && slewing == false)
            {
                Log(logWriter, $"FindHome finished (Slewing=false; AtHome unavailable). Elapsed {sw.Elapsed.TotalSeconds:n1}s. Azimuth={az}");
                return;
            }

            if (sw.Elapsed.TotalSeconds >= lastLog + 10)
            {
                lastLog = (int)sw.Elapsed.TotalSeconds;
                Log(logWriter, $"Waiting for home... elapsed {sw.Elapsed.TotalSeconds:n0}s | Slewing={slewing} AtHome={atHome} Azimuth={az}");
            }

            if (sw.Elapsed.TotalSeconds >= timeoutSec)
            {
                throw new TimeoutException($"Timeout waiting for FindHome completion after {timeoutSec}s. Slewing={slewing} AtHome={atHome} Azimuth={az}");
            }
        }
    }

    private void EnsureFanOn(object swObj, int index, int timeoutSec, RestartLogWriter logWriter)
    {
        var sw = Stopwatch.StartNew();
        Exception? lastErr = null;

        while (true)
        {
            try
            {
                try
                {
                    var canWrite = (bool)swObj.GetType().InvokeMember("CanWrite", System.Reflection.BindingFlags.InvokeMethod, null, swObj, new object[] { index })!;
                    if (!canWrite)
                    {
                        throw new InvalidOperationException($"CanWrite({index})=false");
                    }
                }
                catch (Exception ex)
                {
                    Log(logWriter, $"Warning: couldn't verify CanWrite({index}) ({ex.Message}); attempting to set anyway.");
                }

                var setOk = false;
                try
                {
                    swObj.GetType().InvokeMember("SetSwitch", System.Reflection.BindingFlags.InvokeMethod, null, swObj, new object[] { index, true });
                    setOk = true;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    Log(logWriter, $"Warning: SetSwitch({index}, true) failed: {ex.Message}");
                }

                try
                {
                    swObj.GetType().InvokeMember("SetSwitchValue", System.Reflection.BindingFlags.InvokeMethod, null, swObj, new object[] { index, 1.0 });
                    setOk = true;
                }
                catch (Exception ex)
                {
                    if (!setOk)
                    {
                        lastErr = ex;
                        Log(logWriter, $"Warning: SetSwitchValue({index}, 1) failed: {ex.Message}");
                    }
                }

                if (!setOk)
                {
                    throw new InvalidOperationException($"Failed to set fan switch {index} (both SetSwitch and SetSwitchValue failed). Last error: {lastErr?.Message}");
                }

                bool? state = null;
                double? value = null;
                try { state = (bool)swObj.GetType().InvokeMember("GetSwitch", System.Reflection.BindingFlags.InvokeMethod, null, swObj, new object[] { index })!; } catch { }
                try { value = (double)swObj.GetType().InvokeMember("GetSwitchValue", System.Reflection.BindingFlags.InvokeMethod, null, swObj, new object[] { index })!; } catch { }

                Log(logWriter, $"FanOnOff ensured: index={index} GetSwitch={state} GetSwitchValue={value}");
                return;
            }
            catch (Exception ex)
            {
                lastErr = ex;
                Log(logWriter, "ASCOM fan ensure attempt failed: " + ex.Message);

                if (sw.Elapsed.TotalSeconds >= timeoutSec)
                {
                    throw new TimeoutException($"Timeout ensuring FanOnOff (switch index {index}) after {timeoutSec}s. Last error: {lastErr?.Message}");
                }

                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }
    }

    private void RestoreCachedSwitchStates(object swObj, IReadOnlyList<CachedSwitchState> states, RestartLogWriter logWriter)
    {
        foreach (var cached in states)
        {
            try
            {
                bool setOk = false;
                
                // First check if we can write to this switch
                bool canWrite = true;
                try
                {
                    canWrite = (bool)swObj.GetType().InvokeMember("CanWrite", 
                        System.Reflection.BindingFlags.InvokeMethod, null, swObj, new object[] { cached.Index })!;
                }
                catch
                {
                    // Assume we can write if CanWrite fails
                }

                if (!canWrite)
                {
                    Log(logWriter, $"Switch {cached.Index} ({cached.Name}): CanWrite=false, skipping");
                    continue;
                }

                // Try to restore using SetSwitch first
                if (cached.State.HasValue)
                {
                    try
                    {
                        swObj.GetType().InvokeMember("SetSwitch", 
                            System.Reflection.BindingFlags.InvokeMethod, null, swObj, 
                            new object[] { cached.Index, cached.State.Value });
                        setOk = true;
                        Log(logWriter, $"Restored switch {cached.Index} ({cached.Name}) State={cached.State.Value}");
                    }
                    catch (Exception ex)
                    {
                        Log(logWriter, $"SetSwitch({cached.Index}, {cached.State.Value}) failed: {ex.Message}");
                    }
                }

                // If SetSwitch failed and we have a value, try SetSwitchValue
                if (!setOk && cached.Value.HasValue)
                {
                    try
                    {
                        swObj.GetType().InvokeMember("SetSwitchValue", 
                            System.Reflection.BindingFlags.InvokeMethod, null, swObj, 
                            new object[] { cached.Index, cached.Value.Value });
                        setOk = true;
                        Log(logWriter, $"Restored switch {cached.Index} ({cached.Name}) Value={cached.Value.Value}");
                    }
                    catch (Exception ex)
                    {
                        Log(logWriter, $"SetSwitchValue({cached.Index}, {cached.Value.Value}) failed: {ex.Message}");
                    }
                }

                if (!setOk)
                {
                    Log(logWriter, $"Warning: Failed to restore switch {cached.Index} ({cached.Name})");
                }
            }
            catch (Exception ex)
            {
                Log(logWriter, $"Error restoring switch {cached.Index}: {ex.Message}");
            }
        }
    }

    private void DisconnectAndRelease(object obj)
    {
        try { obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, obj, new object[] { false }); } catch { }
        try { Marshal.FinalReleaseComObject(obj); } catch { }
    }

    private void Log(RestartLogWriter writer, string message)
    {
        writer.WriteLine(message);
    }

    private static Task DelaySeconds(int seconds, CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    private async Task TryWriteCachedEncoderAsync(RestartLogWriter logWriter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.DomeHttpIp))
        {
            Log(logWriter, "Encoder write skipped: Dome HTTP IP is not set.");
            return;
        }

        if (!_config.CachedEncoderValue.HasValue)
        {
            Log(logWriter, "Encoder write skipped: no cached encoder value available.");
            return;
        }

        try
        {
            var client = new ScopeDomeHttpClient(TimeSpan.FromSeconds(_config.HttpTimeoutSec), _config.DomeHttpUsername, _config.DomeHttpPassword);
            var targetValue = _config.CachedEncoderValue.Value;
            Log(logWriter, $"Writing cached encoder value {targetValue} via HTTP...");
            var response = await client.SetEncoderValueAsync(_config.DomeHttpIp, targetValue, cancellationToken);
            var preview = response.Length > 120 ? response.Substring(0, 120) + "..." : response;
            Log(logWriter, $"Encoder write response: {preview}");

            // Verify encoder write succeeded
            Log(logWriter, "Waiting 5 seconds before verification read...");
            await DelaySeconds(5, cancellationToken);

            var readBack = await client.GetEncoderValueAsync(_config.DomeHttpIp, cancellationToken);
            if (!readBack.HasValue)
            {
                Log(logWriter, "Encoder verification FAILED: could not read encoder value after write");
            }
            else
            {
                var tolerance = 5;
                var readValue = readBack.Value;
                var difference = Math.Abs(readValue - targetValue);
                var verified = difference <= tolerance;
                var status = verified ? "PASSED" : "FAILED";
                Log(logWriter, $"Encoder verification {status}: wrote {targetValue}, read back {readValue}, difference={difference} (tolerance=±{tolerance})");
            }
        }
        catch (Exception ex)
        {
            Log(logWriter, "Encoder write failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _staRunner.Dispose();
    }
}
