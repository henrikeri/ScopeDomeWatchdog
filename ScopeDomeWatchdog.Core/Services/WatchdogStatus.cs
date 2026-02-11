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

namespace ScopeDomeWatchdog.Core.Services;

public sealed class WatchdogStatus
{
    public DateTime StartTimeUtc { get; set; } = DateTime.Now;
    public bool LastPingOk { get; set; }
    public int? LastPingMs { get; set; }
    public double? AveragePingMs { get; set; }
    public int ConsecutiveFails { get; set; }
    public int TotalPings { get; set; }
    public int OkPings { get; set; }
    public DateTime? LastRestartUtc { get; set; }
    public bool ManualTriggerSet { get; set; }
    public TimeSpan? CooldownRemaining { get; set; }

    public WatchdogStatus Clone()
    {
        return (WatchdogStatus)MemberwiseClone();
    }
}
