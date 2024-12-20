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

namespace SLS4All.Compact.Graphics
{
    public class BoundaryRectangleOptions
    {
        public required int MinX { get; set; }
        public required int MinY { get; set; }
        public required int MaxX { get; set; }
        public required int MaxY { get; set; }

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;

        public BoundaryRectangle Rectangle
            => new BoundaryRectangle(MinX, MinY, MaxX, MaxY);
    }
}
