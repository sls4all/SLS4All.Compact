// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Temperature
{
    public readonly record struct TemperatureBox(int MinX, int MinY, int MaxX, int MaxY)
    {
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;

        public TemperatureBox OffsetInTopLeft(in TemperatureBox other)
            => new TemperatureBox(MinX + other.MinX, MinY + other.MinY, MinX + other.MaxX, MinY + other.MaxY);

        public TemperatureBox Offset(int x, int y)
            => new TemperatureBox(MinX + x, MinY + y, MaxX + x, MaxY + y);

        public TemperatureBox RebaseTo(in TemperatureBox other)
            => new TemperatureBox(other.MinX, other.MinY, other.MinX + Width - 1, other.MinY + Height - 1);
    }
}
