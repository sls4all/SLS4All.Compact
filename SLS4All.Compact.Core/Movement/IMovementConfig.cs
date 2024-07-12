// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.VisualBasic;
using SLS4All.Compact.Printer;

namespace SLS4All.Compact.Movement
{
    public interface IMovementConfig
    {
        double MaxXY { get; }
        double StepXYDistance { get; }
        (int x, int y) XYToSteps(double x, double y, double stepXYDistance = 0);
        (double x, double y) StepsToXY(int x, int y, double stepXYDistance = 0);

        bool IsMoveXYCode(in CodeCommand cmd, out double x, out double y, out bool relative);
        bool IsSetLaserCode(in CodeCommand cmd, out double value, out bool noCompensation);
    }
}