// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Processing.Meshes;
using System.Numerics;

namespace SLS4All.Compact.Nesting
{
    /// <remarks>
    /// Beware of changes, serialized from JS
    /// </remarks>
    public struct NestingTransformState
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Quaternion Quaternion { get; set; }
        public Vector3 Scale { get; set; }

        public static implicit operator MeshTransform(NestingTransformState state)
            => new MeshTransform(state.Position, state.Rotation, state.Quaternion, state.Scale);

        public static implicit operator NestingTransformState(MeshTransform state)
            => new NestingTransformState
            {
                Position = state.Position,
                Rotation = state.Rotation,
                Quaternion = state.Quaternion,
                Scale = state.Scale
            };
    }
}
