// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Diagnostics.CodeAnalysis;

namespace SLS4All.Compact.UpdateModel
{
    public record class ApplicationIdentity
    {
        public required string Architecture { get; set; }
        public required string Platform { get; set; }
        public required string VersionString { get; set; }
        public required string Channel { get; set; }

        public static bool TryParseVersion(string? versionString, [MaybeNullWhen(false)] out Version version)
        {
            if (string.IsNullOrWhiteSpace(versionString))
            {
                version = null;
                return false;
            }
            var dash = versionString.IndexOf('-');
            if (dash == -1)
                return Version.TryParse(versionString, out version);
            else
                return Version.TryParse(versionString.Substring(0, dash), out version);
        }
    }
}
