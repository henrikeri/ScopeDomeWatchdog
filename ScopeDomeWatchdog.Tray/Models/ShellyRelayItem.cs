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

namespace ScopeDomeWatchdog.Tray.Models;

public sealed class ShellyRelayItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }

    public string Display => $"Relay {Id}: {Name}" + (IsAvailable ? "" : " (unavailable)");
}
