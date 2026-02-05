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
                new object[] { deviceType });

            foreach (var device in devices)
            {
                if (device == null)
                    continue;

                try
                {
                    // Each item is a COM Key/Value pair - use InvokeMember to access properties
                    var progId = (string?)device.GetType().InvokeMember(
                        "Key",
                        System.Reflection.BindingFlags.GetProperty,
                        null,
                        device,
                        null) ?? string.Empty;
                        
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
