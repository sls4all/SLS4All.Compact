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
    public class PowderTuningWizardOptions
    {
        public int GridDim { get; set; } = 5;
        public decimal GridMargin { get; set; } = 3;
        public decimal TemperatureIncreaseStep { get; set; } = 0.5M;
    }
}
