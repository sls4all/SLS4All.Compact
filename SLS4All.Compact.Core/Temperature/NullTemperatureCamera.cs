// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public sealed class NullTemperatureCamera : ITemperatureCamera
    {
        public static NullTemperatureCamera Instance { get; } = new();

        public int Height => 0;

        public int Width => 0;

        public long Version => 0;

        public float[] CurrentPixels => Array.Empty<float>();

        public AsyncEvent CurrentPixelsChanged { get; } = new();

        public BoundaryRectangle MainBox => default;

        public TemperatureMatrix GetBedMatrix()
            => new TemperatureMatrix(SystemTimestamp.Now, 0, 0, CurrentPixels);
    }
}
