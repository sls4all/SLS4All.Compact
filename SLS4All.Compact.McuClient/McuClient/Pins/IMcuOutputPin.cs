// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using System.Runtime.CompilerServices;

namespace SLS4All.Compact.McuClient.Pins
{
    public interface IMcuOutputPin
    {
        static TimeSpan DefaultMaxDuration { get; } = TimeSpan.FromSeconds(5);
        
        string Name { get; }
        McuPinDescription Pin { get; }
        IMcu Mcu { get; }
        McuPinValue CurrentValue { get; }
        void Set(McuPinValue value, int priority, SystemTimestamp timestamp);
        void Set(McuPinValue value, int priority, McuTimestamp timestamp);
        void SetupMaxDuration(TimeSpan maxDuration);
        void SetupStartValue(McuPinValue startValue, McuPinValue shutdownValue, bool isStatic = false);
    }

    public static class McuOutputPinExtensions
    {
        public static void SetImmediate(this IMcuOutputPin pin, McuPinValue value, int priority)
            => pin.Set(value, priority, McuTimestamp.Immediate(pin.Mcu));
    }
}