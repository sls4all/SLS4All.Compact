// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Power
{
    public record PowermanState(double MaxPower, double CurrentPower, double RequiredPower, string PoweredPinsDescription) : INotification;
    public record PowerEntry(SystemTimestamp Timestamp, string Id, double Power) : INotification;
    public record PowerState(PowerEntry[] Entries, PowermanState Powerman) : INotification
    {
        public bool TryGetEntry(string id, [MaybeNullWhen(false)] out PowerEntry entry)
        {
            foreach (var item in Entries)
            {
                if (item.Id == id)
                {
                    entry = item;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        public PowerEntry? TryGetEntry(string id)
        {
            foreach (var item in Entries)
            {
                if (item.Id == id)
                    return item;
            }
            return null;
        }
    }

    public interface IPowerClient
    {
        string LaserId { get; }
        double LaserMinimumVisibleFactor { get; }
        PowerState CurrentState { get; }
        AsyncEvent<PowerState> StateChangedLowFrequency { get; }
        AsyncEvent<PowerState> StateChangedHighFrequency { get; }

        ValueTask SetPower(string id, double value, bool setImmediate, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask SetPowerCode(ChannelWriter<CodeCommand> channel, string id, double value, bool setImmediate, CancellationToken cancel = default);
        Task SetPowermanMax(double value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        
        bool IsSetLaserPowerCode(CodeCommand cmd, out double value);

        bool TryGetRecentPower(string id, out bool isCurrent, out double power, SystemTimestamp now = default, TimeSpan? duration = default);
        bool HasRecentPower(string id, SystemTimestamp now = default, TimeSpan? duration = null);
    }
}