using System;
using System.Collections;
using System.Runtime.InteropServices;

// Quick test: dotnet run --project ScopeDomeWatchdog.Core test_ascom.cs
object? profile = null;
try
{
    var type = Type.GetTypeFromProgID("ASCOM.Utilities.Profile", throwOnError: true);
    profile = Activator.CreateInstance(type!);
    
    Console.WriteLine("Profile COM object created successfully.\n");
    
    foreach (var deviceType in new[] { "Dome", "Switch", "Focuser", "Telescope", "Camera", "Handpad", "SafetyMonitor" })
    {
        try
        {
            var registrations = (IEnumerable)profile.GetType().InvokeMember(
                "RegisteredDevices",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                profile,
                new object[] { deviceType });
            
            int count = 0;
            Console.WriteLine($"\n{deviceType}:");
            foreach (var reg in registrations)
            {
                count++;
                var name = (string)reg.GetType().GetProperty("Name")?.GetValue(reg) ?? string.Empty;
                var progId = (string)reg.GetType().GetProperty("ProgId")?.GetValue(reg) ?? string.Empty;
                Console.WriteLine($"  {count}. {name} ({progId})");
            }
            if (count == 0)
                Console.WriteLine("  (none)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n{deviceType}: Error - {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    if (profile != null)
        Marshal.FinalReleaseComObject(profile);
}
