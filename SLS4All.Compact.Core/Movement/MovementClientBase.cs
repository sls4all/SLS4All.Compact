// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public class MovementClientBaseOptions : MovementConfigOptions
    {
        public TimeSpan LowFrequencyPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan ContinuousMoveStep { get; set; } = TimeSpan.FromSeconds(0.1);
        public TimeSpan ContinuousMoveSkip { get; set; } = TimeSpan.FromSeconds(0.1);
        public double ZToMmFactor { get; set; } = 0.001;

        public override void CopyFrom(MovementConfigOptions config)
        {
            base.CopyFrom(config);
            var other = config as MovementClientBaseOptions;
            if (other != null)
            {
                LowFrequencyPeriod = other.LowFrequencyPeriod;
                ContinuousMoveStep = other.ContinuousMoveStep;
                ContinuousMoveSkip = other.ContinuousMoveSkip;
                ZToMmFactor = other.ZToMmFactor;
            }
        }
    }

    public abstract class MovementClientBase : BackgroundThreadService, IMovementClient
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<MovementClientBaseOptions> _options;
        private volatile Position _positionLowFrequency;

        private readonly static object _moveXYFormatterTag = new();
        private readonly static object _setLaserFormatterTag = new();

        private readonly DelegatedCodeFormatter _dwellFormatter;
        private readonly DelegatedCodeFormatter _moveXYFormatter;
        private readonly DelegatedCodeFormatter _moveRFormatter;
        private readonly DelegatedCodeFormatter _moveZ1Formatter;
        private readonly DelegatedCodeFormatter _moveZ2Formatter;
        private readonly DelegatedCodeFormatter _finishMovementFormatter;
        private readonly DelegatedCodeFormatter _setLaserFormatter;

        public double MaxXY => _options.CurrentValue.MaxXY;
        public double StepXYDistance => _options.CurrentValue.StepXYDistance;
        public Position CurrentPosition => _positionLowFrequency;
        public AsyncEvent<Position> PositionChangedLowFrequency { get; } = new();
        public AsyncEvent<PositionHighFrequency> PositionChangedHighFrequency { get; } = new();

        public MovementClientBase(
            ILogger logger,
            IOptionsMonitor<MovementClientBaseOptions> options)
            : base(logger)
        {
            _logger = logger;
            _options = options;

            _positionLowFrequency = new Position(0, 0, 0, 0, 0);
            _dwellFormatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                Dwell(TimeSpan.FromSeconds(cmd.Arg1), hidden, context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"DWELL SEC={cmd.Arg1}"));
            _moveXYFormatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                MoveXY(cmd.Arg1, cmd.Arg2, cmd.Arg3 != 0, cmd.Arg4Nullable, hidden, context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"MOVE_XY X={cmd.Arg1} Y={cmd.Arg2} RELATIVE={cmd.Arg3} SPEED={cmd.Arg4Nullable}"),
                _moveXYFormatterTag);
            _moveRFormatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                MoveAux(MovementAxis.R, cmd.Arg1, cmd.Arg2 != 0, cmd.Arg3 != float.MinValue ? cmd.Arg3 : null, cmd.Arg4 != float.MinValue ? cmd.Arg4 : null, hidden: hidden, context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"MOVE_R R={cmd.Arg1} RELATIVE={cmd.Arg2} SPEED={cmd.Arg3Nullable} ACCEL={cmd.Arg4Nullable}"));
            _moveZ1Formatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                MoveAux(MovementAxis.Z1, cmd.Arg1, cmd.Arg2 != 0, cmd.Arg3 != float.MinValue ? cmd.Arg3 : null, cmd.Arg4 != float.MinValue ? cmd.Arg4 : null, hidden: hidden, context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"MOVE_Z1 Z1={cmd.Arg1} RELATIVE={cmd.Arg2} SPEED={cmd.Arg3Nullable} ACCEL={cmd.Arg4Nullable}"));
            _moveZ2Formatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                MoveAux(MovementAxis.Z2, cmd.Arg1, cmd.Arg2 != 0, cmd.Arg3 != float.MinValue ? cmd.Arg3 : null, cmd.Arg4 != float.MinValue ? cmd.Arg4 : null, hidden: hidden, context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"MOVE_Z2 Z2={cmd.Arg1} RELATIVE={cmd.Arg2} SPEED={cmd.Arg3Nullable} ACCEL={cmd.Arg4Nullable}"));
            _finishMovementFormatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                FinishMovement(context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"FINISH_MOVEMENT"));
            _setLaserFormatter = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                SetLaser(cmd.Arg1, cmd.Arg2 != 0, context: context, cancel: cancel),
                cmd => string.Create(CultureInfo.InvariantCulture, $"SET_LASER VALUE={cmd.Arg1} NO_COMP={cmd.Arg2}"),
                _setLaserFormatterTag);
        }

        protected abstract Position? TryGetPosition();

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            var timer = new PeriodicTimer(options.LowFrequencyPeriod);
            while (true)
            {
                try
                {
                    var position = TryGetPosition();
                    if (position != null && position != _positionLowFrequency)
                    {
                        _positionLowFrequency = position;
                        await PositionChangedLowFrequency.Invoke(position, cancel);
                    }
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

        public abstract ValueTask Dwell(TimeSpan delay, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public ValueTask DwellCode(ChannelWriter<CodeCommand> channel, TimeSpan delay, CancellationToken cancel = default)
            => channel.WriteAsync(_dwellFormatter.Create((float)delay.TotalSeconds), cancel);

        public abstract Task EnableProjectionPattern(bool enable, CancellationToken cancel = default);

        public abstract Task FinishMovement(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public ValueTask FinishMovementCode(ChannelWriter<CodeCommand> channel, CancellationToken cancel = default)
            => channel.WriteAsync(_finishMovementFormatter.Create(), cancel);

        public abstract ValueTask HomeAux(MovementAxis axis, double maxDistance, double? speed = null, bool noExtraMoves = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        protected ValueTask UpdatePositionHighFrequency(bool hasHomed, CancellationToken cancel)
        {
            var position = TryGetPosition();
            if (position != null)
                return PositionChangedHighFrequency.Invoke(new PositionHighFrequency(position, hasHomed), cancel);
            else
                return ValueTask.CompletedTask;
        }

        public abstract ValueTask HomeXY(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public abstract ValueTask MoveAux(MovementAxis axis, double value, bool relative, double? speed = null, double? acceleration = null, double? decceleration = null, bool hidden = false, double? initialSpeed = null, double? finalSpeed = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public ValueTask MoveAuxCode(ChannelWriter<CodeCommand> channel, MovementAxis axis, double value, bool relative, double? speed = null, double? acceleration = null, CancellationToken cancel = default)
        {
            var formatter = axis switch
            {
                MovementAxis.R => _moveRFormatter,
                MovementAxis.Z1 => _moveZ1Formatter,
                MovementAxis.Z2 => _moveZ2Formatter,
                _ => throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis)),
            };
            return channel.WriteAsync(formatter.Create((float)value, relative ? 1 : 0, (float?)speed ?? float.MinValue, (float?)acceleration ?? float.MinValue), cancel);
        }

        public async ValueTask MoveContinuous(MovementAxis axis, bool positive, double speed, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var step = options.ContinuousMoveStep.TotalSeconds * speed;
            if (!positive)
                step = -step;
            var stopwatch = Stopwatch.StartNew();
            var sum = TimeSpan.Zero;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                if (axis is MovementAxis.X or MovementAxis.Y)
                    await MoveXY(
                        axis == MovementAxis.X ? step : 0,
                        axis == MovementAxis.Y ? step : 0,
                        true,
                        speed,
                        true,
                        context,
                        cancel);
                else
                    await MoveAux(
                        axis,
                        step,
                        true,
                        speed,
                        0,
                        hidden: true,
                        context: context,
                        cancel: cancel);
                sum += options.ContinuousMoveStep;
                var wait = sum - stopwatch.Elapsed - options.ContinuousMoveSkip;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancel);
            }
        }

        public abstract TimeSpan GetMoveXYTime(double rx, double ry, double? speed = null, IPrinterClientCommandContext? context = null);

        public abstract ValueTask MoveXY(double x, double y, bool relative, double? speed = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public ValueTask MoveXYCode(ChannelWriter<CodeCommand> channel, double x, double y, bool relative, double? speed = null, CancellationToken cancel = default)
        {
            if (speed <= 0)
                throw new ArgumentOutOfRangeException(nameof(speed));
            return channel.WriteAsync(_moveXYFormatter.Create((float)x, (float)y, relative ? 1 : 0, (float?)speed ?? float.MinValue), cancel);
        }

        public int XYToSteps(double v)
        {
            var stepXYDistance = _options.CurrentValue.StepXYDistance;
            return (int)Math.Round(v / stepXYDistance);
        }

        public (int x, int y) XYToSteps(double x, double y, double stepXYDistance = 0)
        {
            if (stepXYDistance == 0)
                stepXYDistance = _options.CurrentValue.StepXYDistance;
            return ((int)Math.Round(x / stepXYDistance),
                (int)Math.Round(y / stepXYDistance));
        }

        public (double x, double y) StepsToXY(int x, int y, double stepXYDistance = 0)
        {
            if (stepXYDistance == 0)
                stepXYDistance = _options.CurrentValue.StepXYDistance;
            return (x * stepXYDistance,
                y * stepXYDistance);
        }

        public bool IsMoveXYCode(in CodeCommand cmd, out double x, out double y, out bool relative)
        {
            if (DelegatedCodeFormatter.IsWithTag(cmd, _moveXYFormatterTag))
            {
                x = cmd.Arg1;
                y = cmd.Arg2;
                relative = cmd.Arg3 != 0;
                return true;
            }
            else
            {
                x = 0;
                y = 0;
                relative = false;
                return false;
            }
        }

        public abstract ValueTask SetLaser(double value, bool noCompensation = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public ValueTask SetLaserCode(ChannelWriter<CodeCommand> channel, double value, bool noCompensation = false, CancellationToken cancel = default)
            => channel.WriteAsync(_setLaserFormatter.Create((float)value, noCompensation ? 1 : 0), cancel);

        public bool IsSetLaserCode(in CodeCommand cmd, out double value, out bool noCompensation)
        {
            if (DelegatedCodeFormatter.IsWithTag(cmd, _setLaserFormatterTag))
            {
                value = cmd.Arg1;
                noCompensation = cmd.Arg2 != 0;
                return true;
            }
            else
            {
                value = 0;
                noCompensation = false;
                return false;
            }
        }

        public abstract ValueTask<(TimeSpan Duration, SystemTimestamp Timestamp)> GetRemainingPrintTime(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public abstract double? TryGetMinLaserPwmCycleTime(IPrinterClientCommandContext? context = null);

        protected virtual Task Delay(TimeSpan duration, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            if (context?.HasDelay == true)
                return context.Delay(duration, cancel);
            else
                return Task.Delay(duration, cancel);
        }

        public abstract TimeSpan GetQueueAheadDuration(IPrinterClientCommandContext? context = null);
    }
}
