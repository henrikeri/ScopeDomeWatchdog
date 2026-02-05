#region "copyright"
/*
    ScopeDome Watchdog NINA Plugin
    Copyright (c) 2026 Henrik Erevik Riise
    
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
    
    This plugin uses the ConditionWatchdog pattern and sequence interrupt/restart
    approach from the "When" plugin by Stefan Berg (isbeorn86+NINA@googlemail.com)
    and the N.I.N.A. contributors. See: https://github.com/isbeorn/nina.plugin.when
*/
#endregion "copyright"

using Newtonsoft.Json;
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
using ScopeDomeWatchdog.Nina.Containers;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Nina.Triggers
{
    [ExportMetadata("Name", "Dome Reconnection Trigger")]
    [ExportMetadata("Description", "Pauses the sequence when ScopeDome reconnection is detected and resumes when complete.")]
    [ExportMetadata("Icon", "RefreshSVG")]
    [ExportMetadata("Category", "ScopeDome Watchdog")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
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
        [JsonProperty]
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

        /// <summary>
        /// Indicates whether a reconnection is currently in progress.
        /// Used by the UI to show a spinning activity indicator.
        /// </summary>
        public bool IsReconnecting => _reconnectionInProgress;

        /// <summary>
        /// Child instructions that will be executed after dome reconnection completes.
        /// This allows users to add actions like re-centering the target.
        /// </summary>
        private TriggerInstructionContainer? _instructions;
        
        [JsonProperty]
        public TriggerInstructionContainer Instructions
        {
            get
            {
                // Lazy initialization to avoid MEF composition issues
                if (_instructions == null)
                {
                    _instructions = new TriggerInstructionContainer();
                    _instructions.PseudoParent = this;
                    _instructions.AttachNewParent(Parent);
                }
                return _instructions;
            }
            set
            {
                _instructions = value;
                if (_instructions != null)
                {
                    _instructions.PseudoParent = this;
                }
                RaisePropertyChanged();
            }
        }
        
        // Runner for executing child instructions (separate from base TriggerRunner)
        private TriggerInstructionContainer? InstructionRunner { get; set; }

        [ImportingConstructor]
        public DomeReconnectionTrigger(
            ISequenceMediator sequenceMediator,
            IApplicationStatusMediator applicationStatusMediator)
        {
            this.sequenceMediator = sequenceMediator;
            this.applicationStatusMediator = applicationStatusMediator;

            // Set the display name for the sequence editor
            Name = "Dome Reconnection Trigger";

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
            
            // Clone the instruction container
            Instructions = (TriggerInstructionContainer)cloneMe.Instructions.Clone();
            Instructions.PseudoParent = this;
        }

        public override object Clone()
        {
            return new DomeReconnectionTrigger(this);
        }
        
        /// <summary>
        /// Called after JSON deserialization to fix up parent references.
        /// </summary>
        [OnDeserialized]
        public void OnDeserialized(StreamingContext context)
        {
            if (_instructions != null)
            {
                _instructions.PseudoParent = this;
            }
        }

        public override void SequenceBlockInitialize()
        {
            LogDebug("SequenceBlockInitialize - starting watchdog");
            ConditionWatchdog?.Start();
            
            // Attach child instructions to parent container
            if (_instructions != null && Parent != null)
            {
                _instructions.AttachNewParent(Parent);
            }
        }

        public override void SequenceBlockTeardown()
        {
            LogDebug("SequenceBlockTeardown - stopping watchdog and resetting state");
            try { ConditionWatchdog?.Cancel(); } catch { }
            
            // Detach and reset child instructions
            if (_instructions != null)
            {
                try
                {
                    _instructions.ResetProgress();
                }
                catch { }
                _instructions.AttachNewParent(null);
            }
            
            // Reset all global state when leaving trigger
            bool wasReconnecting = _reconnectionInProgress;
            _reconnectionInProgress = false;
            _inFlight = false;
            _triggered = false;
            _critical = false;
            
            if (wasReconnecting)  // Only notify UI if we were actually reconnecting
            {
                RaisePropertyChanged(nameof(IsReconnecting));
            }
            LogDebug("SequenceBlockTeardown: Reset all global state");
        }

        public override void AfterParentChanged()
        {
            if (Parent == null)
            {
                SequenceBlockTeardown();
            }
            else 
            {
                // Attach child instructions to new parent
                if (_instructions != null)
                {
                    _instructions.AttachNewParent(Parent);
                }
                
                if (Parent.Status == SequenceEntityStatus.RUNNING)
                {
                    SequenceBlockInitialize();
                }
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
            // Also check _reconnectionInProgress to prevent retriggering while Execute is still running
            if (_inFlight || _triggered || _reconnectionInProgress)
            {
                LogDebug($"InterruptWhenReconnecting: Already in progress (inFlight={_inFlight}, triggered={_triggered}, reconnecting={_reconnectionInProgress}), returning");
                return;
            }

            if (Parent != null &&
                ItemUtility.IsInRootContainer(Parent) &&
                Parent.Status == SequenceEntityStatus.RUNNING &&
                Status != SequenceEntityStatus.DISABLED)
            {
                _triggered = true;
                if (!_reconnectionInProgress)  // Only update if not already true
                {
                    _reconnectionInProgress = true;
                    RaisePropertyChanged(nameof(IsReconnecting));
                }
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
                    if (!_reconnectionInProgress)  // Only update if not already true
                    {
                        _reconnectionInProgress = true;
                        RaisePropertyChanged(nameof(IsReconnecting));
                    }
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
                                Source = "ScopeDome Watchdog"
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
                                Source = "ScopeDome Watchdog"
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

                if (_reconnectionInProgress)  // Only update UI if value is actually changing
                {
                    _reconnectionInProgress = false;
                    RaisePropertyChanged(nameof(IsReconnecting));
                }
                LogDebug("Execute: Reconnection complete, resuming sequence");

                progress.Report(new ApplicationStatus
                {
                    Status = "ScopeDome reconnection complete",
                    Source = "ScopeDome Watchdog"
                });

                // Small delay to let dome stabilize
                await Task.Delay(2000, token);
                
                // Run child instructions (e.g., re-center target, resync mount)
                if (Instructions.Items.Count > 0)
                {
                    LogDebug($"Execute: Running {Instructions.Items.Count} post-reconnection instructions");
                    progress.Report(new ApplicationStatus
                    {
                        Status = "Running post-reconnection instructions...",
                        Source = "ScopeDome Watchdog"
                    });
                    
                    InstructionRunner = Instructions;
                    await InstructionRunner.Run(progress, token);
                    
                    LogDebug("Execute: Post-reconnection instructions complete");
                }
            }
            finally
            {
                // Reset all global state
                _inFlight = false;
                _triggered = false;
                
                if (_reconnectionInProgress)  // Only notify UI if value is actually changing
                {
                    _reconnectionInProgress = false;
                    RaisePropertyChanged(nameof(IsReconnecting));
                    LogDebug("Execute: Spinner stopped - reconnection complete");
                }
                
                _critical = false;
                LogDebug("Execute: State reset (inFlight, triggered, reconnectionInProgress, critical)");
                
                // Reset child instructions state
                if (Instructions != null)
                {
                    try
                    {
                        Instructions.ResetProgress();
                        LogDebug("Execute: Reset child instructions state");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Execute: Error resetting instructions: {ex.Message}");
                    }
                }
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
