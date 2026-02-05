#region "copyright"
/*
    ScopeDome Watchdog NINA Plugin
    Copyright (c) 2026 Henrik Erevik Riise
    
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Nina.Instructions
{
    [ExportMetadata("Name", "Dome Reconnection Wait")]
    [ExportMetadata("Description", "Waits for ScopeDome reconnection to complete, then runs child instructions")]
    [ExportMetadata("Icon", "ClockSVG")]
    [ExportMetadata("Category", "ScopeDome Watchdog")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    public class DomeReconnectionWaitInstruction : SequenceContainer
    {
        // Named events for cross-process communication
        private const string RECONNECTION_STARTED_EVENT = @"Global\ScopeDome_ReconnectionStarted";
        private const string RECONNECTION_COMPLETE_EVENT = @"Global\ScopeDome_ReconnectionComplete";

        // Debug log path
        private static readonly string DebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "ScopeDome_Wait_Debug.log");

        // Configuration properties
        private int _timeoutMinutes = 10;
        
        [Category("Reconnection")]
        [DisplayName("Timeout (minutes)")]
        [Description("Maximum time to wait for dome reconnection before failing gracefully. Set to 0 to wait indefinitely.")]
        [DefaultValue(10)]
        public int TimeoutMinutes
        {
            get => _timeoutMinutes;
            set
            {
                if (_timeoutMinutes != value)
                {
                    _timeoutMinutes = value;
                    RaisePropertyChanged();
                }
            }
        }

        [ImportingConstructor]
        public DomeReconnectionWaitInstruction() : base(new SequentialStrategy())
        {
            LogDebug("DomeReconnectionWaitInstruction constructed");
        }

        private DomeReconnectionWaitInstruction(DomeReconnectionWaitInstruction cloneMe) : this()
        {
            CopyMetaData(cloneMe);
            TimeoutMinutes = cloneMe.TimeoutMinutes;
        }

        public override object Clone()
        {
            var clone = new DomeReconnectionWaitInstruction(this)
            {
                Icon = Icon
            };

            foreach (var item in Items)
            {
                clone.Add((ISequenceItem)item.Clone());
            }

            return clone;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            LogDebug($"Execute: Starting (timeout: {TimeoutMinutes} minutes, {Items.Count} child items)");

            // Check if reconnection is active
            bool reconnectionActive = false;
            try
            {
                using var startedEvent = EventWaitHandle.OpenExisting(RECONNECTION_STARTED_EVENT);
                reconnectionActive = startedEvent.WaitOne(0);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                LogDebug("Execute: No reconnection event found, proceeding with children");
            }

            if (reconnectionActive)
            {
                LogDebug("Execute: Reconnection active, waiting for completion");
                await WaitForReconnection(progress, token);
            }
            else
            {
                LogDebug("Execute: No reconnection active, skipping wait");
            }

            // Execute child items
            if (Items.Count > 0)
            {
                LogDebug($"Execute: Running {Items.Count} child items");
                foreach (var item in Items)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        await item.Run(progress, token);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Execute: Child item failed: {ex.Message}");
                        throw;
                    }
                }
            }
            else
            {
                LogDebug("Execute: No child items to run");
            }

            LogDebug("Execute: Complete");
        }

        private async Task WaitForReconnection(IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            var startTime = DateTime.Now;
            var timeoutSpan = TimeoutMinutes > 0 ? TimeSpan.FromMinutes(TimeoutMinutes) : TimeSpan.MaxValue;

            progress.Report(new ApplicationStatus
            {
                Status = $"Waiting for ScopeDome reconnection... (timeout: {TimeoutMinutes} min)",
                Source = "ScopeDome Watchdog"
            });

            try
            {
                using var completeEvent = EventWaitHandle.OpenExisting(RECONNECTION_COMPLETE_EVENT);

                while (!token.IsCancellationRequested)
                {
                    // Check timeout
                    var elapsed = DateTime.Now - startTime;
                    if (elapsed >= timeoutSpan)
                    {
                        LogDebug($"WaitForReconnection: Timeout reached ({TimeoutMinutes} minutes)");
                        Logger.Warning($"ScopeDome reconnection exceeded {TimeoutMinutes} minute timeout. Continuing anyway.");
                        progress.Report(new ApplicationStatus
                        {
                            Status = $"ScopeDome reconnection timeout ({TimeoutMinutes} min) - continuing",
                            Source = "ScopeDome Watchdog"
                        });
                        return;
                    }

                    if (completeEvent.WaitOne(1000))
                    {
                        LogDebug("WaitForReconnection: Complete event received");
                        break;
                    }

                    // Update progress with elapsed time
                    var elapsedStr = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
                    progress.Report(new ApplicationStatus
                    {
                        Status = $"Waiting for ScopeDome reconnection... ({elapsedStr} elapsed)",
                        Source = "ScopeDome Watchdog"
                    });

                    // Check if started event is still set
                    try
                    {
                        using var startedEvent = EventWaitHandle.OpenExisting(RECONNECTION_STARTED_EVENT);
                        if (!startedEvent.WaitOne(0))
                        {
                            LogDebug("WaitForReconnection: Started event cleared");
                            break;
                        }
                    }
                    catch
                    {
                        LogDebug("WaitForReconnection: Started event gone");
                        break;
                    }
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                LogDebug("WaitForReconnection: Complete event doesn't exist, monitoring started event");

                while (!token.IsCancellationRequested)
                {
                    var elapsed = DateTime.Now - startTime;
                    if (elapsed >= timeoutSpan)
                    {
                        LogDebug($"WaitForReconnection: Timeout reached ({TimeoutMinutes} minutes)");
                        Logger.Warning($"ScopeDome reconnection exceeded {TimeoutMinutes} minute timeout.");
                        return;
                    }

                    await Task.Delay(1000, token);

                    var elapsedStr = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
                    progress.Report(new ApplicationStatus
                    {
                        Status = $"Waiting for ScopeDome reconnection... ({elapsedStr} elapsed)",
                        Source = "ScopeDome Watchdog"
                    });

                    try
                    {
                        using var startedEvent = EventWaitHandle.OpenExisting(RECONNECTION_STARTED_EVENT);
                        if (!startedEvent.WaitOne(0))
                        {
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            progress.Report(new ApplicationStatus
            {
                Status = "ScopeDome reconnection complete",
                Source = "ScopeDome Watchdog"
            });

            await Task.Delay(2000, token);
        }

        public override string ToString()
        {
            return $"Dome Reconnection Wait (Timeout: {TimeoutMinutes}min, {Items.Count} items)";
        }

        private static void LogDebug(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(DebugLogPath, logMessage + Environment.NewLine);
                Logger.Info($"[DomeReconnectionWait] {message}");
            }
            catch { }
        }
    }
}
