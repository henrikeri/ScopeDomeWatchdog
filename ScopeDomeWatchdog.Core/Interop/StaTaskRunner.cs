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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Interop;

public sealed class StaTaskRunner : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaTaskRunner(string name = "StaComRunner")
    {
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = name
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public Task<T> RunAsync<T>(Func<T> func, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                var result = func();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }
}
