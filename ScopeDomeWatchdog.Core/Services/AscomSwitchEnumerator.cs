using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScopeDomeWatchdog.Core.Interop;

namespace ScopeDomeWatchdog.Core.Services;

public sealed class AscomSwitchEnumerator
{
    private readonly StaTaskRunner _staRunner;

    public AscomSwitchEnumerator(StaTaskRunner staRunner)
    {
        _staRunner = staRunner;
    }

    public Task<IReadOnlyList<AscomSwitchInfo>> GetSwitchesAsync(string progId, CancellationToken cancellationToken)
    {
        return _staRunner.RunAsync(() =>
        {
            var results = new List<AscomSwitchInfo>();
            object? sw = null;
            try
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: true);
                sw = Activator.CreateInstance(type!);
                if (sw == null)
                {
                    return (IReadOnlyList<AscomSwitchInfo>)results;
                }

                sw.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, sw, new object[] { true });

                var maxSwitchObj = sw.GetType().InvokeMember("MaxSwitch", System.Reflection.BindingFlags.GetProperty, null, sw, Array.Empty<object>());
                var maxSwitch = Convert.ToInt32(maxSwitchObj);
                for (var i = 0; i < maxSwitch; i++)
                {
                    string name;
                    try { name = (string)sw.GetType().InvokeMember("GetSwitchName", System.Reflection.BindingFlags.InvokeMethod, null, sw, new object[] { i })!; }
                    catch { name = string.Empty; }

                    bool? canWrite = null;
                    try { canWrite = (bool)sw.GetType().InvokeMember("CanWrite", System.Reflection.BindingFlags.InvokeMethod, null, sw, new object[] { i })!; }
                    catch { }

                    bool? state = null;
                    try { state = (bool)sw.GetType().InvokeMember("GetSwitch", System.Reflection.BindingFlags.InvokeMethod, null, sw, new object[] { i })!; }
                    catch { }

                    double? value = null;
                    try { value = (double)sw.GetType().InvokeMember("GetSwitchValue", System.Reflection.BindingFlags.InvokeMethod, null, sw, new object[] { i })!; }
                    catch { }

                    results.Add(new AscomSwitchInfo
                    {
                        Index = i,
                        Name = name!,
                        CanWrite = canWrite,
                        State = state,
                        Value = value
                    });
                }

                return results;
            }
            finally
            {
                if (sw != null)
                {
                    try { sw.GetType().InvokeMember("Connected", System.Reflection.BindingFlags.SetProperty, null, sw, new object[] { false }); } catch { }
                    try { Marshal.FinalReleaseComObject(sw); } catch { }
                }
            }
        }, cancellationToken);
    }
}
