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
    public class OpticalSetupWizardOptions
    {
        public double LaserMinimumVisibleWattageLow { get; set; } = 600;
        public double LaserMinimumVisibleWattageHigh { get; set; } = 900;
        public double GalvoHolderLaserX { get; set; } = 500;
        public double GalvoHolderLaserY { get; set; } = 900;
        public double Z2FocusDepth { get; set; } = 3500;
    }
}
