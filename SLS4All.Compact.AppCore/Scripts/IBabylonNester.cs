// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Nesting;

namespace SLS4All.Compact.Scripts
{
    public interface IBabylonNester : IJSProxy
    {
        [JSField("isRenderEnabled")]
        ValueTask<bool> IsRenderEnabled(JSFieldValue<bool> value);
        [JSMethod("addMesh")]
        ValueTask<IBabylonMesh> AddMesh(string name, byte[] vertices, byte[]? uvs, byte[]? normals, byte[] indicies, bool raw, bool instanced, bool showBoundingBox, RgbaF? gridColor, float? edgeRendering);
        [JSMethod("replaceChamber")]
        ValueTask<IBabylonMesh> ReplaceChamber(byte[] vertices, byte[]? uvs, byte[]? normals, byte[] indicies, bool transparent, bool raw, RgbaF color);
        [JSMethod("replaceHandle")]
        ValueTask<IBabylonMesh> ReplaceHandle(byte[] vertices, byte[]? uvs, byte[]? normals, byte[] indicies, bool transparent, bool raw, RgbaF color);
        [JSMethod("setPositionGizmoMode")]
        ValueTask SetPositionGizmoMode();
        [JSMethod("setRotationGizmoMode")]
        ValueTask SetRotationGizmoMode();
        [JSMethod("setScaleGizmoMode")]
        ValueTask SetScaleGizmoMode();
        [JSMethod("clearGizmoMode")]
        ValueTask ClearGizmoMode();
        [JSMethod("setGizmoLocalMode")]
        ValueTask SetGizmoLocalMode(bool enabled);
        [JSMethod("setInstancesState")]
        ValueTask SetInstancesState(IBabylonAbstractMesh[] mesh, NestingTransformState[] transformState, bool[] overlapping);
        [JSMethod("getTransformState")]
        ValueTask<NestingTransformState> GetTransformState(IBabylonAbstractMesh mesh);
        [JSMethod("createInstance")]
        ValueTask<IBabylonInstancedMesh> CreateInstance(IBabylonMesh babylonMesh, string name, RgbaF color, NestingTransformState transformState, bool overlapping, float? edgeRendering);
        [JSMethod("createInstances")]
        ValueTask<IBabylonInstancedMesh[]> CreateInstances(IBabylonMesh[] babylonMesh, string[] name, RgbaF[] color, NestingTransformState[] transformState, bool[] overlapping, float?[] edgeRendering);
        [JSMethod("selectInstance")]
        ValueTask SelectInstance(IBabylonInstancedMesh? instance);
        [JSMethod("removeInstance")]
        ValueTask RemoveInstance(IBabylonInstancedMesh instance);
        [JSMethod("removeInstances")]
        ValueTask RemoveInstances(IBabylonInstancedMesh[] instances);
        [JSMethod("destroy")]
        ValueTask Destroy();
        [JSMethod("setMeshLod")]
        ValueTask SetMeshLod(IBabylonMesh[] lodMeshes, int lod);
        [JSMethod("startRenderLoop")]
        ValueTask StartRenderLoop();
        [JSMethod("stopRenderLoop")]
        ValueTask StopRenderLoop();

        [JSMethod("playConstrainedInstanceAnimation")]
        ValueTask PlayConstrainedInstanceAnimation(IBabylonAbstractMesh mesh, float speed, float freedom, bool allowAnyYaw);
        [JSMethod("stopConstrainedInstanceAnimation")]
        ValueTask StopConstrainedInstanceAnimation();
    }

    public static class BabylonSlicerExtensions
    {
        public static ValueTask<IBabylonNester> CreateBabylonNester<TOwner>(
            this IJSRuntime runtime,
            ElementReference canvas,
            Vector3 chamberSize,
            Vector2 pointerInputScale,
            DotNetObjectReference<TOwner> owner)
            where TOwner : class
            => runtime.InvokeProxyAsync<IBabylonNester>("AppHelpersInvoke", "createBabylonNester", canvas, chamberSize, pointerInputScale, owner);

        private static Vector3 FixVector(Vector3 vec)
            => new Vector3(vec.X, vec.Z, vec.Y);

        public static ValueTask<IBabylonMesh> AddMesh(this IBabylonNester nester, string name, Mesh mesh, bool faceted, bool instanced, bool showBoundingBox, RgbaF? gridColor, float? edgeRendering)
            => AddMesh(mesh, faceted, (byte[] vertices, byte[]? uvs, byte[]? normals, byte[] indicies, bool raw) =>
                nester.AddMesh(name, vertices, uvs, normals, indicies, raw, instanced, showBoundingBox, gridColor, edgeRendering));

        public static async ValueTask ReplaceChamber(this IBabylonNester nester, Mesh mesh, Mesh? handle, bool transparent, bool faceted, RgbaF color)
        {
            await AddMesh(mesh, faceted, (byte[] vertices, byte[]? uvs, byte[]? normals, byte[] indicies, bool raw) =>
                nester.ReplaceChamber(vertices, uvs, normals, indicies, transparent, raw, color));
            if (handle != null)
            {
                await AddMesh(handle, faceted, (byte[] vertices, byte[]? uvs, byte[]? normals, byte[] indicies, bool raw) =>
                    nester.ReplaceHandle(vertices, uvs, normals, indicies, transparent, raw, color));
            }
        }

        private static ValueTask<IBabylonMesh> AddMesh(Mesh mesh, bool faceted, Func<byte[], byte[]?, byte[]?, byte[], bool, ValueTask<IBabylonMesh>> addMesh)
        {
            // NOTE: transfer mesh arrays using byte[], since blazor can send those more efficientely (not as JSON)
            if (faceted || mesh.TriangleCount == 0)
            {
                var vertices = new Vector3[mesh.Indicies.Length];
                var uvs = mesh.UVs != null ? new Vector2[mesh.Indicies.Length] : null;
                var indicies = new int[mesh.Indicies.Length];
                for (int i = 0, t = 0; i < mesh.Indicies.Length; i += 3, t++)
                {
                    var i0 = mesh.Indicies[i + 0];
                    var i1 = mesh.Indicies[i + 1];
                    var i2 = mesh.Indicies[i + 2];
                    var v0 = FixVector(mesh.Vertices[i0]);
                    var v1 = FixVector(mesh.Vertices[i1]);
                    var v2 = FixVector(mesh.Vertices[i2]);
                    var n = FixVector(mesh.Normals[t]);
                    vertices[i + 0] = v0;
                    vertices[i + 1] = v1;
                    vertices[i + 2] = v2;
                    if (uvs != null)
                    {
                        uvs[i + 0] = mesh.UVs![i0];
                        uvs[i + 1] = mesh.UVs![i1];
                        uvs[i + 2] = mesh.UVs![i2];
                    }
                    var dir = Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v1), n);
                    indicies[i + 0] = i + 0;
                    if (dir > 0)
                    {
                        indicies[i + 1] = i + 2;
                        indicies[i + 2] = i + 1;
                    }
                    else
                    {
                        indicies[i + 1] = i + 1;
                        indicies[i + 2] = i + 2;
                    }
                }
                var vertices8 = MemoryMarshal.Cast<Vector3, byte>(vertices).ToArray();
                var uvs8 = uvs != null ? MemoryMarshal.Cast<Vector2, byte>(uvs).ToArray() : null;
                var indicies8 = MemoryMarshal.Cast<int, byte>(indicies).ToArray();
                // NOTE: do not send normals, those will be computed automatically during rendering
                //       there is also issue with explicit normals, instancing and non-uniform scaling
                return addMesh(vertices8, uvs8, null, indicies8, true);
            }
            else
            {
                var trianglesPerVertex = new int[mesh.Vertices.Length];
                for (int i = 0; i < mesh.Indicies.Length; i += 3)
                {
                    var i0 = mesh.Indicies[i + 0];
                    var i1 = mesh.Indicies[i + 1];
                    var i2 = mesh.Indicies[i + 2];
                    trianglesPerVertex[i0]++;
                    trianglesPerVertex[i1]++;
                    trianglesPerVertex[i2]++;
                }
                var vertexToTriangleOffsets = new int[mesh.Vertices.Length + 1];
                for (int i = 1; i < vertexToTriangleOffsets.Length; i++)
                    vertexToTriangleOffsets[i] = vertexToTriangleOffsets[i - 1] + trianglesPerVertex[i - 1];
                Array.Clear(trianglesPerVertex);
                var vertexToTriangleMap = new int[vertexToTriangleOffsets[^1]];
                for (int i = 0, t = 0; i < mesh.Indicies.Length; i += 3, t++)
                {
                    var i0 = mesh.Indicies[i + 0];
                    var i1 = mesh.Indicies[i + 1];
                    var i2 = mesh.Indicies[i + 2];
                    vertexToTriangleMap[vertexToTriangleOffsets[i0] + trianglesPerVertex[i0]++] = t;
                    vertexToTriangleMap[vertexToTriangleOffsets[i1] + trianglesPerVertex[i1]++] = t;
                    vertexToTriangleMap[vertexToTriangleOffsets[i2] + trianglesPerVertex[i2]++] = t;
                }
                var vertices = new Dictionary<(Vector3 pos, Vector3 nsum), int>();
                static int AddVertex(Dictionary<(Vector3 pos, Vector3 n), int> dict, Vector3 pos, Vector3 nsum)
                {
                    var key = (pos, nsum);
                    if (dict.TryGetValue(key, out var res))
                        return res;
                    res = dict.Count;
                    dict.Add(key, res);
                    return res;
                }
                var indicies = new int[mesh.Indicies.Length];
                float threshold = 1 - MathF.Sin(30.0f / 180.0f * MathF.PI);
                for (int i = 0, t = 0; i < mesh.Indicies.Length; i += 3, t++)
                {
                    var i0 = mesh.Indicies[i + 0];
                    var i1 = mesh.Indicies[i + 1];
                    var i2 = mesh.Indicies[i + 2];
                    var v0 = FixVector(mesh.Vertices[i0]);
                    var v1 = FixVector(mesh.Vertices[i1]);
                    var v2 = FixVector(mesh.Vertices[i2]);
                    var n = mesh.Normals[t];
                    var sn0 = CalcNormalAvg(mesh, trianglesPerVertex, vertexToTriangleOffsets, vertexToTriangleMap, threshold, i0, n);
                    var sn1 = CalcNormalAvg(mesh, trianglesPerVertex, vertexToTriangleOffsets, vertexToTriangleMap, threshold, i1, n);
                    var sn2 = CalcNormalAvg(mesh, trianglesPerVertex, vertexToTriangleOffsets, vertexToTriangleMap, threshold, i2, n);
                    var dir = Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v1), FixVector(n));
                    indicies[i + 0] = AddVertex(vertices, v0, sn0);
                    if (dir > 0)
                    {
                        indicies[i + 1] = AddVertex(vertices, v2, sn2);
                        indicies[i + 2] = AddVertex(vertices, v1, sn1);
                    }
                    else
                    {
                        indicies[i + 1] = AddVertex(vertices, v1, sn1);
                        indicies[i + 2] = AddVertex(vertices, v2, sn2);
                    }
                }
                var vertices8 = MemoryMarshal.Cast<Vector3, byte>(vertices.Keys.Select(x => x.pos).ToArray()).ToArray();
                var normals8 = MemoryMarshal.Cast<Vector3, byte>(vertices.Keys.Select(x => FixVector(Vector3.Normalize(x.nsum))).ToArray()).ToArray();
                var indicies8 = MemoryMarshal.Cast<int, byte>(indicies).ToArray();
                return addMesh(vertices8, null, normals8, indicies8, true);
            }
        }

        private static Vector3 CalcNormalAvg(Mesh mesh, int[] trianglesPerVertex, int[] vertexToTriangleOffsets, int[] vertexToTriangleMap, float threshold, int index, Vector3 n)
        {
            var cn = 0;
            var res = Vector3.Zero;
#if DEBUG
            var foundSelf = false;
#endif
            for (int q = vertexToTriangleOffsets[index], qe = q + trianglesPerVertex[index]; q < qe; q++)
            {
                var nq = mesh.Normals[vertexToTriangleMap[q]];
                if (Vector3.Dot(n, nq) >= threshold)
                {
                    Debug.Assert(!float.IsNaN(nq.X) && !float.IsNaN(nq.Y) && !float.IsNaN(nq.Z));
                    res += nq;
                    cn++;
                }
#if DEBUG
                if (n == nq ||
                    (float.IsNaN(n.X) && float.IsNaN(n.Y) && float.IsNaN(n.Z) &&
                     float.IsNaN(nq.X) && float.IsNaN(nq.Y) && float.IsNaN(nq.Z)))
                    foundSelf = true;
#endif
            }
#if DEBUG
            Debug.Assert(foundSelf);
#endif
            if (cn > 1)
                return res / cn;
            else if (cn == 0)
                return n;
            else
                return res;
        }

        public static async ValueTask RenderLock(this IBabylonNester slicer, Func<ValueTask> action)
        {
            await slicer.IsRenderEnabled(false);
            try
            {
                await action();
            }
            finally
            {
                try
                {
                    await slicer.IsRenderEnabled(true);
                }
                catch (JSDisconnectedException)
                {
                    // swallow
                }
            }
        }
    }
}
