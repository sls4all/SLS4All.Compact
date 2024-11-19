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
    public class HeaterCheckOptions
    {
        public TimeSpan CheckGainTime { get; set; } = TimeSpan.FromMinutes(60);
        public double HeatingGain { get; set; } = 2;
        public double Hysteresis { get; set; } = 5;
        public TimeSpan CheckPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public required double MaxError { get; set; }
    }

    public sealed class HeaterCheck
    {
        private readonly ILogger<HeaterCheck> _logger;
        private readonly IOptions<HeaterCheckOptions> _options;
        private readonly McuManager _manager;
        private readonly IMcuHeater _heater;
        private readonly Lock _syncRoot = new();
        private double _error;
        private double? _lastTarget;
        private double _goalTemperature;
        private SystemTimestamp _lastErrorTime;
        private SystemTimestamp _goalTime;
        private bool _approachingTarget;
        private bool _startingApproach;

        public HeaterCheck(
            IOptions<HeaterCheckOptions> options,
            McuManager manager,
            IMcuHeater heater)
        {
            _logger = manager.CreateLogger<HeaterCheck>();
            _options = options;
            _manager = manager;
            _heater = heater;

            _heater.ReadEvent.AddHandler(OnRead);
            _manager.RunningCancel.Register(() => _heater.ReadEvent.RemoveHandler(OnRead));
            Task.Run(ThreadProc);
        }

        private void ProcessInner(double temperature, SystemTimestamp timestamp)
        {
            var options = _options.Value;

            var target = _heater.Target;
            if (target == null || temperature >= target.Value - options.Hysteresis)
            {
                // Temperature near target - reset checks
                if (_approachingTarget && target != null)
                    _logger.LogDebug($"Heater {_heater} within range of {target} with temperature {temperature}");
                _approachingTarget = false;
                _startingApproach = false;
                if (target == null || temperature <= target.Value + options.Hysteresis)
                    _error = 0;
            }
            else
            {
                if (_lastErrorTime.IsEmpty)
                    _lastErrorTime = timestamp;
                var elapsed = timestamp - _lastErrorTime;
                _lastErrorTime = timestamp;
                if (elapsed > options.CheckPeriod)
                    elapsed = options.CheckPeriod;
                _error += ((target.Value - options.Hysteresis) - temperature) * elapsed.TotalSeconds;
                if (_heater.IsValidationSupressed || !_manager.IsHeating)
                    _error = 0;

                if (!_approachingTarget)
                {
                    if (target != _lastTarget) // Target changed - reset checks
                        _logger.LogDebug($"Heater {_heater} approaching new target of {target} with temperature {temperature}");
                    _approachingTarget = true;
                    _startingApproach = true;
                    _goalTemperature = temperature + options.HeatingGain;
                    _goalTime = timestamp + options.CheckGainTime;
                }
                else if (_error > options.MaxError)
                {
                    HeaterFault();
                    return;
                }
                else if (temperature >= _goalTemperature)
                {
                    // Temperature approaching target - reset checks
                    _startingApproach = false;
                    _error = 0;
                    _goalTemperature = temperature + options.HeatingGain;
                    _goalTime = timestamp + options.CheckGainTime;
                }
                else if (timestamp >= _goalTime)
                {
                    // Temperature is no longer approaching target
                    _approachingTarget = false;
                    _logger.LogDebug($"Heater {_heater} no longer approaching new target of {target} with temperature {temperature}");
                }
                else if (_startingApproach)
                    _goalTemperature = Math.Min(_goalTemperature, temperature + options.HeatingGain);
            }
            _lastTarget = target;
        }

        private ValueTask OnRead(McuTemperatureSensorData data, CancellationToken token)
        {
            lock (_syncRoot)
            {
                ProcessInner(data.Temperature, SystemTimestamp.Now); // NOTE: use system timestamp to ensure we wont go back in time between invocations
            }
            return ValueTask.CompletedTask;
        }

        private async void ThreadProc()
        {
            var options = _options.Value;
            var timer = new PeriodicTimer(options.CheckPeriod);
            var cancel = _manager.RunningCancel;
            try
            {
                while (await timer.WaitForNextTickAsync(cancel))
                { 
                    var currentValue = _heater.CurrentValue;
                    if (currentValue == null)
                        continue;
                    ProcessInner(currentValue.Temperature, SystemTimestamp.Now); // NOTE: use system time, not sampling time
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    var msg = $"Unhandled exception when verifying heater {_heater}";
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

        private void HeaterFault()
        {
            _manager.Shutdown(new Messages.McuShutdownMessage
            {
                Mcu = null,
                Reason = $"Heater {_heater} is not heating at expected rate"
            });
        }
    }
}
