// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Validation
{
    public enum ValidationSeverity
    {
        NotSet = 0,
        /// <summary>
        /// Error should be corrected for the instance to be valid, but does not affect other validations or display
        /// </summary>
        NonBreaking = 1,
        /// <summary>
        /// Error may cause other validations or display to fail
        /// </summary>
        Breaking = 2,
    }
}
