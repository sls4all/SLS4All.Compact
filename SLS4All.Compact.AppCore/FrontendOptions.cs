// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SLS4All.Compact
{
    public class FrontendOptions
    {
        public float? LocalScaleX { get; set; } = null;
        public float? LocalScaleY { get; set; } = null;
        /// <summary>
        /// Forces developer mode, uses UseDeveloperExceptionPage()
        /// </summary>
        public bool ShowAdvancedDebugFeatures { get; set; } = false;
        /// <summary>
        /// Displays nesting meshes (slows down UI!)
        /// </summary>
        public bool ShowAdvancedNestingFeatures { get; set; } = false;
    }
}
