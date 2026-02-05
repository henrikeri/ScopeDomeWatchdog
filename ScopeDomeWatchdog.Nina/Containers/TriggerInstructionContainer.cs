#region "copyright"
/*
    ScopeDome Watchdog NINA Plugin
    Copyright (c) 2026 Henrik Erevik Riise
    
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
    
    Inspired by IfContainer from the "When" plugin by Stefan Berg (isbeorn86+NINA@googlemail.com)
*/
#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDomeWatchdog.Nina.Containers
{
    /// <summary>
    /// Internal container for holding child instructions within a trigger.
    /// This allows triggers to contain executable sequences that run after reconnection.
    /// Based on IfContainer from the "When" plugin.
    /// SequenceContainer already provides DropIntoCommand, Items, Validate(), etc.
    /// </summary>
    [ExportMetadata("Name", "")]
    [ExportMetadata("Description", "Container for post-reconnection instructions")]
    [ExportMetadata("Icon", "RefreshSVG")]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TriggerInstructionContainer : SequenceContainer
    {
        private readonly object _lockObj = new object();
        
        /// <summary>
        /// Reference to the parent trigger (not the sequence container parent).
        /// Using object type since SequenceTrigger might not implement ISequenceEntity directly.
        /// </summary>
        public object? PseudoParent { get; set; }

        public TriggerInstructionContainer() : base(new SequentialStrategy())
        {
            Name = "Post-Reconnection Instructions";
            Description = "Instructions to run after dome reconnection completes";
        }

        public override object Clone()
        {
            var clone = new TriggerInstructionContainer();
            clone.Items = new ObservableCollection<ISequenceItem>(
                Items.Select(i => (ISequenceItem)i.Clone())
            );
            
            foreach (var item in clone.Items)
            {
                item.AttachNewParent(clone);
            }
            
            if (Parent != null)
            {
                clone.AttachNewParent(Parent);
            }
            
            return clone;
        }

        public override void Initialize()
        {
            base.Initialize();
            foreach (ISequenceItem item in Items)
            {
                item.Initialize();
            }
        }

        /// <summary>
        /// Runs all child instructions sequentially
        /// </summary>
        public new async Task Run(IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            if (Items.Count == 0)
            {
                return;
            }
            
            await Execute(progress, token);
        }
        
        public new void MoveUp(ISequenceItem item)
        {
            lock (_lockObj)
            {
                var index = Items.IndexOf(item);
                if (index > 0)
                {
                    base.MoveUp(item);
                }
            }
        }
    }
}
