// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
using Lexical.FileSystem.Decoration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Helpers
{
    public class XYLMovementQueueOptions
    {
        /// <summary>
        /// Time to buffer data before step compression and sending it to the X/Y/L steppers.
        /// Should not be larger than <see cref="McuStepperOptions.QueueAheadDuration"/> to avoid the steppers resetting before new data arrives
        /// </summary>
        public TimeSpan CompressFlushPeriod { get; set; } = TimeSpan.FromSeconds(0.25);
        /// <summary>
        /// Maximum number of sent instructions per X/Y produced by step compression. Lower the value the lesser is the chance of MCU timer errors, but
        /// at the cost of simplification of the layer moves.
        /// Magnitude of this value is mainly limited by CPU processing power of used MCU and bandwidth between it and the host.
        /// If the value is too high, MCU timer errors will occur during basic things like fills of a large area of a single object.
        /// </summary>
        public double XYMovesPerSecond { get; set; } = 500;
        /// <summary>
        /// Maximum number of sent instructions for L (laser PWM) produced by step compression. Lower the value the lesser is the chance of MCU timer errors, but
        /// at the cost of simplification of the power transitions.
        /// Magnitude of this value is mainly limited by CPU processing power of used MCU and bandwidth between it and the host. 
        /// If the value is too high, MCU timer errors will occur during basic things like fills of a large area of a single object.
        /// </summary>
        public double LMovesPerSecond { get; set; } = 500;
        /// <summary>
        /// Inncreasing this value induces an effect of compression on slower printing speeds, where it would not be visible. Setting this to "2" will
        /// make the compression behave as the if movement speed is 2x faster.
        /// </summary>
        public double CompressionFactor { get; set; } = 1.0;
        /// <summary>
        /// Minumum difference beween previous laser power factor [0..1] to actually change the laser PWM power
        /// </summary>
        public double MinLaserFactorDifference { get; set; } = 0.01;
        /// <summary>
        /// Flush all 3 axis in parallel, improves flush duration at a cost of high CPU usage.
        /// </summary>
        public bool ParallelFlush { get; set; } = false;
        /// <summary>
        /// Completely disables compression (development only!)
        /// </summary>
        public bool DisableCompression { get; set; } = false;
    }

    public sealed class XYLMovementQueue
    {
        private enum StepperType
        {
            NotSet = 0,
            X,
            Y,
            L,
        }

        private readonly record struct FlushArgs(bool FlushAll, IPrinterClientCommandContext? Context);

        private struct FlushingDataContainer
        {
            public readonly XYLMovementCompress Compress;
            public readonly PrimitiveList<XYLMovementPoint> Intermediate;
            public readonly PrimitiveList<XYLMovementPoint> Compressed;
            public double PrecisionRemainder;
            public double MovesRemainder;
            public double Time;
            public double Value;
            public readonly PrimitiveList<XYLMovementPoint> PrevExecuted;
            public McuTimestamp PrevExecutedInitialTimestamp;

            public FlushingDataContainer()
            {
                Compress = new();
                Intermediate = new();
                Compressed = new();
                PrevExecuted = new();
            }
        }

        private sealed class StepperState
        {
            private FlushingDataContainer _flushingData;

            public readonly StepperType Type;
            /// <remarks>
            /// Must be accesed only in master lock.
            /// </remarks>
            public double LastFlushStartTime;
            /// <summary>
            /// Source data that will be flushed.
            /// </summary>
            /// <remarks>
            /// Must be accesed only in master lock.
            /// </remarks>
            public readonly PrimitiveList<XYLMovementPoint> Source;
            /// <summary>
            /// Internal state for flushing.
            /// </summary>
            /// <remarks>
            /// Access only in flushing code in separate thread!
            /// </remarks>
            public ref FlushingDataContainer FlushingData
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    Debug.Assert(Thread.CurrentThread.Name?.Contains("[_XYLFlushing_]") == true, $"FlushingData accessed from other than dedicated thread. Name={Thread.CurrentThread.Name}");
                    return ref _flushingData;
                }
            }
            public int Discriminator => (int)Type;

            /// <summary>
            /// Returns true if the source list is empty or contains just a reset
            /// </summary>
            /// <remarks>
            /// Must be accesed only in master lock.
            /// </remarks>
            public bool IsPseudoEmpty
            {
                get
                {
                    var span = Source.Span;
                    if (span.Length == 0 ||
                        (span.Length == 1 && span[0].IsReset))
                        return true;
                    else
                        return false;
                }
            }

            /// <summary>
            /// Returns true if the source list is empty
            /// </summary>
            /// <remarks>
            /// Must be accesed only in master lock.
            /// </remarks>
            public bool IsEmpty
            {
                get
                {
                    var span = Source.Span;
                    return span.Length == 0;
                }
            }

            /// <remarks>
            /// Must be accesed only in master lock.
            /// </remarks>
            public bool HasSources => Source.Count != 0;

            public StepperState(StepperType type)
            {
                Type = type;
                Source = new();
                _flushingData = new();
            }

            public bool GetIsFull(TimeSpan compressFlushPeriod)
            {
                var source = Source;
                if (source.Count == 0)
                    return false;
                double elapsed;
                ref var first = ref source[0];
                elapsed = source[^1].Time - first.Time;
                Debug.Assert(elapsed >= 0);
                if (elapsed >= compressFlushPeriod.TotalSeconds)
                    return true;
                if (!first.IsReset)
                {
                    elapsed = first.Time - LastFlushStartTime;
                    if (elapsed >= compressFlushPeriod.TotalSeconds)
                        return true;
                }
                return false;
            }

            public void AddReset(double time, double value)
            {
                Debug.Assert(Source.Count == 0);
                if (time < 0)
                    throw new ArgumentOutOfRangeException(nameof(time));
                Source.Add() = new XYLMovementPoint(-time, value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSource(in XYLMovementPoint item)
            {
                if (Source.Count > 0)
                {
                    ref var last = ref Source[^1];
                    if (last == item)
                        return;
                    Debug.Assert(item.Time >= last.Time);
                }
                Source.Add() = item;
            }

            public void GetFallbackTime(ref double fallbackTime)
            {
                if (FlushingData.Intermediate.Count > 0)
                {
                    var value = FlushingData.Intermediate[0].Time;
                    if (value < fallbackTime)
                        fallbackTime = value;
                }
            }
        }

        private static readonly object _trueBox = true;
        private static readonly object _falseBox = true;
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<XYLMovementQueueOptions> _options;
        private readonly McuPrinterClient _printerClient;
        private readonly McuMovementClient _movementClient;
        private readonly object _flushLock = new();
        private readonly Timer _flushTimer;
        private readonly PriorityScheduler _flushScheduler1;
        private readonly PriorityScheduler _flushScheduler2;
        private readonly Action<object?> _flushXYLOnlyOnDedicatedThread;
        private IPrinterClientCommandContext? _timerFlushContext;
        private TimeSpan _flushAfterNeedsLocks;
        private StepperState _stateX;
        private StepperState _stateY;
        private StepperState _stateL;

        /// <summary>
        /// Gets synchronization object acquired while flushing. Master lock cannot be held when locking <see cref="FlushLock"/>!
        /// While in flush lock, the XYL steppers timestamps might be updated.
        /// </summary>
        public object FlushLock => _flushLock;
        /// <summary>
        /// If flush is scheduled, this is maxiumum time after it will happen. Otherwise set to <see cref="TimeSpan.MaxValue"/>.
        /// Read in flush and then master lock.
        /// </summary>
        public TimeSpan FlushAfterNeedsLocks => _flushAfterNeedsLocks;

        /// <remarks>
        /// Must be accesed only in master lock.
        /// </remarks>
        public bool IsPseudoEmpty
            => _stateX.IsPseudoEmpty && _stateY.IsPseudoEmpty && _stateL.IsPseudoEmpty;

        /// <remarks>
        /// Must be accesed only in master lock.
        /// </remarks>
        public bool IsEmpty
            => _stateX.IsEmpty && _stateY.IsEmpty && _stateL.IsEmpty;

        public XYLMovementQueue(
            ILogger logger,
            IOptionsMonitor<XYLMovementQueueOptions> options,
            McuPrinterClient printerClient,
            McuMovementClient movementClient)
        {
            _logger = logger;
            _options = options;
            _printerClient = printerClient;
            _movementClient = movementClient;
            _stateX = new(StepperType.X);
            _stateY = new(StepperType.Y);
            _stateL = new(StepperType.L);
            _flushTimer = new(OnFlushTimer);
            _flushAfterNeedsLocks = TimeSpan.MaxValue;
            _flushXYLOnlyOnDedicatedThread = state =>
            {
                if (state == null)
                    FlushXYLOnlyOnDedicatedThread(false, null);
                else if (state is IPrinterClientCommandContext context)
                    FlushXYLOnlyOnDedicatedThread(false, context);
                else
                {
                    var args = (FlushArgs)state;
                    FlushXYLOnlyOnDedicatedThread(args.FlushAll, args.Context);
                }
            };
            // compress and flush on own scheduler, to ensure the tasks wont compete with other
            _flushScheduler1 = new PriorityScheduler(
                $"{nameof(XYLMovementQueue)} Dedicated [_XYLFlushing_], thread {{0}}",
                ThreadPriority.AboveNormal,
                1 /* NOTE: neccessary to keep "1"!, see SynchronousFlush()*/ );
            _flushScheduler2 = new PriorityScheduler(
                $"{nameof(XYLMovementQueue)} XYL [_XYLFlushing_], thread {{0}}",
                ThreadPriority.AboveNormal,
                3 /* x + y + l */);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddXY(double time, double x, double y)
        {
            _stateX.AddSource(new XYLMovementPoint(time, x));
            _stateY.AddSource(new XYLMovementPoint(time, y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddL(double time, double pwm)
        {
            _stateL.AddSource(new XYLMovementPoint(time, pwm));
        }

        public void AddReset(double time, double posX, double posY, double dwelled = 0)
        {
            _stateX.Source.Clear();
            _stateY.Source.Clear();
            _stateL.Source.Clear();
            var before = time - dwelled;
            _stateX.AddReset(before, posX);
            _stateY.AddReset(before, posY);
            _stateL.AddReset(before, 0);
            if (dwelled != 0)
            {
                AddXY(time, posX, posY);
                AddL(time, 0);
            }
        }

        private bool ScheduleFlushInner(
            McuManager manager,
            bool forceImmediateFlushAll, 
            bool noImmediateInsteadReturnTrue,
            XYLMovementQueueOptions options,
            IPrinterClientCommandContext? context)
        {
            if (_flushAfterNeedsLocks != TimeSpan.Zero) // not already immediate
            {
                var immediateFlushAll = forceImmediateFlushAll;
                if (!immediateFlushAll)
                {
                    using (var master = manager.LockMasterQueue())
                    {
                        if (_stateX.GetIsFull(options.CompressFlushPeriod) ||
                            _stateY.GetIsFull(options.CompressFlushPeriod) ||
                            _stateL.GetIsFull(options.CompressFlushPeriod))
                            immediateFlushAll = true;
                    }
                }

                if (immediateFlushAll)
                {
                    if (noImmediateInsteadReturnTrue)
                    {
                        _timerFlushContext = context;
                        if (_flushAfterNeedsLocks != TimeSpan.MaxValue)
                        {
                            _flushAfterNeedsLocks = TimeSpan.MaxValue;
                            _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                        }
                        return true;
                    }
                    else
                    {
                        _flushAfterNeedsLocks = TimeSpan.Zero;
                        _timerFlushContext = null;
                        ScheduleCompressFlushImmediately(forceImmediateFlushAll, context);
                    }
                    return false;
                }
            }

            if (_flushAfterNeedsLocks == TimeSpan.MaxValue) // not yet scheduled
            {
                if (_stateX.HasSources ||
                    _stateY.HasSources ||
                    _stateL.HasSources)
                {
                    _flushAfterNeedsLocks = options.CompressFlushPeriod;
                    _timerFlushContext = context;
                    _flushTimer.Change(_flushAfterNeedsLocks, Timeout.InfiniteTimeSpan);
                    return false;
                }
            }
            return false;
        }

        /// <remarks>
        /// Needs to be called outside master lock to prevent deadlock (master lock / flushLock)
        /// </remarks>
        public void SynchronousFlushOutsideMasterLock(IPrinterClientCommandContext? context)
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            Debug.Assert(!manager.IsMasterQueueLocked());
            lock (_flushLock)
            {
                var options = _options.CurrentValue;
                ScheduleFlushInner(manager, true, false, options, context);
            }
            // NOTE: following call ensures that the flush scheduler has finished running all preceding tasks (since it is for exactly one parallel task)
            Task.Factory.StartNew(
                _ => { },
                null,
                default,
                TaskCreationOptions.None,
                _flushScheduler1)
                .Wait();
        }

        /// <remarks>
        /// Needs to be called outside master lock to prevent deadlock (master lock / flushLock)
        /// </remarks>
        public void ScheduleFlushOutsideMasterLock(IPrinterClientCommandContext? context)
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            Debug.Assert(!manager.IsMasterQueueLocked());
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(_flushLock, ref lockTaken);
                if (!lockTaken) // if we are currently flushing, the flush method will automatically reschedule/process any sources remainder
                    return;
                var options = _options.CurrentValue;
                ScheduleFlushInner(manager, false, false, options, context);
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(_flushLock);
            }
        }

        private void ScheduleCompressFlushImmediately(bool flushAll, IPrinterClientCommandContext? context)
        {
            Task.Factory.StartNew(
                _flushXYLOnlyOnDedicatedThread,
                flushAll == false && context == null
                    ? null
                    : (flushAll == false && context != null
                       ? context
                       : new FlushArgs(flushAll, context)), 
                default, 
                TaskCreationOptions.None, 
                _flushScheduler1);
        }

        private void OnFlushTimer(object? state)
            => ScheduleCompressFlushImmediately(false, _timerFlushContext);

        private static void QueueDwellFlushingInner(
            IMcuStepper stepper,
            ref McuTimestamp timestamp,
            StepperState state,
            double elapsed,
            SystemTimestamp now,
            McuMinClockFunc? minClock,
            bool dryRun)
        {
            var pticksf = stepper.GetPrecisionIntervalFromSecondsDouble(elapsed) + state.FlushingData.PrecisionRemainder;
            var pticks = (long)Math.Round(pticksf);
            if (pticks <= 0)
            {
                state.FlushingData.PrecisionRemainder = pticksf;
            }
            else
            {
                state.FlushingData.PrecisionRemainder = pticksf - pticks;
                timestamp = stepper.QueueDwell(
                    pticks,
                    timestamp,
                    now: now,
                    minClock: minClock,
                    dryRun: dryRun);
            }
        }

        private void MoveXYFlushingInner(
            IMcuStepper stepper,
            double posEnd,
            double timeEnd,
            ref McuTimestamp timestamp,
            StepperState state,
            SystemTimestamp now,
            McuMinClockFunc? minClock,
            bool dryRun)
        {
            var posStart = state.FlushingData.Value;
            var timeStart = state.FlushingData.Time;
            var elapsed = timeEnd - timeStart;
            Debug.Assert(elapsed >= 0);

            if (posStart == posEnd)
            {
                QueueDwellFlushingInner(stepper, ref timestamp, state, elapsed, now, minClock, dryRun);
                state.FlushingData.Time = timeEnd;
            }
            else if (elapsed > 0)
            {
                var velocity = Math.Abs(posEnd - posStart) / elapsed;
                var steps = stepper.GetSteps(velocity, posStart, posEnd, state.FlushingData.PrecisionRemainder);
                if (steps.Count == 0)
                {
                    QueueDwellFlushingInner(stepper, ref timestamp, state, elapsed, now, minClock, dryRun);
                }
                else
                {
                    var positive = posEnd > posStart;
                    timestamp = stepper.QueueStep(
                        positive,
                        steps.PrecisionInterval,
                        steps.Count,
                        0,
                        timestamp,
                        now: now,
                        minClock: minClock,
                        dryRun: dryRun);

                    state.FlushingData.PrecisionRemainder = steps.PrecisionRemainder;
                    state.FlushingData.Value = steps.FinalPosition;
                    state.FlushingData.Time = timeEnd;
                }
            }
            else
            {
#if DEBUG
                var start = stepper.GetSteps(posStart);
                var end = stepper.GetSteps(posEnd);
                Debug.Assert(start.Count == end.Count);
#endif
            }
        }

        private void SetLaserFlushingInner(
            IMcuStepper stepper,
            double value,
            double time,
            ref McuTimestamp timestamp,
            StepperState state,
            SystemTimestamp now,
            McuMinClockFunc? minClock,
            bool dryRun)
        {
            var elapsed = time - state.FlushingData.Time;
            Debug.Assert(elapsed >= 0);

            QueueDwellFlushingInner(stepper, ref timestamp, state, elapsed, now, minClock, dryRun);

            Debug.Assert(value != double.MaxValue);
            timestamp = stepper.QueuePwm(
                (float)value,
                timestamp,
                now: now,
                minClock: minClock,
                dryRun: dryRun);

            state.FlushingData.Time = time;
        }

        private void PrepareCompressXYLFlushingInner(
            StepperState state,
            XYLMovementQueueOptions options)
        {
            var source = state.Source.Span;
            state.FlushingData.Intermediate.Clear();

            if (source.Length == 0) // not enough data to further process
                return;

            ref var first = ref source[0];
            state.LastFlushStartTime = first.Time;
            if (source.Length >= 2) // check if data is not too long, if at least two points (including possible reset)
            {
                ref var last = ref source[^1];
                var maxSeconds = options.CompressFlushPeriod.TotalSeconds;
                if (last.Time - first.Time > maxSeconds) // data is too long and might cause performance issues when compressing
                {
                    // get a chunk of data that has at most CompressFlushPeriod of duration
                    for (int i = source.Length - 2; i >= 1; i--)
                    {
                        ref var item = ref source[i];
                        if (item.Time - first.Time <= maxSeconds)
                        {
                            var consumedCount = i + 1;
                            state.FlushingData.Intermediate.AddRange(source.Slice(0, consumedCount));
                            state.Source.RemoveFromBeginning(consumedCount);
                            Debug.Assert(!state.IsPseudoEmpty);
                            return;
                        }
                    }
                }
            }

            // fallback, copy all
            state.FlushingData.Intermediate.AddRange(source);
            state.Source.Clear();
        }

        private void CompressXYLFlushingInner(
            IMcuStepper stepper,
            XYLMovementQueueOptions options,
            StepperState state)
        {
            var intermediate = state.FlushingData.Intermediate.Span;
            var compressed = state.FlushingData.Compressed;

            compressed.Clear();
            if (intermediate.Length == 0)
                return;

            ref var first = ref intermediate[0];
            var firstIsReset = first.IsReset;
            if (firstIsReset) // begins with reset (only valid reset position is at the start)
            {
                compressed.Add() = first;
                intermediate = intermediate.Slice(1);
            }

            if (options.DisableCompression)
            {
                compressed.AddRange(intermediate);
            }
            else if (intermediate.Length > 0)
            {
                var movesPerSecond = state.Type switch
                {
                    StepperType.X or StepperType.Y => options.XYMovesPerSecond,
                    StepperType.L => options.LMovesPerSecond,
                    _ => throw new InvalidOperationException($"Invalid stepper type {state.Type}"),
                };
                var elapsed = intermediate[^1].Time - intermediate[0].Time;
                var maxMovesDouble =
                    movesPerSecond * elapsed / options.CompressionFactor +
                    state.FlushingData.MovesRemainder;
                var maxMoves = (int)maxMovesDouble;
                var movesRemainder = maxMovesDouble - maxMoves;
                state.FlushingData.MovesRemainder = movesRemainder;

                if (state.Type != StepperType.L)
                {
                    // make epsilon 99% of one step distance
                    var minEpsilon = _movementClient.StepXYDistance * 0.99;
                    state.FlushingData.Compress.CompressMoves(
                        intermediate,
                        compressed,
                        maxMoves, minEpsilon);
                }
                else
                {
                    // minEpsilon is 99% of PWM period
                    var minEpsilon = stepper.MinPwmCycleTime!.Value * 0.99;
                    state.FlushingData.Compress.CompressPwm(intermediate, compressed, maxMoves, minEpsilon, options.MinLaserFactorDifference);
                }
            }

            if (compressed.Count == 0)
                throw new InvalidOperationException($"Compression error, there should be at least one item left for {stepper}");
        }

        private void ExecuteXYLInnerDryOrReal(
            IMcuStepper stepper,
            ref McuTimestamp timestamp,
            StepperState state,
            SystemTimestamp now,
            Span<XYLMovementPoint> compressed,
            McuMinClockFunc? minClock,
            bool dryRun)
        {
            for (var i = 0; i < compressed.Length; i++)
            {
                ref var item = ref compressed[i];
                Debug.Assert(!item.IsReset);

                // process item
                if (state.Type == StepperType.L)
                {
                    SetLaserFlushingInner(
                        stepper,
                        item.Value,
                        item.Time,
                        ref timestamp,
                        state,
                        now,
                        minClock,
                        dryRun);
                }
                else
                {
                    MoveXYFlushingInner(
                        stepper,
                        item.Value,
                        item.Time,
                        ref timestamp,
                        state,
                        now,
                        minClock,
                        dryRun);
                }
            }
        }

        private void ExecuteXYLFlushingInner(
            IMcuStepper stepper,
            ref McuTimestamp timestamp,
            StepperState state,
            SystemTimestamp now,
            double fallbackTime,
            TimeSpan minQueueAheadDuration)
        {
            var initialTimestamp = timestamp;
            var initialCompressed = state.FlushingData.Compressed.Span;
            var compressed = initialCompressed;
            var initialTime = state.FlushingData.Time;
            var initialValue = state.FlushingData.Value;
            try
            {
                if (compressed.Length > 0) // data present
                {
                    ref var first = ref compressed[0];
                    if (first.IsReset) // reset indicator at the start
                    {
                        state.FlushingData.PrecisionRemainder = 0;
                        state.FlushingData.MovesRemainder = 0;
                        state.FlushingData.Time = first.Time;
                        state.FlushingData.Value = first.Value;
                        compressed = compressed.Slice(1);

                        // reset stepper
                        timestamp = stepper.Reset(timestamp, McuStepperResetFlags.Force, out _, now: now, minQueueAheadDuration: minQueueAheadDuration);
                    }
                    else
                    {
                        timestamp = stepper.Reset(timestamp, McuStepperResetFlags.ThrowIfResetNecessary, out _, now: now);
                    }

                    // execute
                    ExecuteXYLInnerDryOrReal(stepper, ref timestamp, state, now, compressed, null, dryRun: false);
                }
                else if (fallbackTime > state.FlushingData.Time) // no data, just try to move time in the stepper a bit
                {
                    var elapsed = fallbackTime - state.FlushingData.Time;
                    QueueDwellFlushingInner(
                        stepper,
                        ref timestamp,
                        state,
                        elapsed,
                        now,
                        null,
                        false);
                }
                else // nothing to do
                    return;

                // verify timestamps on MCU
                stepper.QueueFlush();
                timestamp = stepper.QueueNextStepWaketimeVerify(timestamp);
            }
            catch (McuStepperResetNecessaryException ex)
            {
                _logger.LogDebug(ex, $"Stepper reset error during XYL command execution. Type={state.Type}, initialTimestamp={initialTimestamp}. initialTime={initialTime}. initialValue={initialValue}. timestamp={timestamp}. initialCompressed={string.Join(";", initialCompressed.ToArray())}. prevInitialTimestamp={state.FlushingData.PrevExecutedInitialTimestamp}. prevCompressed={string.Join(";", state.FlushingData.PrevExecuted.ToArray())}");
                throw;
            }
            finally
            {
                state.FlushingData.PrevExecuted.CopyFrom(state.FlushingData.Compressed.Span);
                state.FlushingData.PrevExecutedInitialTimestamp = initialTimestamp;
            }
        }

        private static void CalcResetDelay(SystemTimestamp now, McuTimestamp timestamp, ref TimeSpan resetDelay)
        {
            if (timestamp.IsEmpty)
                return;
            var timestampSys = timestamp.ToSystem();
            if (now < timestampSys)
            {
                var delay = timestampSys - now;
                if (resetDelay < delay)
                    resetDelay = delay;
            }
        }

        private void FlushXYLOnlyOnDedicatedThread(bool flushAll, IPrinterClientCommandContext? context)
        {
            try
            {
                var options = _options.CurrentValue;
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                (var stepperX, var stepperY, var stepperL, var mcu) = _movementClient.GetXYL(manager, null);

                var parallelFlush = options.ParallelFlush;

                lock (_flushLock)
                {
                    while (true)
                    {
                        // copy data to compress (in master lock)
                        McuTimestamp timestampX, oldTimestampX;
                        McuTimestamp timestampY, oldTimestampY;
                        McuTimestamp timestampL, oldTimestampL;

                        var startTimestamp = SystemTimestamp.Now;
                        var intermediateTimestamp = startTimestamp;
                        var copyTimestamp = startTimestamp;
                        var executeTimestamp = startTimestamp;
                        var xduration = TimeSpan.Zero;
                        var yduration = TimeSpan.Zero;
                        var lduration = TimeSpan.Zero;
                        var immediateRetry = false;

                        using (var master = manager.LockMasterQueue())
                        {
                            if (_flushAfterNeedsLocks == TimeSpan.MaxValue && !flushAll) // not scheduled and not "flush all" -> this might be a "missed" timer
                                break;
                            if (_flushAfterNeedsLocks != TimeSpan.MaxValue)
                            {
                                _flushAfterNeedsLocks = TimeSpan.MaxValue;
                                _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                            }

                            oldTimestampX = timestampX = master[stepperX];
                            oldTimestampY = timestampY = master[stepperY];
                            oldTimestampL = timestampL = master[stepperL];
                            PrepareCompressXYLFlushingInner(_stateX, options);
                            PrepareCompressXYLFlushingInner(_stateY, options);
                            PrepareCompressXYLFlushingInner(_stateL, options);
                        }
                        copyTimestamp = SystemTimestamp.Now;

                        // process if there is anything
                        //if (_stateX.FlushingData.Intermediate.Count > 0 ||
                        //    _stateY.FlushingData.Intermediate.Count > 0 ||
                        //    _stateL.FlushingData.Intermediate.Count > 0)
                        {
                            // ensure that `now` is not behind current stepper timestamps (may happen after XYL reset)
                            var now = SystemTimestamp.Now;
                            var resetDelay = TimeSpan.Zero;
                            intermediateTimestamp = now;

                            CalcResetDelay(now, timestampX, ref resetDelay);
                            CalcResetDelay(now, timestampY, ref resetDelay);
                            CalcResetDelay(now, timestampL, ref resetDelay);

                            var fallbackTime = double.MaxValue;
                            _stateX.GetFallbackTime(ref fallbackTime);
                            _stateY.GetFallbackTime(ref fallbackTime);
                            _stateL.GetFallbackTime(ref fallbackTime);
                            Debug.Assert(fallbackTime != double.MaxValue);

                            // execute in parallel
                            void FlushX()
                            {
                                var ts = SystemTimestamp.Now;
                                CompressXYLFlushingInner(stepperX, options, _stateX);
                                ExecuteXYLFlushingInner(stepperX, ref timestampX, _stateX, now, fallbackTime, resetDelay);
                                xduration = ts.ElapsedFromNow;
                            }
                            void FlushY()
                            {
                                var ts = SystemTimestamp.Now;
                                CompressXYLFlushingInner(stepperY, options, _stateY);
                                ExecuteXYLFlushingInner(stepperY, ref timestampY, _stateY, now, fallbackTime, resetDelay);
                                yduration = ts.ElapsedFromNow;
                            }
                            void FlushL()
                            {
                                var ts = SystemTimestamp.Now;
                                CompressXYLFlushingInner(stepperL, options, _stateL);
                                ExecuteXYLFlushingInner(stepperL, ref timestampL, _stateL, now, fallbackTime, resetDelay);
                                lduration = ts.ElapsedFromNow;
                            }

                            if (parallelFlush)
                            {
                                var parallelTasks = new Task[]
                                {
                                    Task.Factory.StartNew(() =>
                                    {
                                        Thread.MemoryBarrier();
                                        FlushX();
                                        Thread.MemoryBarrier();
                                    }, default, TaskCreationOptions.None, _flushScheduler2),
                                    Task.Factory.StartNew(() =>
                                    {
                                        Thread.MemoryBarrier();
                                        FlushY();
                                        Thread.MemoryBarrier();
                                    }, default, TaskCreationOptions.None, _flushScheduler2),
                                    Task.Factory.StartNew(() =>
                                    {
                                        Thread.MemoryBarrier();
                                        FlushL();
                                        Thread.MemoryBarrier();
                                    }, default, TaskCreationOptions.None, _flushScheduler2),
                                };
                                Task.WaitAll(parallelTasks);
                                Thread.MemoryBarrier();
                            }
                            else
                            {
                                FlushX();
                                FlushY();
                                FlushL();
                            }

                            // update timestamps, reschedule flush (important!)
                            using (var master = manager.LockMasterQueue())
                            {
                                Debug.Assert(master[stepperX] == oldTimestampX);
                                Debug.Assert(master[stepperY] == oldTimestampY);
                                Debug.Assert(master[stepperL] == oldTimestampL);
                                master[stepperX] = timestampX;
                                master[stepperY] = timestampY;
                                master[stepperL] = timestampL;

                                // reschedule/loop again
                                if (flushAll)
                                {
                                    if (_stateX.HasSources || _stateY.HasSources || _stateL.HasSources)
                                        immediateRetry = true;
                                }
                                else if (ScheduleFlushInner(manager, false, true, options, context))
                                {
                                    immediateRetry = true;
                                }
                            }
                        }
                        //else
                        //{
                        //    Debug.Assert(!immediateRetry);
                        //    stepperX.QueueFlush();
                        //    stepperY.QueueFlush();
                        //    stepperL.QueueFlush();
                        //}

                        var endTimestamp = SystemTimestamp.Now;
                        var elapsed = endTimestamp - startTimestamp;
                        if (elapsed >= options.CompressFlushPeriod && !flushAll)
                        {
                            var elapsedCopy = copyTimestamp - startTimestamp;
                            var elapsedIntermediate = endTimestamp - intermediateTimestamp;
                            _logger.LogInformation($"XYL flush took {elapsed} (copy={elapsedCopy}, intermediate={elapsedIntermediate}, x={xduration}, y={yduration}, l={lduration}), which is more than flush period {options.CompressFlushPeriod}, this may be indication that the system CPU is overloaded. ");
                        }

                        if (!immediateRetry)
                            break;
                    }
                }

                // try to reschedule once more, while outside of FlushLock. Sources may have been added.
                // use general method, not "inner" version, to ensure noop, if next flush is already running
                ScheduleFlushOutsideMasterLock(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in XYL flush");
            }
        }
    }
}
