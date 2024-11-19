// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public readonly record struct PositionHighFrequency(Position Position, bool HasHomed);

    public readonly record struct MoveAuxItem(
        double Value, 
        bool Relative, 
        double? Speed = null, 
        double? Acceleration = null, 
        double? Decceleration = null,
        double? InitialSpeed = null, 
        double? FinalSpeed = null);

    public enum EndstopSensitivity
    {
        NotSet = 0,
        Homing,
        StuckDetection,
        User,
    }

    public interface IMovementClient : IMovementConfig
    {
        Position CurrentPosition { get; }
        AsyncEvent<Position> PositionChangedLowFrequency { get; }
        AsyncEvent<PositionHighFrequency> PositionChangedHighFrequency { get; }

        TimeSpan GetQueueAheadDuration(IPrinterClientCommandContext? context = null);
        TimeSpan GetMoveXYTime(double rx, double ry, double? speed = null, bool? laserOn = null, IPrinterClientCommandContext? context = null);
        ValueTask MoveXY(double x, double y, bool relative, double? speed = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask HomeXY(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask MoveAux(MovementAxis axis, MoveAuxItem item, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask<bool> EndstopMoveAux(MovementAxis axis, EndstopSensitivity sensitivity, IReadOnlyList<MoveAuxItem> items, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask HomeAux(MovementAxis axis, EndstopSensitivity sensitivity, double maxDistance, double? speed = null, bool noExtraMoves = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask<(TimeSpan Duration, SystemTimestamp Timestamp)> GetRemainingPrintTime(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        Task FinishMovement(bool performMajorCleanup = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask MoveContinuous(MovementAxis axis, bool positive, double speed, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask Dwell(TimeSpan delay, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask SetLaser(double value, bool noCompensation = false, IPrinterClientCommandContext ? context = null, CancellationToken cancel = default);

        ValueTask MoveXYCode(ChannelWriter<CodeCommand> channel, double x, double y, bool relative, double? speed = null, CancellationToken cancel = default);
        ValueTask MoveAuxCode(ChannelWriter<CodeCommand> channel, MovementAxis axis, double value, bool relative, double? speed = null, double? acceleration = null, CancellationToken cancel = default);
        ValueTask DwellCode(ChannelWriter<CodeCommand> channel, TimeSpan delay, CancellationToken cancel = default);
        ValueTask SetLaserCode(ChannelWriter<CodeCommand> channel, double value, bool noCompensation = false,CancellationToken cancel = default);

        double? TryGetMinLaserPwmCycleTime(IPrinterClientCommandContext? context = null);
    }
}