using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Interop;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class AscomConnectionTester
{
    private readonly StaTaskRunner _staRunner;

    public AscomConnectionTester(StaTaskRunner staRunner)
    {
        _staRunner = staRunner;
    }

    public Task<string> TestAsync(string domeProgId, string switchProgId, int timeoutSec, CancellationToken cancellationToken)
    {
        return _staRunner.RunAsync(() =>
        {
            var sw = Stopwatch.StartNew();
            var result = "";

            if (!string.IsNullOrWhiteSpace(domeProgId))
            {
                result += TestSingle(domeProgId, "Dome");
            }
            if (!string.IsNullOrWhiteSpace(switchProgId))
            {
                result += TestSingle(switchProgId, "Switch");
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                result = "No ASCOM ProgID configured.";
            }

            if (sw.Elapsed.TotalSeconds > timeoutSec)
            {
                result += $" (Exceeded {timeoutSec}s timeout)";
            }

            return result.Trim();
        }, cancellationToken);
    }

    private static string TestSingle(string progId, string label)
    {
        object? obj = null;
        try
        {
            var type = Type.GetTypeFromProgID(progId, throwOnError: true);
            obj = Activator.CreateInstance(type!);
            if (obj == null)
            {
                return $"{label} '{progId}': failed to create COM instance. ";
            }

            obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, obj, new object[] { true });
            var connected = (bool)obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.GetProperty, null, obj, Array.Empty<object>());
            return $"{label} '{progId}': Connected={connected}. ";
        }
        catch (Exception ex)
        {
            return $"{label} '{progId}': {ex.Message}. ";
        }
        finally
        {
            if (obj != null)
            {
                try { obj.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, obj, new object[] { false }); } catch { }
                try { Marshal.FinalReleaseComObject(obj); } catch { }
            }
        }
    }
}
