// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printer;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Slicing
{
    public sealed class NullCodePlotter : ICodePlotter
    {
        public bool OutsideDraw => false;

        public int LayerCount => 1;

        public long Version => 0;

        public bool IsEmpty => true;

        public SystemTimestamp[] TimestampMap => Array.Empty<SystemTimestamp>();

        public int Width => 0;

        public int Height => 0;

        public void Clear()
        {
        }

        public Vector2 GetCenter()
            => Vector2.Zero;

        public (int width, int height) GetMask(ref float[] output)
            => throw new NotSupportedException();

        public void ReplaceWith(float[] mask)
        {
        }

        public void Process(CodeCommand cmd)
        {
        }

        public MimeData CreateImage(TimeSpan newerThan = default, string caption = "", int? layerIndex = null, bool drawHotspot = false)
            => MimeData.BlackPng;
    }
}
