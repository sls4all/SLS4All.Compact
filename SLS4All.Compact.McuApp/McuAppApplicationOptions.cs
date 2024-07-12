// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact
{
    public enum McuAppSerialDeviceFactoryType
    {
        NotSet = 0,
        ProxySerial,
        LocalSerial,
        SshSerial,
    }

    public class McuAppApplicationOptions
    {
        public string[] ConfigurationSources { get; set; } = [];
        public McuAppSerialDeviceFactoryType McuSerialDeviceFactory { get; set; } = McuAppSerialDeviceFactoryType.NotSet;
    }
}
