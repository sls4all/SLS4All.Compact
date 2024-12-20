// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

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
        /// <summary>
        /// Diagnostic timeout for XYL flushing
        /// </summary>
        public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(30);
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

            public bool GetIsFull(double flushSeconds)
            {
                var source = Source;
                if (source.Count == 0)
                    return false;
                double elapsed;
                ref var first = ref source[0];
                elapsed = source[^1].Time - first.Time;
                Debug.Assert(elapsed >= 0);
                if (elapsed >= flushSeconds)
                    return true;
                if (!first.IsReset)
                {
                    elapsed = first.Time - LastFlushStartTime;
                    if (elapsed >= flushSeconds)
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

            public bool ShouldTimeoutFlush(double timeNow, double flushSeconds, ref double flushAfterResult)
            {
                if (IsEmpty)
                    return false;
                var timeLast = Source[^1].Time;
                var flushAfter = timeLast < timeNow
                    ? 0
                    : Math.Max(flushSeconds - (timeLast - timeNow), 0);
                if (flushAfter < flushAfterResult)
                    flushAfterResult = flushAfter;
                return flushAfterResult <= 0;
            }

            public void Dump(ILogger logger, LogLevel level, double time)
            {
                var buf = new StringBuilder();
                buf.Append($"Dump StepperState({Type}): time={time}, LastFlushStartTime={LastFlushStartTime}, Source={{");
                foreach (var item in Source)
                {
                    buf.Append(item);
                    buf.Append("; ");
                }
                buf.Append($"}}");
                logger.Log(level, buf.ToString());
            }
        }

        public struct FlushLockDisposable(XYLMovementQueue? queue) : IDisposable
        {
            private XYLMovementQueue? _queue = queue;

            public void Dispose()
            {
                var queue = _queue;
                if (queue != null)
                {
                    _queue = null;
                    queue._flushLock.Exit();
                }
            }
        }

        private static readonly object _trueBox = true;
        private static readonly object _falseBox = true;
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<XYLMovementQueueOptions> _options;
        private readonly McuPrinterClient _printerClient;
        private readonly McuMovementClient _movementClient;
        private readonly IThreadStackTraceDumper _stackTraceDumper;
        private readonly Lock _flushLock = new();
        private readonly Timer _flushTimer;
        private readonly PriorityScheduler _flushScheduler1;
        private readonly PriorityScheduler _flushScheduler2;
        private readonly Action<object?> _flushXYLOnlyOnDedicatedThread;
        private IPrinterClientCommandContext? _timerFlushContext;
        private TimeSpan _flushAfterNeedsFlushLock;
        private StepperState _stateX;
        private StepperState _stateY;
        private StepperState _stateL;
        private Timer _flushGuardTimer;
        private volatile Thread? _flushGuardThread;

        /// <summary>
        /// If flush is scheduled, this is maxiumum time after it will happen. Otherwise set to <see cref="TimeSpan.MaxValue"/>.
        /// Read in flush and then master lock.
        /// </summary>
        public TimeSpan FlushAfterNeedsLocks => _flushAfterNeedsFlushLock;

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
            McuMovementClient movementClient,
            IThreadStackTraceDumper stackTraceDumper)
        {
            _logger = logger;
            _options = options;
            _printerClient = printerClient;
            _movementClient = movementClient;
            _stackTraceDumper = stackTraceDumper;
            _stateX = new(StepperType.X);
            _stateY = new(StepperType.Y);
            _stateL = new(StepperType.L);
            _flushTimer = new(OnFlushTimer);
            _flushAfterNeedsFlushLock = TimeSpan.MaxValue;
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
            _flushGuardTimer = new Timer(OnFlushTimeout);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddXY(double time, double x, double y)
        {
            Debug.Assert(_stateX.IsEmpty || _stateX.Source[^1].Time <= time);
            Debug.Assert(_stateY.IsEmpty || _stateY.Source[^1].Time <= time);
            _stateX.AddSource(new XYLMovementPoint(time, x));
            _stateY.AddSource(new XYLMovementPoint(time, y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddL(double time, double pwm)
        {
            Debug.Assert(_stateL.IsEmpty || _stateL.Source[^1].Time <= time);
            _stateL.AddSource(new XYLMovementPoint(time, pwm));
        }

        public void Clear()
        {
            _stateX.Source.Clear();
            _stateY.Source.Clear();
            _stateL.Source.Clear();
        }

        public void AddReset(double time, double posX, double posY)
        {
            Clear();
            _stateX.AddReset(time, posX);
            _stateY.AddReset(time, posY);
            _stateL.AddReset(time, 0);
        }

        private bool ScheduleFlushInFlushLock(
            McuManager manager,
            IMcu mcu,
            bool forceImmediateFlushAll,
            bool noImmediateInsteadReturnTrue,
            XYLMovementQueueOptions options,
            IPrinterClientCommandContext? context)
        {
            Debug.Assert(_flushLock.IsHeldByCurrentThread);
            var flushSeconds = options.CompressFlushPeriod.TotalSeconds;
            var flushAfterResult = flushSeconds;
            if (_flushAfterNeedsFlushLock != TimeSpan.Zero || // not already immediate
                noImmediateInsteadReturnTrue || // or we want true instead
                forceImmediateFlushAll) // or we are force flushing
            {
                var immediateFlushAll = forceImmediateFlushAll;
                if (!immediateFlushAll)
                {
                    var now = SystemTimestamp.Now;
                    var time = ToTime(mcu, now);
                    using (var master = manager.EnterMasterQueueLock())
                    {
                        if (_stateX.ShouldTimeoutFlush(time, flushSeconds, ref flushAfterResult) | // USE | NOT || here!
                            _stateX.ShouldTimeoutFlush(time, flushSeconds, ref flushAfterResult) | // USE | NOT || here!
                            _stateX.ShouldTimeoutFlush(time, flushSeconds, ref flushAfterResult))
                            immediateFlushAll = true;
                        else if (_stateX.GetIsFull(flushSeconds) || _stateY.GetIsFull(flushSeconds) || _stateL.GetIsFull(flushSeconds))
                            immediateFlushAll = true;
                    }
                }

                if (immediateFlushAll)
                {
                    if (noImmediateInsteadReturnTrue)
                    {
                        // immediate flush will be handled directly outside of this method
                        _timerFlushContext = context;
                        if (_flushAfterNeedsFlushLock != TimeSpan.MaxValue)
                        {
                            _flushAfterNeedsFlushLock = TimeSpan.MaxValue;
                            _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                        }
                    }
                    else if (_flushAfterNeedsFlushLock != TimeSpan.Zero)
                    {
                        // schedule immediate flush
                        if (_flushAfterNeedsFlushLock != TimeSpan.MaxValue) // timer not needed
                            _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                        _flushAfterNeedsFlushLock = TimeSpan.Zero;
                        _timerFlushContext = null; // context passed in argument below
                        ScheduleCompressFlushImmediately(forceImmediateFlushAll, context);
                    }
                    else
                    {
                        // immediate flush is already scheduled
                    }
                    return true;
                }
            }

            if (_flushAfterNeedsFlushLock == TimeSpan.MaxValue) // flush not yet scheduled
            {
                if (_stateX.HasSources || _stateY.HasSources || _stateL.HasSources)
                {
                    _flushAfterNeedsFlushLock = TimeSpan.FromSeconds(flushAfterResult);
                    _timerFlushContext = context;
                    _flushTimer.Change(_flushAfterNeedsFlushLock, Timeout.InfiniteTimeSpan);
                    return false;
                }
            }
            return false;
        }

        /// <remarks>
        /// Needs to be called outside master lock to prevent deadlock (master lock / flushLock)
        /// </remarks>
        public void FlushAllAndWaitOutsideMasterLock(IPrinterClientCommandContext? context, CancellationToken cancel = default)
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            (_, _, _, var mcu) = _movementClient.GetXYL(manager, null);
            using (EnterFlushLock())
            {
                var options = _options.CurrentValue;
                ScheduleFlushInFlushLock(manager, mcu, forceImmediateFlushAll: true, noImmediateInsteadReturnTrue: false, options, context);
            }
            // NOTE: following call ensures that the flush scheduler has finished running all preceding tasks (since it is for exactly one parallel task)
            Task.Factory.StartNew(
                static _ => { },
                null,
                default,
                TaskCreationOptions.None,
                _flushScheduler1)
                .Wait(cancel);
        }

        /// <remarks>
        /// Needs to be called outside master lock to prevent deadlock (master lock / flushLock)
        /// </remarks>
        public void ScheduleFlushOutsideMasterLock(IPrinterClientCommandContext? context)
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            using (TryEnterFlushLock(out var entered))
            {
                if (entered) // if not, it will be automatically scheduled once the flush lock ends
                {
                    var options = _options.CurrentValue;
                    (_, _, _, var mcu) = _movementClient.GetXYL(manager, null);
                    ScheduleFlushInFlushLock(manager, mcu, forceImmediateFlushAll: false, noImmediateInsteadReturnTrue: false, options, context);
                }
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
            bool dryRun)
        {
            var timeStart = state.FlushingData.Time;
            var elapsed = timeEnd - timeStart;
            var posStart = state.FlushingData.Value;

            Debug.Assert(elapsed >= 0);
            if (elapsed >= 0)
            {
                if (elapsed > 0)
                {
                    if (posStart == posEnd)
                    {
                        QueueDwellFlushingInner(stepper, ref timestamp, state, elapsed, now, dryRun);
                    }
                    else
                    {
                        var velocity = Math.Abs(posEnd - posStart) / elapsed;
                        var steps = stepper.GetSteps(velocity, posStart, posEnd, state.FlushingData.PrecisionRemainder);
                        if (steps.Count == 0)
                        {
                            QueueDwellFlushingInner(stepper, ref timestamp, state, elapsed, now, dryRun);
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
                                dryRun: dryRun);
                            state.FlushingData.PrecisionRemainder = steps.PrecisionRemainder;
                        }
                    }
                }
                state.FlushingData.Time = timeEnd;
                state.FlushingData.Value = posEnd;
            }
        }

        private void SetLaserFlushingInner(
            IMcuStepper stepper,
            double value,
            double time,
            ref McuTimestamp timestamp,
            StepperState state,
            SystemTimestamp now,
            bool dryRun)
        {
            var elapsed = time - state.FlushingData.Time;

            Debug.Assert(elapsed >= 0);
            if (elapsed >= 0)
            {
                QueueDwellFlushingInner(stepper, ref timestamp, state, elapsed, now, dryRun);

                Debug.Assert(value != double.MaxValue);
                timestamp = stepper.QueuePwm(
                    (float)value,
                    timestamp,
                    now: now,
                    dryRun: dryRun);

                state.FlushingData.Time = time;
            }
        }

        private void PrepareCompressXYLFlushingInner(
            StepperState state,
            bool forceFlush,
            double flushSeconds)
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
                if (last.Time - first.Time > flushSeconds) // data is too long and might cause performance issues when compressing
                {
                    // get a chunk of data that has at most CompressFlushPeriod of duration
                    for (int i = source.Length - 2; i >= 1; i--)
                    {
                        ref var item = ref source[i];
                        if (item.Time - first.Time < flushSeconds)
                        {
                            var consumedCount = i + 1;
                            state.FlushingData.Intermediate.AddRange(source.Slice(0, consumedCount));
                            state.Source.RemoveFromBeginning(consumedCount);
                            return;
                        }
                    }
                }
            }

            if (!forceFlush)
                return;

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
                    ExecuteXYLInnerDryOrReal(stepper, ref timestamp, state, now, compressed, dryRun: false);
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
                        false);
                    state.FlushingData.Time = fallbackTime;
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
            var options = _options.CurrentValue;
            try
            {
                var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
                (var stepperX, var stepperY, var stepperL, var mcu) = _movementClient.GetXYL(manager, null);

                var parallelFlush = options.ParallelFlush;
                var prevSourceCount = (-1, -1, -1);
                var sourceCountSame = 0;
                const int maxSaneSourceCountSame = 100; // sanity check!

                // NOTE: can do outside of _flushLock (intentionaly), since this method runs on thread pool with single thread!
                if (options.FlushTimeout != TimeSpan.Zero)
                {
                    _flushGuardThread = Thread.CurrentThread;
                    _flushGuardTimer.Change(options.FlushTimeout, Timeout.InfiniteTimeSpan);
                }

                lock (_flushLock)
                {
                    if (_flushAfterNeedsFlushLock != TimeSpan.MaxValue) // disable timer, we will reschedule at the end
                    {
                        _flushAfterNeedsFlushLock = TimeSpan.MaxValue;
                        _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }

                    while (true)
                    {
                        // copy data to compress (in master lock)
                        McuTimestamp timestampX, oldTimestampX;
                        McuTimestamp timestampY, oldTimestampY;
                        McuTimestamp timestampL, oldTimestampL;

                        var startTimestamp = SystemTimestamp.Now;
                        var startTime = ToTime(mcu, startTimestamp);
                        var intermediateTimestamp = startTimestamp;
                        var copyTimestamp = startTimestamp;
                        var executeTimestamp = startTimestamp;
                        var xduration = TimeSpan.Zero;
                        var yduration = TimeSpan.Zero;
                        var lduration = TimeSpan.Zero;
                        var immediateRetry = false;

                        using (var master = manager.EnterMasterQueueLock())
                        {
                            oldTimestampX = timestampX = master[stepperX];
                            oldTimestampY = timestampY = master[stepperY];
                            oldTimestampL = timestampL = master[stepperL];

                            var sourceCount = (_stateX.Source.Count, _stateY.Source.Count, _stateL.Source.Count);
                            if (prevSourceCount == sourceCount) // sanity check
                            {
                                if (sourceCountSame++ > maxSaneSourceCountSame)
                                {
                                    _logger.LogError($"XYLFlush is looping without source count change. FlushAll={flushAll}, SourceCount={sourceCount}, FlushAfterNeedsLocks={_flushAfterNeedsFlushLock}, TimestampX={timestampX}, TimestampY={timestampY}, TimestampL={timestampL}, ImmediateRetry={immediateRetry}");
                                    _stateX.Dump(_logger, LogLevel.Error, startTime);
                                    _stateY.Dump(_logger, LogLevel.Error, startTime);
                                    _stateL.Dump(_logger, LogLevel.Error, startTime);
                                    throw new InvalidOperationException($"XYLFlush is looping without source count change, this is probably a bug");
                                }
                            }
                            else
                            {
                                sourceCountSame = 0;
                                prevSourceCount = sourceCount;
                            }

                            var forceFlush = flushAll;
                            var flushSeconds = options.CompressFlushPeriod.TotalSeconds;
                            if (!forceFlush)
                            {
                                double dummy = double.MaxValue;
                                if (_stateX.ShouldTimeoutFlush(startTime, flushSeconds, ref dummy) ||
                                    _stateY.ShouldTimeoutFlush(startTime, flushSeconds, ref dummy) ||
                                    _stateL.ShouldTimeoutFlush(startTime, flushSeconds, ref dummy))
                                    forceFlush = true;
                            }

                            PrepareCompressXYLFlushingInner(_stateX, forceFlush, flushSeconds);
                            PrepareCompressXYLFlushingInner(_stateY, forceFlush, flushSeconds);
                            PrepareCompressXYLFlushingInner(_stateL, forceFlush, flushSeconds);
                        }
                        copyTimestamp = SystemTimestamp.Now;

                        // process if there is anything
                        if (_stateX.FlushingData.Intermediate.Count > 0 ||
                            _stateY.FlushingData.Intermediate.Count > 0 ||
                            _stateL.FlushingData.Intermediate.Count > 0)
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
                                Task.WaitAll(
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
                                    }, default, TaskCreationOptions.None, _flushScheduler2)
                                );
                                Thread.MemoryBarrier();
                            }
                            else
                            {
                                FlushX();
                                FlushY();
                                FlushL();
                            }

                            // update timestamps, reschedule flush (important!)
                            using (var master = manager.EnterMasterQueueLock())
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
                                else if (ScheduleFlushInFlushLock(manager, mcu, forceImmediateFlushAll: false, noImmediateInsteadReturnTrue: true, options, context))
                                {
                                    immediateRetry = true;
                                }
                            }
                        }

                        var endTimestamp = SystemTimestamp.Now;
                        var elapsed = endTimestamp - startTimestamp;
                        if (elapsed >= options.CompressFlushPeriod && !flushAll)
                        {
                            var elapsedCopy = copyTimestamp - startTimestamp;
                            var elapsedIntermediate = endTimestamp - intermediateTimestamp;
                            _logger.LogInformation($"XYL flush took {elapsed} (copy={elapsedCopy}, intermediate={elapsedIntermediate}, x={xduration}, y={yduration}, l={lduration}), which is more than flush period {options.CompressFlushPeriod}, this may be indication that the system CPU is overloaded. ");
                        }

                        if (!immediateRetry)
                        {
                            if (flushAll)
                            {
                                stepperX.QueueFlush();
                                stepperY.QueueFlush();
                                stepperL.QueueFlush();
                            }
                            break;
                        }
                    }
                }

                // try to reschedule once more, while outside of FlushLock. Sources may have been added.
                // use general method, not "inner" version, to ensure noop, if next flush is already running
                ScheduleFlushOutsideMasterLock(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in XYL flush, shutting down firmware");
                _printerClient.Shutdown("Exception in XYL flush, shutting down firmware", ex, context);
            }
            finally
            {
                if (options.FlushTimeout != TimeSpan.Zero)
                {
                    _flushGuardTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _flushGuardThread = null;
                }
            }
        }

        private void OnFlushTimeout(object? state)
        {
            var thread = _flushGuardThread;
            _logger.LogError($"XYL flush has timed out. Shutting down firmware. {(thread != null ? $"StackTrace={_stackTraceDumper.DumpThreads(thread.ManagedThreadId)}" : "")}");
            if (!Debugger.IsAttached)
                _printerClient.Shutdown("XYL flush has timed out. Shutting down firmware.", null);
        }

        public double ToTime(IMcu mcu, SystemTimestamp timestamp)
            => ToTime(McuTimestamp.FromSystem(mcu, timestamp));

        public double ToTime(McuTimestamp timestamp)
            => timestamp.ToRelativeSeconds();

        public FlushLockDisposable EnterFlushLock()
        {
            var manager = _printerClient.ManagerEvenInShutdown;
            if (manager.IsMasterQueueLocked())
                throw new InvalidOperationException($"Flush lock cannot be called in Master lock. This is probably indication of a bug.");
            if (_flushLock.IsHeldByCurrentThread)
                throw new InvalidOperationException($"Flush lock is already entered. This is probably indication of a bug.");
            _flushLock.Enter();
            return new FlushLockDisposable(this);
        }

        public FlushLockDisposable TryEnterFlushLock(out bool hasEntered)
        {
            var manager = _printerClient.ManagerEvenInShutdown;
            if (manager.IsMasterQueueLocked())
                throw new InvalidOperationException($"Flush lock cannot be called in Master lock. This is probably indication of a bug.");
            if (_flushLock.IsHeldByCurrentThread)
                throw new InvalidOperationException($"Flush lock is already entered. This is probably indication of a bug.");
            hasEntered = _flushLock.TryEnter();
            if (hasEntered)
                return new FlushLockDisposable(this);
            else
                return new FlushLockDisposable(null);
        }
    }
}
