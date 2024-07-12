// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.McuClient.Sensors;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Validation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public class McuTemperatureClientOptions
    {
        public TimeSpan LowFrequencyPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public bool FakeCurrentTemperatures { get; set; } = false;
        public bool FakeBedMatrix { get; set; } = false;
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

        public int FakeCameraWidth { get; set; } = 32;
        public int FakeCameraHeight { get; set; } = 24;
        public TimeSpan AverageDuration { get; set; } = TimeSpan.FromSeconds(2);
    }

    public sealed class McuTemperatureClient : BackgroundThreadService, ITemperatureClient
    {
        private sealed class StateItem
        {
            public double? Target { get; set; }
            public double Current { get; set; }
            public double Average { get; set; }
            public bool Settable { get; set; }
            public bool TargetReached { get; set; }
            public SystemTimestamp Timestamp { get; set; }
            public PrimitiveDeque<(double Temperature, SystemTimestamp Timestamp)> AverageQueue { get; } = new();
        }

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<McuTemperatureClientOptions> _options;
        private readonly IMediator _mediator;
        private readonly McuPrinterClient _printerClient;
        private readonly ITemperatureCamera _temperatureCamera;

        private readonly object _stateLock = new object();
        private volatile TemperatureState _lowFrequencyState;
        private readonly Dictionary<string, StateItem> _stateDict;

        private TemperatureState _fakeState;
        private readonly Stopwatch _fakeStateStopwatch;
        private readonly Random _fakeRandom;

        public string SurfaceId => _options.CurrentValue.SurfaceId;
        public string AvgSurfaceId => _options.CurrentValue.AvgSurfaceId;
        public TemperatureState CurrentState => _lowFrequencyState;
        public AsyncEvent<TemperatureState> StateChangedLowFrequency { get; } = new();
        public AsyncEvent<TemperatureState> StateChangedHighFrequency { get; } = new();
        public TemperatureClientSensorPair[] PrintBedHeaterIds =>
            _options.CurrentValue.PrintBedHeaterIds?.GetOrderedEnabledValues() ??
            McuTemperatureClientOptions.PrintBedHeaterIdsDefault;
        public TemperatureClientSensorPair[] SurfaceSensorIds =>
            _options.CurrentValue.SurfaceSensors?.GetOrderedEnabledValues() ??
            McuTemperatureClientOptions.SurfaceSensorsDefault;
        public TemperatureClientSensorPair[] ExtraSurfaceSensorIds =>
            _options.CurrentValue.ExtraSurfaceSensors?.GetOrderedEnabledValues() ??
            McuTemperatureClientOptions.ExtraSurfaceSensorsDefault;
        public TemperatureClientSensorPair[] PrintChamberSensorIds =>
            _options.CurrentValue.PrintChamberSensors?.GetOrderedEnabledValues() ??
            McuTemperatureClientOptions.PrintChamberSensorsDefault;
        public TemperatureClientSensorPair[] PowderChamberSensorIds =>
            _options.CurrentValue.PowderChamberSensors?.GetOrderedEnabledValues() ??
            McuTemperatureClientOptions.PowderChamberSensorsDefault;

        public McuTemperatureClient(
            ILogger<McuTemperatureClient> logger,
            IOptionsMonitor<McuTemperatureClientOptions> options,
            IMediator mediator,
            McuPrinterClient printerClient,
            ITemperatureCamera temperatureCamera)
            : base(logger)
        {
            _logger = logger;
            _options = options;
            _mediator = mediator;
            _printerClient = printerClient;
            _temperatureCamera = temperatureCamera;

            var o = options.CurrentValue;
            _fakeState = _lowFrequencyState = new TemperatureState(Array.Empty<TemperatureEntry>(), null);
            _stateDict = new();
            _fakeStateStopwatch = new();
            _fakeRandom = new();

            printerClient.ManagerSetEvent.AddHandler(OnManagerSet);
        }

        private ValueTask OnManagerSet(McuManager manager, CancellationToken token)
        {
            foreach (var pair_ in manager.TemperatureSensors)
            {
                (var key, var value) = pair_;
                value.ReadEvent.AddHandler((data, cancel) => OnTempeartureSensorRead(key, value, data, cancel));
            }
            foreach (var pair_ in manager.Heaters)
            {
                (var key, var value) = pair_;
                value.ReadEvent.AddHandler((data, cancel) => OnTempeartureSensorRead(key, value, data, cancel));
            }
            return ValueTask.CompletedTask;
        }

        private ValueTask OnTempeartureSensorRead(string key, IMcuTemperatureSensor sensorOrHeater, McuTemperatureSensorData data, CancellationToken cancel)
        {
            var heater = sensorOrHeater as IMcuHeater;
            var targetReached = heater != null ? heater.TargetReached : default;
            UpdateTemperatureDict(key, data.Temperature, heater != null, targetReached.Target, targetReached.Reached);
            return StateChangedHighFrequency.Invoke(GetState(), cancel);
        }

        private TemperatureEntry[] ReadEntriesNeedsLock()
        {
            var entries = new TemperatureEntry[_stateDict.Count];
            var i = 0;
            foreach ((var key, var value) in _stateDict)
                entries[i++] = new TemperatureEntry(value.Timestamp, key, value.Target, value.Current, value.Average, value.Settable, value.TargetReached);
            return entries;
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
                var entries = ReadEntriesNeedsLock();
                var bedMatrix = GetBedMatrix();

                if (options.FakeCurrentTemperatures || options.FakeBedMatrix)
                {
                    var timeFactor = _fakeStateStopwatch.IsRunning ? _fakeStateStopwatch.Elapsed.TotalSeconds : 1;
                    _fakeStateStopwatch.Restart();

                    if (options.FakeCurrentTemperatures)
                    {
                        if (entries.Length == 0) // seed some initial values
                        {
                            var now = SystemTimestamp.Now;
                            entries = SurfaceSensorIds
                                .Concat(PowderChamberSensorIds)
                                .Concat(PrintChamberSensorIds)
                                .Select(x => new TemperatureEntry(now, x.Id, null, 0, 0, true, true))
                                .Append(new TemperatureEntry(now, options.SurfaceId, null, 0, 0, true, true))
                                .ToArray();
                        }
                        var oldValues = _fakeState.Entries.ToDictionary(x => x.Id, x => x.CurrentTemperature);
                        const double minValue = 0;
                        const double maxValue = 200;
                        double randomRange = (maxValue - minValue) / 10 * timeFactor;
                        for (int i = 0; i < entries.Length; i++)
                        {
                            var entry = entries[i];
                            if (!oldValues.TryGetValue(entry.Id, out var value))
                                value = _fakeRandom.NextDouble() * (maxValue - minValue) + minValue;
                            value += (_fakeRandom.NextDouble() - 0.5) * randomRange;
                            if (value < minValue)
                                value = minValue;
                            else if (value > maxValue)
                                value = maxValue;
                            entries[i] = entry with { CurrentTemperature = value, AverageTemperature = value };
                        }
                    }

                    if (options.FakeBedMatrix)
                    {
                        var now = SystemTimestamp.Now;
                        var temps = new float[options.FakeCameraWidth * options.FakeCameraHeight];
                        for (int i = 0; i < temps.Length; i++)
                            temps[i] = (float)(_fakeRandom.NextDouble() * 10 * timeFactor + 20);
                        bedMatrix = new TemperatureMatrix(now, options.FakeCameraWidth, options.FakeCameraHeight, temps);
                    }
                }

                Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id));
                var state = new TemperatureState(entries, bedMatrix);
                _fakeState = state;
                return state;
            }
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

        public async Task SetTarget(string id, double? value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var heater = manager.Heaters[id];
            lock (_stateLock)
            {
                heater.Target = (float?)value;
                var current = heater.CurrentValue;
                if (current != null)
                    UpdateTemperatureDict(id, current.Temperature, true, value, false);
            }
            await StateChangedHighFrequency.Invoke(GetState(), cancel);
        }

        public async Task<bool> TryIncreaseTarget(string id, double offset, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var heater = manager.Heaters[id];
            lock (_stateLock)
            {
                if (heater.Target == null)
                    return false;
                var value = heater.Target.Value + offset;
                heater.Target = (float?)value;
                var current = heater.CurrentValue;
                if (current != null)
                    UpdateTemperatureDict(id, current.Temperature, true, value, false);
            }
            await StateChangedHighFrequency.Invoke(GetState(), cancel);
            return true;
        }

        private bool UpdateTemperatureDict(string id, double temperature, bool settable, double? target, bool targetReached)
        {
            lock (_stateLock)
            {
                ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(_stateDict, id, out _);
                if (value == null)
                    value = new();
                if ((value.Target, value.Current, value.Settable, value.TargetReached) != (target, temperature, settable, targetReached))
                {
                    var options = _options.CurrentValue;
                    var timestamp = SystemTimestamp.Now;
                    var evictTimestamp = timestamp - options.AverageDuration;
                    while (value.AverageQueue.Count > 0 && value.AverageQueue.PeekFront().Timestamp <= evictTimestamp)
                        value.AverageQueue.PopFront();
                    var sum = 0.0;
                    value.AverageQueue.PushBack((temperature, timestamp));
                    for (int i = 0; i < value.AverageQueue.Count; i++)
                        sum += value.AverageQueue[i].Temperature;
                    value.Target = target;
                    value.Current = temperature;
                    value.Average = sum / value.AverageQueue.Count;
                    value.Settable = settable;
                    value.TargetReached = targetReached;
                    value.Timestamp = timestamp;
                    return true;
                }
                else
                    return false;
            }
        }

        public override void Dispose()
        {
            _printerClient.ManagerSetEvent.RemoveHandler(OnManagerSet);
            base.Dispose();
        }

        public IDisposable SuppressValidation(string id)
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, null);
            if (manager.Heaters.TryGetValue(id, out var heater))
                return heater.SupressValidation();
            else
                return NullDisposable.Instance;
        }
    }
}
