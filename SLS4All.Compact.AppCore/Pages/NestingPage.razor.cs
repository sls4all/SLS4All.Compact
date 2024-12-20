// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Scripts;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Processing.Volumes;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.Printing;

namespace SLS4All.Compact.Pages
{
    public partial class NestingPage
    {
        private sealed class MeshInfo
        {
            public IBabylonMesh[] LodMeshes = Array.Empty<IBabylonMesh>();
            public int CurrentLod;
        }

        private readonly Dictionary<string, MeshInfo> _meshes = new();
        private readonly SortedDictionary<long, IBabylonInstancedMesh> _instances = new();
        private readonly AsyncLock _asyncLock = new();
        private long? _selectedMeshIndex;
        private DotNetObjectReference<NestingPage> _self = null!;
        private IBabylonAbstractMesh? _chamberVoxelMesh;
        private long _chamberVoxelMeshVersion = -1;
        private ElementReference _canvas;
        private IBabylonNester _root = null!;
        private bool _isGizmoLocalMode = false;
        private NestingStats? _nestingStats;

        public int? Quantity { get; set; }
        public bool Aggressive { get; set; }
        public float Scale { get; set; } = 100;

        public bool IsGizmoLocalMode
        {
            get => _isGizmoLocalMode;
            set
            {
                if (_isGizmoLocalMode == value)
                    return;
                _isGizmoLocalMode = value;
                _ = _root.SetGizmoLocalMode(value);
            }
        }

        [Inject]
        protected IJSRuntime JSRuntime { get; set; } = null!;

        [Inject]
        protected NavigationManager Navigation { get; set; } = null!;

        [Inject]
        protected INestingService Nesting { get; set; } = null!;

        [Inject]
        protected IPrintingService Printing { get; set; } = null!;

        [Inject]
        protected ILogger<NestingPage> Logger { get; set; } = null!;

        [Inject]
        protected IOptionsMonitor<FrontendOptions> FrontendOptions { get; set; } = null!;

        protected override Task OnInitializedAsync()
        {
            _self = DotNetObjectReference.Create(this);
            return base.OnInitializedAsync();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
            if (firstRender)
            {
                var chamberMesh = Nesting.CreateChamberMesh(false, false);
                var invScale = Vector2.One / MainLayout!.Scale;
                _root = await JSRuntime.CreateBabylonNester(_canvas, Nesting.NestingDim.Size, invScale, _self);
                await _root.ReplaceChamber(chamberMesh, null, false, true, color: MainLayout.BackgroundColor, cancel: default);
                await _root.SetGizmoLocalMode(_isGizmoLocalMode);
                await _root.SetPositionGizmoMode();
                Nesting.BackgroundTask.StateChanged.AddHandler(TryInvokeStateHasChangedAsync);
                Nesting.StateChanged.AddHandler(OnProviderStateChanged);
                await Sync();
            }
        }

        private Task OnProviderStateChanged(CancellationToken cancel)
        {
            // sync asynchronously without await, not to block the caller with other "windows"
            _ = InvokeAsync(async () =>
            {
                try
                {
                    await Sync();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Failed to sync");
                }
            });
            return Task.CompletedTask;
        }

        private async Task<MeshInfo> CreateMeshInner(NestingMesh item)
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
                    var input = item.SimplifiedMeshes[i];
                    res.LodMeshes[i] = await _root.AddMesh(
                        item.Hash,
                        input,
                        true, // NOTE: we need to use faceted meshes, since specifiing explicit normals does not work with non-uniform scaling and instances
                        true,
                        false,
                        null,
                        null,
                        cancel: default);
                }
            }
            _meshes[item.Hash] = res;
            return res;
        }

        private async Task<IBabylonInstancedMesh> CreateInstanceInner(NestingInstance item)
        {
            if (_instances.TryGetValue(item.Index, out var instance))
                return instance;
            var mesh = await CreateMeshInner(item.Mesh);
            instance = await _root.CreateInstance(
                mesh.LodMeshes[0],
                item.Index.ToString(),
                item.Color,
                item.TransformState,
                item.IsOverlapping,
                null);
            _instances.Add(item.Index, instance);
            return instance;
        }

        private async Task Sync()
        {
            using (await _asyncLock.LockAsync())
            {
                var instances = Nesting.GetInstances().ToDictionary(x => x.Index);
                var meshes = Nesting.GetMeshes(out _, MainLayout!.IsLocalSession).ToDictionary(x => x.mesh.Hash);
                foreach (var item in meshes.Values)
                {
                    await CreateMeshInner(item.mesh);
                }
                await SetStatesInner(instances.Values);
                // remove instances
                foreach (var pair in _instances.ToArray())
                {
                    if (!instances.ContainsKey(pair.Key))
                    {
                        _instances.Remove(pair.Key);
                        await _root.RemoveInstance(pair.Value);
                        await pair.Value.DisposeAsync();
                    }
                }
                // remove meshes
                foreach (var pair in _meshes.ToArray())
                {
                    if (!meshes.ContainsKey(pair.Key))
                    {
                        foreach (var item in pair.Value.LodMeshes)
                        {
                            _meshes.Remove(pair.Key);
                            await item.DisposeFromScene();
                            await item.DisposeAsync();
                        }
                    }
                }
                // set lod
                foreach (var pair in _meshes)
                {
                    var lod = meshes[pair.Key].lod;
                    if (pair.Value.CurrentLod != lod)
                    {
                        pair.Value.CurrentLod = lod;
                        await _root.SetMeshLod(pair.Value.LodMeshes, lod);
                    }
                }
                await UpdateChamberVoxelMeshInner();
                await _root.StartRenderLoop();
            }
            StateHasChanged();
        }

        [JSInvokable("onGizmoDragEnd")]
        public Task OnGizmoDragEnd()
            => StartCheckCollision();

        [JSInvokable("onGizmoAttachedToMesh")]
        public void OnGizmoAttachedToMesh(string? name)
        {
            var index = name != null ? (long?)long.Parse(name) : null;
            if (index != null && Nesting.ContainsInstance(index.Value))
                _selectedMeshIndex = index.Value;
            else
                _selectedMeshIndex = null;
        }

        private async Task RemoveClick()
        {
            var selectedIndex = _selectedMeshIndex;
            if (selectedIndex == null)
                return;
            await Nesting.RemoveInstance(selectedIndex.Value);
            var next = Nesting.GetFirstInstanceName();
            if (next != null && _instances.TryGetValue(next.Value, out var instance))
                await _root.SelectInstance(instance);
            await StartCheckCollision();
        }

        private async Task ClearClick()
        {
            _selectedMeshIndex = null;
            await Nesting.ClearInstances(true);
        }

        private async Task MoveClick()
        {
            await _root.SetPositionGizmoMode();
        }

        private async Task ScaleClick()
        {
            await _root.SetScaleGizmoMode();
        }

        private async Task RotateClick()
        {
            await _root.SetRotationGizmoMode();
        }

        private async ValueTask UpdateChamberVoxelMeshInner()
        {
            if (!FrontendOptions.CurrentValue.ShowAdvancedNestingFeatures)
                return;
            var chamberDim = Nesting.NestingDim;
            var whole = Nesting.VoxelChamber;
            var version = Nesting.VoxelChamberVersion;
            if (whole != null && _chamberVoxelMeshVersion != version)
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
                    _chamberVoxelMesh = await _root.AddMesh("chamberVoxel", chamberMesh, true, false, true, null, null, cancel: default);
                    await _chamberVoxelMesh.Position(new Vector3(chamberDim.SizeX * 0.5f + 20, 0, -chamberDim.SizeY * 0.5f));
                });
                _chamberVoxelMeshVersion = version;
            }
            else if (_chamberVoxelMesh != null)
            {
                await _chamberVoxelMesh.DisposeFromScene();
                await _chamberVoxelMesh.DisposeAsync();
                _chamberVoxelMesh = null;
            }
        }

        private Task StartCheckCollision()
            => Nesting.BackgroundTask.StartTask("Collision", CheckCollisionInner);

        private Task StartDoNest()
            => Nesting.BackgroundTask.StartTask("Nest", DoNestInner);

        private Task StartDoSlice()
        {
            Navigation.NavigateTo(SlicingPage.SelfPath);
            return Task.CompletedTask;
        }

        private async Task SetStatesInner(IEnumerable<NestingInstance> items)
        {
            var instances = new List<IBabylonAbstractMesh>();
            var states = new List<MeshTransform>();
            var overlapping = new List<bool>();
            foreach (var item in items)
            {
                if (!_instances.TryGetValue(item.Index, out var instance))
                    await CreateInstanceInner(item);
                else
                {
                    instances.Add(instance);
                    states.Add(item.TransformState);
                    overlapping.Add(item.IsOverlapping);
                }
            }
            if (instances.Count > 0)
                await _root.SetInstancesState(instances.ToArray(), states.Select(x => (NestingTransformState)x).ToArray(), overlapping.ToArray());
        }

        private async Task<MeshTransform> GetState(NestingInstance item)
        {
            if (_instances.TryGetValue(item.Index, out var instance))
                return await _root.GetTransformState(instance);
            else
                return item.TransformState;
        }

        private async Task DoNestInner(CancellationToken cancel)
        {
            var context = CreateNestingContext();
            var res = await Nesting.RunNesting(
                context,
                Aggressive ? NestingFlags.Aggressive : NestingFlags.None, 
                GetState);
            _nestingStats = res;
            StateHasChanged();
        }

        private async Task CheckCollisionInner(CancellationToken cancel)
        {
            var context = CreateNestingContext();
            await Nesting.RunCheckCollision(context, GetState, cancel: cancel);
            _nestingStats = null;
            StateHasChanged();
        }

        private NestingServiceContext CreateNestingContext()
        {
            return new NestingServiceContext(
                NullShrinkageCorrection.Instance,
                LimitThickness: null);
            //return new NestingServiceContext(
            //    new SingleShrinkageCorrection(new Vector3(1.25f)));
        }

        private async Task LoadFiles(InputFileChangeEventArgs e)
        {
            await Nesting.BackgroundTask.StartTask(new object(), (cancel) => LoadFilesInner(e, cancel));
            await StartCheckCollision();
        }

        private async Task LoadFilesInner(InputFileChangeEventArgs e, CancellationToken cancel)
        { 
            var files = e.GetMultipleFiles(int.MaxValue);
            for (int i = 0; i < files.Count; i++)
            {
                using (var fileStream = files[i].OpenReadStream(long.MaxValue))
                {
                    await Nesting.LoadInstances(
                        fileStream,
                        Quantity ?? 1,
                        null,
                        Scale / 100.0f,
                        default,
                        0,
                        null,
                        null,
                        null,
                        cancel: cancel);
                }
                await Nesting.BackgroundTask.UpdateProgress(i, files.Count, null, null);
            }
        }
    }
}

