// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printer;
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
    public class FakePowerClientOptions : PowerClientBaseOptions
    {
    }

    public sealed class FakePowerClient : PowerClientBase
    {
        private readonly ILogger<FakePowerClient> _logger;
        private readonly IOptionsMonitor<FakePowerClientOptions> _options;

        public FakePowerClient(
            ILogger<FakePowerClient> logger, 
            IOptionsMonitor<FakePowerClientOptions> options,
            IMediator mediator)
            : base(logger, options, mediator)
        {
            _logger = logger;
            _options = options;

            var o = options.CurrentValue;
            var now = SystemTimestamp.Now;
            UpdatePowerDictInner(o.LaserId, 0, now);
        }


        public override ValueTask SetPower(string id, double value, bool setImmediate, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var now = SystemTimestamp.Now;
            return UpdatePowerDictWithNotify(id, value, now, cancel);
        }

        public override Task SetPowermanMax(double value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        protected override PowerState GetState()
        {
            lock (_stateLock)
            {
                var entries = ReadEntriesNeedsLock();
                Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id));
                return new PowerState(entries, _fallbackPowermanState);
            }
        }
    }
}
