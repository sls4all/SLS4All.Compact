// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public interface IBedProjection
    {
        public double CenterAngle { get; }
        public (double x, double y) Size { get; }
        public (double x, double y) Center { get; }
        public double BaseH { get; }
        public (double ax, double by) FromPosition(double x, double y, double h = 0);
        public (double ax, double by, double h) FromPositionLength(double x, double y, double l);
        public (double x, double y, double l) FromAngle(double ax, double by, double h = 0);
        public (double x, double y) RadToXY(double ax, double by);
        public (double ax, double by) XYToRad(double x, double y);
        /// <remarks>
        /// Be careful calling this at runtime. It will reload the options which may cause problems with components that have already precalculated
        /// values based on pevious options. Like hotspot initializer.
        /// </remarks>
        void Reset();
        public IBedProjection Recreate(
            Vector3 laserOffset,
            Vector3 laserDirectionBase);
    }
}
