// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;

namespace SLS4All.Compact.Temperature
{
    public interface ITemperatureCamera
    {
        int Height { get; }
        int Width { get; }
        long Version { get; }
        float[] CurrentPixels { get; }
        AsyncEvent CurrentPixelsChanged { get; }
        TemperatureBox MainBox { get; }

        TemperatureMatrix GetBedMatrix();
    }

    public delegate (int width, int height) FakeCameraGenerator(float[] pixels);

    public interface IFakeTemperatureCamera : ITemperatureCamera
    {
        event FakeCameraGenerator? Generator;
    }
}