// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public class FakeMovementClientOptions : MovementClientBaseOptions
    {
        public double PwmCycleTime { get; set; } = 1.0 / 4500;
        public TimeSpan AccelerationStepDuration { get; set; } = TimeSpan.FromSeconds(0.01);
        public double MaxXYVelocity { get; set; }
        public double SpeedFactor { get; set; } = 1;

        public override void CopyFrom(MovementConfigOptions config)
        {
            base.CopyFrom(config);
            var other = config as FakeMovementClientOptions;
            if (other != null)
            {
                PwmCycleTime = other.PwmCycleTime;
                AccelerationStepDuration = other.AccelerationStepDuration;
            }
        }
    }

    public sealed class FakeMovementClient : MovementClientBase
    {
        private const double _maxReaasonableVelocity = 1_000_000_000;
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<FakeMovementClientOptions> _options;
        private readonly IPowerClient _powerClient;
        private SystemTimestamp _timestamp;
        private double _posX, _posY, _posR, _posZ1, _posZ2;
        private readonly object _lock = new();
        private readonly AsyncLock _homingLock;
        private readonly TrapezoidCalculator _trapezoid;

        public FakeMovementClient(
            ILogger<FakeMovementClient> logger,
            IOptionsMonitor<FakeMovementClientOptions> options,
            IPowerClient powerClient)
            : base(logger, options)
        {
            _logger = logger;
            _options = options;
            _powerClient = powerClient;

            _homingLock = new();
            _trapezoid = new();
        }

        protected override Position? TryGetPosition()
        {
            lock (_lock)
            {
                return new Position(_posX, _posY, _posZ1, _posZ2, _posR);
            }
        }

        private void ResetTimestampInner()
        {
            var now = SystemTimestamp.Now;
            if (_timestamp.IsEmpty || _timestamp < now)
                _timestamp = now;
        }

        public override ValueTask Dwell(TimeSpan delay, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            lock (_lock)
            {
                ResetTimestampInner();
                _timestamp += delay / options.SpeedFactor;
            }
            return ValueTask.CompletedTask;
        }

        public override Task FinishMovement(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            TimeSpan duration;
            lock (_lock)
            {
                var now = SystemTimestamp.Now;
                if (!_timestamp.IsEmpty && _timestamp > now)
                    duration = _timestamp - now;
                else
                    duration = TimeSpan.Zero;
            }
            if (duration != TimeSpan.Zero)
                return Delay(duration, context, cancel);
            else
                return Task.CompletedTask;
        }

        public override async ValueTask HomeAux(MovementAxis axis, EndstopSensitivity sensitivity, double maxDistance, double? speed = null, bool noExtraMoves = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            using (await _homingLock.LockAsync(cancel))
            {
                await FinishMovement(context, cancel);
                double distance;
                lock (_lock)
                {
                    switch (axis)
                    {
                        case MovementAxis.Z1:
                            distance = _posZ1;
                            _posZ1 = 0;
                            break;
                        case MovementAxis.Z2:
                            distance = _posZ2;
                            _posZ2 = 0;
                            break;
                        case MovementAxis.R:
                            distance = _posR;
                            _posR = 0;
                            break;
                        default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                    }
                }
                if (distance != 0 && speed != null)
                {
                    double travel;
                    if (distance >= 0)
                        travel = Math.Max(Math.Min(distance, -maxDistance), 0);
                    else
                        travel = Math.Max(Math.Min(-distance, maxDistance), 0);
                    var duration = TimeSpan.FromSeconds(travel / speed.Value);
                    await Delay(duration, context,cancel);
                }
                await UpdatePositionHighFrequency(true, cancel);
            }
        }

        public override async ValueTask HomeXY(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            using (await _homingLock.LockAsync(cancel))
            {
                await FinishMovement(context, cancel);
                lock (_lock)
                {
                    _posX = 0;
                    _posY = 0;
                }
                await UpdatePositionHighFrequency(true, cancel);
            }
        }

        public override ValueTask MoveAux(MovementAxis axis, MoveAuxItem item, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            lock (_lock)
            {
                double distance;
                ResetTimestampInner();
                switch (axis)
                {
                    case MovementAxis.Z1:
                        distance = item.Relative ? item.Value : item.Value - _posZ1;
                        _posZ1 = item.Relative ? _posZ1 + item.Value : item.Value;
                        break;
                    case MovementAxis.Z2:
                        distance = item.Relative ? item.Value : item.Value - _posZ2;
                        _posZ2 = item.Relative ? _posZ2 + item.Value : item.Value;
                        break;
                    case MovementAxis.R:
                        distance = item.Relative ? item.Value : item.Value - _posR;
                        _posR = item.Relative ? _posR + item.Value : item.Value;
                        break;
                    default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                }
                _trapezoid.Values.Clear();
                var duration = _trapezoid.Move(
                    item.InitialSpeed ?? 0,
                    item.FinalSpeed ?? 0,
                    item.Acceleration ?? 0,
                    item.Decceleration ?? item.Acceleration ?? 0,
                    item.Speed ?? _maxReaasonableVelocity,
                    0,
                    Math.Abs(distance),
                    0);
                _timestamp += duration / options.SpeedFactor;
            }
            return UpdatePositionHighFrequency(false, cancel);
        }

        public override async ValueTask<bool> EndstopMoveAux(MovementAxis axis, EndstopSensitivity sensitivity, IReadOnlyList<MoveAuxItem> items, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            foreach (var item in items)
                await MoveAux(axis, item, hidden, context, cancel);
            await FinishMovement(context, cancel);
            return false;
        }

        public override TimeSpan GetMoveXYTime(double rx, double ry, double? speed = null, IPrinterClientCommandContext? context = null)
        {
            var options = _options.CurrentValue;
            var distance = Math.Sqrt(rx * rx + ry * ry);
            var velocity = Math.Min(speed / 60 ?? options.MaxXYVelocity, options.MaxXYVelocity);
            if (velocity <= 0)
                throw new ArgumentOutOfRangeException(nameof(speed));
            if (distance != 0)
                return TimeSpan.FromSeconds(distance / velocity);
            else
                return TimeSpan.Zero;
        }

        public override ValueTask MoveXY(double x, double y, bool relative, double? speed = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var velocity = Math.Min(speed / 60 ?? options.MaxXYVelocity, options.MaxXYVelocity);
            if (velocity <= 0)
                throw new ArgumentOutOfRangeException(nameof(speed));
            lock (_lock)
            {
                double distance;
                TimeSpan duration;
                ResetTimestampInner();
                distance = Math.Sqrt(relative ? x * x + y * y : NumberExtensions.Square(x - _posX) + NumberExtensions.Square(y - _posY));
                _posX = relative ? _posX + x : x;
                _posY = relative ? _posY + y : y;
                if (distance != 0)
                    duration = TimeSpan.FromSeconds(distance / velocity);
                else
                    duration = TimeSpan.Zero;
                _timestamp += duration / options.SpeedFactor;
            }
            return UpdatePositionHighFrequency(false, cancel);
        }

        public override ValueTask SetLaser(double value, bool noCompensation = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            lock (_lock)
            {
                ResetTimestampInner();
            }
            // bit of a hack here, but we need to pass the updates to the UI. Timestamps will be all wrong.
            // we also use this timestamp to check whether last value writtern to powerClient has been by this class
            if (_powerClient is PowerClientBase powerClientBase)
                return powerClientBase.UpdatePowerDictWithNotify(_powerClient.LaserId, value, SystemTimestamp.Now, cancel);
            else
                return ValueTask.CompletedTask;
        }

        public override ValueTask<(TimeSpan Duration, SystemTimestamp Timestamp)> GetRemainingPrintTime(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            TimeSpan duration;
            lock (_lock)
            {
                var now = SystemTimestamp.Now;
                if (!_timestamp.IsEmpty && _timestamp > now)
                    duration = _timestamp - now;
                else
                    duration = TimeSpan.Zero;
            }
            return new ValueTask<(TimeSpan, SystemTimestamp)>((duration, SystemTimestamp.Now + duration));
        }

        public override double? TryGetMinLaserPwmCycleTime(IPrinterClientCommandContext? context = null)
            => _options.CurrentValue.PwmCycleTime;

        public override TimeSpan GetQueueAheadDuration(IPrinterClientCommandContext? context = null)
            => TimeSpan.Zero;
    }
}
