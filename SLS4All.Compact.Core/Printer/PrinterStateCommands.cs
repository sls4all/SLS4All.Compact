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

namespace SLS4All.Compact.Printer
{
    public static class PrinterStateCommands
    {
        public const string BeginPrintComment = "; Begin print";
        public const string BedPreparationComment = "; Bed preparation";
        public const string BeginLayerComment = "; Begin layer";
        public const string EndLayerComment = "; End layer";
        public const string PrintCapComment = "; Print cap";
        public const string EndPrintComment = "; End print";
        public const string SafeCheckpointComment = "; Safepoint";
    }
}
