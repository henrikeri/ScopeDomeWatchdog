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
using Microsoft.Win32;

namespace ScopeDomeWatchdog.Core.Services;

/// <summary>
/// Direct registry-based ASCOM device enumeration as a fallback/diagnostic.
/// </summary>
public sealed class AscomRegistryDiagnostics
{
    /// <summary>
    /// Enumerate devices from ASCOM registry without using Profile COM object.
    /// </summary>
    public IReadOnlyList<(string Name, string ProgId)> GetDevicesFromRegistry(string deviceType)
    {
        var results = new List<(string Name, string ProgId)>();

        try
        {
            // ASCOM devices are registered at HKLM\Software\ASCOM\[DeviceType]\[ProgID]
            var baseKey = Registry.LocalMachine.OpenSubKey($@"Software\ASCOM\{deviceType}");
            if (baseKey == null)
                return results;

            foreach (var subkeyName in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var subkey = baseKey.OpenSubKey(subkeyName);
                    if (subkey == null) continue;

                    // Try to get the descriptive name
                    var name = (string?)subkey.GetValue("Description") ?? 
                               (string?)subkey.GetValue("Name") ?? 
                               subkeyName;
                    
                    results.Add((name, subkeyName));
                }
                catch
                {
                    // Skip problematic entries
                    results.Add((subkeyName, subkeyName));
                }
            }
        }
        catch
        {
            // Registry access failed
        }

        return results;
    }

    /// <summary>
    /// Try the Profile COM object but with more detailed error reporting.
    /// </summary>
    public (bool Success, string Message, IReadOnlyList<(string Name, string ProgId)> Devices) TryProfileComObject(string deviceType)
    {
        var results = new List<(string Name, string ProgId)>();
        object? profile = null;

        try
        {
            var type = Type.GetTypeFromProgID("ASCOM.Utilities.Profile", throwOnError: false);
            if (type == null)
                return (false, "ASCOM.Utilities.Profile ProgID not found in registry", results);

            profile = Activator.CreateInstance(type);
            if (profile == null)
                return (false, "Failed to instantiate ASCOM.Utilities.Profile", results);

            // Get the registrations - try both Method and Property
            object? registrations = null;
            try
            {
                // Try as method first
                registrations = profile.GetType().InvokeMember(
                    "RegisteredDevices",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    profile,
                    new object[] { deviceType });
            }
            catch
            {
                try
                {
                    // Try as property with method call
                    registrations = profile.GetType().InvokeMember(
                        "RegisteredDevices",
                        System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        profile,
                        new object[] { deviceType });
                }
                catch
                {
                    return (false, $"Could not call RegisteredDevices for {deviceType}", results);
                }
            }

            if (registrations == null)
                return (false, $"RegisteredDevices returned null for {deviceType}", results);

            if (registrations is not System.Collections.IEnumerable enumerable)
                return (false, $"RegisteredDevices result is not enumerable (type: {registrations.GetType().Name})", results);

            int count = 0;
            foreach (var reg in enumerable)
            {
                count++;
                if (reg == null) continue;

                try
                {
                    var name = (string?)reg.GetType().GetProperty("Name")?.GetValue(reg) ?? string.Empty;
                    var progId = (string?)reg.GetType().GetProperty("ProgId")?.GetValue(reg) ?? string.Empty;

                    if (!string.IsNullOrEmpty(progId))
                        results.Add((name, progId));
                }
                catch
                {
                    // Skip problematic entries
                }
            }

            return (true, $"Successfully enumerated {count} device(s), {results.Count} valid", results);
        }
        catch (Exception ex)
        {
            return (false, $"COM Error: {ex.GetType().Name}: {ex.Message}", results);
        }
        finally
        {
            if (profile != null)
            {
                try { Marshal.FinalReleaseComObject(profile); }
                catch { }
            }
        }
    }
}
