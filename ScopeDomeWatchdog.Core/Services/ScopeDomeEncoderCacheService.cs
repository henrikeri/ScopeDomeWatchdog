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
using ScopeDomeWatchdog.Core.Models;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class ScopeDomeEncoderCacheService : IDisposable
{
    private readonly WatchdogConfig _config;
    private readonly string _configPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private Task? _loopTask;
    private Func<bool>? _isRestartInProgress;

    /// <summary>Fires when encoder value is updated</summary>
    public event Action? EncoderUpdated;

    public ScopeDomeEncoderCacheService(WatchdogConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;
    }

    /// <summary>
    /// Sets a callback to check if a restart is in progress (to pause encoding caching).
    /// </summary>
    public void SetRestartCheckCallback(Func<bool> callback)
    {
        _isRestartInProgress = callback;
    }

    public void Start()
    {
        if (_loopTask != null)
        {
            return;
        }

        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void RequestImmediateRefresh()
    {
        _ = Task.Run(() => ReadAndCacheOnceAsync(CancellationToken.None));
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        await ReadAndCacheOnceAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var seconds = Math.Max(1, _config.EncoderPollSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Skip caching during restart
            if (_isRestartInProgress?.Invoke() == true)
            {
                continue;
            }

            await ReadAndCacheOnceAsync(cancellationToken);
        }
    }

    private async Task ReadAndCacheOnceAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.DomeHttpIp))
        {
            return;
        }

        try
        {
            var client = new ScopeDomeHttpClient(TimeSpan.FromSeconds(_config.HttpTimeoutSec), _config.DomeHttpUsername, _config.DomeHttpPassword);
            var value = await client.GetEncoderValueAsync(_config.DomeHttpIp, cancellationToken);
            if (!value.HasValue)
            {
                return;
            }

            lock (_sync)
            {
                _config.CachedEncoderValue = value.Value;
                _config.CachedEncoderUtc = DateTime.Now;
                var persisted = ConfigService.LoadOrCreate(_configPath);
                persisted.CachedEncoderValue = _config.CachedEncoderValue;
                persisted.CachedEncoderUtc = _config.CachedEncoderUtc;
                ConfigService.Save(persisted, _configPath);
            }

            // Notify UI that encoder was updated
            EncoderUpdated?.Invoke();
        }
        catch
        {
            // swallow background polling errors
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _cts.Dispose();
    }
}
