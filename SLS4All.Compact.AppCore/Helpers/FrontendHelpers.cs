// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Transactions;
using SLS4All.Compact.Pages;

namespace SLS4All.Compact.Helpers
{
    public static class FrontendHelpers
    {
        public const string LocalSessionKey = "local";
        public const string UIScaleXKey = "sx";
        public const string UIScaleYKey = "sy";
        public static bool IsEnabled(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
