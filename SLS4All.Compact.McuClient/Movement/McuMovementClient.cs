// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.McuClient.Helpers;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static SLS4All.Compact.McuClient.McuManager;
using System.Buffers;

namespace SLS4All.Compact.Movement
{
    public class McuMovementClientOptions : MovementClientBaseOptions
    {
        public string XStepperName { get; set; } = "x";
        public string YStepperName { get; set; } = "y";
        public string RStepperName { get; set; } = "r";
        public string Z1StepperName { get; set; } = "z1";
        public string Z2StepperName { get; set; } = "z2";
        public string LStepperName { get; set; } = "laser";
        public bool ZPreHomingEnable { get; set; } = false;
        public double ZPreHomingDistance { get; set; } = 0.0;
        public TimeSpan ZPreHomingDelay { get; set; } = TimeSpan.Zero;
        public bool ZPostHomingEnable { get; set; } = false;
        public double ZPostHomingDistance { get; set; } = 0.0;
        public bool RPostHomingEnable { get; set; } = false;
        public double RPostHomingDistance { get; set; } = 0.0;
        /// <summary>
        /// Just a bit of extra tolerance when wating for <see cref="McuMovementClient.FinishMovement"/>
        /// </summary>
        public TimeSpan FinishMovementDelay { get; set; } = TimeSpan.FromSeconds(0.01);
        public XYLMovementQueueOptions XYLQueue { get; set; } = new XYLMovementQueueOptions();
        public bool CompensatePwmLatency { get; set; } = false;
        /// <summary>
        /// Number of steps at stepper critical speed to get a minimum duration for laser-off movement, to ensure that the MCU is not overloaded with too many commands.
        /// Value should correspond to how many times is CPU duration of loading new stepper command and executing it in one step - is larger than - average CPU duration of single step.
        /// </summary>
        public int LaserOffMinDurationSteps { get; set; } = 15;
        public bool CollectGarbageOnFinishMovement { get; set; } = false;
        public TimeSpan CollectGarbageOnFinishMovementMinPeriod { get; set; } = TimeSpan.FromMinutes(1);
    }

    public sealed class McuMovementClient : MovementClientBase
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<McuMovementClientOptions> _options;
        private readonly McuPrinterClient _printerClient;
        private readonly McuPowerClient _powerClient;

        private double _posX, _posY, _posR, _posZ1, _posZ2;
        private readonly AsyncLock _homingLock;
        private readonly XYLMovementQueue _xylQueue;
        private readonly Stopwatch _sinceFinishMovementCollect;
        private readonly AsyncLock _finishMovementLock;
        private double _lastLaserTimeOffset;
        private SystemTimestamp _lastLaserTime;
        private double _lastLaserOnFactor;
        private bool _shouldPeformMajorCleanup;

        public McuMovementClientOptions Options => _options.CurrentValue;

        /// <summary>
        /// Gets whether laser is off and not overwritten in powerClient
        /// </summary>
        private bool IsLaserOnByThisNeedsLock
            => _lastLaserOnFactor > 0 && _powerClient.GetLastTimestamp(_powerClient.LaserId) == _lastLaserTime;

        /// <summary>
        /// Gets whether laser is off and not overwritten in powerClient
        /// </summary>
        private bool IsLaserOffByThisNeedsLock
            => _lastLaserOnFactor == 0 && _powerClient.GetLastTimestamp(_powerClient.LaserId) == _lastLaserTime;

        public McuMovementClient(
            ILogger<McuMovementClient> logger,
            IOptionsMonitor<McuMovementClientOptions> options,
            McuPrinterClient printerClient,
            McuPowerClient powerClient)
            : base(logger, options)
        {
            var o = options.CurrentValue;
            _logger = logger;
            _options = options;
            _printerClient = printerClient;
            _powerClient = powerClient;
            _homingLock = new();
            _xylQueue = new(
                logger,
                TransformOptionsMonitor.Create(options, x => x.XYLQueue),
                printerClient,
                this);
            _sinceFinishMovementCollect = new();
            _finishMovementLock = new();
        }

        public (IMcuStepper X, IMcuStepper Y, IMcuStepper L, IMcu Mcu) GetXYL(McuManager manager, McuMovementClientOptions? options)
        {
            if (options == null)
                options = _options.CurrentValue;
            var stepperX = manager.Steppers[options.XStepperName];
            var stepperY = manager.Steppers[options.YStepperName];
            var stepperL = manager.Steppers[options.LStepperName];
            if (stepperX.Mcu != stepperY.Mcu || stepperX.Mcu != stepperL.Mcu)
                throw new InvalidOperationException("X/Y/L steppers need to be on the same Mcu");
            if (stepperX.SendAheadDuration != stepperY.SendAheadDuration || stepperX.SendAheadDuration != stepperL.SendAheadDuration)
                throw new InvalidOperationException($"X/Y/L steppers {stepperL.SendAheadDuration} needs to be the same");
            if (stepperX.QueueAheadDuration != stepperY.QueueAheadDuration || stepperX.QueueAheadDuration != stepperL.QueueAheadDuration)
                throw new InvalidOperationException($"X/Y/L steppers {stepperL.QueueAheadDuration} needs to be the same");
            if (stepperX.UnderflowDuration != stepperY.UnderflowDuration || stepperX.UnderflowDuration != stepperL.UnderflowDuration)
                throw new InvalidOperationException($"X/Y/L steppers {stepperL.UnderflowDuration} needs to be the same");
            var mcu = stepperX.Mcu;
            return (stepperX, stepperY, stepperL, mcu);
        }

        public override ValueTask Dwell(TimeSpan delay, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay));

            cancel.ThrowIfCancellationRequested();

            var seconds = delay.TotalSeconds;
            if (seconds == 0)
                return ValueTask.CompletedTask;

            var options = _options.CurrentValue;
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            (_, _, _, var mcu) = GetXYL(manager, options);

            cancel.ThrowIfCancellationRequested();
            var master = manager.LockMasterQueue();
            try
            {
                MoveResetXYLInner(options, mcu, ref master, manager, context, out var startTime, out _, cancel);
                var endTime = startTime + seconds;
                master[this] = endTime;
                master[_xylQueue] = endTime;
            }
            finally
            {
                master.Dispose();
            }
            _xylQueue.ScheduleFlushOutsideMasterLock(context);
            return ValueTask.CompletedTask;
        }

        private (TimeSpan Duration, SystemTimestamp Timestamp) GetRemainingPrintTimeOfKeyInner(
            LockMasterQueueDisposable master,
            object key,
            TimeSpan duration,
            SystemTimestamp timestamp,
            TimeSpan subtractDelay,
            out McuTimestamp mcuTimestamp)
        {
            mcuTimestamp = master[key];
            if (!mcuTimestamp.IsEmpty)
            {
                var to = mcuTimestamp.ToSystem();
                var now = SystemTimestamp.Now;
                var candidateDuration = (to - now) - subtractDelay;
                if (candidateDuration < TimeSpan.Zero)
                    candidateDuration = TimeSpan.Zero;
                if (candidateDuration > duration)
                    duration = candidateDuration;
                if (to > timestamp)
                    timestamp = to;
            }
            return (duration, timestamp);
        }

        public override ValueTask<(TimeSpan Duration, SystemTimestamp Timestamp)> GetRemainingPrintTime(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            (var stepperX, var stepperY, var stepperL, _) = GetXYL(manager, options);

            var duration = TimeSpan.Zero;
            var timestamp = SystemTimestamp.Now;
            using (var master = manager.LockMasterQueue())
            {
                (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, this, duration, timestamp, TimeSpan.Zero, out _);
                (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, stepperX, duration, timestamp, TimeSpan.Zero, out _);
                (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, stepperY, duration, timestamp, TimeSpan.Zero, out _);
                (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, stepperL, duration, timestamp, TimeSpan.Zero, out _);
            }
            return ValueTask.FromResult((duration, timestamp));
        }

        public override async Task FinishMovement(bool performMajorCleanup = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            using (await _finishMovementLock.LockAsync(cancel))
            {
                _logger.LogDebug($"Finish movement - begin");

                var options = _options.CurrentValue;
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                (var stepperX, var stepperY, var stepperL, _) = GetXYL(manager, options);

                var shouldCollectGarbage = performMajorCleanup;
                var totalRawDuration = TimeSpan.Zero;
                var loops = 0;
                _shouldPeformMajorCleanup |= performMajorCleanup;

                for (; ; loops++)
                {
                    // flush all moves synchronously
                    _xylQueue.ScheduleFlushOutsideMasterLock(context);

                    // get duration from all possible steppers to ensure all movement will be stopped after wait
                    var duration = TimeSpan.Zero;
                    var timestamp = SystemTimestamp.Now;
                    lock (_xylQueue.FlushLock) // ensure we are not currently flusing, timestamps would be changing
                    {
                        using (var master = manager.LockMasterQueue())
                        {
                            if (options.CollectGarbageOnFinishMovement &&
                                (!_sinceFinishMovementCollect.IsRunning ||
                                 _sinceFinishMovementCollect.Elapsed >= options.CollectGarbageOnFinishMovementMinPeriod))
                            {
                                shouldCollectGarbage = true;
                            }

                            (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, this, duration, timestamp, TimeSpan.Zero, out _);
                            (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, stepperX, duration, timestamp, TimeSpan.Zero, out _);
                            (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, stepperY, duration, timestamp, TimeSpan.Zero, out _);
                            (duration, timestamp) = GetRemainingPrintTimeOfKeyInner(master, stepperL, duration, timestamp, TimeSpan.Zero, out _);

                            // NOTE: commented out, since we add FinishMovementDelay below anyway and try again
                            //// if a flush is scheduled, wait after it completes
                            //var flushAfter = _xylQueue.FlushAfterNeedsLocks;
                            //if (flushAfter != TimeSpan.MaxValue)
                            //    duration += flushAfter;

                            var queuesEmpty = _xylQueue.IsPseudoEmpty;

                            if (loops > 0 && duration == TimeSpan.Zero && queuesEmpty)
                            {
                                // force stepper reset
                                master[this] = default;
                                master[stepperX] = default;
                                master[stepperY] = default;
                                master[stepperL] = default;

                                // done
                                break;
                            }
                        }
                    }

                    // NOTE: commented out, since we add FinishMovementDelay below anyway and try again
                    //foreach (var stepper in manager.Steppers.Values)
                    //{
                    //    var candidate = stepper.SendAheadDuration;
                    //    if (duration < candidate)
                    //        duration = candidate;
                    //}

                    // wait
                    duration += options.FinishMovementDelay;
                    await Delay(duration, context, cancel);
                    totalRawDuration += duration;
                }
                var hasTimingCriticalCommandsScheduled = manager.HasTimingCriticalCommandsScheduled;
                if (shouldCollectGarbage && !hasTimingCriticalCommandsScheduled)
                {
                    _logger.LogDebug($"Finish movement - GC. Raw movement duration = {totalRawDuration}. Loops = {loops}.");
                    var start = SystemTimestamp.Now;
                    var hasCollected = manager.TryCollectGarbageBlocking(_shouldPeformMajorCleanup);
                    _logger.LogDebug($"Finish movement - end. HasCollected = {hasCollected}, Raw movement duration = {totalRawDuration}. Loops = {loops}. GC duration = {start.ElapsedFromNow}. SinceCollect={_sinceFinishMovementCollect.Elapsed}. CreatedCommands={manager.CreatedCommands}, CreatedArenas={manager.CreatedArenas}");
                    if (hasCollected)
                    {
                        _sinceFinishMovementCollect.Restart();
                        _shouldPeformMajorCleanup = false;
                    }
                }
                else
                    _logger.LogDebug($"Finish movement - end. Raw movement duration = {totalRawDuration}. Loops = {loops}. HasTimingCriticalCommandsScheduled={hasTimingCriticalCommandsScheduled}. ShouldCollectGarbage={shouldCollectGarbage}. SinceCollect={_sinceFinishMovementCollect.Elapsed}");
                PrinterGC.LogCollectionCount(_logger);
            }
        }

        public override async ValueTask HomeAux(MovementAxis axis, EndstopSensitivity sensitivity, double maxDistance, double? speed = null, bool noExtraMoves = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            using (await _homingLock.LockAsync(cancel))
            {
                _logger.LogDebug($"Homing axis {axis} by distance {maxDistance}");

                cancel.ThrowIfCancellationRequested();
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                var options = _options.CurrentValue;
                var stepper = manager.Steppers[axis switch
                {
                    MovementAxis.Z1 => options.Z1StepperName,
                    MovementAxis.Z2 => options.Z2StepperName,
                    MovementAxis.R => options.RStepperName,
                    _ => throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis)),
                }];
                await FinishMovement(context: context, cancel: cancel);

                if (options.ZPreHomingEnable && axis is MovementAxis.Z1 or MovementAxis.Z2 && !noExtraMoves)
                {
                    var counterDistance = -Math.Sign(maxDistance) * options.ZPreHomingDistance;
                    await MoveAux(axis, new MoveAuxItem(counterDistance, true, speed), context: context, cancel: cancel);
                    await FinishMovement(context: context, cancel: cancel);
                    await Delay(options.ZPreHomingDelay, context, cancel);
                    maxDistance -= counterDistance;
                }

                double startPos, endPos;
                using (var master = manager.LockMasterQueue())
                {
                    switch (axis)
                    {
                        case MovementAxis.Z1:
                            startPos = _posZ1;
                            endPos = startPos + maxDistance;
                            break;
                        case MovementAxis.Z2:
                            startPos = _posZ2;
                            endPos = startPos + maxDistance;
                            break;
                        case MovementAxis.R:
                            startPos = _posR;
                            endPos = startPos + maxDistance;
                            break;
                        default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                    }
                }

                // homing should be done with maximum precision, i.e. critical velocity
                // MaxSpeed may also be too fast for EndStop checking (we may get endstop_event timer error otherwise)
                var velocity = Math.Min(speed ?? stepper.CriticalVelocity, stepper.CriticalVelocity);
                var startTimestamp = SystemTimestamp.Now;
                var res = await stepper.EndstopMove(
                    sensitivity,
                    velocity,
                    startPos,
                    endPos,
                    null,
                    true,
                    cancel);
                var homingElapsed = startTimestamp.ElapsedFromNow;

                // update position before resetting to zero, for `PrinterWearCapture` 
                var homingApproxEndPos = endPos > startPos
                    ? Math.Min(startPos + homingElapsed.TotalSeconds * velocity, endPos)
                    : Math.Max(startPos - homingElapsed.TotalSeconds * velocity, endPos);
                using (var master = manager.LockMasterQueue())
                {
                    switch (axis)
                    {
                        case MovementAxis.Z1: _posZ1 = homingApproxEndPos; break;
                        case MovementAxis.Z2: _posZ2 = homingApproxEndPos; break;
                        case MovementAxis.R: _posR = homingApproxEndPos; break;
                        default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                    }
                }
                await UpdatePositionHighFrequency(false, cancel);

                if (res && !noExtraMoves)
                {
                    var postHomingDistance = 0.0;
                    if (options.ZPostHomingEnable && axis is MovementAxis.Z1 or MovementAxis.Z2)
                        postHomingDistance = options.ZPostHomingDistance;
                    else if (options.RPostHomingEnable && axis is MovementAxis.R)
                        postHomingDistance = options.RPostHomingDistance;
                    if (postHomingDistance > 0)
                    {
                        var counterDistance = -Math.Sign(maxDistance) * postHomingDistance;
                        await MoveAux(axis, new MoveAuxItem(counterDistance, true, velocity), context: context, cancel: cancel);
                        await FinishMovement(context: context, cancel: cancel);

                        res = await stepper.EndstopMove(
                            sensitivity,
                            velocity,
                            startPos + counterDistance,
                            endPos,
                            (postHomingDistance * 0.25) / stepper.MicrostepDistance,
                            true,
                            cancel);
                    }
                }

                // reset homed position to zero
                using (var master = manager.LockMasterQueue())
                {
                    switch (axis)
                    {
                        case MovementAxis.Z1: _posZ1 = 0; break;
                        case MovementAxis.Z2: _posZ2 = 0; break;
                        case MovementAxis.R: _posR = 0; break;
                        default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                    }
                }
                await UpdatePositionHighFrequency(true, cancel);
            }
        }

        public override async ValueTask HomeXY(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            using (await _homingLock.LockAsync(cancel))
            {
                var options = _options.CurrentValue;
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                (var stepperX, var stepperY, _, _) = GetXYL(manager, options);
                var mid = options.MaxXY * 0.5;

                await FinishMovement(context: context, cancel: cancel); // flushes, waits

                lock (_xylQueue.FlushLock) // ensure we are not currently flushing, timestamps would be changing
                {
                    using (var master = manager.LockMasterQueue())
                    {
                        // NOTE: Reset DACs in case something went wrong in SPI communication.
                        //       This will not save the print if that happened, but it is mainly a safety feature to ensure the galvos work in future layers
                        //       and dont target the laser hotspot to the same position until printer power-off or firmware restart.
                        var timestampX = stepperX.DacReset(default, mid);
                        var timestampY = stepperY.DacReset(default, mid);

                        // direct homing of galvo DACs "steppers"
                        timestampX = stepperX.DacHome(timestampX, mid, mid);
                        _posX = mid;
                        master[stepperX] = timestampX;

                        timestampY = stepperY.DacHome(timestampY, mid, mid);
                        _posY = mid;
                        master[stepperY] = timestampY;
                    }
                }

                await UpdatePositionHighFrequency(true, cancel);
                await FinishMovement(context: context, cancel: cancel); // flushes, waits
            }
        }

        public override ValueTask MoveAux(MovementAxis axis, MoveAuxItem item, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var options = _options.CurrentValue;
            var stepper = manager.Steppers[axis switch
            {
                MovementAxis.Z1 => options.Z1StepperName,
                MovementAxis.Z2 => options.Z2StepperName,
                MovementAxis.R => options.RStepperName,
                _ => throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis)),
            }];
            using (var master = manager.LockMasterQueue())
            {
                double startPos, endPos;
                switch (axis)
                {
                    case MovementAxis.Z1:
                        startPos = _posZ1;
                        _posZ1 = endPos = item.Relative ? startPos + item.Value : item.Value;
                        break;
                    case MovementAxis.Z2:
                        startPos = _posZ2;
                        _posZ2 = endPos = item.Relative ? startPos + item.Value : item.Value;
                        break;
                    case MovementAxis.R:
                        startPos = _posR;
                        _posR = endPos = item.Relative ? startPos + item.Value : item.Value;
                        break;
                    default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                }

                var timestamp = master[this];
                timestamp = stepper.Reset(
                    timestamp,
                    McuStepperResetFlags.None,
                    out _,
                    // NOTE: need to add following, since we are subtracting this value in GetRemainingPrintTime/FinishMovement
                    minQueueAheadDuration: manager.StepperQueueHigh);

                timestamp = stepper.Move(
                    item.InitialSpeed ?? 0,
                    item.FinalSpeed ?? 0,
                    item.Acceleration ?? 0,
                    item.Decceleration ?? item.Acceleration ?? 0,
                    item.Speed ?? stepper.MaxVelocity,
                    startPos,
                    endPos,
                    timestamp);

                master[this] = timestamp;
            }
            return UpdatePositionHighFrequency(false, cancel);
        }

        public override async ValueTask<bool> EndstopMoveAux(MovementAxis axis, EndstopSensitivity sensitivity, IReadOnlyList<MoveAuxItem> items, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            using (await _homingLock.LockAsync(cancel))
            {
                cancel.ThrowIfCancellationRequested();
                if (items.Count == 0)
                    return false;

                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                var options = _options.CurrentValue;
                var stepper = manager.Steppers[axis switch
                {
                    MovementAxis.Z1 => options.Z1StepperName,
                    MovementAxis.Z2 => options.Z2StepperName,
                    MovementAxis.R => options.RStepperName,
                    _ => throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis)),
                }];

                await FinishMovement(context: context, cancel: cancel);

                var positions = new (double StartPos, double EndPos, double Velocity, double InitialSpeed, double FinalSpeed, double Acceleration, double Decceleration)[items.Count];
                var maxVelocity = 0.0;
                bool res;
                using (var master = manager.LockMasterQueue())
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        double startPos, endPos;
                        switch (axis)
                        {
                            case MovementAxis.Z1:
                                startPos = _posZ1;
                                _posZ1 = endPos = item.Relative ? startPos + item.Value : item.Value;
                                break;
                            case MovementAxis.Z2:
                                startPos = _posZ2;
                                _posZ2 = endPos = item.Relative ? startPos + item.Value : item.Value;
                                break;
                            case MovementAxis.R:
                                startPos = _posR;
                                _posR = endPos = item.Relative ? startPos + item.Value : item.Value;
                                break;
                            default: throw new ArgumentException($"Invalid aux axis {axis}", nameof(axis));
                        }
                        var velocity = item.Speed ?? stepper.MaxVelocity;
                        if (velocity > maxVelocity)
                            maxVelocity = velocity;
                        positions[i] = (startPos, endPos, velocity, item.InitialSpeed ?? 0, item.FinalSpeed ?? 0, item.Acceleration ?? 0, item.Decceleration ?? item.Acceleration ?? 0);
                    }
                }

                res = await stepper.EndstopMove(
                    sensitivity,
                    maxVelocity,
                    timestamp =>
                    {
                        foreach (var position in positions)
                        {
                            timestamp = stepper.Move(
                                position.InitialSpeed,
                                position.FinalSpeed,
                                position.Acceleration,
                                position.Decceleration,
                                position.Velocity,
                                position.StartPos,
                                position.EndPos,
                                timestamp);
                        }
                        return timestamp;
                    },
                    null,
                    true,
                    cancel);

                await UpdatePositionHighFrequency(false, cancel);
                return res;
            }
        }

        private void MoveResetXYLInner(
            McuMovementClientOptions options,
            IMcu mcu,
            ref LockMasterQueueDisposable master,
            McuManager manager,
            IPrinterClientCommandContext? context,
            out McuTimestamp timestamp,
            out double offset,
            CancellationToken cancel)
        {
            var now = SystemTimestamp.Now;
            var hasReset = false;

            var xylTimestamp = master[_xylQueue];
            timestamp = master[this];

            var lowNow = now + manager.StepperQueueLow;
            var dwelled = 0.0;
            if (timestamp.IsEmpty || timestamp.ToSystem() < lowNow ||
                xylTimestamp.IsEmpty || xylTimestamp.ToSystem() < lowNow)
            {
                var newTimestamp = McuTimestamp.FromSystem(mcu, now + manager.StepperQueueHigh);
                if (!timestamp.IsEmpty && newTimestamp < timestamp)
                {
                    dwelled = timestamp.ToRelativeSeconds() - newTimestamp.ToRelativeSeconds();
                    newTimestamp = timestamp;
                }
                _logger.LogInformation(
                    $"Resetting X/Y/L to {newTimestamp.Clock}, flushing existing data. " +
                    $"This should happen only when a batch is starting, otherwise it is an indication that feeding or processing is too slow at times. " +
                    $"Was Timestamp={timestamp.Clock}, Now={McuTimestamp.FromSystem(mcu, now).Clock}, QueueLow={manager.StepperQueueLow}, QueueHigh={manager.StepperQueueHigh}, Dwelled={dwelled}");
                hasReset = true;
                timestamp = newTimestamp;
            }
            offset = timestamp.ToRelativeSeconds();
            if (hasReset)
            {
                var xySteps = XYToSteps(_posX, _posY);
                var posX = xySteps.x * options.StepXYDistance;
                var posY = xySteps.y * options.StepXYDistance;

                // loop until master queue is empty
                while (!_xylQueue.IsPseudoEmpty)
                {
                    master.Dispose();
                    cancel.ThrowIfCancellationRequested();
                    _xylQueue.ScheduleFlushOutsideMasterLock(context); // NOTE: need to flush right now on this thread to empty the Source collections, reset needs to be first
                    master = manager.LockMasterQueue();
                }

                _xylQueue.AddReset(offset, posX, posY, dwelled: dwelled);
                _lastLaserOnFactor = double.MinValue;

                _lastLaserTimeOffset = offset;
                _lastLaserTime = timestamp.ToSystem();

                // NOTE: if PWM compensation is enabled, move next command after reset two (more than one) PWM periods into the future.
                //       This ensures that any first compensated move will have enough space to move back to.
                var stepperL = manager.Steppers[options.LStepperName];
                if (options.CompensatePwmLatency && stepperL.MinPwmCycleTime != null)
                {
                    timestamp += stepperL.MinPwmCycleTime.Value * 2;
                    offset = timestamp.ToRelativeSeconds();
                }

                master[this] = timestamp;
                master[_xylQueue] = timestamp;
            }
        }

        public override TimeSpan GetMoveXYTime(double rx, double ry, double? speed = null, bool? laserOn = null, IPrinterClientCommandContext ? context = null)
        {
            var manager = McuInitializeCommandContext.GetManagerEvenInShutdown(_printerClient, context);
            var options = _options.CurrentValue;
            var distance = Math.Sqrt(rx * rx + ry * ry);
            if (distance == 0)
                return TimeSpan.Zero;
            (var stepperX, var stepperY, _, var mcu) = GetXYL(manager, options);
            var maxVelocity = Math.Min(stepperX.MaxVelocity, stepperY.MaxVelocity);
            var velocity = Math.Min(speed / 60 ?? maxVelocity, maxVelocity);
            if (velocity <= 0)
                throw new ArgumentOutOfRangeException(nameof(speed));
            var duration = distance / velocity;
            if (laserOn == false)
            {
                // delay extra fast movements with laser off, so we wont overload the MCU with too many commands
                // NOTE: see similar code in MoveXY()!
                var stepXYDistance = options.StepXYDistance;
                var criticalVelocity = Math.Min(stepperX.CriticalVelocity, stepperY.CriticalVelocity);
                var minDuration = options.LaserOffMinDurationSteps * stepXYDistance / criticalVelocity;
                if (duration < minDuration)
                    duration = minDuration;
            }
            return TimeSpan.FromSeconds(distance / velocity);
        }

        public override ValueTask MoveXY(double x, double y, bool relative, double? speed = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var options = _options.CurrentValue;
            var stepXYDistance = options.StepXYDistance;
            (var stepperX, var stepperY, var stepperL, var mcu) = GetXYL(manager, options);

            var maxVelocity = Math.Min(stepperX.MaxVelocity, stepperY.MaxVelocity);
            var criticalVelocity = Math.Min(stepperX.CriticalVelocity, stepperY.CriticalVelocity);
            var velocity = Math.Min(speed / 60 ?? maxVelocity, maxVelocity);
            if (velocity <= 0)
                throw new ArgumentOutOfRangeException(nameof(speed));

            var master = manager.LockMasterQueue();
            try
            {
                var rawStartX = _posX;
                var rawStartY = _posY;
                var rawEndX = relative ? rawStartX + x : x;
                var rawEndY = relative ? rawStartY + y : y;
                if (rawEndX < 0 || rawEndY < 0 ||
                    rawEndX > options.MaxXY || rawEndY > options.MaxXY)
                    throw new McuException($"Final position, X={rawEndX}, Y={rawEndY}, is out of range");

                // ensure we have moved at least one step on any of the axes
                var startSteps = XYToSteps(rawStartX, rawStartY, stepXYDistance);
                var endSteps = XYToSteps(rawEndX, rawEndY, stepXYDistance);
                if (startSteps != endSteps)
                {
                    // calc start and end rounded to a step
                    var startX = startSteps.x * stepXYDistance;
                    var startY = startSteps.y * stepXYDistance;
                    var endX = endSteps.x * stepXYDistance;
                    var endY = endSteps.y * stepXYDistance;
                    Debug.Assert(XYToSteps(startX, stepXYDistance) != XYToSteps(endX, stepXYDistance) || startX == endX);
                    Debug.Assert(XYToSteps(startY, stepXYDistance) != XYToSteps(endY, stepXYDistance) || startY == endY);
                    var dirX = endX - startX;
                    var dirY = endY - startY;
                    var dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);

                    MoveResetXYLInner(options, mcu, ref master, manager, context, out var startTime, out var startTimeOffset, cancel);
                    var duration = dirLen / velocity;
                    if (IsLaserOffByThisNeedsLock)
                    {
                        // delay extra fast movements with laser off, so we wont overload the MCU with too many commands
                        // NOTE: see similar code in GetMoveXYTime()!
                        var minDuration = options.LaserOffMinDurationSteps * stepXYDistance / criticalVelocity;
                        if (duration < minDuration)
                            duration = minDuration;
                    }
                    var endTime = startTime + duration;
                    var endTimeOffset = endTime.ToRelativeSeconds();

                    if (endTime == startTime || startTimeOffset == endTimeOffset) // may happen, if we are moving by some microscopic distance that happens to be at one step threshold
                    {
                        // just ignore the move then
                        // if we would place the value to compression queue, we would face issues since there would be two points at one x(time) axis position
                        // other non-zero values should not be a problem and will go away with compression
                    }
                    else
                    {
                        // update position and time
                        master[this] = endTime;
                        master[_xylQueue] = endTime;

                        // add steps to sources (both, to keep them alive)
                        _xylQueue.AddXY(startTimeOffset, startX, startY);
                        _xylQueue.AddXY(endTimeOffset, endX, endY);
                    }

                    // keep the laser queue alive
                    // NOTE: just put a point in laser queue somewhere where it wont hurt, compression will remove them later
                    var oneLaserCycle = stepperL.MinPwmCycleTime ?? 0; // NOTE: more that almostOneLaserCycsle
                    var laserKeepTime = endTimeOffset - oneLaserCycle;
                    if (laserKeepTime > _lastLaserTimeOffset && _lastLaserOnFactor >= 0)
                        _xylQueue.AddL(laserKeepTime, _lastLaserOnFactor);
                }

                // update as the last thing, reset needs to have the original value
                _posX = rawEndX;
                _posY = rawEndY;
            }
            finally
            {
                master.Dispose();
            }
            _xylQueue.ScheduleFlushOutsideMasterLock(context);

            return ValueTask.CompletedTask;
        }

        public override ValueTask SetLaser(double value, bool noCompensation = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            value = Math.Clamp(value, 0, 1);
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var options = _options.CurrentValue;
            (_, _, var stepperL, var mcu) = GetXYL(manager, options);

            McuTimestamp endTime;
            ValueTask result;
            var master = manager.LockMasterQueue();
            try
            {
                MoveResetXYLInner(options, mcu, ref master, manager, context, out endTime, out var endTimeOffset, cancel);

                if (value > 0) // turning on
                {
                    _lastLaserOnFactor = value;
                    var almostOneCycle = stepperL.MinPwmCycleTime * 0.999999;
                    if (!noCompensation && options.CompensatePwmLatency && almostOneCycle != null) // move to the past a bit to combat latency
                    {
                        _xylQueue.AddL(Math.Max(endTimeOffset - almostOneCycle.Value, _lastLaserTimeOffset), value);
                    }
                    else
                    {
                        _xylQueue.AddL(endTimeOffset, value);
                    }
                }
                else // turning off
                {
                    _lastLaserOnFactor = 0;
                    _xylQueue.AddL(endTimeOffset, value);
                }
                _lastLaserTimeOffset = endTimeOffset;
                _lastLaserTime = endTime.ToSystem();

                master[this] = endTime;
                master[_xylQueue] = endTime;

                // bit of a hack here, but we need to pass the updates to the UI. Timestamps will be all wrong.
                // we also use this timestamp to check whether last value writtern to powerClient has been by this class
                result = _powerClient.UpdatePowerDictWithNotify(options.LStepperName, value, _lastLaserTime, cancel);
            }
            finally
            {
                master.Dispose();
            }
            _xylQueue.ScheduleFlushOutsideMasterLock(context);
            return result;
        }

        protected override Position? TryGetPosition()
        {
            var manager = _printerClient.ManagerIfReady;
            if (manager == null)
                return null;
            using (manager.LockMasterQueue())
            {
                return new Position(_posX, _posY, _posZ1, _posZ2, _posR);
            }
        }

        public (TimeSpan QueueAheadDuration, TimeSpan SendAheadDuration) GetXYLQueueTimes(IPrinterClientCommandContext? context = null)
        {
            var manager = McuInitializeCommandContext.GetManagerEvenInShutdown(_printerClient, context);
            var options = _options.CurrentValue;
            (var stepperX, _, _, _) = GetXYL(manager, options);
            return (stepperX.QueueAheadDuration, stepperX.SendAheadDuration);
        }

        public override TimeSpan GetQueueAheadDuration(IPrinterClientCommandContext? context = null)
        {
            var manager = McuInitializeCommandContext.GetManagerEvenInShutdown(_printerClient, context);
            var options = _options.CurrentValue;
            (var stepperX, _, _, _) = GetXYL(manager, options);
            return stepperX.QueueAheadDuration;
        }

        public override double? TryGetMinLaserPwmCycleTime(IPrinterClientCommandContext? context = null)
        {
            var manager = McuInitializeCommandContext.GetManagerEvenInShutdown(_printerClient, context);
            var options = _options.CurrentValue;
            var stepperL = manager.Steppers[options.LStepperName];
            return stepperL.MinPwmCycleTime;
        }
    }
}
