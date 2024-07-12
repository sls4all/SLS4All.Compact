// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Network
{
    public static class NetworkExtensions
    {
        public static int ToPrefixLength(this IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            var max = bytes.Length << 3;
            for (int i = 0; i < max; i++)
            {
                if ((bytes[i >> 3] & (1 << (i & 7))) == 0)
                    return i;
            }
            return 0;
        }
    }
}
