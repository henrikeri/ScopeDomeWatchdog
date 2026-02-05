#region "copyright"
/*
    ScopeDome Watchdog NINA Plugin
    Pauses NINA sequence during dome reconnection events.
    
    Uses the same pattern as the "When Unsafe" trigger from isbeorn's When plugin.
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
            LogDebug("Execute: Starting wait for reconnection");

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

                progress.Report(new ApplicationStatus
                {
                    Status = "Waiting for ScopeDome reconnection...",
                    Source = "ScopeDome Watchdog"
                });

                // Wait for the reconnection complete event
                try
                {
                    using var completeEvent = EventWaitHandle.OpenExisting(RECONNECTION_COMPLETE_EVENT);

                    while (!token.IsCancellationRequested)
                    {
                        if (completeEvent.WaitOne(1000))
                        {
                            LogDebug("Execute: Reconnection complete event received");
                            break;
                        }

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
                        await Task.Delay(1000, token);
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
