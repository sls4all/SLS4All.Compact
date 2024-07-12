// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Power;
using SLS4All.Compact.McuClient.Pins;

namespace SLS4All.Compact.McuClient
{
    public interface IMcuPowerManager
    {
        PowermanState GetState();
        void Set(IMcuOutputPin pin, McuPinValue value);
        void SetupPin(IMcuOutputPin pin, double watts, int priority);
        void SetTotalMaxConsumption(double watts);
    }
}