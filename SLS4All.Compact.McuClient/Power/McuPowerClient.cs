// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Power
{
    public class McuPowerClientOptions : PowerClientBaseOptions
    {
        public TimeSpan SetPinTime { get; set; } = TimeSpan.FromSeconds(0.1);
    }

    public sealed class McuPowerClient : PowerClientBase
    {
        private readonly ILogger<McuPowerClient> _logger;
        private readonly IOptionsMonitor<McuPowerClientOptions> _options;
        private readonly McuPrinterClient _printerClient;

        public McuPowerClient(
            ILogger<McuPowerClient> logger, 
            IOptionsMonitor<McuPowerClientOptions> options,
            McuPrinterClient printerClient)
            : base(logger, options)
        {
            _logger = logger;
            _options = options;
            _printerClient = printerClient;
            
            printerClient.ManagerSetEvent.AddHandler(OnManagerSet);
        }

        private ValueTask OnManagerSet(McuManager manager, CancellationToken token)
        {
            lock (_stateLock)
            {
                var now = SystemTimestamp.Now;
                foreach (var pair in manager.OutputPins)
                {
                    UpdatePowerDictInner(pair.Key, pair.Value.CurrentValue.Single, now);
                }
            }
            return ValueTask.CompletedTask;
        }

        public override ValueTask SetPower(string id, double value, bool setImmediate, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var options = _options.CurrentValue;
            var pin = manager.OutputPins[id];
            var now = SystemTimestamp.Now;
            if (setImmediate)
            {
                pin.SetImmediate(
                    (float)value,
                    McuCommandPriority.Default);
            }
            else
            {
                using (var master = manager.EnterMasterQueueLock())
                {
                    var timestamp = master[pin];
                    if (timestamp.IsEmpty || timestamp.ToSystem() < now + options.SetPinTime)
                        timestamp = McuTimestamp.FromSystem(pin.Mcu, now + options.SetPinTime);
                    else
                        timestamp += options.SetPinTime;
                    pin.Set(
                        (float)value,
                        McuCommandPriority.Default,
                        timestamp);
                    master[pin] = timestamp;
                }
            }

            return UpdatePowerDictWithNotify(id, value, now, cancel);
        }

        private PowermanState? ReadPowermanStateNeedsLock()
        {
            var manager = _printerClient.ManagerIfReady;
            return manager?.PowerManager.GetState();
        }

        protected override PowerState GetState()
        {
            lock (_stateLock)
            {
                var entries = ReadEntriesNeedsLock();
                var powermanState = ReadPowermanStateNeedsLock();
                Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id));
                return new PowerState(entries, powermanState ?? _fallbackPowermanState);
            }
        }

        public override Task SetPowermanMax(double value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            manager.PowerManager.SetTotalMaxConsumption(value);
            return Task.CompletedTask;
        }


        public override void Dispose()
        {
            _printerClient.ManagerSetEvent.RemoveHandler(OnManagerSet);
            base.Dispose();
        }
    }
}
