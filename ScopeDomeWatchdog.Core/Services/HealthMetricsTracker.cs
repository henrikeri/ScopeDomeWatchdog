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
using System.Linq;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class HealthMetricsTracker
{
    private readonly CircularBuffer<LatencyDataPoint> _latencyHistory;
    private readonly object _sync = new();

    public int MaxHistoryPoints { get; }

    public HealthMetricsTracker(int maxHistoryPoints = 1440) // ~1 hour at 1 sample per 2.5 seconds
    {
        MaxHistoryPoints = maxHistoryPoints;
        _latencyHistory = new CircularBuffer<LatencyDataPoint>(maxHistoryPoints);
    }

    public void RecordPing(int? latencyMs, bool success)
    {
        lock (_sync)
        {
            _latencyHistory.Add(new LatencyDataPoint
            {
                TimestampUtc = DateTime.UtcNow,
                LatencyMs = latencyMs ?? 0,
                Success = success
            });
        }
    }

    public List<LatencyDataPoint> GetLatencyHistory()
    {
        lock (_sync)
        {
            return _latencyHistory.GetAll();
        }
    }

    public (double? avg, int? min, int? max) GetLatencyStats()
    {
        lock (_sync)
        {
            var points = _latencyHistory.GetAll().Where(p => p.Success).ToList();
            if (points.Count == 0)
                return (null, null, null);

            var latencies = points.Select(p => p.LatencyMs).ToList();
            return (
                latencies.Average(),
                latencies.Min(),
                latencies.Max()
            );
        }
    }

    public double GetSuccessRate()
    {
        lock (_sync)
        {
            var points = _latencyHistory.GetAll();
            if (points.Count == 0)
                return 0;
            return points.Count(p => p.Success) / (double)points.Count;
        }
    }
}

public sealed class LatencyDataPoint
{
    public DateTime TimestampUtc { get; set; }
    public int LatencyMs { get; set; }
    public bool Success { get; set; }
}

// Simple circular buffer implementation
public sealed class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _head = 0;
    private int _count = 0;

    public CircularBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
    }

    public List<T> GetAll()
    {
        var result = new List<T>(_count);
        for (int i = 0; i < _count; i++)
        {
            var index = (_head - _count + i + _buffer.Length) % _buffer.Length;
            result.Add(_buffer[index]);
        }
        return result;
    }
}
