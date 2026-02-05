using System;
using System.Collections;
using System.Runtime.InteropServices;

var profile = Activator.CreateInstance(Type.GetTypeFromProgID("ASCOM.Utilities.Profile", throwOnError: true)!);

Console.WriteLine("Profile COM object created successfully.\n");

foreach (var deviceType in new[] { "Dome", "Switch" })
{
    Console.WriteLine($"\n{deviceType}:");
    
    var registrations = (IEnumerable)profile!.GetType().InvokeMember(
        "RegisteredDevices",
        System.Reflection.BindingFlags.InvokeMethod,
        null,
        profile,
        new object[] { deviceType });

    int count = 0;
    foreach (var device in registrations)
    {
        count++;
        try
        {
            // Use InvokeMember to get Key and Value from COM object
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
            
            Console.WriteLine($"  {count}. {name} ({progId})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {count}. Error: {ex.Message}");
        }
    }
}

if (profile != null)
    Marshal.FinalReleaseComObject(profile);
