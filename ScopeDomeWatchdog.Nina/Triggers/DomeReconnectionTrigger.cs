#region "copyright"
/*
    ScopeDome Watchdog NINA Plugin
    Copyright (c) 2026 henrikeri
    
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
    
    This plugin uses the ConditionWatchdog pattern and sequence interrupt/restart
    approach from the "When" plugin by Stefan Berg (isbeorn86+NINA@googlemail.com)
    and the N.I.N.A. contributors. See: https://github.com/isbeorn/nina.plugin.when
*/
#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Nina.Triggers
{
    [ExportMetadata("Name", "Dome Reconnection Trigger")]
    [ExportMetadata("Description", "Pauses the sequence when ScopeDome reconnection is detected and resumes when complete.")]
    [ExportMetadata("Icon", "RefreshSVG")]
    [ExportMetadata("Category", "ScopeDome Watchdog")]
    [Export(typeof(ISequenceTrigger))]
    public class DomeReconnectionTrigger : SequenceTrigger
    {
        // Named events for cross-process communication
        private const string RECONNECTION_STARTED_EVENT = @"Global\ScopeDome_ReconnectionStarted";
        private const string RECONNECTION_COMPLETE_EVENT = @"Global\ScopeDome_ReconnectionComplete";

        // Debug log path
        private static readonly string DebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "ScopeDome_Trigger_Debug.log");

        // Static state shared across all trigger instances (NINA clones triggers)
        private static bool _reconnectionInProgress = false;
        private static bool _inFlight = false;
        private static bool _triggered = false;
        private static bool _critical = false;

        // Mediators injected by MEF
        private readonly ISequenceMediator sequenceMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;

        // Background monitoring
        private ConditionWatchdog? ConditionWatchdog { get; set; }

        // Configuration properties
        private int _timeoutMinutes = 10;
        
        /// <summary>
        /// Maximum time to wait for reconnection before failing gracefully (in minutes).
        /// Set to 0 to wait indefinitely.
        /// </summary>
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
                    base.RaisePropertyChanged();
                }
            }
        }

        [ImportingConstructor]
        public DomeReconnectionTrigger(
            ISequenceMediator sequenceMediator,
            IApplicationStatusMediator applicationStatusMediator)
        {
            this.sequenceMediator = sequenceMediator;
            this.applicationStatusMediator = applicationStatusMediator;

            // Create watchdog that polls every 5 seconds (same as "When" plugin)
            ConditionWatchdog = new ConditionWatchdog(InterruptWhenReconnecting, TimeSpan.FromSeconds(5));

            LogDebug("DomeReconnectionTrigger constructed");
        }

        // Copy constructor for cloning
        protected DomeReconnectionTrigger(DomeReconnectionTrigger cloneMe)
            : this(cloneMe.sequenceMediator, cloneMe.applicationStatusMediator)
        {
            CopyMetaData(cloneMe);
            TimeoutMinutes = cloneMe.TimeoutMinutes;
        }

        public override object Clone()
        {
            return new DomeReconnectionTrigger(this);
        }

        public override void SequenceBlockInitialize()
        {
            LogDebug("SequenceBlockInitialize - starting watchdog");
            ConditionWatchdog?.Start();
        }

        public override void SequenceBlockTeardown()
        {
            LogDebug("SequenceBlockTeardown - stopping watchdog");
            try { ConditionWatchdog?.Cancel(); } catch { }
        }

        public override void AfterParentChanged()
        {
            if (Parent == null)
            {
                SequenceBlockTeardown();
            }
            else if (Parent.Status == SequenceEntityStatus.RUNNING)
            {
                SequenceBlockInitialize();
            }
        }

        /// <summary>
        /// Background task that runs every 5 seconds to check for reconnection events.
        /// Uses the same pattern as "When" plugin's InterruptWhen method.
        /// </summary>
        private async Task InterruptWhenReconnecting()
        {
            // Check if reconnection event is signaled
            bool eventSignaled = false;
            try
            {
                using var startedEvent = EventWaitHandle.OpenExisting(RECONNECTION_STARTED_EVENT);
                eventSignaled = startedEvent.WaitOne(0);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Event doesn't exist - no reconnection in progress
                return;
            }

            if (!eventSignaled)
            {
                return; // Not triggered
            }

            LogDebug($"InterruptWhenReconnecting: Event signaled, inFlight={_inFlight}, triggered={_triggered}, critical={_critical}");

            if (!sequenceMediator.Initialized) return;
            if (!sequenceMediator.IsAdvancedSequenceRunning()) return;
            
            // Prevent re-entry (same as "When" plugin)
            if (_inFlight || _triggered)
            {
                LogDebug("InterruptWhenReconnecting: Already inFlight or triggered, returning");
                return;
            }

            if (Parent != null &&
                ItemUtility.IsInRootContainer(Parent) &&
                Parent.Status == SequenceEntityStatus.RUNNING &&
                Status != SequenceEntityStatus.DISABLED)
            {
                _triggered = true;
                _reconnectionInProgress = true;
                LogDebug("InterruptWhenReconnecting: Canceling sequence for reconnection...");

                _critical = true;
                try
                {
                    // Cancel the sequence (stops current exposure)
                    sequenceMediator.CancelAdvancedSequence();
                    LogDebug("InterruptWhenReconnecting: CancelAdvancedSequence called, waiting for stop...");

                    await Task.Delay(1000);
                    while (sequenceMediator.Initialized && sequenceMediator.IsAdvancedSequenceRunning())
                    {
                        LogDebug("InterruptWhenReconnecting: Still running, waiting...");
                        await Task.Delay(1000);
                    }
                    LogDebug("InterruptWhenReconnecting: Sequence stopped");
                }
                finally
                {
                    _critical = false;
                }

                // Restart the sequence (it will trigger Execute which waits for reconnection)
                await sequenceMediator.StartAdvancedSequence(true);
                LogDebug("InterruptWhenReconnecting: StartAdvancedSequence called");
            }
        }

        /// <summary>
        /// Called by NINA to check if trigger should fire.
        /// Returns true when reconnection is in progress.
        /// </summary>
        public override bool ShouldTrigger(ISequenceItem? previousItem, ISequenceItem? nextItem)
        {
            if (_inFlight)
            {
                LogDebug("ShouldTrigger: FALSE (already inFlight)");
                return false;
            }

            // Check if we detected a reconnection via the background task
            if (_reconnectionInProgress)
            {
                LogDebug("ShouldTrigger: TRUE (reconnection in progress)");
                return true;
            }

            // Also check the event directly (for when called between items)
            try
            {
                using var startedEvent = EventWaitHandle.OpenExisting(RECONNECTION_STARTED_EVENT);
                if (startedEvent.WaitOne(0))
                {
                    _reconnectionInProgress = true;
                    LogDebug("ShouldTrigger: TRUE (event signaled)");
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Event doesn't exist
            }

            return false;
        }

        /// <summary>
        /// Called when trigger fires. Waits for reconnection to complete.
        /// </summary>
        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            LogDebug($"Execute: Starting wait for reconnection (timeout: {TimeoutMinutes} minutes)");

            if (_critical)
            {
                LogDebug("Execute: In critical section, returning");
                return;
            }

            if (_inFlight)
            {
                LogDebug("Execute: Already inFlight, returning");
                return;
            }

            try
            {
                _inFlight = true;
                _triggered = false;

                var startTime = DateTime.Now;
                var timeoutSpan = TimeoutMinutes > 0 ? TimeSpan.FromMinutes(TimeoutMinutes) : TimeSpan.MaxValue;

                progress.Report(new ApplicationStatus
                {
                    Status = $"Waiting for ScopeDome reconnection... (timeout: {TimeoutMinutes} min)",
                    Source = "ScopeDome Watchdog"
                });

                // Wait for the reconnection complete event
                try
                {
                    using var completeEvent = EventWaitHandle.OpenExisting(RECONNECTION_COMPLETE_EVENT);

                    while (!token.IsCancellationRequested)
                    {
                        // Check timeout
                        var elapsed = DateTime.Now - startTime;
                        if (elapsed >= timeoutSpan)
                        {
                            LogDebug($"Execute: Timeout reached ({TimeoutMinutes} minutes), failing gracefully");
                            Logger.Warning($"ScopeDome reconnection exceeded {TimeoutMinutes} minute timeout. Sequence will continue but dome may not be ready.");
                            progress.Report(new ApplicationStatus
                            {
                                Status = $"ScopeDome reconnection timeout ({TimeoutMinutes} min) - continuing anyway",
                                Source = "ScopeDome Watchdog",
                                Status2 = "WARNING",
                                Status3 = "Sequence will continue but dome may not be ready"
                            });
                            break;
                        }

                        if (completeEvent.WaitOne(1000))
                        {
                            LogDebug("Execute: Reconnection complete event received");
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
                                LogDebug("Execute: Started event cleared, reconnection complete");
                                break;
                            }
                        }
                        catch
                        {
                            LogDebug("Execute: Started event gone, reconnection complete");
                            break;
                        }
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    LogDebug("Execute: Complete event doesn't exist, waiting for started event to clear");

                    // Wait for started event to be cleared
                    while (!token.IsCancellationRequested)
                    {
                        // Check timeout
                        var elapsed = DateTime.Now - startTime;
                        if (elapsed >= timeoutSpan)
                        {
                            LogDebug($"Execute: Timeout reached ({TimeoutMinutes} minutes), failing gracefully");
                            Logger.Warning($"ScopeDome reconnection exceeded {TimeoutMinutes} minute timeout. Sequence will continue but dome may not be ready.");
                            progress.Report(new ApplicationStatus
                            {
                                Status = $"ScopeDome reconnection timeout ({TimeoutMinutes} min) - continuing anyway",
                                Source = "ScopeDome Watchdog",
                                Status2 = "WARNING"
                            });
                            break;
                        }

                        await Task.Delay(1000, token);
                        
                        // Update progress with elapsed time
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
                            break; // Event gone
                        }
                    }
                }

                _reconnectionInProgress = false;
                LogDebug("Execute: Reconnection complete, resuming sequence");

                progress.Report(new ApplicationStatus
                {
                    Status = "ScopeDome reconnection complete",
                    Source = "ScopeDome Watchdog"
                });

                // Small delay to let dome stabilize
                await Task.Delay(2000, token);
            }
            finally
            {
                _inFlight = false;
                _triggered = false;
            }
        }

        public override string ToString()
        {
            return $"Trigger: DomeReconnectionTrigger";
        }

        private static void LogDebug(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(DebugLogPath, logMessage + Environment.NewLine);
                Logger.Info($"[DomeReconnectionTrigger] {message}");
            }
            catch { }
        }
    }
}
