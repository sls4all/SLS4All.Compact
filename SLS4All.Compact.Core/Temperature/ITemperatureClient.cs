// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.Temperature
{
    public record TemperatureState(TemperatureEntry[] Entries, TemperatureMatrix? BedMatrix) : INotification
    {
        public bool TryGetValue(string id, [MaybeNullWhen(false)] out TemperatureEntry value)
        {
            foreach (var entry in Entries)
            {
                if (id == entry.Id)
                {
                    value = entry;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }

    public record TemperatureEntry(SystemTimestamp Timestamp, string Id, double? TargetTemperature, double CurrentTemperature, double AverageTemperature, bool Settable, bool TargetReached);

    public record TemperatureMatrix(SystemTimestamp Timestamp, int Width, int Height, float[] Values);

    public class TemperatureClientSensorPair : IOptionsItemEnable
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }

    public interface ITemperatureClient
    {
        string SurfaceId { get; }
        string AvgSurfaceId { get; }
        TemperatureClientSensorPair[] PrintBedHeaterIds { get; }
        TemperatureClientSensorPair[] SurfaceSensorIds { get; }
        TemperatureClientSensorPair[] ExtraSurfaceSensorIds { get; }
        TemperatureClientSensorPair[] PrintChamberSensorIds { get; }
        TemperatureClientSensorPair[] PowderChamberSensorIds { get; }
        TemperatureState CurrentState { get; }
        AsyncEvent<TemperatureState> StateChangedLowFrequency { get; }
        AsyncEvent<TemperatureState> StateChangedHighFrequency { get; }

        IDisposable SuppressValidation(string id);
        Task SetTarget(string id, double? value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        Task<bool> TryIncreaseTarget(string id, double offset, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
    }
}