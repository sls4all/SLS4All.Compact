// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Messages
{
    public class McuShutdownMessage
    {
        public required IMcu? Mcu { get; set; }
        public required string Reason { get; set; }
        public Exception? Exception { get; set; }

        public override string ToString()
            => $"{{MCU={Mcu}, Reason={Reason}, Exception={Exception}}}";
    }
}
