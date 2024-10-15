// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SLS4All.Compact.Slicing
{
    public interface ICodePlotter
    {
        bool OutsideDraw { get; }
        int LayerCount { get; }
        long Version { get; }
        bool IsEmpty { get; }
        int Width { get; }
        int Height { get; }
        SystemTimestamp[] TimestampMap { get; }

        void Clear();
        MimeData CreateImage(TimeSpan newerThan = default, string caption = "", int? layerIndex = null, bool drawHotspot = false, int? maxSize = null);
        (int width, int height) GetMask(ref float[] output);
        void ReplaceWith(float[] mask);
        Vector2 GetCenter();
        void Process(CodeCommand cmd);
    }
}