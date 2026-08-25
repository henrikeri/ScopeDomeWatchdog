// ScopeDome Watchdog - Automated recovery system for ScopeDome observatory domes
// Copyright (C) 2026
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Core.Services;

public sealed record ShellyBleScanResult(
    string Address,
    string AdvertisedName,
    short SignalStrengthDbm,
    string? AssignedName = null)
{
    public string PreferredName =>
        !string.IsNullOrWhiteSpace(AssignedName)
            ? AssignedName
            : AdvertisedName;

    public string DisplayLabel =>
        $"{(string.IsNullOrWhiteSpace(PreferredName) ? "(unnamed BLE device)" : PreferredName)}" +
        (!string.IsNullOrWhiteSpace(AssignedName) &&
         !string.IsNullOrWhiteSpace(AdvertisedName) &&
         !string.Equals(AssignedName, AdvertisedName, StringComparison.OrdinalIgnoreCase)
            ? $"  [{AdvertisedName}]"
            : string.Empty) +
        "  |  " +
        $"{Address}  |  {SignalStrengthDbm} dBm";
}

public interface IShellyBleScanner
{
    Task<IReadOnlyList<ShellyBleScanResult>> ScanAsync(
        TimeSpan duration,
        CancellationToken cancellationToken);
}
