// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Pages.Wizards
{
    public class GalvoCalibrationWizardOptions
    {
        public double Z2MoveDepth { get; set; } = 3500;
        public decimal CalibrationRadius { get; set; } = 435;
        public int CalibrationSteps { get; set; } = 42;
        public decimal CalibrationDensityStep { get; set; } = 5;
        public decimal CalibrationMargin { get; set; } = 5;
        public TimeSpan Dwell { get; set; } = TimeSpan.Zero;
        public int Steps { get; set; } = 360;
        public decimal DefaultSpeedA { get; set; } = 75000;
        public decimal LaserOnPrecent { get; set; } = 100;
        public decimal DefaultSpeedARelativeLaserWattage { get; set; } = 10000;
        public decimal ScalingRadius { get; set; } = 80;
        public decimal ScalingStep { get; set; } = 5;
        public decimal Precision { get; set; } = 1;
    }
}
