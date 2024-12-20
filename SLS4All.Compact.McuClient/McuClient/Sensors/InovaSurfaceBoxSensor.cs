// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;
using System.Xml.Linq;
using SLS4All.Compact.Helpers;
using System.Reflection.Metadata.Ecma335;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Graphics;

namespace SLS4All.Compact.McuClient.Sensors
{
    public class InovaSurfaceBoxSensorOptions
    {
        public required int MinX { get; set; }
        public required int MaxX { get; set; }
        public required int MinY { get; set; }
        public required int MaxY { get; set; }
        public BoundaryRectangle Box => new BoundaryRectangle(MinX, MinY, MaxX, MaxY);
        public required InovaSurfaceHeaterMode Mode { get; set; }
    }

    public sealed class InovaSurfaceBoxSensor : IMcuTemperatureSensor
    {
        private readonly IOptions<InovaSurfaceBoxSensorOptions> _options;
        private readonly Lock _locker = new();
        private readonly McuManager _manager;
        private readonly ITemperatureCamera _camera;
        private readonly TaskQueue _readEventQueue;
        private volatile McuTemperatureSensorData? _current;
        private readonly List<float> _calcTemperatureTemp;
        private readonly string _name;
        private readonly ReferenceCounter _validationSupressor;

        public AsyncEvent<McuTemperatureSensorData> ReadEvent { get; } = new();
        public McuTemperatureSensorData? CurrentValue => _current;
        public bool IsValidationSupressed => _validationSupressor.IsIncremented;

        public InovaSurfaceBoxSensor(
            IOptions<InovaSurfaceBoxSensorOptions> options,
            McuManager manager,
            ITemperatureCamera camera,
            string name)
        {
            _options = options;
            _manager = manager;
            _camera = camera;
            _name = name;

            _validationSupressor = new();
            _readEventQueue = new();
            _calcTemperatureTemp = new();

            // hook
            _camera.CurrentPixelsChanged.AddHandler(OnPixelsChanged);
            manager.RunningCancel.Register(() => _camera.CurrentPixelsChanged.RemoveHandler(OnPixelsChanged));
        }

        private void Update()
        {
            var options = _options.Value;
            lock (_locker)
            {
                var time = SystemTimestamp.Now;

                var temperature = CalcTemperature();
                var current = new McuTemperatureSensorData(temperature, time);
                _current = current;

                _readEventQueue.EnqueueValue(() => ReadEvent.Invoke(current, _manager.RunningCancel), null);
            }
        }

        private ValueTask OnPixelsChanged(CancellationToken cancel)
        {
            Update();
            return ValueTask.CompletedTask;
        }

        private float CalcTemperature()
        {
            var options = _options.Value;
            var matrix = _camera.CurrentPixels;
            var width = _camera.Width;
            var height = _camera.Height;
            var values = _calcTemperatureTemp;
            var mainBox = _camera.MainBox;
            var box = mainBox.OffsetInTopLeft(options.Box);
            values.Clear();
            for (var y = box.MinY; y <= box.MaxY; y++)
                for (int x = box.MinX; x <= box.MaxX; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                        values.Add(matrix[x + y * width]);
                }
            if (values.Count == 0)
                values.Add(0);
            float temperature;
            switch (options.Mode)
            {
                case InovaSurfaceHeaterMode.Max:
                    temperature = values.Max();
                    break;
                case InovaSurfaceHeaterMode.Min:
                    temperature = values.Min();
                    break;
                case InovaSurfaceHeaterMode.Avg:
                    temperature = values.Average();
                    break;
                default:
                    throw new InvalidOperationException($"Invalid mode {options.Mode}");
            }
            return temperature;
        }

        public IDisposable SupressValidation()
            => _validationSupressor.Increment();

        public override string ToString()
            => $"{_name} [InovaSurfaceBox]";
    }
}
