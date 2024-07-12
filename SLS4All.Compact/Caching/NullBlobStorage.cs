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

namespace SLS4All.Compact.Caching
{
    public sealed class NullBlobStorage : IBlobStorage
    {
        public static NullBlobStorage Instance { get; } = new();

        public void Complete(Stream stream)
        {
        }

        public Stream GetBlobStream(object key, out bool existing)
        {
            existing = false;
            return Stream.Null;
        }
    }
}
