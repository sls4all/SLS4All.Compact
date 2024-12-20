// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
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
using System.Security.Cryptography.Xml;

namespace SLS4All.Compact.Pages
{
    partial class CurrentJobProfiling
    {
        public bool IsNesting => Jobs!.IsNesting;
        public bool IsPrinting => Jobs!.IsPrinting;

        private async Task StartDoCheck()
        {
            if (IsNesting || IsPrinting)
                return;
            await Jobs!.DeselectConstraint();
            await Nesting.BackgroundTask.StartTask("Nest", DoCheckInner);
        }

        private Task StopDoCheck()
        {
            Nesting.BackgroundTask.Cancel();
            return Task.CompletedTask;
        }
        private NestingMesh? TryGetOriginalNestingMesh(ProfilingJobObject? obj)
        {
            if (obj == null || Job == null)
                return null;
            var file = Job.TryGetObjectFileByName(obj.Name);
            if (file == null)
                return null;
            var nestingMesh = Nesting.TryGetOriginalMesh(file.Hash);
            return nestingMesh;
        }

        public async Task OnValidate()
        {
            var job = Job;
            if (job == null) 
                return;

            var profile = Jobs!.PrintProfiles.FirstOrDefault(x => x.Reference == job.PrintProfile);
            if (profile?.IsValid == false)
                profile = null;
            var context = GetNestingContext(job, profile);

            // NOTE: need to sync first to get the meshes to the nester, which meshes we need to calculate the positions
            await Jobs!.Sync(nestingOnly: true);
            
            // calc positions
            var nestingDim = Nesting.NestingDim;
            var width = Math.Max(job.Width, 1);
            var height = Math.Max(job.Height, 1);
            job.EnsureObjects();

            var items = new (ProfilingJobObject Obj, NestingMesh Mesh, Bounds3 Bounds, MeshTransform Transform, MeshTransform CorrectionTransform)[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var obj = job.Objects[x + y * job.Width];
                    items[x, y].Obj = obj;

                    var mesh = TryGetOriginalNestingMesh(obj);
                    if (obj.IsEmpty || mesh == null)
                        continue;
                    var scale = (float)(obj.Scale / 100) * obj.Units.GetScale();
                    var meshMatrix = (
                        Matrix4x4.CreateTranslation(-mesh.Bounds.Center) *
                        Matrix4x4.CreateFromYawPitchRoll(
                            (float)(obj.Yaw / 180) * MathF.PI,
                            (float)(obj.Pitch / 180) * MathF.PI,
                            (float)(obj.Roll / 180) * MathF.PI) *
                        Matrix4x4.CreateScale(scale))
                            .FromToMeshTransform() *
                        Matrix4x4.CreateScale(context.ShrinkageCorrection.Scale);
                    var rotationQuaternion = Quaternion.CreateFromYawPitchRoll(
                        (float)(obj.Yaw / 180) * MathF.PI,
                        (float)(obj.Pitch / 180) * MathF.PI,
                        (float)(obj.Roll / 180) * MathF.PI);
                    var bounds = mesh.Mesh.GetBounds(meshMatrix);
                    var center = bounds.Center;
                    var bottomMeshCenter = new Vector3(center.X, bounds.Min.Z, center.Y);
                    var transform = new MeshTransform(
                        -bottomMeshCenter, 
                        rotationQuaternion,
                        new Vector3(scale));
                    var correctionTransform = new MeshTransform(
                        -bottomMeshCenter,
                        rotationQuaternion,
                        (scale * context.ShrinkageCorrection.Scale).FromToMeshTransform());
                    items[x, y] = (obj, mesh, bounds, transform, correctionTransform);
                }
            }

            var widths = new Vector3[width];
            var heights = new Vector3[height];
            var xTolerance = 0.0f;
            var yTolerance = 0.0f;
            var zTolerance = 0.0f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var item = items[x, y];
                    if (item.Mesh == null || !item.Obj.IsEnabled)
                    {
                        item.Obj.TransformState = null;
                        continue;
                    }

                    // get larges size in this row and column
                    var size = item.Bounds.Size;
                    for (int i = 0; i < width; i++)
                        widths[i] = items[i, y].Bounds.Size;
                    for (int i = 0; i < height; i++)
                        heights[i] = items[x, i].Bounds.Size;

                    var chamberPosition = new Vector3(0, zTolerance, 0);
                    if (width > 1)
                    {
                        var spacing = (nestingDim.SizeX - xTolerance - widths.Sum(x => x.X)) / (width - 1);
                        chamberPosition.X = -(nestingDim.SizeX - size.X - xTolerance) * 0.5f + (widths.Take(x).Sum(x => x.X) + spacing * x);
                    }
                    if (height > 1)
                    {
                        var spacing = (nestingDim.SizeY - yTolerance - heights.Sum(x => x.Y)) / (height - 1);
                        chamberPosition.Z = (nestingDim.SizeY - size.Y - yTolerance) * 0.5f - (heights.Take(y).Sum(x => x.Y) + spacing * y);
                    }
                    chamberPosition += new Vector3((float)item.Obj.XOffset, (float)item.Obj.ZOffset, (float)-item.Obj.YOffset);
                    var correctionTransform = item.CorrectionTransform with
                    {
                        Position = item.Transform.Position + chamberPosition,
                    };
                    var transform = item.Transform with
                    {
                        Position = item.Transform.Position + chamberPosition,
                    };
                    item.Obj.TransformState = transform;
                    item.Obj.MeshPrintTransform = correctionTransform.GetMeshMatrix(
                        new Vector3(nestingDim.SizeX, 0, nestingDim.SizeY) * 0.5f)
                        .FromToMeshTransform();
                }
            }
        }

        private Task<MeshTransform> GetState(NestingInstance item)
        {
            var obj = ((ProfilingJobObjectEntry)item.UserData!).Obj;
            MeshTransform res;
            if (obj.TransformState != null)
                res = obj.TransformState;
            else
                res = item.TransformState;
            return Task.FromResult(res);
        }

        private NestingServiceContext GetNestingContext(ProfilingJob? job, PrintProfileEntry? profile)
        {
            var context = new NestingServiceContext(
                ShrinkageCorrection: profile != null && job != null
                    ? profile.Profile.GetShrinkageCorrection()
                    : NullShrinkageCorrection.Instance,
                IgnoreOverlaps: true,
                LimitThickness: (float?)job?.LimitThickness / 1000 /* um to mm */
            );
            return context;
        }

        private async Task DoCheckInner(CancellationToken cancel)
        {
            var job = Job; // capture, in case user switches to another job
            if (job != null)
            {
                await Jobs!.ValidateInner(true);

                var profile = Jobs!.PrintProfiles.FirstOrDefault(x => x.Reference == job.PrintProfile);
                if (profile?.IsValid == false)
                    profile = null;
                var objects = job.Objects.Select(x => x.Clone()).ToArray();
                job.NestingState.PrintProfile = job.PrintProfile;
                job.NestingState.PrintProfileJobHash = profile?.Profile.GetJobHash() ?? "";
                job.NestingState.Objects = objects;
                job.NestingState.LimitThickness = job.LimitThickness;
                var context = GetNestingContext(job, profile);
                var res = await Nesting.RunCheckCollision(
                    context,
                    GetState,
                    recalculateValues: true,
                    cancel: cancel);
                job.NestingState.ChamberDepth = res.ZMax.RoundToDecimal(3);
                job.NestingState.ChamberVolume = res.ChamberVolume.RoundToDecimal(3);
                job.NestingState.SinteredVolume = res.SinteredVolume.RoundToDecimal(3);
                job.NestingState.DensityPercent = (res.Density * 100).RoundToDecimal(2);
                job.NestingState.AvailableDepth = job.AvailableDepth;

                // save explictely, in case user switched to another job
                await JobStorage.UpsertJob(job);
            }
            StateHasChanged();
            await Jobs!.ValidateInner(true);
        }

        internal static Task SyncInner(Jobs jobs, IJobStorage storage, ProfilingJob job, List<SyncObject> objects)
        {
            for (int y = 0; y < job.Height; y++)
            {
                for (int x = 0; x < job.Width; x++)
                {
                    var index = x + y * job.Width;
                    if (index >= job.Objects.Length)
                        continue;
                    var obj = job.Objects[index];
                    if (obj.IsEmpty)
                        continue;
                    var file = job.TryGetObjectFileByName(obj.Name);
                    if (file == null)
                        continue;
                    var objEntry = JobObjectEntry.Create(obj);
                    var path = new JobObjectPath(job.Id, obj.Name);
                    if (obj.TransformState == null || !obj.IsEnabled)
                    {
                        var fakeTransform = new MeshTransform(Vector3.Zero, Quaternion.Identity, Vector3.Zero);
                        objects.Add(new SyncObject(obj.Name, file.Hash, null, 1, 0.0f, 0.0f, path, objEntry, default, fakeTransform));
                    }
                    else
                    {
                        var scale = (float)(obj.Scale / 100) * obj.Units.GetScale();
                        objects.Add(new SyncObject(obj.Name, file.Hash, null, 1, scale, 0.0f, path, objEntry, default, (MeshTransform)obj.TransformState));
                    }
                }
            }
            return Task.CompletedTask;
        }
    }
}

