// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;

namespace SLS4All.Compact.McuClient.Devices
{
    public class McuAliasesOptions
    {
        public const int DefaultBaud = 250000;

        public Dictionary<string, McuDeviceFactoryAlias?> Aliases { get; set; } = new();

        public static (string Endpoint, int Baud) GetEndpointAndBaud(string deviceName)
        {
            var index = deviceName.LastIndexOf('@');
            if (index != -1 && int.TryParse(deviceName[(index + 1)..], out var baud))
                return (deviceName[..index], baud);
            else
                return (deviceName, DefaultBaud);
        }

        public static string FormatDeviceName(string path, int baud)
            => $"{path}@{baud}";

        public McuDeviceInfo[] GetMatches(IEnumerable<string> devicePaths)
        {
            var res = new HashSet<McuDeviceInfo>();
            foreach (var pair in Aliases.GetOrderedEnabledKeyValues())
            {
                foreach ((var alias, var expression) in pair.Value.Expressions.GetOrderedEnabledKeyValues(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var expressionParts = GetEndpointAndBaud(expression);
                    var wildcard = new Wildcard(expressionParts.Endpoint, false);
                    foreach (var path in devicePaths)
                    {
                        if (wildcard.IsMatch(path))
                            res.Add(new McuDeviceInfo(alias, pair.Key, path, expressionParts.Baud));
                    }
                }
            }
            return res.ToArray();
        }
    }
}
