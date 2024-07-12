// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.McuClient.Pins;

namespace SLS4All.Compact.McuClient
{
    public readonly record struct McuBusKey(string McuAlias, string BusName);

    public sealed record class McuBusDescription(IMcu Mcu, string Bus, string? ShareType = null)
    {
        public McuBusKey Key => new McuBusKey(Mcu.Name, Bus);

        public static (string McuAlias, string BusName) Parse(string description)
        {
            description = description.Trim();
            var colon = description.IndexOf(':');
            if (colon == -1)
                return ("mcu", description);
            else
                return (description[..colon], description[(colon + 1)..]);
        }
    }
}
