// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Lexical.FileSystem;
using Lexical.FileSystem.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Components;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Scripts;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Storage.PrintJobs;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Processing.Volumes;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Validation;
using System.Data;
using static SLS4All.Compact.Pages.Jobs;
using SLS4All.Compact.Storage.PrintProfiles;
using System.Diagnostics.CodeAnalysis;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printing;
using System.Threading.Channels;
using SLS4All.Compact.PrintSessions;

namespace SLS4All.Compact.Pages
{
    partial class Jobs
    {
        public sealed class PrintProfileEntry
        {
            public PrintProfileReference Reference => new PrintProfileReference(Profile.Id);
            public string Name => Profile.Name;
            public PrintProfile Profile { get; set; } = default!;
            public bool IsValid { get; set; }
            public bool IsDefault { get; set; }
        }

        public abstract record class JobEntry
        {
            public abstract IPrintJob Job { get; }
            public Guid Id => Job.Id;
            public string Name
            {
                get => Job.Name;
                set => Job.Name = value;
            }
            public abstract PrintProfileReference PrintProfile { get; set; }
            public abstract IEnumerable<JobObjectEntry> Objects { get; set; }
            public abstract bool HighlightOverlapping { get; }

            public JobEntry CloneJob()
                => Create((IPrintJob)Job.Clone())!;

            [return: NotNullIfNotNull("obj")]
            public static implicit operator JobEntry?(AutomaticJob? obj)
                => obj != null ? new AutomaticJobEntry(obj) : null;
            [return: NotNullIfNotNull("obj")]
            public static implicit operator JobEntry?(ProfilingJob? obj)
                => obj != null ? new ProfilingJobEntry(obj) : null;

            [return: NotNullIfNotNull("obj")]
            public static JobEntry? Create(IPrintJob? job)
            {
                if (job == null)
                    return null;
                else if (job is AutomaticJob auto)
                    return auto;
                else if (job is ProfilingJob profiling)
                    return profiling;
                else
                    throw new ArgumentException("Invalid job type");
            }
        }

        public record class AutomaticJobEntry(AutomaticJob Obj) : JobEntry
        {
            public override IPrintJob Job => Obj;
            public override PrintProfileReference PrintProfile
            {
                get => Obj.PrintProfile;
                set => Obj.PrintProfile = value;
            }
            public override IEnumerable<JobObjectEntry> Objects
            {
                get => Obj.Objects.Select(x => new AutomaticJobObjectEntry(x));
                set => Obj.Objects = value.OfType<AutomaticJobObjectEntry>().Select(x => x.Obj).ToArray();
            }
            public override bool HighlightOverlapping => true;
        }

        public record class ProfilingJobEntry(ProfilingJob Obj) : JobEntry
        {
            public override IPrintJob Job => Obj;
            public override PrintProfileReference PrintProfile
            {
                get => Obj.PrintProfile;
                set => Obj.PrintProfile = value;
            }
            public override IEnumerable<JobObjectEntry> Objects
            {
                get => Obj.Objects.Select(x => new ProfilingJobObjectEntry(x));
                set => Obj.Objects = value.OfType<ProfilingJobObjectEntry>().Select(x => x.Obj).ToArray();
            }
            public override bool HighlightOverlapping => false;
        }

        public abstract record class JobObjectEntry
        {
            public abstract IEnumerable<JobObjectConstraintEntry> Constraints { get; set; }
            public abstract string Name { get; set; }


            public abstract void OnLoadError();

            [return: NotNullIfNotNull("obj")]
            public static implicit operator JobObjectEntry?(AutomaticJobObject? obj)
                => obj != null ? new AutomaticJobObjectEntry(obj) : null;
            [return: NotNullIfNotNull("obj")]
            public static implicit operator JobObjectEntry?(ProfilingJobObject? obj)
                => obj != null ? new ProfilingJobObjectEntry(obj) : null;

            [return: NotNullIfNotNull("obj")]
            public static JobObjectEntry? Create(IPrintJobObject? job)
            {
                if (job == null)
                    return null;
                else if (job is AutomaticJobObject auto)
                    return auto;
                else if (job is ProfilingJobObject profiling)
                    return profiling;
                else
                    throw new ArgumentException("Invalid job object type");
            }
        }

        public record class AutomaticJobObjectEntry(AutomaticJobObject Obj) : JobObjectEntry
        {
            public override string Name
            {
                get => Obj.Name;
                set => Obj.Name = value;
            }

            public override IEnumerable<JobObjectConstraintEntry> Constraints
            {
                get => Obj.Constraints.Select(x => new AutomaticJobObjectConstraintEntry(x));
                set => Obj.Constraints = value.OfType<AutomaticJobObjectConstraintEntry>().Select(x => x.Obj).ToArray();
            }

            public override void OnLoadError()
            {
                Obj.InstanceCount = 0;
            }
        }

        public record class ProfilingJobObjectEntry(ProfilingJobObject Obj) : JobObjectEntry
        {
            public override string Name
            {
                get => Obj.Name;
                set => Obj.Name = value;
            }

            public override IEnumerable<JobObjectConstraintEntry> Constraints
            {
                get => Array.Empty<JobObjectConstraintEntry>();
                set { }
            }

            public override void OnLoadError()
            {
                Obj.IsEnabled = false;
            }
        }

        public abstract record class JobObjectConstraintEntry
        {
            public abstract decimal BaseYaw { get; set; }
            public abstract decimal BasePitch { get; set; }
            public abstract decimal BaseRoll { get; set; }
            public abstract decimal Freedom { get; set; }
            public abstract AutomaticJobObjectConstraintAnyYawMode AllowAnyYawMode { get; set; }

            [return: NotNullIfNotNull("obj")]
            public static implicit operator JobObjectConstraintEntry?(AutomaticJobObjectConstraint? obj)
                => obj != null ? new AutomaticJobObjectConstraintEntry(obj) : null;
        }

        public record class AutomaticJobObjectConstraintEntry(AutomaticJobObjectConstraint Obj) : JobObjectConstraintEntry
        {
            public override decimal BaseYaw
            {
                get => Obj.BaseYaw;
                set => Obj.BaseYaw = value;
            }

            public override decimal BasePitch
            {
                get => Obj.BasePitch;
                set => Obj.BasePitch = value;
            }

            public override decimal BaseRoll
            {
                get => Obj.BaseRoll;
                set => Obj.BaseRoll = value;
            }

            public override decimal Freedom
            {
                get => Obj.Freedom;
                set => Obj.Freedom = value;
            }

            public override AutomaticJobObjectConstraintAnyYawMode AllowAnyYawMode
            {
                get => Obj.AllowAnyYawMode;
                set => Obj.AllowAnyYawMode = value;
            }
        }

        private sealed class MeshInfo
        {
            public IBabylonMesh[] LodMeshes = Array.Empty<IBabylonMesh>();
            public int CurrentLod;
        }

        private sealed record class ConstrainedInstanceInfo(
            JobObjectEntry Obj,
            MeshInfo MeshInfo,
            IBabylonInstancedMesh Instance,
            MeshTransform Transform,
            int Lod);

        internal sealed record SyncObject(
            string Name, 
            string Hash, 
            int? NestingPriority,
            int InstanceCount, 
            float Scale,
            float Inset,
            JobObjectPath Path, 
            object? UserData, 
            NestedRotationConstraints ConstraintsAroundZ, 
            NestingTransformState? TransformState);

        private ElementReference _canvas;
        private readonly AsyncLock _sync = new();
        private readonly Dictionary<string, MeshInfo> _meshes = new();
        private readonly SortedDictionary<long, IBabylonInstancedMesh> _instances = new();
        private readonly BackgroundTask _syncCollapse = new();
        private static readonly float? _edgeRendering = null; // NOTE: does not work correctly with instanced meshes
        private ConstrainedInstanceInfo? _constrainedInstance;
        private IBabylonNester _root = null!;
        private const int _constraintDecimals = 3;
        private bool? _chamberConstrainedInstance;
        private bool _playConstraint;
        private const float _playConstraintFullRotationAfter = 1.5f;
        private const float _playConstraintFreedomFactor = 5.0f;
        private const float _playConstraintIncrementPerSec = MathF.PI * 2 / _playConstraintFullRotationAfter;
        private IBabylonAbstractMesh? _chamberVoxelMesh;
        private long _chamberVoxelMeshVersion = -1;
        private bool _disposing;

        public bool IsNesting => Nesting.BackgroundTask.Status?.IsCompleted == false;
        public bool IsPrinting => PrintingGlobal.IsPrinting;
        public bool PlayConstraint => _playConstraint;

        private async ValueTask DestroyBabylonInner()
        {
            if (_chamberVoxelMesh != null)
            {
                await _chamberVoxelMesh.DisposeFromScene();
                await _chamberVoxelMesh.DisposeAsync();
                _chamberVoxelMesh = null;
            }
            if (_root != null && _instances.Count > 0)
                await _root.RemoveInstances(_instances.Values.ToArray());
            foreach (var instance in _instances.Values)
            {
                await instance.DisposeFromScene();
                await instance.DisposeAsync();
            }
            _instances.Clear();
            foreach (var pair in _meshes)
            {
                foreach (var item in pair.Value.LodMeshes)
                {
                    await item.DisposeFromScene();
                    await item.DisposeAsync();
                }
            }
            _meshes.Clear();
            if (_root != null)
            {
                await _root.Destroy();
                await _root.DisposeAsync();
                _root = null!;
            }
        }

        private async Task ResetRootInner()
        {
            var job = _job;
            Debug.Assert(job != null);
            await DestroyBabylonInner();
            _constrainedInstance = null;
            _chamberConstrainedInstance = null;
            // recreate root
            var profile = _printProfiles.FirstOrDefault(x => x.Reference == job.PrintProfile);
            if (profile?.IsValid == false)
                profile = null;
            _resetPrintProfile = profile?.Reference ?? default;
            if (profile != null && job.Job.AvailableDepth != null)
            {
                Nesting.NestingDim = new NestingDimension(
                    (float)profile.Profile.PrintableWidth!,
                    (float)profile.Profile.PrintableHeight!,
                    (float)job.Job.AvailableDepth.Value,
                    profile.Profile.PrintableXDiameter > 0 ? (float?)profile.Profile.PrintableXDiameter : null,
                    profile.Profile.PrintableYDiameter > 0 ? (float?)profile.Profile.PrintableYDiameter : null,
                    profile.Profile.CutCornerDistanceTopLeft > 0 ? (float?)profile.Profile.CutCornerDistanceTopLeft : null,
                    profile.Profile.CutCornerDistanceTopRight > 0 ? (float?)profile.Profile.CutCornerDistanceTopRight : null,
                    profile.Profile.CutCornerDistanceBottomLeft > 0 ? (float?)profile.Profile.CutCornerDistanceBottomLeft : null,
                    profile.Profile.CutCornerDistanceBottomRight > 0 ? (float?)profile.Profile.CutCornerDistanceBottomRight : null);
                Nesting.MeshMargin = (float)(profile.Profile.NestingSpacing! / 2);
            }
            var invScale = Vector2.One / MainLayout!.Scale;
            _root = await JSRuntime.CreateBabylonNester(_canvas, Nesting.NestingDim.Size, invScale, _self);
        }

        private async ValueTask<MeshInfo> CreateMeshInner(NestingMesh item, Action? beginWork, CancellationToken cancel)
        {
            if (_meshes.TryGetValue(item.Hash, out var res) &&
                res.LodMeshes.Length == item.SimplifiedMeshes.Length)
                return res;
            if (res == null)
                res = new MeshInfo();
            Array.Resize(ref res.LodMeshes, item.SimplifiedMeshes.Length);
            for (int i = 0; i < res.LodMeshes.Length; i++)
            {
                if (res.LodMeshes[i] == null)
                {
                    beginWork?.Invoke();
                    var input = item.SimplifiedMeshes[i];
                    res.LodMeshes[i] = await _root.AddMesh(
                        item.Hash,
                        input,
                        true, // NOTE: we need to use faceted meshes, since specifiing explicit normals does not work with non-uniform scaling and existingInstances
                        true,
                        false,
                        null,
                        null,
                        cancel);
                }
            }
            Debug.Assert(!_meshes.ContainsKey(item.Hash) || _meshes[item.Hash] == res);
            _meshes[item.Hash] = res;
            return res;
        }

        private MeshTransform ConstraintToTransform(
            NestingMesh nestingMesh,
            JobObjectConstraintEntry constraint)
        {
            var quaternion = Quaternion.CreateFromYawPitchRoll(
                (float)(constraint.BaseYaw / 180) * MathF.PI,
                (float)(constraint.BasePitch / 180) * MathF.PI,
                (float)(constraint.BaseRoll / 180) * MathF.PI);
            var scale = Nesting.NestingDim.SizeMin * 0.5f / nestingMesh.Bounds.SphereRadius;
            var matrix = (Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(quaternion)).FromToMeshTransform();
            var rotatedBounds = nestingMesh.Mesh.GetBounds(matrix);
            return new MeshTransform(
                new Vector3(0, -rotatedBounds.Min.Z, 0),
                new Vector3((float)(constraint.BasePitch / 180) * MathF.PI, (float)(constraint.BaseYaw / 180) * MathF.PI, (float)(constraint.BaseRoll / 180) * MathF.PI),
                new Vector3(scale));
        }

        public NestedRotationConstraint ConstraintToNested(
            JobObjectConstraintEntry constraint)
        {
            var matrix = Matrix4x4.CreateFromYawPitchRoll(
                (float)(constraint.BaseYaw / 180) * MathF.PI,
                (float)(constraint.BasePitch / 180) * MathF.PI,
                (float)(constraint.BaseRoll / 180) * MathF.PI);
            return new NestedRotationConstraint(matrix, (float)(constraint.Freedom / 180) * MathF.PI, constraint.AllowAnyYawMode);
        }

        private async Task RemoveConstrainedInstanceInner(Action? beginWork)
        {
            if (_constrainedInstance == null)
                return;
            beginWork?.Invoke();
            await _root.StopConstrainedInstanceAnimation();
            await _root.RemoveInstance(_constrainedInstance.Instance);
            await _constrainedInstance.Instance.DisposeAsync();
            _constrainedInstance = null;
            await _root.ClearGizmoMode();
        }

        private async ValueTask<bool> TrySyncConstrainedInstanceInner(JobObjectEntry? obj, JobObjectConstraintEntry? constraint, Action? beginWork, CancellationToken cancel)
        {
            var job = _job;
            if (obj == null || constraint == null || job == null)
                return false;
            var file = job.Job.TryGetObjectFileByName(obj.Name);
            if (file == null)
                return false;
            var nestingMesh = Nesting.TryGetMeshForSingleDisplay(file.Hash, MainLayout!.IsLocalSession);
            if (nestingMesh == null)
                return false;
            var transform = ConstraintToTransform(nestingMesh.Value.mesh, constraint);
            var play = _playConstraint && _selectedConstraint != null;
            if (!(_constrainedInstance != null &&
                 _constrainedInstance.Obj == obj &&
                 _constrainedInstance.Transform == transform))
            {
                var mesh = await CreateMeshInner(nestingMesh.Value.mesh, beginWork: null, cancel: cancel);

                if (_constrainedInstance == null ||
                    (_constrainedInstance.MeshInfo != mesh ||
                     _constrainedInstance.Lod != nestingMesh.Value.lod))
                {
                    await RemoveConstrainedInstanceInner(beginWork);
                    var instance = await _root.CreateInstance(
                        mesh.LodMeshes[nestingMesh.Value.lod],
                        "constrainedInstance",
                        new RgbaF(1, 1, 1, 1),
                        transform,
                        false,
                        _edgeRendering);
                    _constrainedInstance = new ConstrainedInstanceInfo(
                        obj,
                        mesh,
                        instance,
                        transform,
                        nestingMesh.Value.lod);
                    await _root.SetGizmoLocalMode(true);
                    await _root.SetRotationGizmoMode();
                    await _root.SelectInstance(instance);
                }
                else
                {
                    if (!play)
                        await _root.StopConstrainedInstanceAnimation();
                    await _root.SetInstancesState(new[]
                        {
                            _constrainedInstance.Instance,
                        },
                        new NestingTransformState[]
                        {
                            transform,
                        },
                        new[]
                        {
                            false
                        });
                    _constrainedInstance = _constrainedInstance with { Transform = transform };
                }
            }

            if (play)
            {
                var freedomFactorInput = _selectedConstraint!.Freedom <= 90
                    ? _selectedConstraint!.Freedom
                    : 180 - _selectedConstraint!.Freedom;
                var freedomFactor = (float)(freedomFactorInput / 90) * _playConstraintFreedomFactor + 1;
                var freedom = (float)(constraint.Freedom / 180) * MathF.PI;
                await _root.PlayConstrainedInstanceAnimation(
                    _constrainedInstance.Instance, 
                    _playConstraintIncrementPerSec / freedomFactor, 
                    freedom, 
                    freedom == 0 && _selectedConstraint.AllowAnyYawMode is not AutomaticJobObjectConstraintAnyYawMode.Disabled);
            }
            else
                await _root.StopConstrainedInstanceAnimation();
            return true;
        }

        private async Task SetStatesInner(IEnumerable<NestingInstance> items, bool highlightOverlapping, Action? beginWork, CancellationToken cancel)
        {
            var create = new Dictionary<long, (IBabylonMesh babylonMesh, string name, RgbaF color, NestingTransformState transformState, bool overlapping)>();
            var set = new Dictionary<IBabylonAbstractMesh, (NestingTransformState transform, bool overlapping)>();
            foreach (var item in items)
            {
                var isOverlapping = highlightOverlapping && item.IsOverlapping;
                cancel.ThrowIfCancellationRequested();
                if (!_instances.TryGetValue(item.Index, out var instance))
                {
                    var mesh = await CreateMeshInner(item.Mesh, beginWork, cancel);
                    //cancel.ThrowIfCancellationRequested();
                    //beginWork?.Invoke();
                    //instance = await _root.CreateInstance(
                    //    mesh.LodMeshes[0],
                    //    item.Index.ToString(),
                    //    item.Color,
                    //    item.TransformState,
                    //    item.IsOverlapping);
                    //_instances.Add(item.Index, instance);
                    create[item.Index] = (mesh.LodMeshes[0],
                        item.Index.ToString(),
                        item.Color,
                        item.TransformState,
                        isOverlapping);
                }
                else
                {
                    set[instance] = (item.TransformState, isOverlapping);
                }
            }
            if (create.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                beginWork?.Invoke();
                var instances = await _root.CreateInstances(
                    create.Values.Select(x => x.babylonMesh).ToArray(),
                    create.Values.Select(x => x.name).ToArray(),
                    create.Values.Select(x => x.color).ToArray(),
                    create.Values.Select(x => x.transformState).ToArray(),
                    create.Values.Select(x => x.overlapping).ToArray(),
                    create.Values.Select(x => _edgeRendering).ToArray());
                var i = 0;
                foreach (var pair in create)
                    _instances.Add(pair.Key, instances[i++]);
            }
            if (set.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                beginWork?.Invoke();
                await _root.SetInstancesState(
                    set.Keys.ToArray(), 
                    set.Values.Select(x => x.transform).ToArray(),
                    set.Values.Select(x => x.overlapping).ToArray());
            }
        }

        private async ValueTask SyncChamberMeshInner(Action? beginWork, CancellationToken cancel)
        {
            var chamberConstrainedInstance = ShouldShowConstrainedInstance;
            if (_chamberConstrainedInstance == chamberConstrainedInstance)
                return;
            beginWork?.Invoke();
            _chamberConstrainedInstance = chamberConstrainedInstance;
            if (chamberConstrainedInstance)
            {
                var chamberMesh = Nesting.CreateChamberMesh(chamberConstrainedInstance, noRadiuses: true);
                var chamberHandle = CreateChamberHandle(chamberMesh);
                await _root.ReplaceChamber(chamberMesh, chamberHandle, transparent: true, faceted: true, color: MainLayout!.BackgroundColor, cancel: cancel);
            }
            else
            {
                var chamberMesh = Nesting.CreateChamberMesh(chamberConstrainedInstance, noRadiuses: false);
                var chamberHandle = CreateChamberHandle(chamberMesh);
                await _root.ReplaceChamber(chamberMesh, chamberHandle, transparent: false, faceted: true, color: MainLayout!.BackgroundColor, cancel: cancel);
            }
        }

        private Mesh CreateChamberHandle(Mesh chamber)
        {
            var bounds = chamber.GetBounds();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indicies = new List<int>();

            int Add(
                float x, float y, float z,
                float u, float v)
            {
                var index = vertices.Count;
                vertices.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(u, v));
                return index;
            }
            void AddFace(int a, int b, int c, float nx, float ny, float nz)
            {
                indicies.Add(a);
                indicies.Add(b);
                indicies.Add(c);
                normals.Add(new Vector3(nx, ny, nz));
            }
            var length = bounds.Size.X / 6.4f;
            var a = Add(bounds.Min.X, bounds.Min.Y, bounds.Min.Z, 0, 1);
            var b = Add(bounds.Max.X, bounds.Min.Y, bounds.Min.Z, 1, 1);
            var c = Add(bounds.Min.X, bounds.Min.Y - length, bounds.Min.Z, 0, 0);
            var d = Add(bounds.Max.X, bounds.Min.Y - length, bounds.Min.Z, 1, 0);

            AddFace(a, b, c, 0, 0, 1);
            AddFace(b, c, d, 0, 0, 1);
            var mesh = new Mesh
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                Indicies = indicies.ToArray(),
                UVs = uvs.ToArray(),
            };
            return mesh;
        }

        public async Task Sync(bool wait = true, bool fromNester = false, bool nestingOnly = false, bool cancelPrev = false)
        {
            if (cancelPrev)
            {
                _syncCollapse.Cancel();
                await _syncCollapse.Wait();
            }
            await _syncCollapse.StartTask(
                new { FromNester = fromNester }, 
                cancel => SyncInner(fromNester, nestingOnly, cancel));
            if (wait)
                await _syncCollapse.Wait();
        }

        private async Task SyncInner(bool fromNester, bool nestingOnly, CancellationToken cancel)
        {
            if (!_hadFirstRender)
                return;

            IDisposable? workingDisposable = null;
            bool forceBeginWork = false;
            bool needsGC = false;
            void BeginWork()
            {
                var working = _working;
                if (working == null)
                    return;
                lock (working)
                {
                    if ((forceBeginWork || !fromNester) && workingDisposable == null && _working != null) // _working may be null if the page is initializing
                        workingDisposable = working.BeginWork();
                }
            }
            try
            {
                using (await _sync.LockAsync(cancel))
                {
                    if (_disposing) // NOTE: in lock
                        return;
                    var shouldShowConstrainedInstance = ShouldShowConstrainedInstance;
                    var job = _job;
                    if (job != null &&
                        _validationErrors.SelectMany(x => x.Value.Errors).All(x => x.severity < ValidationSeverity.Breaking))
                    {
                        // sync job with nester
                        if (!shouldShowConstrainedInstance && !fromNester)
                        {
                            var objects = new List<SyncObject>();
                            var automaticJob = (job as AutomaticJobEntry)?.Obj;
                            var profilingJob = (job as ProfilingJobEntry)?.Obj;
                            // NOTE: _currentJobAutomatic and _currentJobProfiling are not created yet in case we open the first job,
                            //       hence the static methods
                            if (automaticJob != null)
                                await CurrentJobAutomatic.SyncInner(this, JobStorage, automaticJob, objects);
                            else if (profilingJob != null)
                                await CurrentJobProfiling.SyncInner(this, JobStorage, profilingJob, objects);
                            var failedLoad = new List<(Exception ex, string name)>();
                            await Nesting.SyncInstances(objects.Select(item =>
                                {
                                    var streamFactory = async (CancellationToken cancel) =>
                                    {
                                        BeginWork();
                                        needsGC = true;
                                        return await JobStorage.GetObject(item.Path, cancel);
                                    };
                                    return (item.Hash, streamFactory, item.NestingPriority, item.InstanceCount, item.Scale, item.Inset, item.UserData, item.ConstraintsAroundZ, item.TransformState);
                                }), (fileHashes, ex) =>
                                {
                                    foreach (var fileHash in fileHashes)
                                    {
                                        var file = objects.First(x => x.Hash == fileHash);
                                        failedLoad.Add((ex, file.Name));
                                        foreach (var obj in job.Objects)
                                        {
                                            if (obj.Name == file.Name)
                                                obj.OnLoadError();
                                        }
                                    }
                                    return Task.CompletedTask;
                                },
                                removeUnreferencedMeshes: true,
                                doNotKeepTransform: fromNester,
                                stateChanged: false,
                                cancel: cancel);
                            if (failedLoad.Count > 0)
                            {
                                ToastProvider.Show(new ToastMessage
                                {
                                    Type = ToastMessageType.Error,
                                    HeaderText = "Cannot load object(s)",
                                    BodyText = string.Join(Environment.NewLine, failedLoad.Select(x => $"{x.name}: {x.ex.Message}")),
                                    Exception = new AggregateException(failedLoad.Select(x => x.ex).ToArray()),
                                });
                            }
                        }
                    }
                    else
                    {
                        await Nesting.ClearInstances(false, false);
                    }

                    if (!nestingOnly)
                    {
                        if (_root == null || (job != null && _resetPrintProfile != job.PrintProfile))
                        {
                            if (job == null)
                                return;
                            forceBeginWork = true;
                            BeginWork();
                            await ResetRootInner();
                        }
                        Debug.Assert(_root != null);

                        // sync chamber mesh
                        cancel.ThrowIfCancellationRequested();
                        await SyncChamberMeshInner(BeginWork, cancel);

                        if (shouldShowConstrainedInstance)
                        {
                            // remove any nesting instances
                            if (_instances.Count > 0)
                            {
                                cancel.ThrowIfCancellationRequested();
                                BeginWork();
                                await _root.RemoveInstances(_instances.Values.ToArray());
                                foreach (var instance in _instances.Values)
                                    await instance.DisposeAsync();
                                _instances.Clear();
                            }
                            // sync constrained instance
                            if (!await TrySyncConstrainedInstanceInner(
                                _selectedObject!,
                                _selectedConstraint!,
                                BeginWork,
                                cancel))
                            {
                                await RemoveConstrainedInstanceInner(BeginWork);
                            }
                        }
                        else
                        {
                            // sync nester with babylon
                            var nestingItems = Nesting.GetMeshesAndInstances(MainLayout!.IsLocalSession); // NOTE: returns meshes and instances together atomically (neccessary to avoid races)
                            var meshes = nestingItems.meshes.ToDictionary(x => x.mesh.Hash);
                            var instances = nestingItems.instances.ToDictionary(x => x.Index);
                            foreach (var item in meshes.Values)
                            {
                                cancel.ThrowIfCancellationRequested();
                                await CreateMeshInner(item.mesh, BeginWork, cancel);
                            }
                            await RemoveConstrainedInstanceInner(BeginWork);
                            // set instances
                            await SetStatesInner(instances.Values.OrderBy(x => x.Index), job?.HighlightOverlapping == true, BeginWork, cancel);
                            // remove babylon instances no longer present in nesting
                            var instancesToRemove = _instances
                                .Where(x => !instances.ContainsKey(x.Key))
                                .ToArray();
                            if (instancesToRemove.Length > 0)
                            {
                                cancel.ThrowIfCancellationRequested();
                                BeginWork();
                                await _root.RemoveInstances(instancesToRemove.Select(x => x.Value).ToArray());
                                foreach (var pair in instancesToRemove)
                                {
                                    _instances.Remove(pair.Key);
                                    await pair.Value.DisposeFromScene();
                                    await pair.Value.DisposeAsync();
                                }
                            }

                            // remove babylon meshes no longer present in nesting
                            foreach (var pair in _meshes.ToArray())
                            {
                                if (!meshes.ContainsKey(pair.Key))
                                {
                                    _meshes.Remove(pair.Key);
                                    foreach (var item in pair.Value.LodMeshes)
                                    {
                                        cancel.ThrowIfCancellationRequested();
                                        BeginWork();
                                        await item.DisposeFromScene();
                                        await item.DisposeAsync();
                                    }
                                }
                            }

                            // set lod
                            foreach (var pair in _meshes)
                            {
                                cancel.ThrowIfCancellationRequested();
                                var lod = meshes[pair.Key].lod;
                                if (pair.Value.CurrentLod != lod)
                                {
                                    pair.Value.CurrentLod = lod;
                                    await _root.SetMeshLod(pair.Value.LodMeshes, lod);
                                }
                            }
                        }

                        // sync selection
                        if (!shouldShowConstrainedInstance)
                        {
                            IBabylonInstancedMesh? selectedInstancedMesh = null;
                            var selectedNestingInstance = Nesting.GetFirstInstanceWithUserData(_selectedObject);
                            if (selectedNestingInstance != null)
                            {
                                _instances.TryGetValue(selectedNestingInstance.Index, out selectedInstancedMesh);
                            }
                            if (selectedInstancedMesh != null)
                                await _root.SelectInstance(selectedInstancedMesh);
                        }

                        cancel.ThrowIfCancellationRequested();
                        await UpdateChamberVoxelMeshInner(cancel);
                        await _root.StartRenderLoop();
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is JSException || ex is JSDisconnectedException || ex is OperationCanceledException) // may happen on page change
            {
                // swallow
            }
            finally
            {
                if (needsGC)
                    PrinterGC.CollectGarbageBlockingAggressive(); // cleanup LOH
                if (workingDisposable != null)
                    workingDisposable.Dispose();
            }
        }

        [JSInvokable("onGizmoDragEnd")]
        public async Task OnGizmoDragEnd()
        {
            if (ShouldShowConstrainedInstance && _constrainedInstance != null && _selectedConstraint != null && !_playConstraint)
            {
                MeshTransform transform;
                using (await _sync.LockAsync())
                {
                    transform = await _root.GetTransformState(_constrainedInstance.Instance);
                }
                Matrix4x4 matrix;
                if (!transform.IsQuaternionEmpty)
                {
                    matrix = Matrix4x4.CreateFromQuaternion(transform.Quaternion);
                    matrix = matrix.RoundZeroOneEpsilon().FromToMeshTransform();
                    var rotation = matrix.GetRotationYawPitchRoll();
                    _selectedConstraint.BaseYaw = (rotation.yaw * 180 / MathF.PI).RoundToDecimal(_constraintDecimals);
                    _selectedConstraint.BasePitch = (rotation.pitch * 180 / MathF.PI).RoundToDecimal(_constraintDecimals);
                    _selectedConstraint.BaseRoll = (rotation.roll * 180 / MathF.PI).RoundToDecimal(_constraintDecimals);
                }
                else
                {
                    _selectedConstraint.BaseYaw = (transform.Rotation.Y * 180 / MathF.PI).RoundToDecimal(_constraintDecimals);
                    _selectedConstraint.BasePitch = (transform.Rotation.X * 180 / MathF.PI).RoundToDecimal(_constraintDecimals);
                    _selectedConstraint.BaseRoll = (transform.Rotation.Z * 180 / MathF.PI).RoundToDecimal(_constraintDecimals);
                }
                StateHasChanged();
                // validate and sync in separate task to avoid deadlock in Sync
                _ = Task.Delay(TimeSpan.FromSeconds(0.1)).ContinueWith(prev =>
                {
                    TryInvokeStateHasChanged(async () =>
                    {
                        await ValidateInner(true);
                    });
                }, TaskScheduler.Current);
            }
        }

        [JSInvokable("onGizmoAttachedToMesh")]
        public void OnGizmoAttachedToMesh(string? name)
        {
            if (name != null && long.TryParse(name, out var index) && Nesting.TryGetInstance(index, out var instance))
            {
                var obj = (JobObjectEntry)instance.UserData!;
                _selectedObject = obj;
                StateHasChanged();
            }
        }

        private async Task<PrintingObject[]> GetJobNestedSlicingMeshesForPrint(PrintSetup setup)
        {
            await Sync(nestingOnly: true);
            var job = _job;
            if (job == null)
                return Array.Empty<PrintingObject>();
            var res = new List<PrintingObject>();
            var automaticJob = (job as AutomaticJobEntry)?.Obj;
            var profilingJob = (job as ProfilingJobEntry)?.Obj;
            if (automaticJob != null)
            {
                foreach (var instance in automaticJob.NestingState.Instances)
                {
                    var mesh = Nesting.TryGetMeshData(instance.Hash);
                    if (mesh == null) // should not happen
                    {
                        Debug.Assert(false);
                        continue;
                    }
                    var isThinObject = instance.ObjectCopy?.IsThinObject == true;
                    var overrideSetup = (PrintSetup setup) =>
                    {
                        var objSetup = setup.Clone();
                        objSetup.IsThinObject = isThinObject;
                        return objSetup;
                    };
                    res.Add(new PrintingObject(instance.Hash, mesh, instance.MeshPrintTransform, overrideSetup));
                }
            }
            else if (profilingJob != null)
            {
                foreach (var item in profilingJob.Objects)
                {
                    if (item.IsEmpty || !item.IsEnabled) // disabled or invalid
                        continue;
                    var file = profilingJob.TryGetObjectFileByName(item.Name);
                    if (file == null) // should not happen
                    {
                        Debug.Assert(false);
                        continue;
                    }
                    var mesh = Nesting.TryGetMeshData(file.Hash);
                    if (mesh == null) // should not happen
                    {
                        Debug.Assert(false);
                        continue;
                    }
                    var source = item.Clone();
                    var overrideSetup = (PrintSetup setup) =>
                    {
                        var objSetup = setup.Clone();
                        if (source.LaserOnPercent != null)
                            objSetup.LaserOnPercent = source.LaserOnPercent.Value;
                        if (source.LaserFirstOutlineEnergyDensity != null)
                            objSetup.LaserFirstOutlineEnergyDensity = source.LaserFirstOutlineEnergyDensity.Value;
                        if (source.LaserOtherOutlineEnergyDensity != null)
                            objSetup.LaserOtherOutlineEnergyDensity = source.LaserOtherOutlineEnergyDensity.Value;
                        if (source.LaserFillEnergyDensity != null)
                            objSetup.LaserFillEnergyDensity = source.LaserFillEnergyDensity.Value;
                        if (source.OutlineCount != null)
                            objSetup.OutlineCount = source.OutlineCount.Value;
                        if (source.FillOutlineSkipCount != null)
                            objSetup.FillOutlineSkipCount = source.FillOutlineSkipCount.Value;
                        if (source.HotspotOverlapPercent != null)
                            objSetup.HotspotOverlapPercent = source.HotspotOverlapPercent.Value;
                        if (source.OutlinePowerPrecision != null)
                            objSetup.OutlinePowerPrecision = source.OutlinePowerPrecision.Value;
                        if (source.OutlinePowerIncrease != null)
                            objSetup.OutlinePowerIncrease = source.OutlinePowerIncrease.Value;
                        objSetup.IsThinObject = source.IsThinObject;
                        objSetup.FillPhase += source.FillPhase;
                        return objSetup;
                    };
                    res.Add(new PrintingObject(file.Hash, mesh, source.MeshPrintTransform, overrideSetup));
                }
            }
            return res.ToArray();
        }

        private async ValueTask UpdateChamberVoxelMeshInner(CancellationToken cancel)
        {
            if (!FrontendOptions.CurrentValue.ShowAdvancedNestingFeatures)
                return;
            var whole = Nesting.VoxelChamber;
            var version = Nesting.VoxelChamberVersion;
            if (whole != null)
            {
                if (_chamberVoxelMeshVersion != version)
                {
                    //whole.Set(0, 0, 0);
                    //whole.Set(whole.XSize - 1, 0, 0);
                    //whole.Set(0, whole.YSize - 1, 0);
                    //whole.Set(whole.XSize - 1, whole.YSize - 1, 0);
                    //whole.Set(0, 0, whole.ZSize - 1);
                    //whole.Set(whole.XSize - 1, 0, whole.ZSize - 1);
                    //whole.Set(0, whole.YSize - 1, whole.ZSize - 1);
                    //whole.Set(whole.XSize - 1, whole.YSize - 1, whole.ZSize - 1);
                    await _root.RenderLock(async () =>
                    {
                        if (_chamberVoxelMesh != null)
                        {
                            await _chamberVoxelMesh.DisposeFromScene();
                            await _chamberVoxelMesh.DisposeAsync();
                        }
                        var chamberMesh = whole.Buffer.GenerateMesh(Nesting.ChamberStep, true);
                        var chamberMeshDim = whole.Dim;
                        _chamberVoxelMesh = await _root.AddMesh("chamberVoxel", chamberMesh, true, false, true, null, null, cancel);
                        await _chamberVoxelMesh.Position(new Vector3(chamberMeshDim.SizeX * 0.5f + 20, 0, -chamberMeshDim.SizeY * 0.5f));
                    });
                    _chamberVoxelMeshVersion = version;
                }
            }
            else if (_chamberVoxelMesh != null)
            {
                await _chamberVoxelMesh.DisposeFromScene();
                await _chamberVoxelMesh.DisposeAsync();
                _chamberVoxelMesh = null;
            }
        }

        private async Task<PrintingParameters?> TryGetParametersBeforePrint()
        {
            var job = _job;
            if (job == null)
            {
                ToastProvider.Show(new ToastMessage
                {
                    HeaderText = "Invalid job",
                    BodyText = "Job is missing",
                    Type = ToastMessageType.Error,
                    Key = this,
                });
                return default;
            }
            var context = ValidationContextFactory.CreateContext();
            var powerSettings = SettingsStorage.GetPowerSettingsDefaults();
            powerSettings.MergeFrom(SettingsStorage.GetPowerSettings());
            var powerSettingsValidation = await powerSettings.Validate(context);
            if (!powerSettingsValidation!.IsValid)
            {
                ToastProvider.Show(new ToastMessage
                {
                    HeaderText = "Invalid settings",
                    BodyText = "Printer power settings contain errors",
                    Type = ToastMessageType.Error,
                    Key = this,
                });
                return default;
            }
            var profile = await ProfileStorage.TryGetMergedProfile(job.PrintProfile.Id);
            var profileValidation = profile != null ? await profile.Validate(context) : null;
            if (profile == null || !profileValidation!.IsValid)
            {
                ToastProvider.Show(new ToastMessage
                {
                    HeaderText = "Invalid print profile",
                    BodyText = "Print profile is missing or contains errors",
                    Type = ToastMessageType.Error,
                    Key = this,
                });
                return default;
            }
            Debug.Assert(profile != null);
            var jobValidation = await job.Job.Validate(context);
            if (!jobValidation.IsValid)
            {
                ToastProvider.Show(new ToastMessage
                {
                    HeaderText = "Incomplete job",
                    BodyText = "Job is incomplete or contains errors",
                    Type = ToastMessageType.Error,
                    Key = this,
                });
                return default;
            }
            await Sync(nestingOnly: true, cancelPrev: true);
            var setup = await PrintingGlobal.CreateSetup(job.Job, profile, powerSettings);
            var instances = await GetJobNestedSlicingMeshesForPrint(setup);
            return new PrintingParameters(
                powerSettings, 
                profile, 
                instances, 
                job.Job is ProfilingJob profiling && profiling.LimitThickness != null, 
                setup,
                job.Job);
        }

        public override async ValueTask DisposeAsync()
        {
            // save first
            await Save(false);

            // dispose
            _disposing = true;
            _syncCollapse.Cancel();
            using (await _sync.LockAsync()) // ensure we are not syncing
            {
                await DestroyBabylonInner();
            }

            _playConstraint = false;
            Nesting.StateChanged.RemoveHandler(OnProviderStateChanged);
            Nesting.BackgroundTask.StateChanged.RemoveHandler(TryInvokeStateHasChangedAsync);
            JobStorage.JobUpdatedEvent.RemoveHandler(OnStorageJobUpdated);
            _self?.Dispose();
            _locationChangingSubscription?.Dispose();
            await CollectGarbage(browserOnly: true);

            await base.DisposeAsync();
        }

        private async Task CollectGarbage(bool browserOnly = false, CancellationToken cancel = default)
        {
            await JSRuntime.CollectGarbage(cancel);
            if (!browserOnly)
                PrinterGC.CollectGarbageBlockingAggressive(); // cleanup after/before LOH objecs are created
        }
    }
}

