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

namespace ScopeDomeWatchdog.Core.Services;

public sealed class AscomProfileService
{
    public IReadOnlyList<(string Name, string ProgId)> GetRegisteredDevices(string deviceType)
    {
        var results = new List<(string Name, string ProgId)>();
        object? profile = null;

        try
        {
            var type = Type.GetTypeFromProgID("ASCOM.Utilities.Profile", throwOnError: true);
            if (type == null)
                return results;

            profile = Activator.CreateInstance(type);
            if (profile == null)
                return results;

            // RegisteredDevices returns an IEnumerable of Key/Value pairs
            var devices = (System.Collections.IEnumerable)profile.GetType().InvokeMember(
                "RegisteredDevices",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                profile,
                new object[] { deviceType })!;

            foreach (var device in devices)
            {
                if (device == null)
                    continue;

                try
                {
                    // Each item is a COM Key/Value pair - use InvokeMember to access properties
                    var progId = (string)device.GetType().InvokeMember(
                        "Key",
                        System.Reflection.BindingFlags.GetProperty,
                        null,
                        device,
                        null)! ?? string.Empty;
                        
                    var name = (string?)device.GetType().InvokeMember(
                        "Value",
                        System.Reflection.BindingFlags.GetProperty,
                        null,
                        device,
                        null) ?? string.Empty;

                    if (!string.IsNullOrEmpty(progId))
                        results.Add((name, progId));
                }
                catch
                {
                    // Skip problematic entries
                }
            }
        }
        catch
        {
            // Return empty list on error
        }
        finally
        {
            if (profile != null)
            {
                try { Marshal.FinalReleaseComObject(profile); }
                catch { }
            }
        }

        return results;
    }
}
