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
using System.Runtime.ExceptionServices;

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
        public TimeSpan CollectGarbageMajorOnFinishMovementMinPeriod { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan XYLResetTimeoutWarning { get; set; } = TimeSpan.FromMinutes(1);
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
        private readonly object _auxKey = new();
        private readonly Stopwatch _sinceFinishMovementCollect;
        private readonly Stopwatch _sinceFinishMovementMajorCollect;
        private readonly AsyncLock _finishMovementLock;
        private double _lastLaserTimeOffset;
        private SystemTimestamp _lastLaserTime;
        private double _lastLaserOnFactor;
        private bool _shouldPeformMajorCleanup;
        private TimeSpan _totalPrintTimeDuration;

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
            McuPowerClient powerClient,
            IThreadStackTraceDumper stackTraceDumper)
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
                options.Transform(x => x.XYLQueue),
                printerClient,
                this,
                stackTraceDumper);
            _sinceFinishMovementCollect = new();
            _sinceFinishMovementMajorCollect = new();
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

        public override ValueTask Dwell(TimeSpan delay, bool includeAux, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
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
            var master = manager.EnterMasterQueueLock();
            try
            {
                MoveResetXYLInner(options, mcu, ref master, manager, context, out var startTime, out _, cancel);
                if (includeAux)
                    DwellAux(manager, options, startTime, seconds, context, cancel);
                var endTime = startTime + seconds;
                master[this] = endTime;
                master[_xylQueue] = endTime;
                if (includeAux)
                    master[_auxKey] = endTime;
            }
            finally
            {
                master.Dispose();
            }
            _xylQueue.ScheduleFlushOutsideMasterLock(context);
            return ValueTask.CompletedTask;
        }

        private void DwellAux(McuManager manager, McuMovementClientOptions options, McuTimestamp timestamp, double seconds, IPrinterClientCommandContext? context, CancellationToken cancel)
        {
            foreach ((var name, var stepper) in manager.Steppers)
            {
                if (name == options.XStepperName ||
                    name == options.YStepperName ||
                    name == options.LStepperName)
                    continue;
                if (stepper.GetResetNeccessary(timestamp))
                {
                    var precisionInternal = stepper.GetPrecisionIntervalFromSeconds(seconds);
                    stepper.QueueDwell(precisionInternal, timestamp);
                }
            }
        }

        private void GetRemainingPrintTimeOfKeyInner(
            LockMasterQueueDisposable master,
            object key,
            ref TimeSpan duration,
            ref SystemTimestamp timestamp,
            ref RemainingPrintTimeFlags flags,
            TimeSpan subtractDelay,
            RemainingPrintTimeFlags keyFlags,
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
                if (candidateDuration > TimeSpan.Zero)
                    flags |= keyFlags;
                if (candidateDuration > duration)
                    duration = candidateDuration;
                if (to > timestamp)
                    timestamp = to;
            }
        }

        private void TrimRemainingPrintTimeOfKeyInner(
            LockMasterQueueDisposable master,
            object key,
            McuTimestamp max)
        {
            var mcuTimestamp = master[key];
            if (!mcuTimestamp.IsEmpty && mcuTimestamp > max)
                master[key] = max;
        }

        public override ValueTask<RemainingPrintTime> GetRemainingPrintTime(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var manager = McuInitializeCommandContext.GetManagerEvenInShutdown(_printerClient, context);

            var duration = TimeSpan.Zero;
            var totalDuration = TimeSpan.Zero;
            var timestamp = SystemTimestamp.Now;
            var flags = RemainingPrintTimeFlags.NotSet;
            using (var master = manager.EnterMasterQueueLock())
            {
                GetRemainingPrintTimeOfAllInner(manager, options, master, ref duration, ref timestamp, ref flags);
                totalDuration = _totalPrintTimeDuration;
            }
            return ValueTask.FromResult(new RemainingPrintTime(totalDuration, duration, timestamp, flags));
        }

        private void GetRemainingPrintTimeOfAllInner(
            McuManager manager,
            McuMovementClientOptions options,
            LockMasterQueueDisposable master,
            ref TimeSpan duration, 
            ref SystemTimestamp timestamp, 
            ref RemainingPrintTimeFlags flags)
        {
            (var stepperX, var stepperY, var stepperL, _) = GetXYL(manager, options);
            GetRemainingPrintTimeOfKeyInner(master, this, ref duration, ref timestamp, ref flags, TimeSpan.Zero, RemainingPrintTimeFlags.NotSet, out _);
            GetRemainingPrintTimeOfKeyInner(master, stepperX, ref duration, ref timestamp, ref flags, TimeSpan.Zero, RemainingPrintTimeFlags.XYL, out _);
            GetRemainingPrintTimeOfKeyInner(master, stepperY, ref duration, ref timestamp, ref flags, TimeSpan.Zero, RemainingPrintTimeFlags.XYL, out _);
            GetRemainingPrintTimeOfKeyInner(master, stepperL, ref duration, ref timestamp, ref flags, TimeSpan.Zero, RemainingPrintTimeFlags.XYL, out _);
            GetRemainingPrintTimeOfKeyInner(master, _xylQueue, ref duration, ref timestamp, ref flags, TimeSpan.Zero, RemainingPrintTimeFlags.XYL, out _);
            GetRemainingPrintTimeOfKeyInner(master, _auxKey, ref duration, ref timestamp, ref flags, TimeSpan.Zero, RemainingPrintTimeFlags.Motors, out _);
            if (duration == TimeSpan.Zero)
                _totalPrintTimeDuration = TimeSpan.Zero;
            else if (duration > _totalPrintTimeDuration)
                _totalPrintTimeDuration = duration;
        }

        public override async Task FinishMovement(bool performMajorCleanup = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            _logger.LogDebug($"Finish movement - begin");
            using (await _finishMovementLock.LockAsync(cancel))
            {
                var options = _options.CurrentValue;
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                (var stepperX, var stepperY, var stepperL, _) = GetXYL(manager, options);

                var shouldCollectGarbage = performMajorCleanup;
                var totalRawDuration = TimeSpan.Zero;
                var loops = 0;
                _shouldPeformMajorCleanup |= performMajorCleanup;
                _sinceFinishMovementMajorCollect.Start();
                if (_sinceFinishMovementMajorCollect.Elapsed > options.CollectGarbageMajorOnFinishMovementMinPeriod)
                    _shouldPeformMajorCleanup = true;

                for (; ; loops++)
                {
                    // flush all moves synchronously
                    _xylQueue.FlushAllAndWaitOutsideMasterLock(context, cancel: cancel);

                    // get duration from all possible steppers to ensure all movement will be stopped after wait
                    var duration = TimeSpan.Zero;
                    var timestamp = SystemTimestamp.Now;
                    var flags = RemainingPrintTimeFlags.NotSet;
                    using (_xylQueue.EnterFlushLock()) // ensure we are not currently flusing, timestamps would be changing
                    {
                        using (var master = manager.EnterMasterQueueLock())
                        {
                            if (options.CollectGarbageOnFinishMovement &&
                                (!_sinceFinishMovementCollect.IsRunning ||
                                 _sinceFinishMovementCollect.Elapsed >= options.CollectGarbageOnFinishMovementMinPeriod))
                            {
                                shouldCollectGarbage = true;
                            }

                            GetRemainingPrintTimeOfAllInner(manager, options, master, ref duration, ref timestamp, ref flags);
                            var queuesEmpty = _xylQueue.IsPseudoEmpty;
                            if (loops > 0 && duration == TimeSpan.Zero && queuesEmpty)
                            {
                                // force stepper reset
                                master[this] = default;
                                master[stepperX] = default;
                                master[stepperY] = default;
                                master[stepperL] = default;
                                master[_xylQueue] = default;
                                master[_auxKey] = default;

                                // done
                                break;
                            }
                        }
                    }

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
                    _logger.LogDebug($"Finish movement - end. HasCollected = {hasCollected}, MajorCleanup = {_shouldPeformMajorCleanup}, Raw movement duration = {totalRawDuration}. Loops = {loops}. GC duration = {start.ElapsedFromNow}. SinceCollect={_sinceFinishMovementCollect.Elapsed}. CreatedCommands={manager.CreatedCommands}, CreatedArenas={manager.CreatedArenas}");
                    if (hasCollected)
                    {
                        _sinceFinishMovementCollect.Restart();
                        if (_shouldPeformMajorCleanup)
                        {
                            _sinceFinishMovementMajorCollect.Restart();
                            _shouldPeformMajorCleanup = false;
                        }
                    }
                }
                else
                    _logger.LogDebug($"Finish movement - end. Raw movement duration = {totalRawDuration}. Loops = {loops}. HasTimingCriticalCommandsScheduled={hasTimingCriticalCommandsScheduled}. ShouldCollectGarbage={shouldCollectGarbage}. SinceCollect={_sinceFinishMovementCollect.Elapsed}");
                PrinterGC.LogCollectionCount(_logger);
            }
        }

        public override async Task StopAndFinishMovement(IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            _logger.LogDebug($"Stop movement");

            using (await _finishMovementLock.LockAsync(cancel))
            {
                var options = _options.CurrentValue;
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                (var stepperX, var stepperY, var stepperL, var mcu) = GetXYL(manager, options);

                using (_xylQueue.EnterFlushLock()) // ensure we are not currently flusing, timestamps would be changing
                {
                    using (var master = manager.EnterMasterQueueLock())
                    {
                        // stop all queued moves
                        _logger.LogDebug($"Sending {nameof(mcu.MovementCancel)}() to mcu");
                        _xylQueue.Clear();
                        var cancelTimestamp = mcu.MovementCancel();
                        var trimTimestamp = cancelTimestamp;
                        var soonestTimestamp = McuTimestamp.FromSystem(mcu, SystemTimestamp.Now);
                        if (trimTimestamp.IsEmpty || trimTimestamp < soonestTimestamp)
                            trimTimestamp = soonestTimestamp;
                        trimTimestamp += stepperX.SendAheadDuration;

                        _logger.LogDebug($"Updating timestamps after {nameof(mcu.MovementCancel)}() to: {trimTimestamp} ({nameof(soonestTimestamp)} = {soonestTimestamp}, {nameof(cancelTimestamp)}={cancelTimestamp})");
                        TrimRemainingPrintTimeOfKeyInner(master, this, trimTimestamp);
                        TrimRemainingPrintTimeOfKeyInner(master, stepperX, trimTimestamp);
                        TrimRemainingPrintTimeOfKeyInner(master, stepperY, trimTimestamp);
                        TrimRemainingPrintTimeOfKeyInner(master, stepperL, trimTimestamp);
                        TrimRemainingPrintTimeOfKeyInner(master, _xylQueue, trimTimestamp);
                        TrimRemainingPrintTimeOfKeyInner(master, _auxKey, trimTimestamp);

                        _lastLaserTime = default;
                        _lastLaserTimeOffset = 0;
                        _lastLaserOnFactor = 0;

                        foreach (var stepper in manager.Steppers.Values)
                            stepper.OnAfterStopMovement(); // clears _lastClock, non-flushed commands, forces next reset
                    }
                }
            }

            await FinishMovement(context: context, cancel: cancel);
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
                using (var master = manager.EnterMasterQueueLock())
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
                using (var master = manager.EnterMasterQueueLock())
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
                using (var master = manager.EnterMasterQueueLock())
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

                using (_xylQueue.EnterFlushLock()) // ensure we are not currently flushing, timestamps would be changing
                {
                    using (var master = manager.EnterMasterQueueLock())
                    {
                        // NOTE: Reset DACs in case something went wrong in SPI communication.
                        //       This will not save the print if that happened, but it is mainly a safety feature to ensure the galvos work in future layers
                        //       and dont target the laser hotspot to the same position until printer power-off or firmware restart.
                        var timestampX = stepperX.DacResetAndHome(master[stepperX], mid, mid);
                        _posX = mid;
                        master[stepperX] = timestampX;

                        var timestampY = stepperY.DacResetAndHome(master[stepperY], mid, mid);
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
            using (var master = manager.EnterMasterQueueLock())
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

                if (startPos != endPos)
                {
                    timestamp = stepper.Move(
                        item.InitialSpeed ?? 0,
                        item.FinalSpeed ?? 0,
                        item.Acceleration ?? 0,
                        item.Decceleration ?? item.Acceleration ?? 0,
                        item.Speed ?? stepper.MaxVelocity,
                        startPos,
                        endPos,
                        timestamp);
                }

                if (item.Dwell > TimeSpan.Zero)
                    timestamp = stepper.QueueDwell(stepper.GetPrecisionIntervalFromSeconds(item.Dwell.TotalSeconds), timestamp);

                master[this] = timestamp;
                master[_auxKey] = timestamp;
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
                using (var master = manager.EnterMasterQueueLock())
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
            var xylTimestamp = master[_xylQueue];
            timestamp = master[this];

            var lowNow = now + manager.StepperQueueLow;
            if (timestamp.IsEmpty || timestamp.ToSystem() < lowNow ||
                xylTimestamp.IsEmpty || xylTimestamp.ToSystem() < lowNow)
            {
                _logger.LogInformation(
                    $"Resetting X/Y/L, flushing existing data. " +
                    $"This should happen only when a batch is starting, otherwise it is an indication that feeding or processing is too slow at times. " +
                    $"Was Timestamp={timestamp.Clock}, Now={McuTimestamp.FromSystem(mcu, now).Clock}, QueueLow={manager.StepperQueueLow}, QueueHigh={manager.StepperQueueHigh}");

                var xySteps = XYToSteps(_posX, _posY);
                var posX = xySteps.x * options.StepXYDistance;
                var posY = xySteps.y * options.StepXYDistance;

                // loop until master queue is empty
                var loopStartTimestamp = SystemTimestamp.Now;
                while (!_xylQueue.IsPseudoEmpty)
                {
                    master.Dispose();
                    cancel.ThrowIfCancellationRequested();
                    _xylQueue.FlushAllAndWaitOutsideMasterLock(context, cancel: cancel); // NOTE: need to flush right now on this thread to empty the Source collections, reset needs to be first
                    master = manager.EnterMasterQueueLock();
                    if (!loopStartTimestamp.IsEmpty && loopStartTimestamp.ElapsedFromNow > options.XYLResetTimeoutWarning)
                    {
                        loopStartTimestamp = default;
                        _logger.LogWarning("Resetting X/Y/L, reset loop is taking longer than expected");
                    }
                }

                // set new timestamp
                now = SystemTimestamp.Now;
                var newTimestamp = McuTimestamp.FromSystem(mcu, now + manager.StepperQueueHigh);
                if (!xylTimestamp.IsEmpty && xylTimestamp > newTimestamp)
                    newTimestamp = xylTimestamp;
                if (!timestamp.IsEmpty && timestamp > newTimestamp)
                    newTimestamp = timestamp;
                var dwelled = !timestamp.IsEmpty ? _xylQueue.ToTime(newTimestamp) - _xylQueue.ToTime(timestamp) : 0;
                _logger.LogInformation(
                    $"Resetting X/Y/L to {newTimestamp.Clock}, flushed existing data. " +
                    $"Was Timestamp={timestamp.Clock}, Now={McuTimestamp.FromSystem(mcu, now).Clock}, QueueLow={manager.StepperQueueLow}, QueueHigh={manager.StepperQueueHigh}, Dwelled={dwelled}");
                timestamp = newTimestamp;
                offset = _xylQueue.ToTime(timestamp);

                _xylQueue.AddReset(offset, posX, posY);
                _lastLaserOnFactor = 0;

                _lastLaserTimeOffset = offset;
                _lastLaserTime = timestamp.ToSystem();

                // NOTE: if PWM compensation is enabled, move next command after reset two (more than one) PWM periods into the future.
                //       This ensures that any first compensated move will have enough space to move back to.
                var stepperL = manager.Steppers[options.LStepperName];
                if (options.CompensatePwmLatency && stepperL.MinPwmCycleTime != null)
                {
                    timestamp += stepperL.MinPwmCycleTime.Value * 2;
                    offset = _xylQueue.ToTime(timestamp);
                }

                master[this] = timestamp;
                master[_xylQueue] = timestamp;

                _logger.LogDebug($"X/Y/L reset complete");
            }
            else
                offset = _xylQueue.ToTime(timestamp);
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

        public override ValueTask MoveXY(double x, double y, bool relative, double? speed = null, bool clamp = false, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
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

            var master = manager.EnterMasterQueueLock();
            try
            {
                var rawStartX = _posX;
                var rawStartY = _posY;
                var rawEndX = relative ? rawStartX + x : x;
                var rawEndY = relative ? rawStartY + y : y;
                if (rawEndX < 0 || rawEndY < 0 ||
                    rawEndX > options.MaxXY || rawEndY > options.MaxXY)
                {
                    if (clamp)
                    {
                        rawEndX = Math.Clamp(rawEndX, 0, options.MaxXY);
                        rawEndY = Math.Clamp(rawEndY, 0, options.MaxXY);
                    }
                    else
                        throw new McuException($"Final position, X={rawEndX}, Y={rawEndY}, is out of range");
                }

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
                    var endTimeOffset = _xylQueue.ToTime(endTime);

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
                    if (laserKeepTime > _lastLaserTimeOffset)
                    {
                        _lastLaserTimeOffset = laserKeepTime;
                        _xylQueue.AddL(laserKeepTime, _lastLaserOnFactor);
                    }
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
            var master = manager.EnterMasterQueueLock();
            try
            {
                MoveResetXYLInner(options, mcu, ref master, manager, context, out endTime, out var endTimeOffset, cancel);

                double nextLaserTimeOffset;
                if (value > 0) // turning on
                {
                    _lastLaserOnFactor = value;
                    var almostOneCycle = stepperL.MinPwmCycleTime * 0.999;
                    if (!noCompensation && options.CompensatePwmLatency && almostOneCycle != null) // move to the past a bit to combat latency
                        nextLaserTimeOffset = Math.Max(endTimeOffset - almostOneCycle.Value, _lastLaserTimeOffset);
                    else
                        nextLaserTimeOffset = Math.Max(endTimeOffset, _lastLaserTimeOffset);
                }
                else // turning off
                {
                    _lastLaserOnFactor = 0;
                    nextLaserTimeOffset = Math.Max(endTimeOffset, _lastLaserTimeOffset);
                }
                _xylQueue.AddL(nextLaserTimeOffset, value);
                _lastLaserTimeOffset = nextLaserTimeOffset;
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
            using (manager.EnterMasterQueueLock())
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
