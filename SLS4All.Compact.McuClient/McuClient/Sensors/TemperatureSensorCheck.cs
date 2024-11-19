// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SLS4All.Compact.McuClient.Sensors
{
    public class TemperatureSensorCheckOptions
    {
        public TimeSpan CheckPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan OutOfRangePeriod { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan MaxChangePeriod { get; set; } = TimeSpan.FromSeconds(15);
        public bool ExpectChangeInTemperature { get; set; } = true;
        public required double MinTemperature { get; set; }
        public required double MaxTemperature { get; set; }
    }

    public sealed class TemperatureSensorCheck
    {
        private readonly ILogger<TemperatureSensorCheck> _logger;
        private readonly IOptions<TemperatureSensorCheckOptions> _options;
        private readonly McuManager _manager;
        private readonly IMcuTemperatureSensor _sensor;
        private readonly Lock _syncRoot = new();
        private SystemTimestamp _outOfRangeStartTime;
        private SystemTimestamp _readLastTime;
        private double _readLastTemperature;

        public TemperatureSensorCheck(
            IOptions<TemperatureSensorCheckOptions> options,
            McuManager manager,
            IMcuTemperatureSensor sensor)
        {
            _logger = manager.CreateLogger<TemperatureSensorCheck>();
            _options = options;
            _manager = manager;
            _sensor = sensor;
            _readLastTemperature = double.MinValue;

            _sensor.ReadEvent.AddHandler(OnRead);
            _manager.RunningCancel.Register(() => _sensor.ReadEvent.RemoveHandler(OnRead));
            Task.Run(ThreadProc);
        }

        private void ProcessInner(bool hasReadData, double temperature, SystemTimestamp timestamp)
        {
            var options = _options.Value;

            // check frequency of updates
            if (!_sensor.IsValidationSupressed)
            {
                if (_readLastTime.IsEmpty ||
                    (hasReadData && (_readLastTemperature != temperature || !options.ExpectChangeInTemperature)))
                {
                    _readLastTime = timestamp;
                    _readLastTemperature = temperature;
                }
                else
                {
                    var elapsed = timestamp - _readLastTime;
                    if (elapsed > options.MaxChangePeriod)
                    {
                        _manager.Shutdown(new Messages.McuShutdownMessage
                        {
                            Mcu = null,
                            Reason = $"Change period {elapsed} for sensor {_sensor} is larger than set period of {options.MaxChangePeriod}. Temperature={temperature}. LastTemperature={_readLastTemperature}. Does the sensor work correctly?",
                        });
                    }
                }
            }

            // check temperature out of range
            if ((temperature >= options.MinTemperature && temperature <= options.MaxTemperature) ||
                _sensor.IsValidationSupressed || !_manager.IsHeating)
                _outOfRangeStartTime = default;
            else if (_outOfRangeStartTime.IsEmpty)
            {
                _logger.LogInformation($"Temperature {temperature} on sensor {_sensor} is currently out of range, if timer elapses, printer will shutdown. Min={options.MinTemperature}. Max={options.MaxTemperature}. Does the sensor work correctly?");
                _outOfRangeStartTime = timestamp;
            }
            else if (timestamp - _outOfRangeStartTime >= options.OutOfRangePeriod)
            {
                _manager.Shutdown(new Messages.McuShutdownMessage
                {
                    Mcu = null,
                    Reason = $"Temperature {temperature} on sensor {_sensor} is out of range. Min={options.MinTemperature}. Max={options.MaxTemperature}. Does the sensor work correctly?",
                });
            }
        }

        private ValueTask OnRead(McuTemperatureSensorData data, CancellationToken token)
        {
            lock (_syncRoot)
            {
                ProcessInner(true, data.Temperature, SystemTimestamp.Now); // NOTE: use system timestamp to ensure we wont go back in time between invocations
            }
            return ValueTask.CompletedTask;
        }

        private async Task ThreadProc()
        {
            var options = _options.Value;
            var timer = new PeriodicTimer(options.CheckPeriod);
            var cancel = _manager.RunningCancel;
            try
            {
                while (await timer.WaitForNextTickAsync(cancel))
                {
                    lock (_syncRoot)
                    {
                        var currentValue = _sensor.CurrentValue;
                        if (currentValue == null)
                            continue;
                        ProcessInner(false, currentValue.Temperature, SystemTimestamp.Now); // NOTE: use system time, not sampling time
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    var msg = $"Unhandled exception when verifying temperature sensor {_sensor}";
                    _logger.LogCritical(ex, msg);
                    _manager.Shutdown(new Messages.McuShutdownMessage
                    {
                        Mcu = null,
                        Reason = msg,
                        Exception = ex,
                    });
                }
            }
        }
    }
}
