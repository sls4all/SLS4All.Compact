// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Collections;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.Printing;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SLS4All.Compact.Nesting
{
    public interface INestingService
    {
        public static WeakConcurrentDictionary<string, INestingService> Services { get; } = new();
        string Id { get; }
        BackgroundTask BackgroundTask { get; }
        float ChamberStep { get; }
        float MeshMargin { get; set; }
        NestingDimension NestingDim { get; set; }
        AsyncEvent StateChanged { get; }
        ExhaustiveNesterVoxelChamber? VoxelChamber { get; }
        long VoxelChamberVersion { get; }

        Task ClearInstances(bool includeMeshes, bool stateChanged = true);
        bool ContainsInstance(long index);
        Mesh CreateChamberMesh(bool bottomOnly, bool noRadiuses);
        long? GetFirstInstanceName();
        NestingInstance? GetFirstInstanceWithUserData(object? userData);
        NestingInstance[] GetInstances();
        NestingMeshWithLod[] GetMeshes(out int actualTriangles, bool isLocalSession);
        (NestingMeshWithLod[] meshes, NestingInstance[] instances) GetMeshesAndInstances(bool isLocalSession);
        Task LoadInstances(Stream stream, int quanity, float scale, NestedRotationConstraints constraintsAroundZ, float inset, MeshTransform? transform, Vector3? position, object? userData, bool stateChanged = true, CancellationToken cancel = default);
        Task LoadInstances(string fileHash, Func<CancellationToken, Task<Stream>> streamFactory, int quantity, float scale, NestedRotationConstraints constraintsAroundZ, float inset, Vector3? position, MeshTransform? transform, object? userData, bool stateChanged = true, CancellationToken cancel = default);
        Task<bool> RemoveInstance(long index, bool stateChanged = true);
        void RemoveUnreferencedMeshes();
        Task<NestingStats> RunCheckCollision(NestingServiceContext context, GetNestingTransform getState, bool recalculateValues = false, CancellationToken cancel = default);
        Task<NestingStats> RunNesting(NestingServiceContext context, NestingFlags flags, GetNestingTransform getState, CancellationToken cancel = default);
        Task SyncInstances(IEnumerable<(string fileHash, Func<CancellationToken, Task<Stream>> streamFactory, int count, float scale, float inset, object? userData, NestedRotationConstraints constraintsAroundZ, NestingTransformState? transformState)> instances, Func<string[], Exception, Task> loadError, bool removeUnreferencedMeshes, bool doNotKeepTransform, bool stateChanged = true, CancellationToken cancel = default);
        bool TryGetInstance(long index, [MaybeNullWhen(false)] out NestingInstance instance);
        Mesh? TryGetMeshData(string hash);
        NestingMeshWithLod? TryGetMeshForSingleDisplay(string hash, bool isLocalSession);
        NestingMesh? TryGetOriginalMesh(string hash);
    }

    public interface INestingServiceScoped : INestingService
    {
    }
}