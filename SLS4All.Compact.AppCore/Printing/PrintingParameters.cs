// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Collections;
using SLS4All.Compact.Printing;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Storage.PrinterSettings;
using SLS4All.Compact.Storage.PrintJobs;
using SLS4All.Compact.Storage.PrintProfiles;

namespace SLS4All.Compact.Printing
{
    public sealed record class PrintingParameters(
        PrinterPowerSettings PowerSettings,
        PrintProfile Profile,
        PrintingObject[] Instances,
        bool ExactThickness,
        PrintSetup Setup,
        IPrintJob Job)
    {
    }
}
