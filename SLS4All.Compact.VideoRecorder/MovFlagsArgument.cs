// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using FFMpegCore.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.VideoRecorder
{
    public class MovFlagsArgument : IArgument
    {
        public readonly string Type;

        public string Text => "-movflags " + Type;

        public MovFlagsArgument(string type)
        {
            Type = type;
        }
    }
}
