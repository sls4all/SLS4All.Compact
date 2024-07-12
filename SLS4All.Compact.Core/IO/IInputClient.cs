// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public record InputEntry(SystemTimestamp Timestamp, string Id, bool Value) : INotification;
    public record InputState(InputEntry[] Entries) : INotification
    {
        public bool TryGetEntry(string id, [MaybeNullWhen(false)] out InputEntry entry)
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

        public InputEntry? TryGetEntry(string id)
        {
            foreach (var item in Entries)
            {
                if (item.Id == id)
                    return item;
            }
            return null;
        }
    }

    public interface IInputClient
    {
        string SafeButtonId { get; }
        string LidClosedId { get; }
        InputState CurrentState { get; }
        AsyncEvent<InputState> StateChangedLowFrequency { get; }
        AsyncEvent<InputState> StateChangedHighFrequency { get; }
    }
}