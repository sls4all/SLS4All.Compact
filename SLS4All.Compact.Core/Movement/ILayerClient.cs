// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public class BedPreparationSetup
    {
        /// <summary>
        /// Gets or sets whether to execute bed-preparation
        /// </summary>
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// Gets or sets the thickness of single layer [μm]
        /// </summary>
        public double LayerThickness { get; set; } = 150;
        /// <summary>
        /// Gets or sets the total thickness of bed preparation [μm]
        /// </summary>
        public double TotalThickness { get; set; }
        /// <summary>
        /// Gets or sets temperature target to reach before recoating a layer [°C]
        /// </summary>
        public double? TemperatureTarget { get; set; }
        /// <summary>
        /// Gets or sets delay before recoating after <see cref="TemperatureTarget"/> has been reached
        /// </summary>
        public TimeSpan TemperatureDelay { get; set; }
        /// <summary>
        /// Makes the Z movement behave similarily like normal, but in fact Z will not additively move down or up in consequent layer moves calls.
        /// </summary>
        public bool DisableLayerAdditiveMovement { get; set; }
    }

    public class BeginLayerSetup
    {
        /// <summary>
        /// Gets or sets whether to execute begin-layer
        /// </summary>
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// Gets or sets the thickness of single layer [μm]
        /// </summary>
        public double LayerThickness { get; set; }
        /// <summary>
        /// Gets or sets an sintered/filled area of previous layer [mm^2]
        /// </summary>
        public double? PrevLayerFillArea { get; set; }
        /// <summary>
        /// Gets or sets temperature target to reach before recoating a layer [°C]
        /// </summary>
        public double? TemperatureTarget { get; set; }
        /// <summary>
        /// Gets or sets delay before recoating after <see cref="TemperatureTarget"/> has been reached
        /// </summary>
        public TimeSpan TemperatureDelay { get; set; }
        /// <summary>
        /// Gets or sets whether to use existing temperature target (true) or use <see cref="TemperatureTarget"/> (false) 
        /// </summary>
        public bool KeepTemperatureTarget { get; set; }
        /// <summary>
        /// Gets or sets whether slower speed of recoater should be used
        /// </summary>
        public bool UseSlowRecoaterSpeed { get; set; }
        /// <summary>
        /// Gets or sets how many times is the sintered volume denser than not-sintered volume
        /// </summary>
        public double SinteredVolumeFactor { get; set; }
        /// <summary>
        /// Makes the Z movement behave similarily like normal, but in fact Z will not additively move down or up in consequent layer moves calls.
        /// </summary>
        public bool DisableLayerAdditiveMovement { get; set; }
    }

    public class PrintCapSetup
    {
        /// <summary>
        /// Gets or sets whether to execute begin-layer
        /// </summary>
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// Gets or sets the thickness of single layer [μm]
        /// </summary>
        public double LayerThickness { get; set; }
        /// <summary>
        /// Gets or sets the total thickness of print cap [μm]
        /// </summary>
        public double TotalThickness { get; set; }
        /// <summary>
        /// Gets or sets temperature target to reach before recoating a layer [°C]
        /// </summary>
        public double? TemperatureTarget { get; set; }
        /// <summary>
        /// Gets or sets delay before recoating after <see cref="TemperatureTarget"/> has been reached
        /// </summary>
        public TimeSpan TemperatureDelay { get; set; }
        /// <summary>
        /// Makes the Z movement behave similarily like normal, but in fact Z will not additively move down or up in consequent layer moves calls.
        /// </summary>
        public bool DisableLayerAdditiveMovement { get; set; }
    }

    public class PowderVolumeSetup
    {
        /// <summary>
        /// Depth of print chamber [mm]
        /// </summary>
        public double ChamberDepth { get; set; }
        /// <summary>
        /// Gets or sets the inner volume of objects. I.e. sum of volumes of all object instances in the chamber [mm^3]
        /// </summary>
        public double JobSinteredVolume { get; set; }
        /// <summary>
        /// Gets or sets the depth of chamber volume around nested objects. I.e. how far the nested objects reach from the bottom of the chamber [mm]
        /// </summary>
        public double JobChamberDepth { get; set; }
        /// <summary>
        /// Gets or sets the thickness of single layer [μm]
        /// </summary>
        public double LayerThickness { get; set; }
        /// <summary>
        /// Gets or sets the bed preparation thickness [μm]
        /// </summary>
        public double BedPreparationThickness { get; set; }
        /// <summary>
        /// Gets or sets the print cap thickness [μm]
        /// </summary>
        public double PrintCapThickness { get; set; }
        /// <summary>
        /// Gets or sets how many times is the sintered volume denser than not sintered volume
        /// </summary>
        public double SinteredVolumeFactor { get; set; }
    }

    /// <param name="Depth">Depth in mm</param>
    /// <param name="Volume">Volume in mm^3</param>
    public readonly record struct VolumeAndDepth(double Depth, double Volume)
    {
        public static VolumeAndDepth operator +(VolumeAndDepth x, VolumeAndDepth y)
            => new VolumeAndDepth(x.Depth + y.Depth, x.Volume + y.Volume);
    }

    public sealed record class PowderVolumeTotals(
        VolumeAndDepth BedLeveling,
        VolumeAndDepth BedPreparation,
        VolumeAndDepth Job, 
        VolumeAndDepth PrintCap,
        VolumeAndDepth Total,
        (double by, double max)? ReduceDepth,
        double PrintChamberArea,
        double PowderChamberArea)
    {
        public VolumeAndDepth BedPreparationAndCap => BedPreparation + PrintCap;
    }

    public class LayerClientStuckException : Exception
    {
        public LayerClientStuckException() { }
        public LayerClientStuckException(string message) : base(message) { }
        public LayerClientStuckException(string message, Exception inner) : base(message, inner) { }
    }

    public interface ILayerClient
    {
        /// <summary>
        /// Minimum powder bed powder depth [mm]
        /// </summary>
        double MinimumPowderBedDepth { get; }
        PowderVolumeTotals GetPowderVolume(PowderVolumeSetup setup);
        Task BeginPrint(CancellationToken cancel = default);
        Task BedPreparation(BedPreparationSetup setup, StatusUpdater? onStatus, CancellationToken cancel = default);
        Task BeginLayer(BeginLayerSetup setup, CancellationToken cancel = default);
        Task EndLayer(CancellationToken cancel = default);
        Task PrintCap(PrintCapSetup setup, StatusUpdater? onStatus, CancellationToken cancel = default);
        Task EndPrint(CancellationToken cancel = default);
        Task HomeBedsAndRecoater(double powderChamberDepth, StatusUpdater? status = null, CancellationToken cancel = default);
        Task SetPowderDepth(double totalPowderChamberDepth, CancellationToken cancel = default);
        Task EjectCake(double? expectedDepth, CancellationToken cancel = default);
    }
}
