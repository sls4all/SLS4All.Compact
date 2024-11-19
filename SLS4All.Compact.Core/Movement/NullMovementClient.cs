// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public sealed class NullMovementClient : IMovementClient
    {
        private const double _maxXY = 1000;
        private const double _stepXYDistance = 0.1220852154804053;

        public static NullMovementClient Instance { get; } = new();

        public NullMovementClient()
        {
        }

        public Position CurrentPosition => new Position(0, 0, 0, 0, 0);

        public AsyncEvent<Position> PositionChangedLowFrequency { get; } = new();

        public AsyncEvent<PositionHighFrequency> PositionChangedHighFrequency { get; } = new();

        public double MaxXY => _maxXY;

        public double StepXYDistance => _stepXYDistance;

        public ValueTask Dwell(TimeSpan delay, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask DwellCode(ChannelWriter<CodeCommand> channel, TimeSpan delay, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public Task FinishMovement(bool performMajorCleanup = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public ValueTask HomeAux(MovementAxis axis, EndstopSensitivity sensitivity, double maxDistance, double? speed = null, bool noExtraMoves = false,IPrinterClientCommandContext ? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask HomeXY(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask MoveAux(MovementAxis axis, MoveAuxItem item, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> EndstopMoveAux(MovementAxis axis, EndstopSensitivity sensitivity, IReadOnlyList<MoveAuxItem> items, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.FromResult(false);

        public ValueTask MoveAuxCode(ChannelWriter<CodeCommand> channel, MovementAxis axis, double value, bool relative, double? speed = null, double? acceleration = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask MoveContinuous(MovementAxis axis, bool positive, double speed, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public TimeSpan GetMoveXYTime(double rx, double ry, double? speed = null, bool? laserOn = null, IPrinterClientCommandContext ? context = null)
            => TimeSpan.Zero;

        public ValueTask MoveXY(double x, double y, bool relative, double? speed = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask MoveXYCode(ChannelWriter<CodeCommand> channel, double x, double y, bool relative, double? speed = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public (int x, int y) XYToSteps(double x, double y, double stepXYDistance = 0)
        {
            if (stepXYDistance == 0)
                stepXYDistance = _stepXYDistance;
            return ((int)Math.Round(x / stepXYDistance), (int)Math.Round(y / stepXYDistance));
        }

        public (double x, double y) StepsToXY(int x, int y, double stepXYDistance = 0)
        {
            if (stepXYDistance == 0)
                stepXYDistance = _stepXYDistance;
            return (x * stepXYDistance, y * stepXYDistance);
        }

        public bool IsMoveXYCode(in CodeCommand cmd, out double x, out double y, out bool relative)
        {
            x = 0;
            y = 0;
            relative = false;
            return false;
        }

        public ValueTask SetLaser(double value, bool noCompensation = false, IPrinterClientCommandContext ? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask SetLaserCode(ChannelWriter<CodeCommand> channel, double value, bool noCompensation = false,CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public bool IsSetLaserCode(in CodeCommand cmd, out double value, out bool noCompensation)
        {
            value = 0;
            noCompensation = false;
            return false;
        }

        public ValueTask<(TimeSpan, SystemTimestamp)> GetRemainingPrintTime(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => new ValueTask<(TimeSpan, SystemTimestamp)>((TimeSpan.Zero, SystemTimestamp.Now));

        public double? TryGetMinLaserPwmCycleTime(IPrinterClientCommandContext? context = null)
            => null;

        public TimeSpan GetQueueAheadDuration(IPrinterClientCommandContext? context = null)
            => TimeSpan.Zero;
    }
}
