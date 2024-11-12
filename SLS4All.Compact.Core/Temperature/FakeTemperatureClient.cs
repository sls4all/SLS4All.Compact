// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public class FakeTemperatureClientOptions
    {
        public TimeSpan LowFrequencyPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public string SurfaceId { get; set; } = "surface";
        public string AvgSurfaceId { get; set; } = "surfaceAvg";
        public Dictionary<string, TemperatureClientSensorPair?>? PrintBedHeaterIds { get; set; }
        public static TemperatureClientSensorPair[] PrintBedHeaterIdsDefault { get; } = new[]
        {
            new TemperatureClientSensorPair{Id = "printBed", Description = "Print Bed" },
        };
        public Dictionary<string, TemperatureClientSensorPair?>? SurfaceSensors { get; set; }
        public static TemperatureClientSensorPair[] SurfaceSensorsDefault { get; } = new[]
        {
            new TemperatureClientSensorPair{Id = "surface", Description = "Surface" },
            new TemperatureClientSensorPair{Id = "quadrant1", Description = "Surface Quadrant 1" },
            new TemperatureClientSensorPair{Id = "quadrant2", Description = "Surface Quadrant 2" },
            new TemperatureClientSensorPair{Id = "quadrant3", Description = "Surface Quadrant 3" },
            new TemperatureClientSensorPair{Id = "quadrant4", Description = "Surface Quadrant 4" },
        };
        public Dictionary<string, TemperatureClientSensorPair?>? ExtraSurfaceSensors { get; set; }
        public static TemperatureClientSensorPair[] ExtraSurfaceSensorsDefault { get; } = new[]
        {
            new TemperatureClientSensorPair{Id = "surfaceMin", Description = "Surface Min" },
            new TemperatureClientSensorPair{Id = "surfaceMax", Description = "Surface Max" },
            new TemperatureClientSensorPair{Id = "surfaceAvg", Description = "Surface Avg" },
        };
        public Dictionary<string, TemperatureClientSensorPair?>? PrintChamberSensors { get; set; }
        public static TemperatureClientSensorPair[] PrintChamberSensorsDefault { get; } = new[]
        {
            new TemperatureClientSensorPair{Id = "printChamber1", Description = "Print Chamber 1" },
            new TemperatureClientSensorPair{Id = "printChamber2", Description = "Print Chamber 2" },
            new TemperatureClientSensorPair{Id = "printChamber3", Description = "Print Chamber 3" },
            new TemperatureClientSensorPair{Id = "printChamber4", Description = "Print Chamber 4" },
            new TemperatureClientSensorPair{Id = "printBed", Description = "Print Bed" },
        };
        public Dictionary<string, TemperatureClientSensorPair?>? PowderChamberSensors { get; set; }
        public static TemperatureClientSensorPair[] PowderChamberSensorsDefault { get; } = new[]
        {
            new TemperatureClientSensorPair{Id = "powderChamber1", Description = "Powder Chamber 1" },
            new TemperatureClientSensorPair{Id = "powderChamber2", Description = "Powder Chamber 2" },
            new TemperatureClientSensorPair{Id = "powderChamber3", Description = "Powder Chamber 3" },
            new TemperatureClientSensorPair{Id = "powderChamber4", Description = "Powder Chamber 4" },
            new TemperatureClientSensorPair{Id = "powderBed", Description = "Powder Bed" },
        };
        public bool FakeBedMatrix { get; set; } = false;
        public int FakeCameraWidth { get; set; } = 32;
        public int FakeCameraHeight { get; set; } = 24;
    }

    public sealed class FakeTemperatureClient : BackgroundThreadService, ITemperatureClient
    {
        private sealed class StateItem
        {
            public double? Target { get; set; }
            public double Current { get; set; }
            public double Average { get; set; }
            public bool Settable { get; set; }
            public bool TargetReached { get; set; }
            public SystemTimestamp Timestamp { get; set; }
        }

        private readonly ILogger<FakeTemperatureClient> _logger;
        private readonly IOptionsMonitor<FakeTemperatureClientOptions> _options;
        private readonly IMediator _mediator;
        private readonly ITemperatureCamera _temperatureCamera;

        private readonly object _stateLock = new object();
        private volatile TemperatureState _lowFrequencyState;
        private readonly Dictionary<string, StateItem> _stateDict;

        private readonly Stopwatch _fakeStateStopwatch;
        private readonly Random _fakeRandom;

        public string SurfaceId => _options.CurrentValue.SurfaceId;
        public string AvgSurfaceId => _options.CurrentValue.AvgSurfaceId;
        public TemperatureState CurrentState => _lowFrequencyState;
        public AsyncEvent<TemperatureState> StateChangedLowFrequency { get; } = new();
        public AsyncEvent<TemperatureState> StateChangedHighFrequency { get; } = new();
        public TemperatureClientSensorPair[] PrintBedHeaterIds =>
            _options.CurrentValue.PrintBedHeaterIds?.GetOrderedEnabledValues() ??
            FakeTemperatureClientOptions.PrintBedHeaterIdsDefault;
        public TemperatureClientSensorPair[] SurfaceSensorIds =>
            _options.CurrentValue.SurfaceSensors?.GetOrderedEnabledValues() ??
            FakeTemperatureClientOptions.SurfaceSensorsDefault;
        public TemperatureClientSensorPair[] ExtraSurfaceSensorIds =>
            _options.CurrentValue.ExtraSurfaceSensors?.GetOrderedEnabledValues() ??
            FakeTemperatureClientOptions.ExtraSurfaceSensorsDefault;
        public TemperatureClientSensorPair[] PrintChamberSensorIds =>
            _options.CurrentValue.PrintChamberSensors?.GetOrderedEnabledValues() ??
            FakeTemperatureClientOptions.PrintChamberSensorsDefault;
        public TemperatureClientSensorPair[] PowderChamberSensorIds =>
            _options.CurrentValue.PowderChamberSensors?.GetOrderedEnabledValues() ??
            FakeTemperatureClientOptions.PowderChamberSensorsDefault;

        public FakeTemperatureClient(
            IOptionsMonitor<FakeTemperatureClientOptions> options,
            ILogger<FakeTemperatureClient> logger,
            IMediator mediator,
            ITemperatureCamera temperatureCamera)
            : base(logger)
        {
            _options = options;
            _logger = logger;
            _mediator = mediator;
            _temperatureCamera = temperatureCamera;

            _lowFrequencyState = new TemperatureState(Array.Empty<TemperatureEntry>(), null);
            _stateDict = new();
            _fakeStateStopwatch = new();
            _fakeRandom = new();
        }

        public async Task SetTarget(string id, double? value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                UpdateTemperatureDict(id, null, true, value);
            }
            await StateChangedHighFrequency.Invoke(GetState(), cancel);
        }

        public async Task<bool> TryIncreaseTarget(string id, double offset, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (!_stateDict.TryGetValue(id, out var state) || state.Target == null)
                    return false;
                UpdateTemperatureDict(id, null, true, state.Target + offset);
            }
            await StateChangedHighFrequency.Invoke(GetState(), cancel);
            return true;
        }

        private bool UpdateTemperatureDict(string id, double? temperature, bool settable, double? target)
        {
            lock (_stateLock)
            {
                ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(_stateDict, id, out _);
                if (value == null)
                    value = new();
                if (temperature == null)
                    temperature = value.Current;
                var targetReached = target != null;
                if ((value.Target, value.Current, value.Settable, value.TargetReached) != (target, temperature.Value, settable, targetReached))
                {
                    var timestamp = SystemTimestamp.Now;
                    value.Target = target;
                    value.Current = target ?? temperature.Value;
                    value.Average = target ?? temperature.Value;
                    value.Settable = settable;
                    value.TargetReached = targetReached;
                    value.Timestamp = timestamp;
                    return true;
                }
                else
                    return false;
            }
        }

        private TemperatureMatrix? GetBedMatrix()
        {
            var options = _options.CurrentValue;
            if (options.FakeBedMatrix)
                return null;
            return _temperatureCamera.GetBedMatrix();
        }

        private TemperatureState GetState()
        {
            lock (_stateLock)
            {
                var options = _options.CurrentValue;
                var bedMatrix = GetBedMatrix();

                var timeFactor = _fakeStateStopwatch.IsRunning ? _fakeStateStopwatch.Elapsed.TotalSeconds : 1;
                _fakeStateStopwatch.Restart();

                var now = SystemTimestamp.Now;
                if (_stateDict.Count == 0) // seed some initial values
                {
                    foreach (var id in SurfaceSensorIds
                        .Concat(ExtraSurfaceSensorIds)
                        .Concat(PowderChamberSensorIds)
                        .Concat(PrintChamberSensorIds))
                    {
                        UpdateTemperatureDict(
                            id.Id,
                            double.MinValue,
                            true,
                            null);
                    }
                }
                const double minValue = 0;
                const double maxValue = 200;
                double randomRange = (maxValue - minValue) / 10 * timeFactor;
                foreach (var value in _stateDict.Values)
                {
                    if (value.Target == null)
                    {
                        var current = value.Current == double.MinValue ? _fakeRandom.NextDouble() * (maxValue - minValue) + minValue : value.Current;
                        current = Math.Clamp(current + (_fakeRandom.NextDouble() - 0.5) * randomRange, minValue, maxValue);
                        value.Current = current;
                        value.Average = current;
                    }
                    value.Timestamp = now;
                }

                if (options.FakeBedMatrix)
                {
                    var temps = new float[options.FakeCameraWidth * options.FakeCameraHeight];
                    for (int i = 0; i < temps.Length; i++)
                        temps[i] = (float)(_fakeRandom.NextDouble() * 10 * timeFactor + 20);
                    bedMatrix = new TemperatureMatrix(now, options.FakeCameraWidth, options.FakeCameraHeight, temps);
                }

                var entries = ReadEntriesNeedsLock();
                Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id));
                var state = new TemperatureState(entries, bedMatrix);
                return state;
            }
        }

        private TemperatureEntry[] ReadEntriesNeedsLock()
        {
            var entries = new TemperatureEntry[_stateDict.Count];
            var i = 0;
            foreach ((var key, var value) in _stateDict)
                entries[i++] = new TemperatureEntry(value.Timestamp, key, value.Target, value.Current, value.Average, value.Settable, value.TargetReached);
            return entries;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            var timer = new PeriodicTimer(options.LowFrequencyPeriod);
            while (true)
            {
                try
                {
                    var state = GetState();
                    _lowFrequencyState = state;
                    await _mediator.Publish(state, cancel);
                    await StateChangedHighFrequency.Invoke(state, cancel);
                    await StateChangedLowFrequency.Invoke(state, cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to get/process low frequency data");
                }
                try
                {
                    await timer.WaitForNextTickAsync(cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to wait for next period");
                }
            }
        }

        public IDisposable SuppressValidation(string id)
            => NullDisposable.Instance;
    }
}
