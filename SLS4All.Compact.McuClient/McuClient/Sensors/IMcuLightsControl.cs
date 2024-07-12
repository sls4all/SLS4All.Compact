// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;

namespace SLS4All.Compact.McuClient.Sensors
{
    public interface IMcuLightsControl
    {
        bool HasLightsEnabled { get; }
        int LightCount { get; }

        void GetEnabledLights(ICollection<KeyValuePair<int, float>> res);
        void SetLights(bool enabled, int? mask = null, float? power = null);
        void SetLights(Span<(bool Enabled, int Index, float? Power)> items);

        bool TryGetRecentLightPower(int index, out bool isCurrent, out float power, SystemTimestamp now, TimeSpan? duration);
        bool HasRecentLightPower(int index, SystemTimestamp now = default, TimeSpan? duration = null);
    }
}