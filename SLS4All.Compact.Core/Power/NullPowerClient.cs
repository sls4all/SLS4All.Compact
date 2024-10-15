// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Power
{
    public sealed class NullPowerClient : IPowerClient
    {
        public string LaserId => "laser";

        public PowerState CurrentState { get; } = new PowerState(Array.Empty<PowerEntry>(), new PowermanState(0, 0, 0, ""));

        public AsyncEvent<PowerState> StateChangedLowFrequency { get; } = new AsyncEvent<PowerState>();

        public AsyncEvent<PowerState> StateChangedHighFrequency { get; } = new AsyncEvent<PowerState>();

        public bool TryGetRecentPower(string id, out bool isCurrent, out double power, SystemTimestamp now, TimeSpan? duration)
        {
            isCurrent = false;
            power = 0;
            return false;
        }

        public bool HasRecentPower(string id, SystemTimestamp now = default, TimeSpan? duration = null)
            => false;

        public bool IsSetLaserPowerCode(CodeCommand cmd, out double value)
        {
            value = 0;
            return false;
        }

        public ValueTask SetPower(string id, double value, bool setImmediate, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask SetPowerCode(ChannelWriter<CodeCommand> channel, string id, double value, bool setImmediate, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public Task SetPowermanMax(double value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => Task.CompletedTask;
    }
}
