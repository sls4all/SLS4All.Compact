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

namespace SLS4All.Compact.Pages
{
    partial class CurrentJobAutomatic
    {
        public bool IsNesting => Jobs!.IsNesting;
        public bool IsPrinting => Jobs!.IsPrinting;

        private async Task StartDoNest()
        {
            if (IsNesting || IsPrinting)
                return;
            await Jobs!.DeselectConstraint();
            await Nesting.BackgroundTask.StartTask("Nest", DoNestInner);
        }

        private Task StopDoNest()
        {
            Nesting.BackgroundTask.Cancel();
            return Task.CompletedTask;
        }

        private Task<MeshTransform> GetState(NestingInstance item)
            => Task.FromResult(item.TransformState);

        private NestingMesh? TryGetOriginalNestingMesh(AutomaticJobObject? obj)
        {
            if (obj == null || Job == null)
                return null;
            var file = Job.TryGetObjectFileByName(obj.Name);
            if (file == null)
                return null;
            var nestingMesh = Nesting.TryGetOriginalMesh(file.Hash);
            return nestingMesh;
        }

        private async Task DoNestInner(CancellationToken cancel)
        {
            var job = Job; // capture, in case user switches to another job
            if (job != null)
            {
                try
                {
                    await Jobs!.ValidateInner(true);

                    var profile = Jobs!.PrintProfiles.FirstOrDefault(x => x.Reference == job.PrintProfile);
                    if (profile?.IsValid == false)
                        profile = null;
                    var objects = job.Objects.Select(x => x.Clone()).ToArray();
                    job.NestingState.AggressiveNestingEnabled = job.AggressiveNestingEnabled;
                    job.NestingState.PrintProfile = job.PrintProfile;
                    job.NestingState.PrintProfileJobHash = profile?.Profile.GetJobHash() ?? "";
                    var context = new NestingServiceContext(
                        ShrinkageCorrection: profile?.Profile.GetShrinkageCorrection()
                            ?? NullShrinkageCorrection.Instance,
                        LimitThickness: null
                    );
                    var stopwatch = Stopwatch.StartNew();
                    var res = await Nesting.RunNesting(
                        context,
                        job.AggressiveNestingEnabled ? NestingFlags.Aggressive : NestingFlags.None,
                        GetState,
                        cancel);
                    stopwatch.Stop();
                    Logger.LogDebug($"Nesting completed in {stopwatch.Elapsed}");
                    job.NestingState.ChamberDepth = res.ZMax.RoundToDecimal(3);
                    job.NestingState.ChamberVolume = res.ChamberVolume.RoundToDecimal(3);
                    job.NestingState.SinteredVolume = res.SinteredVolume.RoundToDecimal(3);
                    job.NestingState.DensityPercent = (res.Density * 100).RoundToDecimal(2);
                    job.NestingState.Objects = objects;
                    job.NestingState.AvailableDepth = job.AvailableDepth;
                    var nestingInstances = Nesting.GetInstances();
                    job.NestingState.Instances = nestingInstances
                        .Where(x => x.Nested != null)
                        .Select(x => new AutomaticJobInstance
                        {
                            ObjectCopy = ((AutomaticJobObjectEntry)x.UserData!).Obj.Clone(),
                            IsOverlapping = x.IsOverlapping,
                            TransformState = x.TransformState,
                            MeshPrintTransform = x.Nested!.NestedMeshTransformWithCorrection.FromToMeshTransform(),
                            Hash = x.Mesh.Hash,
                        }).ToArray();
                }
                finally
                {
                    // cleanup memory explicitely, we have processed a lot of LOH buffers
                    PrinterGC.CollectGarbageBlockingAggressive();
                }
                // save explictely
                await Jobs!.Save(false);
            }
            StateHasChanged();
            await Jobs!.ValidateInner(true);
        }

        public Task OnValidate()
            => Task.CompletedTask;

        internal static Task SyncInner(Jobs jobs, IJobStorage storage, AutomaticJob job, List<SyncObject> objects)
        {
            var remainingNestedInstances = job.NestingState.Instances.ToHashSet();
            foreach (var obj in job.Objects)
            {
                var file = job.TryGetObjectFileByName(obj.Name);
                if (file == null)
                    continue;
                var objEntry = JobObjectEntry.Create(obj);
                var nestedInstance = job.NestingState.Instances.FirstOrDefault(x => x.Id == obj.Id);
                var path = new JobObjectPath(job.Id, obj.Name);
                var scale = (float)(obj.Scale / 100) * obj.Units.GetScale();
                NestedRotationConstraints constraintsAroundZ;
                if (obj.Constraints.Length == 0)
                    constraintsAroundZ = new NestedRotationConstraints();
                else
                    constraintsAroundZ = new NestedRotationConstraints(obj.Constraints.Select(x => jobs.ConstraintToNested(x)).ToArray());
                // restore nested state
                var remaining = obj.InstanceCount;
                var prototype = new SyncObject(obj.Name, file.Hash, 1, scale, (float)obj.Inset, path, objEntry, constraintsAroundZ, null);
                while (remaining > 0 && remainingNestedInstances.Count > 0)
                {
                    var instance = remainingNestedInstances.FirstOrDefault(x =>
                        x.Hash == file.Hash &&
                        x.ObjectCopy?.Scale == obj.Scale &&
                        x.ObjectCopy?.Units == obj.Units &&
                        x.ObjectCopy?.Constraints.SequenceEqual(obj.Constraints) == true);
                    if (instance == null)
                        break;
                    var added = prototype with { TransformState = (MeshTransform)instance.TransformState };
                    objects.Add(added);
                    remainingNestedInstances.Remove(instance);
                    remaining--;
                }
                // add rest as random
                if (remaining > 0)
                {
                    var added = prototype with { InstanceCount = remaining };
                    objects.Add(added);
                }
            }
            return Task.CompletedTask;
        }
    }
}

