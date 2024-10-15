// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Numerics;

namespace SLS4All.Compact.Scripts
{
    public interface IBabylonGizmoManager : IJSProxy
    {
        [JSField("rotationGizmoEnabled")]
        ValueTask<bool> RotationGizmoEnabled(JSFieldValue<bool> value = default);
        [JSField("positionGizmoEnabled")]
        ValueTask<bool> PositionGizmoEnabled(JSFieldValue<bool> value = default);
        [JSField("scaleGizmoEnabled")]
        ValueTask<bool> ScaleGizmoEnabled(JSFieldValue<bool> value = default);
    }
    
    public interface IBabylonAbstractMesh : IJSProxy
    {
        [JSField("isVisible")]
        ValueTask<bool> IsVisible(JSFieldValue<bool> value = default);
        [JSField("showBoundingBox")]
        ValueTask<bool> ShowBoundingBox(JSFieldValue<bool> value = default);
        [JSField("position")]
        ValueTask<Vector3> Position(JSFieldValue<Vector3> value = default);
        [JSField("rotation")]
        ValueTask<Vector3> Rotation(JSFieldValue<Vector3> value = default);
        [JSField("rotationQuaternion")]
        ValueTask<Quaternion?> RotationQuaternion(JSFieldValue<Quaternion?> value = default);
        [JSField("scaling")]
        ValueTask<Vector3> Scaling(JSFieldValue<Vector3> value = default);
        [JSMethod("clone")]
        ValueTask<IBabylonAbstractMesh> Clone(string name);
        [JSMethod("dispose")]
        ValueTask DisposeFromScene();
    }

    public interface IBabylonMesh : IBabylonAbstractMesh
    {
        [JSMethod("createInstance")]
        ValueTask<IBabylonInstancedMesh> CreateInstance(string name);
        [JSMethod("registerInstancedBuffer")]
        ValueTask RegisterInstancedBuffer(string kind, int stride);
        [JSMethod("addLODLevel")]
        ValueTask AddLodLevel(float distanceOrScreenCoverage, IBabylonMesh? mesh);
        [JSMethod("removeLODLevel")]
        ValueTask RemoveLodLevel(IBabylonMesh mesh);
    }

    public interface IBabylonInstancedMesh : IBabylonAbstractMesh
    {
    }
}
