// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using System.Numerics;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Slicing;

namespace SLS4All.Compact.Printing
{
    public readonly record struct PrintingObject(string Hash, Mesh Mesh, Matrix4x4 Transform, Func<PrintSetup, PrintSetup>? OverrideSetup);
}
