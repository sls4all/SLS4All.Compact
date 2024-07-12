// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Graphics;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.Processing.Meshes;

namespace SLS4All.Compact.Nesting
{
    public sealed record NestingInstance
    {
        public long Index { get; set; }
        public object? UserData { get; set; }
        public NestingMesh Mesh { get; set; } = null!;
        public MeshTransform TransformState { get; set; }
        public MeshTransform TransformStateOverlapping { get; set; }
        public NestedRotationConstraints ConstraintsAroundZ { get; set; }
        public INestedObject? Nested { get; set; }
        public RgbaF Color { get; set; }
        public bool IsOverlapping { get; set; }
        public float Inset { get; set; }
    }

    public delegate Task<MeshTransform> GetNestingTransform(NestingInstance mesh);
}
