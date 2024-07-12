// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Movement
{
    public record Position(double X, double Y, double Z1, double Z2, double R)
    {
        public double this[MovementAxis axis]
            => axis switch
            {
                MovementAxis.R => R,
                MovementAxis.Z1 => Z1,
                MovementAxis.Z2 => Z2,
                _ => throw new ArgumentException("Invalid axis: " + axis, nameof(axis)),
            };
        public Position With(MovementAxis axis, double value)
            => axis switch
            {
                MovementAxis.R => this with { R = value },
                MovementAxis.Z1 => this with { Z1 = value },
                MovementAxis.Z2 => this with { Z2 = value },
                _ => throw new ArgumentException("Invalid axis: " + axis, nameof(axis)),
            };
    }
}