// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Numerics;
using SLS4All.Compact.Processing.Meshes;

namespace SLS4All.Compact.Nesting
{
    public sealed class NestingMesh
    {
        private volatile Mesh[] _simplifiedMeshes;

        public Mesh Mesh { get; }
        public string Hash { get; }
        public Bounds3 Bounds { get; }
        public Mesh[] SimplifiedMeshes
        {
            get => _simplifiedMeshes;
            set => _simplifiedMeshes = value;
        }

        public NestingMesh(Mesh mesh, Mesh[] simplifiedMeshes, string hash, Bounds3 bounds)
        {
            Mesh = mesh;
            Hash = hash;
            Bounds = bounds;
            _simplifiedMeshes = simplifiedMeshes;
        }
    }
}
