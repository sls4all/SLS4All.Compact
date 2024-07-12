// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Security.Cryptography;
using SLS4All.Compact.Scripts;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Processing.Volumes;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.Collections;
using Microsoft.AspNetCore.WebUtilities;

namespace SLS4All.Compact.Nesting
{
    public class NestingServiceOptions
    {
        public HashSet<int> SimplifyTriangleCounts { get; } = new HashSet<int>
        {
            100000, 50000, 25000, 20000, 10000, 5000
        };
        public int TargetTrianglesLocal { get; set; } = 500_000;
        public int TargetTrianglesDefault { get; set; } = 10_000_000;
    }

    /// <param name="LimitThickness">Maximum thickness for produced stats [mm]</param>
    public sealed record class NestingServiceContext(
        IShrinkageCorrection ShrinkageCorrection,
        float? LimitThickness = null,
        bool IgnoreOverlaps = false
    );

    public class NestingService : INestingService, IDisposable
    {
        private readonly IOptions<NestingServiceOptions> _options;
        private readonly IObjectFactory<INester, object> _nesterFactory;
        private readonly IMeshLoader _meshLoader;
        private readonly Dictionary<string, NestingMesh> _meshes = new();
        private readonly SortedDictionary<long, NestingInstance> _instances = new();
        private readonly ConcurrentStack<INester> _nesterBag = new();
        private readonly Random _random = new Random();
        private readonly object _locker = new();
        private readonly BackgroundTask _collapseTask = new();
        private volatile ExhaustiveNesterVoxelChamber? _voxelChamber;
        private long _voxelChamberVersion;
        private long _lastInstanceIndex;

        protected object Locker => _locker;
        protected Dictionary<string, NestingMesh> MeshesNeedsLock => _meshes;
        protected SortedDictionary<long, NestingInstance> InstancesNeedsLock => _instances;

        public BackgroundTask BackgroundTask => _collapseTask;
        public NestingDimension NestingDim { get; set; }
        public float MeshMargin { get; set; }
        public float ChamberStep { get; }
        public virtual ExhaustiveNesterVoxelChamber? VoxelChamber
        {
            get => _voxelChamber;
            protected set => _voxelChamber = value;
        }
        public AsyncEvent StateChanged { get; } = new();
        public long VoxelChamberVersion => Interlocked.Read(ref _voxelChamberVersion);
        public string Id { get; } = Guid.NewGuid().ToString();

        public NestingService(
            IOptions<NestingServiceOptions> options,
            IObjectFactory<INester, object> nesterFactory,
            IMeshLoader meshLoader)
        {
            _options = options;
            _nesterFactory = nesterFactory;
            _meshLoader = meshLoader;
            var nester = nesterFactory.CreateObject();
            NestingDim = nester.NestingDim;
            ChamberStep = nester.ChamberStep;
            MeshMargin = nester.MeshMargin;
            _nesterBag.Push(nester);

            // register
            INestingService.Services.Add(Id, this);
        }

        public virtual Mesh? TryGetMeshData(string hash)
        {
            lock (_locker)
            {
                if (!_meshes.TryGetValue(hash, out var mesh))
                    return null;
                return mesh.Mesh;
            }
        }

        public virtual NestingMesh? TryGetOriginalMesh(string hash)
        {
            lock (_locker)
            {
                if (!_meshes.TryGetValue(hash, out var mesh))
                    return null;
                return mesh;
            }
        }

        public virtual NestingMeshWithLod? TryGetMeshForSingleDisplay(string hash, bool isLocalSession)
        {
            lock (_locker)
            {
                if (!_meshes.TryGetValue(hash, out var mesh))
                    return null;
                var options = _options.Value;
                var minLod = -1;
                var bestLod = -1;
                foreach (var lod in mesh.SimplifiedMeshes.Select((x, i) => (x, i)).OrderBy(x => x.x.TriangleCount))
                {
                    if (minLod == -1)
                        minLod = lod.i;
                    if (lod.x.TriangleCount < (isLocalSession ? options.TargetTrianglesLocal : options.TargetTrianglesDefault))
                        bestLod = lod.i;
                    else
                        break;
                }
                if (bestLod == -1)
                    bestLod = minLod;
                return new NestingMeshWithLod(mesh, bestLod);
            }
        }

        public virtual void RemoveUnreferencedMeshes()
        {
            lock (_locker)
            {
                var referencedMeshes = _instances.Select(x => x.Value.Mesh).ToHashSet();
                foreach (var pair in _meshes.ToArray())
                {
                    if (!referencedMeshes.Contains(pair.Value))
                        _meshes.Remove(pair.Key);
                }
            }
        }

        public virtual  (NestingMeshWithLod[] meshes, NestingInstance[] instances) GetMeshesAndInstances(bool isLocalSession)
        {
            lock (_locker)
            {
                return (GetMeshes(out _, isLocalSession), GetInstances());
            }
        }

        public virtual NestingMeshWithLod[] GetMeshes(out int actualTriangles, bool isLocalSession)
        {
            lock (_locker)
            {
                var options = _options.Value;
                actualTriangles = 0;
                if (_meshes.Count == 0)
                    return Array.Empty<NestingMeshWithLod>();
                var lods = _meshes.Keys.ToDictionary(x => x, x => 0);
                while (true)
                {
                    actualTriangles = 0;
                    foreach (var instance in _instances.Values)
                    {
                        var mesh = _meshes[instance.Mesh.Hash];
                        var lod = lods[instance.Mesh.Hash];
                        actualTriangles += mesh.SimplifiedMeshes[lod].TriangleCount;
                    }
                    if (actualTriangles <= (isLocalSession ? options.TargetTrianglesLocal : options.TargetTrianglesDefault))
                        break;
                    var decreased = false;
                    foreach (var lod in lods.OrderByDescending(x => _meshes[x.Key].SimplifiedMeshes[x.Value].TriangleCount))
                    {
                        var mesh = _meshes[lod.Key];
                        if (lod.Value + 1 < mesh.SimplifiedMeshes.Length)
                        {
                            lods[lod.Key]++;
                            decreased = true;
                            break;
                        }
                    }
                    if (!decreased)
                        break;
                }
                return lods.Select(pair => new NestingMeshWithLod(_meshes[pair.Key], pair.Value)).ToArray();
            }
        }

        public virtual NestingInstance[] GetInstances()
        {
            lock (_locker)
            {
                return _instances.Values.Select(x => x with { }).ToArray();
            }
        }

        public virtual NestingInstance? GetFirstInstanceWithUserData(object? userData)
        {
            lock (_locker)
            {
                return _instances.Values.Where(x => Equals(x.UserData, userData)).Select(x => x with { }).FirstOrDefault();
            }
        }

        public virtual long? GetFirstInstanceName()
        {
            lock (_locker)
            {
                var res = _instances.Keys.FirstOrDefault(-1);
                return res == -1 ? null : res;
            }
        }

        public virtual bool ContainsInstance(long index)
        {
            lock (_locker)
            {
                return _instances.ContainsKey(index);
            }
        }

        public virtual bool TryGetInstance(long index, [MaybeNullWhen(false)] out NestingInstance instance)
        {
            lock (_locker)
            {
                if (_instances.TryGetValue(index, out var item))
                {
                    instance = item with { };
                    return true;
                }
                else
                {
                    instance = default;
                    return false;
                }
            }
        }

        public virtual async Task<bool> RemoveInstance(long index, bool stateChanged = true)
        {
            lock (_locker)
            {
                if (!_instances.TryGetValue(index, out var selected))
                    return false;
                var info = selected.Mesh;
                _instances.Remove(index);
            }
            if (stateChanged)
                await OnStateChanged(null);
            return true;
        }

        public virtual async Task SyncInstances(
            IEnumerable<(string fileHash, Func<CancellationToken, Task<Stream>> streamFactory, int count, float scale, float inset, object? userData, NestedRotationConstraints constraintsAroundZ, NestingTransformState? transformState)> instances,
            Func<string[], Exception, Task> loadError,
            bool removeUnreferencedMeshes,
            bool doNotKeepTransform,
            bool stateChanged = true,
            CancellationToken cancel = default)
        {
            var differences = new Dictionary<string, (Func<CancellationToken, Task<Stream>>? streamFactory, int requestedCount, int presentCount, List<(float scale, float inset, NestedRotationConstraints constraintsAroundZ, NestingTransformState? transformState, object? userData)> data)>();
            var removedInstances = new List<long>();
            foreach (var group in instances.GroupBy(x => x.fileHash))
            {
                var first = group.First();
                differences.Add(group.Key, (
                    first.streamFactory,
                    group.Sum(x => x.count),
                    0,
                    group.SelectMany(x => Enumerable.Repeat((x.scale, x.inset, x.constraintsAroundZ, x.transformState, x.userData), x.count)).ToList())
                );
            }
            lock (_locker)
            {
                foreach (var presentGroup in _instances.Values.GroupBy(x => x.Mesh.Hash))
                {
                    if (differences.TryGetValue(presentGroup.Key, out var existing))
                    {
                        var presentCount = presentGroup.Count();
                        existing.presentCount = presentCount;
                        foreach (var tuple in presentGroup.Zip(existing.data))
                        {
                            if (!doNotKeepTransform)
                            {
                                tuple.First.TransformState = tuple.Second.transformState ??
                                    tuple.First.TransformState with { Scale = new Vector3(tuple.Second.scale) };
                            }
                            tuple.First.ConstraintsAroundZ = tuple.Second.constraintsAroundZ;
                            tuple.First.Inset = tuple.Second.inset;
                            tuple.First.UserData = tuple.Second.userData;
                        }
                        existing.data.RemoveRange(0, Math.Min(presentCount, existing.data.Count));
                        differences[presentGroup.Key] = existing;
                    }
                    else
                        differences[presentGroup.Key] = (null, 0, presentGroup.Count(), new());
                }
                foreach ((var fileHash, var diff) in differences)
                {
                    if (diff.requestedCount < diff.presentCount)
                    {
                        removedInstances.AddRange(_instances.Values
                            .Where(x => x.Mesh.Hash == fileHash)
                            .Reverse() // take the newest added
                            .Take(diff.presentCount - diff.requestedCount)
                            .Select(x => x.Index));
                    }
                }
                foreach (var index in removedInstances)
                    _instances.Remove(index);
            }
            var loadGroups = differences
                .Where(x => x.Value.requestedCount > x.Value.presentCount)
                .SelectMany(x => x.Value.data, (item, diffItem) => (fileHash: item.Key, diff: item.Value, diffItem))
                .GroupBy(x => (x.fileHash, x.diffItem.scale, x.diffItem.constraintsAroundZ, x.diffItem.inset, x.diffItem.transformState, userData: ValueByReference.Create(x.diffItem.userData)));
            var fails = new ConcurrentDictionary<string, bool>();
            try
            {
                foreach (var meshGroup in loadGroups.GroupBy(x => x.Key.fileHash))
                {
                    foreach (var group in meshGroup)
                    {
                        try
                        {
                            await LoadInstances(
                                meshGroup.Key,
                                group.First().diff.streamFactory!,
                                group.Count(),
                                group.Key.scale,
                                group.Key.constraintsAroundZ,
                                group.Key.inset,
                                null,
                                group.Key.transformState,
                                group.Key.userData.Value,
                                stateChanged: false,
                                cancel: cancel);
                        }
                        catch
                        {
                            fails.TryAdd(group.Key.fileHash, true);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex) when (!cancel.IsCancellationRequested)
            {
                await loadError(fails.Keys.Order().ToArray(), ex);
            }
            if (removeUnreferencedMeshes)
                RemoveUnreferencedMeshes();
            if (stateChanged)
                await OnStateChanged(null);
        }

        public virtual async Task ClearInstances(bool includeMeshes, bool stateChanged = true)
        {
            lock (_locker)
            {
                _instances.Clear();
                if (includeMeshes)
                    _meshes.Clear();
            }
            if (stateChanged)
                await OnStateChanged(null);
        }

        private async Task<NestingMesh> GetMeshInfo(string fileHash, Func<CancellationToken, Task<Stream>> streamFactory, CancellationToken cancel)
        {
            lock (_locker)
            {
                if (_meshes.TryGetValue(fileHash, out var info))
                    return info;
            }

            var options = _options.Value;
            var mesh = await Task.Run(() => _meshLoader.Load(streamFactory, cancel)).WaitAsync(cancel);
            mesh.Center();
            var bounds = mesh.GetBounds();
            var nestingMeshSource = new TaskCompletionSource<NestingMesh>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                NestingMesh? nestingMesh = null;
                try
                {
                    foreach (var simplified in mesh.SimplifyClone(options.SimplifyTriangleCounts.OrderDescending().ToArray()))
                    {
                        if (nestingMesh == null)
                        {
                            cancel.ThrowIfCancellationRequested(); // NOTE: cancellable just to the point we return the "first" result

                            nestingMesh = new NestingMesh(mesh, new[] { simplified }, fileHash, bounds);
                            var isConflict = false;
                            lock (_locker)
                            {
                                if (!_meshes.TryAdd(fileHash, nestingMesh))
                                {
                                    nestingMesh = _meshes[fileHash];
                                    isConflict = true;
                                }
                            }
                            nestingMeshSource.TrySetResult(nestingMesh);
                            if (isConflict)
                                break;
                        }
                        else
                        {
                            nestingMesh.SimplifiedMeshes = nestingMesh.SimplifiedMeshes.Append(simplified).ToArray();
                            await OnStateChanged(null);
                        }
                    }
                    if (nestingMesh == null)
                    {
                        nestingMesh = new NestingMesh(mesh, new[] { mesh }, fileHash, bounds);
                        lock (_locker)
                        {
                            if (!_meshes.TryAdd(fileHash, nestingMesh))
                                nestingMesh = _meshes[fileHash];
                        }
                        nestingMeshSource.TrySetResult(nestingMesh);
                    }
                }
                catch (Exception ex)
                {
                    nestingMeshSource.TrySetException(ex);
                }
            });
            return await nestingMeshSource.Task;
        }

        public virtual async Task LoadInstances(
            Stream stream,
            int quanity,
            float scale,
            NestedRotationConstraints constraintsAroundZ,
            float inset,
            MeshTransform? transform,
            Vector3? position,
            object? userData,
            bool stateChanged = true,
            CancellationToken cancel = default)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancel); // copy to MemoryStream, since we will do synchronous reads, and those are not valid for web data
            ms.Position = 0;
            string fileHash;
            using (var sha = SHA1.Create())
            {
                fileHash = Convert.ToBase64String(sha.ComputeHash(ms));
            }
            await LoadInstances(
                fileHash,
                cancel => Task.FromResult<Stream>(new MemoryStream(ms.GetBuffer(), 0, (int)ms.Length, false)),
                quanity,
                scale,
                constraintsAroundZ,
                inset,
                position,
                transform,
                userData,
                stateChanged,
                cancel: cancel);
        }

        public virtual async Task LoadInstances(
            string fileHash,
            Func<CancellationToken, Task<Stream>> streamFactory,
            int quantity,
            float scale,
            NestedRotationConstraints constraintsAroundZ,
            float inset,
            Vector3? position,
            MeshTransform? transform,
            object? userData,
            bool stateChanged = true,
            CancellationToken cancel = default)
        {
            var info = await GetMeshInfo(fileHash, streamFactory, cancel);
            lock (_locker)
            {
                for (int q = quantity; q > 0; q--)
                {
                    var instanceColor = NewInstanceColorInner();
                    var xr = position?.X ?? _random.NextSingle() * (NestingDim.SizeX - info.Bounds.Size.X * scale);
                    var yr = position?.Z ?? _random.NextSingle() * (NestingDim.SizeZ - info.Bounds.Size.Z * scale);
                    var zr = position?.Y ?? _random.NextSingle() * (NestingDim.SizeY - info.Bounds.Size.Y * scale);
                    var offset = -0.5f * new Vector3(NestingDim.SizeX, 0, NestingDim.SizeY) +
                        0.5f * new Vector3(info.Bounds.Size.X, 0, info.Bounds.Size.Y) * scale +
                        new Vector3(MeshMargin, MeshMargin, MeshMargin);
                    var transformState = transform ?? new MeshTransform
                    {
                        Position = new Vector3(xr, 0.5f * info.Bounds.Size.Z * scale, zr) + offset,
                        Scale = new Vector3(scale),
                    };
                    var transformStateOverlapping = new MeshTransform
                    {
                        Position = new Vector3(xr, yr, zr) + offset - new Vector3(Math.Max(NestingDim.SizeX, info.Bounds.Size.X * scale) * 1.1f, 0, 0),
                        Scale = new Vector3(scale),
                    };
                    var working = new NestingInstance
                    {
                        Index = GetNextInstanceIndexNeedsLock(),
                        Mesh = info,
                        TransformState = transformState,
                        TransformStateOverlapping = transformStateOverlapping,
                        IsOverlapping = false,
                        Color = instanceColor,
                        UserData = userData,
                        ConstraintsAroundZ = constraintsAroundZ,
                        Inset = inset,
                    };
                    _instances.Add(working.Index, working);
                }
            }
            if (stateChanged)
                await OnStateChanged(null);
        }

        protected long GetNextInstanceIndexNeedsLock()
            => ++_lastInstanceIndex;

        protected RgbaF NewInstanceColorInner()
        {
            while (true)
            {
                var c = new RgbaF(
                    _random.NextSingle(),
                    _random.NextSingle(),
                    _random.NextSingle());
                if (c.R > c.G && c.R > c.B) // too much red
                    continue;
                var min = c.RGBMin;
                if (min < 0.2) // too dark
                    continue;
                return c;
            }
        }

        protected async Task<Dictionary<NestingInstance, MeshTransform>> GetStates(GetNestingTransform getState)
        {
            NestingInstance[] initInstances;
            lock (_locker)
            {
                initInstances = _instances.Values.Select(x => x with { }).ToArray();
            }
            var states = new Dictionary<NestingInstance, MeshTransform>();
            foreach (var instance in initInstances)
            {
                var state = await getState(instance);
                states.Add(instance, state);
            }
            return states;
        }

        public virtual async Task<NestingStats> RunNesting(NestingServiceContext context, NestingFlags flags, GetNestingTransform getState, CancellationToken cancel = default)
        {
            var states = await GetStates(getState);
            var nester = BorrowNester();
            try
            {
                var map = new Dictionary<NestingInstance, INestedObject>();
                lock (_locker)
                {
                    nester.ClearObjects();
                    foreach (var working in _instances.Values)
                    {
                        if (!states.TryGetValue(working, out var transformState))
                            continue;
                        INestedObject nested;
                        if (working.Nested != null)
                            nested = nester.AddObject(working.Nested);
                        else
                            nested = nester.AddObject(working.Mesh.Mesh, working.Mesh.Hash);
                        nested.MeshTransform = Matrix4x4.CreateScale(transformState.Scale);
                        nested.ConstraintsAroundZ = working.ConstraintsAroundZ;
                        nested.Inset = working.Inset;
                        map.Add(working, nested);
                    }
                }
                var nextingContext = CreateNestingContext(context);
                var res = await Task.Run(() => nester.RunNesting(nextingContext, flags, cancel));
                lock (_locker)
                {
                    foreach (var pair in _instances)
                    {
                        var working = pair.Value;
                        if (!map.TryGetValue(working, out var item))
                            continue;
                        var transform = item.MeshTransform;
                        const int roundDigits = 3; // helps to reduce precision errors
                        MeshTransform transformState;
                        if (item.Status == NestedObjectStatus.Succeeded)
                        {
                            transformState = new MeshTransform(
                                MatrixHelpers.GetTranslation(transform).Round(roundDigits) - new Vector3(NestingDim.SizeX, 0, NestingDim.SizeY) * 0.5f,
                                MatrixHelpers.GetRotationQuaternion(transform),
                                MatrixHelpers.GetScale(transform).Round(roundDigits)
                            );
                        }
                        else // instance was not nested, place it to some predictable out of range position
                        {
                            var info = working.Mesh;
                            transformState = working.TransformStateOverlapping;
                        }
                        working.TransformState = transformState;
                        working.Nested = item;
                        working.IsOverlapping = item.Status != NestedObjectStatus.Succeeded;
                    }
                }
                IncrementVoxelChamberVersion();
                await OnStateChanged(nester);
                return res;
            }
            finally
            {
                ReturnNester(nester);
            }
        }

        protected void IncrementVoxelChamberVersion()
        {
            Interlocked.Increment(ref _voxelChamberVersion);
        }

        protected virtual NestingContext CreateNestingContext(NestingServiceContext context)
        {
            return new NestingContext(
                ShrinkageCorrection: context.ShrinkageCorrection,
                LimitThickness: context.LimitThickness,
                IgnoreOverlaps: context.IgnoreOverlaps,
                OnStatus: _collapseTask.UpdateProgress);
        }

        protected virtual async Task OnStateChanged(INester? nester)
        {
            if (nester != null)
                _voxelChamber = (nester as ExhaustiveNester)?.Chamber;
            await StateChanged.Invoke(default);
        }

        protected void ReturnNester(INester nester)
        {
            nester.ClearObjects(); // free memory
            lock (_locker)
            {
                if (_nesterBag.Count == 0)
                {
                    if (nester is ExhaustiveNester nester2)
                    {
                        _nesterBag.Push(nester2);
                        return;
                    }
                }
            }
            _nesterFactory.DestroyObject(nester);
        }

        protected INester BorrowNester()
        {
            if (!_nesterBag.TryPop(out var res))
                res = _nesterFactory.CreateObject();
            res.NestingDim = NestingDim;
            res.MeshMargin = MeshMargin;
            return res;
        }

        public virtual async Task<NestingStats> RunCheckCollision(NestingServiceContext context, GetNestingTransform getState, bool recalculateValues = false, CancellationToken cancel = default)
        {
            var states = await GetStates(getState);
            var nester = BorrowNester();
            try
            {
                var map = new Dictionary<NestingInstance, (INestedObject nested, MeshTransform state)>();
                lock (_locker)
                {
                    nester.ClearObjects();
                    foreach (var working in _instances.Values)
                    {
                        if (!states.TryGetValue(working, out var transformState))
                            continue;
                        INestedObject nested;
                        if (working.Nested != null)
                            nested = nester.AddObject(working.Nested);
                        else
                            nested = nester.AddObject(working.Mesh.Mesh, working.Mesh.Hash);
                        nested.MeshTransform = transformState.GetMeshMatrix(
                            new Vector3(NestingDim.SizeX, 0, NestingDim.SizeY) * 0.5f);
                        map.Add(working, (nested, transformState));
                    }
                }
                var nestingContext = CreateNestingContext(context);
                var res = await Task.Run(() => nester.RunCheckOnly(nestingContext, recalculateValues, cancel));
                lock (_locker)
                {
                    foreach (var working in _instances.Values)
                    {
                        if (!map.TryGetValue(working, out var item))
                            continue;
                        (var nested, var transformState) = item;
                        working.TransformState = transformState;
                        working.Nested = nested;
                        working.IsOverlapping = nested.Status != NestedObjectStatus.Succeeded;
                    }
                }
                IncrementVoxelChamberVersion();
                await OnStateChanged(nester);
                return res;
            }
            finally
            {
                ReturnNester(nester);
            }
        }

        public virtual Mesh CreateChamberMesh(bool bottomOnly, bool noRadiuses)
        {
            var nester = BorrowNester();
            var res = nester.CreateChamberMesh(bottomOnly, noRadiuses);
            ReturnNester(nester);
            return res;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            INestingService.Services.Remove(Id);
        }
    }

    public class NestingServiceScoped : NestingService, INestingServiceScoped
    {
        public NestingServiceScoped(IOptions<NestingServiceOptions> options, IObjectFactory<INester, object> nesterFactory, IMeshLoader meshLoader)
            : base(options, nesterFactory, meshLoader)
        {
        }
    }
}
