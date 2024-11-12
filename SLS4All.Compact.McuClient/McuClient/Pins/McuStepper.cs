// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
using Lexical.FileSystem.Decoration;
using Lexical.FileSystem.Operation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.McuClient.Pins.Tmc2208;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using SLS4All.Compact.Printer;

namespace SLS4All.Compact.McuClient.Pins
{
    public class McuStepperGlobalOptions
    {
        /// <summary>
        /// Delta-t time for solving acceleration curve
        /// </summary>
        public TimeSpan AccelerationStepDuration { get; set; } = TimeSpan.FromSeconds(0.05);
        /// <summary>
        /// Time the commands should be placed in the queue before they are sent to MCU using <see cref="SendAheadDuration"/> 
        /// Queing commands ahead gives the application time to prepare as much data as possible before gradually starting to send them away.
        /// Value should be larger than <see cref="SendAheadDuration"/>.
        /// </summary>
        public TimeSpan QueueAheadDuration { get; set; } = TimeSpan.FromSeconds(0.6);
        /// <summary>
        /// Time to send the command to the MCU before it is its time to be executed.
        /// </summary>
        public TimeSpan SendAheadDuration { get; set; } = TimeSpan.FromSeconds(0.15);
        /// <summary>
        /// Time between now and command execution to deem it necessary to reset the stepper queue. 
        /// Should be less than <see cref="SendAheadDuration"/>
        /// </summary>
        public TimeSpan UnderflowDuration { get; set; } = TimeSpan.FromSeconds(0.10);
        /// <summary>
        /// Endstop sampling cycle-time
        /// </summary>
        public TimeSpan EndstopSampleTime { get; set; } = TimeSpan.FromSeconds(0.000015);
        /// <summary>
        /// Number of endstop samples neccesary for confirmation
        /// </summary>
        public int EndstopSampleCount { get; set; } = 4;
        /// <summary>
        /// Additional delay after homing (necessary to ensure movement stopped)
        /// </summary>
        public TimeSpan FinishHomeDelay { get; set; } = TimeSpan.FromSeconds(0.1);
    }

    public class McuStepperOptions
    {
        public string? StepPin { get; set; }
        public string? DirPin { get; set; }
        public string? EnablePin { get; set; }
        public string? EndstopPin { get; set; }
        public required double FullStepDistance { get; set; }
        public required double MinVelocity { get; set; }
        public required double MaxVelocity { get; set; }
        /// <summary>
        /// This value is valid only for DAC steppers, not real stepper drivers.
        /// It represents maximum velocity that the MCU is capable of safely stepping the DAC.
        /// If the actually requested velocity is larger than this value, the DAC will increment/decrement the DAC value with value larger
        /// than "one" with each step.
        /// If this value is too large, MCU will fail with timer errors, but only in cases where large movements speeds are used, like
        /// a printed layer with lots of small islands (not a small number of large filled islands like the timer errors related 
        /// to large value of XYL moves per second).
        /// </summary>
        public double? CriticalVelocity { get; set; }
        public bool NoDelay { get; set; } = false;
        /// <summary>
        /// Whether step should be dumped to file for debugging purposes
        /// </summary>
        public string? DumpCommandsFileFormat { get; set; }
        public bool DumpCommandsToFile { get; set; }
        public int IntervalPrecision { get; set; } = 0;
        public int? EndstopSampleCount { get; set; }
    }

    public sealed class McuStepper : McuStepperBase, IMcuStepper
    {
        public const int MaxStepsInCmd = short.MaxValue;
        public const int MaxStepMovesInV1Cmd = 7;
        public const int MaxStepMovesInV2Cmd = 9;
        public const int MaxStepMovesInV3Cmd = 14;
        public const int MaxStepMovesInV4Cmd = 9;
        private readonly ILogger<McuStepper> _logger;
        private readonly IOptions<McuStepperGlobalOptions> _optionsGlobal;
        private readonly IOptions<McuStepperOptions> _options;
        private readonly IStepperDriver? _driver;
        private readonly McuManager _manager;
        private readonly string _name;
        private readonly IMcuOutputPin? _pwmPin;
        private readonly McuPinDescription? _stepPinDesc;
        private readonly McuPinDescription? _dirPinDesc;
        private readonly McuPinDescription? _enablePinDesc;
        private readonly McuPinDescription? _endstopPinDesc;
        private readonly IMcuOutputPin? _enablePin;
        private readonly AsyncLock _homingLock = new();
        private readonly double _fullStepDistance;
        private readonly double _microstepDistance;
        private readonly double _minVelocity;
        private readonly double _maxVelocity;
        private readonly double _criticalVelocity;
        private readonly TimeSpan _accelerationStepDuration;
        private readonly long _maxInterval32;
        private readonly int _intervalPrecision;
        private readonly int _intervalPrecisionMul;
        private BinaryWriter? _dumpWriter;
        private int _dumpCounter;
        private long _criticalMinInterval;
        private int _stepperOid;
        private int? _endstopOid;
        private McuCommand _queueStepsV1Cmd = McuCommand.PlaceholderCommand;
        private McuCommand _queueStepsV2Cmd = McuCommand.PlaceholderCommand;
        private McuCommand _queueStepsV3Cmd = McuCommand.PlaceholderCommand;
        private McuCommand _queueStepsV4Cmd = McuCommand.PlaceholderCommand;
        private (McuCommand Cmd, int Clock, int ClockHi) _resetCmd = (McuCommand.PlaceholderCommand, 0, 0);
        private McuCommand _resetDacCmd = McuCommand.PlaceholderCommand;
        private (McuCommand Cmd, int Value) _updateDacCmd = (McuCommand.PlaceholderCommand, 0);
        private McuCommand _homeCmd = McuCommand.PlaceholderCommand;
        private McuCommand _endstopQueryCmd = McuCommand.PlaceholderCommand;
        private McuCommand _endstopStateResponse = McuCommand.PlaceholderCommand;
        private (McuCommand Cmd, int Clock, int ClockHi) _verifyNextStepWaketimeCmd = (McuCommand.PlaceholderCommand, 0, 0);

#if DEBUG
        private static readonly long[] _verSentCount = new long[5];
#endif
        private readonly PrimitiveList<byte> _cmdStepBuffer;
        private readonly PrimitiveList<StepMoveV1> _cmdSteps;
        private readonly IMcu _mcu;
        [ThreadStatic]
        private static HashSet<McuSendResult>? _homingStepCommandIds;
        private McuSendResult _cmdValuesId;
        private int _cmdValuesSentCount;
        private int _cmdValuesVer;

        private readonly TimeSpan _queueAheadDuration;
        private readonly TimeSpan _sendAheadDuration;
        private readonly TimeSpan _underflowDuration;

        private readonly object _queueStepLock = new object();
        private McuTimestamp _lastClock;
        private bool _enabled;

        public IMcu Mcu => _mcu;
        public double? MinPwmCycleTime => _pwmPin?.Pin.CycleTime;
        public TimeSpan QueueAheadDuration => _queueAheadDuration;
        public TimeSpan SendAheadDuration => _sendAheadDuration;
        public TimeSpan UnderflowDuration => _underflowDuration;
        public override int IntervalPrecisionMul => _intervalPrecisionMul;
        public override double FullStepDistance => _fullStepDistance;
        public override double MicrostepDistance => _microstepDistance;
        public override double MinVelocity => _minVelocity;
        public override double MaxVelocity => _maxVelocity;
        public override double CriticalVelocity => _criticalVelocity;
        protected override TimeSpan AccelerationStepDuration => _accelerationStepDuration;
        protected override IMcuClockSync Clock => _mcu.ClockSync;

        public McuStepper(
            IOptions<McuStepperGlobalOptions> optionsGlobal,
            IOptions<McuStepperOptions> options,
            McuManager manager,
            IStepperDriver? driver,
            IMcuOutputPin? pwmPin,
            string name)
        {
            _logger = manager.CreateLogger<McuStepper>();
            _optionsGlobal = optionsGlobal;
            _options = options;
            _driver = driver;
            _manager = manager;
            _name = name;

            var o = options.Value;
            var og = optionsGlobal.Value;
            _maxInterval32 = o.IntervalPrecision == 0 ? int.MaxValue - 1 : uint.MaxValue - 1; // NOTE: TODO: following does not yet work correctly, StepMoveVx values are set to int.MaxValue - 1 for safety till fixed
            _intervalPrecision = o.IntervalPrecision;
            _intervalPrecisionMul = 1 << o.IntervalPrecision;
            _cmdSteps = new();
            _cmdStepBuffer = new();
            _queueAheadDuration = og.QueueAheadDuration;
            _sendAheadDuration = og.SendAheadDuration;
            _underflowDuration = og.UnderflowDuration;
            _fullStepDistance = o.FullStepDistance;
            _microstepDistance = o.FullStepDistance / (driver?.Microsteps ?? 1);
            _minVelocity = o.MinVelocity;
            _maxVelocity = o.MaxVelocity;
            _criticalVelocity = Math.Min(o.CriticalVelocity ?? o.MaxVelocity, o.MaxVelocity);
            _accelerationStepDuration = og.AccelerationStepDuration;

            _stepPinDesc = o.StepPin != null ? manager.ClaimPin(McuPinType.Stepper, o.StepPin, canInvert: true) : null;
            _dirPinDesc = o.DirPin != null ? manager.ClaimPin(McuPinType.Stepper, o.DirPin, canInvert: true) : null;
            _enablePinDesc = !string.IsNullOrEmpty(o.EnablePin) ? manager.ClaimPin(McuPinType.Digital, o.EnablePin, canInvert: true) : null;
            _endstopPinDesc = o.EndstopPin != null ? manager.ClaimPin(McuPinType.Stepper, o.EndstopPin, canInvert: true) : null;
            _pwmPin = pwmPin;

            if (_stepPinDesc != null)
            {
                if (_dirPinDesc == null)
                    throw new InvalidOperationException($"Step pin ({_stepPinDesc}) and dir pin ({_dirPinDesc}) must be set together");
                if (_pwmPin != null)
                    throw new InvalidOperationException($"Pwm pin {_pwmPin.Pin} cannot be set together with step pin {_stepPinDesc}");
                _mcu = _stepPinDesc.Mcu;
            }
            else
            {
                if (_pwmPin == null)
                    throw new InvalidOperationException($"If not step pin, the pwm pin must be set");
                if (_dirPinDesc != null || _endstopPinDesc != null)
                    throw new InvalidOperationException($"Direction pin ({_dirPinDesc}) and endstop pin ({_endstopPinDesc}) cannot be set together with pwm pin ({_pwmPin})");
                _mcu = _pwmPin.Mcu;
            }

            if (_stepPinDesc != null && _stepPinDesc.Mcu != _mcu ||
                _dirPinDesc != null && _dirPinDesc.Mcu != _mcu ||
                _enablePinDesc != null && _enablePinDesc.Mcu != _mcu ||
                _endstopPinDesc != null && _endstopPinDesc.Mcu != _mcu ||
                _pwmPin != null && _pwmPin.Mcu != _mcu)
                throw new InvalidOperationException($"Step pin ({_stepPinDesc}), dir pin ({_dirPinDesc}), enable pin ({_enablePinDesc}), endstop pin ({_endstopPinDesc}) and pwm pin ({_pwmPin}) must be on the same MCU");

            manager.RegisterSetup(Mcu, OnSetup);
            _mcu.RegisterConfigCommand(BuildConfig);
            if (_enablePinDesc != null)
            {
                _enablePin = _enablePinDesc.SetupPin($"stepper-{_name}-enable");
                _enablePin.SetupMaxDuration(TimeSpan.Zero);
            }
        }

        private ValueTask OnSetup(CancellationToken token)
        {
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Ensures small delay so there are no sudden changes to galvo input that might damage it
        /// </summary>
        private McuTimestamp AddDacSafetyDelay(McuTimestamp timestamp, double maxDistance)
        {
            Debug.Assert(!timestamp.IsEmpty && timestamp.Precision == _intervalPrecision);
            return timestamp + maxDistance / _maxVelocity;
        }

        public McuTimestamp DacReset(McuTimestamp timestamp, double maxDistance)
        {
            lock (_queueStepLock)
            {
                var options = _options.Value;
                var now = SystemTimestamp.Now;
                timestamp = ResetInner(timestamp, McuStepperResetFlags.None, out _, null, now, false, 0, TimeSpan.Zero);
                EnsureEnabledInner(timestamp);
                timestamp = AddDacSafetyDelay(timestamp, maxDistance);
                lock (_resetDacCmd)
                {
                    var occasion = new McuOccasion((timestamp - _sendAheadDuration).Clock, timestamp.Clock);
                    _mcu.Send(
                        _resetDacCmd,
                        McuCommandPriority.Printing,
                        occasion);
                }
                timestamp = AddDacSafetyDelay(timestamp, maxDistance);
            }
            return timestamp;
        }

        public McuTimestamp DacHome(McuTimestamp timestamp, double position, double maxDistance)
        {
            var count = (long)Math.Round(position / MicrostepDistance);
            lock (_queueStepLock)
            {
                var now = SystemTimestamp.Now;
                timestamp = ResetInner(timestamp, McuStepperResetFlags.None, out _, null, now, false, 0, TimeSpan.Zero);
                EnsureEnabledInner(timestamp);
                timestamp = AddDacSafetyDelay(timestamp, maxDistance);
                lock (_updateDacCmd.Cmd)
                {
                    _updateDacCmd.Cmd[_updateDacCmd.Value] = count;
                    var occasion = new McuOccasion((timestamp - _sendAheadDuration).Clock, timestamp.Clock);
                    _mcu.Send(
                        _updateDacCmd.Cmd,
                        McuCommandPriority.Printing,
                        occasion);
                }
                timestamp = AddDacSafetyDelay(timestamp, maxDistance);
            }
            return timestamp;
        }

        public async Task<bool> EndstopMove(
            EndstopSensitivity sensitivity,
            double maxVelocity,
            Func<McuTimestamp, McuTimestamp> queueSteps,
            double? clearanceSteps,
            bool expectedEndstopState,
            CancellationToken cancel)
        {
            if (maxVelocity < 0)
                throw new ArgumentOutOfRangeException(nameof(maxVelocity), $"{nameof(maxVelocity)} cannot be negative");
            if (_endstopPinDesc == null)
                throw new InvalidOperationException($"Cannot home stepper {this}, endstop pin was not set");

            var options = _options.Value;
            var optionsGlobal = _optionsGlobal.Value;
            var pinterval = GetPrecisionIntervalFromVelocity(maxVelocity);
            var tinterval = pinterval / _intervalPrecisionMul;

            using (await _homingLock.LockAsync(cancel))
            {
                Task<McuCommand> responseTask;
                var stepCommandIds = new HashSet<McuSendResult>();
                try
                {
                    if (_driver != null)
                        await _driver.BeginEndstopMove(sensitivity, cancel);
                    var timestamp = Reset(default, McuStepperResetFlags.Force, out _);
                    lock (_homeCmd)
                    {
                        if (clearanceSteps == null)
                            clearanceSteps = (4 * (_driver?.Microsteps ?? 1) + 0.5f); // NOTE: start sampling 4 steps and 0.5 microsteps after we have started
                        var occasion = new McuOccasion((timestamp - _sendAheadDuration).Clock, timestamp.Clock);
                        var sampleTicks = _mcu.ClockSync.GetClockDuration(optionsGlobal.EndstopSampleTime);
                        _homeCmd
                            .Bind("clock", timestamp.Clock + (long)(tinterval * clearanceSteps.Value))
                            .Bind("sample_ticks", (int)sampleTicks)
                            .Bind("sample_count", options.EndstopSampleCount ?? optionsGlobal.EndstopSampleCount)
                            .Bind("rest_ticks", (int)Math.Max(sampleTicks, tinterval)) // TODO: calculate better?
                            .Bind("pin_value", expectedEndstopState ^ _endstopPinDesc.Invert ? 1 : 0);
                        responseTask = _mcu.SendWithResponse(
                            _homeCmd,
                            _endstopStateResponse,
                            response => response["oid"].Int32 == _endstopOid!.Value,
                            McuCommandPriority.Printing,
                            occasion,
                            timeout: TimeSpan.MaxValue,
                            cancel: cancel);
                    }
                    _homingStepCommandIds = stepCommandIds;
                    timestamp = queueSteps(timestamp);
                    var duration = timestamp.ToSystem() - SystemTimestamp.Now + optionsGlobal.FinishHomeDelay;
                    if (duration < TimeSpan.Zero)
                        duration = TimeSpan.Zero;
                    var timeoutTask = Task.Delay(duration, cancel);
                    var task = await Task.WhenAny(responseTask, timeoutTask);
                    await task; // cancellation
                    var hasEndedOnEndstop = task != timeoutTask;
                    if (hasEndedOnEndstop) // if we ended on endstop, _lastClock would be in the future, reset next
                        _lastClock = default;
                    return (hasEndedOnEndstop ? expectedEndstopState : !expectedEndstopState);
                }
                finally
                {
                    _homingStepCommandIds = null;
                    _mcu.SendCancel(stepCommandIds);

                    // stop checking
                    lock (_homeCmd)
                    {
                        _homeCmd
                            .Bind("clock", 0)
                            .Bind("sample_ticks", 0)
                            .Bind("sample_count", 0)
                            .Bind("rest_ticks", 0)
                            .Bind("pin_value", 0);
                        _mcu.Send(
                            _homeCmd,
                            McuCommandPriority.Printing,
                            McuOccasion.Now);
                    }
                    if (_driver != null)
                        await _driver.FinishEndstopMove(default); // NOTE: no cancellation here
                }
            }
        }

        public Task<bool> EndstopMove(
            EndstopSensitivity sensitivity,
            double velocity,
            double startPosition,
            double finalPosition,
            double? clearanceSteps,
            bool expectedEndstopState,
            CancellationToken cancel)
        {
            if (velocity < 0)
                throw new ArgumentOutOfRangeException(nameof(velocity), $"{nameof(velocity)} cannot be negative");

            var pinterval = GetPrecisionIntervalFromVelocity(velocity);
            var tinterval = pinterval / _intervalPrecisionMul;
            var startCount = (long)Math.Round(startPosition / MicrostepDistance);
            var endCount = (long)Math.Round(finalPosition / MicrostepDistance);
            var count = Math.Abs(endCount - startCount);
            var positive = endCount >= startCount;

            return EndstopMove(
                sensitivity,
                velocity,
                (timestamp) => QueueStep(positive, pinterval, count, 0, timestamp, true, default, default, false),
                clearanceSteps,
                expectedEndstopState,
                cancel);
        }

        private void SetLastClockInner(in McuTimestamp timestamp)
        {
            Debug.Assert(!timestamp.IsEmpty && timestamp.Precision == _intervalPrecision);
            Debug.Assert(timestamp.ClockPrecise >= _lastClock.ClockPrecise);
            Debug.Assert(Monitor.IsEntered(_queueStepLock));
            _lastClock = timestamp;
        }

        private static int GetMaxStepMoves(int ver)
            => ver switch
            {
                1 => MaxStepMovesInV1Cmd,
                2 => MaxStepMovesInV2Cmd,
                3 => MaxStepMovesInV3Cmd,
                4 => MaxStepMovesInV4Cmd,
                _ => throw new ArgumentOutOfRangeException(nameof(ver)),
            };

        private McuCommand PrepareCommand(out int consumedItems, out bool isFull, out int ver)
        {
            var steps = _cmdSteps.Span;
            Debug.Assert(steps.Length > 0);
            var buffer = _cmdStepBuffer;
            int maxItems;

            //// universal but unoptimised approach (commented out, testing only)
            //ver = 1;
            //maxItems = GetMaxStepMoves(ver);
            //consumedItems = Math.Min(steps.Length, maxItems);
            //isFull = consumedItems == maxItems;
            //buffer.Count = Unsafe.SizeOf<StepMoveV1>() * consumedItems;
            //MemoryMarshal.AsBytes(steps.Slice(0, consumedItems)).CopyTo(buffer.Span);
            //_queueStepsV1Cmd[1] = buffer.Segment;
            //return _queueStepsV1Cmd;

            if (_pwmPin != null) // try pwm specific approach first
            {
                ver = 4; // specific PWM choice

                buffer.Count = Unsafe.SizeOf<StepMoveV4>() * MaxStepMovesInV4Cmd;
                var v4 = MemoryMarshal.Cast<byte, StepMoveV4>(buffer.Span);
                var inputCount = 0;
                var outputCount = 0;
                var delay = 0L;
                var lastPower = int.MinValue;
                var maxInterval = Math.Min(_maxInterval32, StepMoveV4.MaxInterval);
                for (var i = 0; i < steps.Length; i++)
                {
                    ref var item = ref steps[i];
                    var type = item.Type;
                    if (type == StepMoveType.Dwell)
                    {
                        var newDelay = delay + item.Interval;
                        Debug.Assert(delay <= maxInterval);
                        if (lastPower != int.MinValue &&
                            newDelay > maxInterval)
                        {
                            if (delay > 0)
                                v4[outputCount++] = new StepMoveV4((uint)delay, (ushort)lastPower);
                            delay = item.Interval;
                            inputCount = i;
                        }
                        else if (lastPower != int.MinValue &&
                            newDelay < maxInterval &&
                            i + 1 == steps.Length)
                        {
                            if (newDelay > 0)
                                v4[outputCount++] = new StepMoveV4((uint)newDelay, (ushort)lastPower);
                            delay = 0;
                            inputCount = i + 1;
                        }
                        else
                            delay = newDelay;
                        if (delay > maxInterval)
                            break;
                    }
                    else if (type == StepMoveType.Pwm)
                    {
                        v4[outputCount++] = new StepMoveV4((uint)delay, item.Power);
                        lastPower = item.Power;
                        delay = 0;
                        inputCount = i + 1;
                        if (outputCount == MaxStepMovesInV4Cmd)
                            break;
                    }
                    else
                        throw new InvalidOperationException($"PWM stepper does not support move type: {type}");
                }

                if (inputCount > 0 && outputCount > 0)
                {
                    consumedItems = inputCount;
                    isFull = outputCount == MaxStepMovesInV4Cmd;
                    buffer.Count = Unsafe.SizeOf<StepMoveV4>() * outputCount;
                    _queueStepsV4Cmd[1] = buffer.Segment;
                    return _queueStepsV4Cmd;
                }
            }

            // determine best command version
            ver = 3; // best choice, will downgrade if neccessary
            maxItems = GetMaxStepMoves(ver);
            consumedItems = Math.Min(steps.Length, maxItems);
            for (int i = 0; i < consumedItems; i++)
            {
                ref var item = ref steps[i];
                var type = item.Type;
                if (type is StepMoveType.Move &&
                    item.Add != 0)
                {
                    if (i >= MaxStepMovesInV1Cmd)
                    {
                        consumedItems = i;
                        break;
                    }
                    ver = 1;
                    maxItems = MaxStepMovesInV1Cmd;
                    consumedItems = Math.Min(steps.Length, maxItems);
                    break;
                }
                if (ver == 3 &&
                    item.Type is StepMoveType.Move or StepMoveType.Dwell &&
                    item.Interval >= ushort.MaxValue)
                {
                    if (i >= MaxStepMovesInV2Cmd)
                    {
                        consumedItems = i;
                        break;
                    }
                    ver = 2;
                    maxItems = MaxStepMovesInV2Cmd;
                    consumedItems = Math.Min(steps.Length, maxItems);
                }
            }

            // convert steps to ver-specific values
            isFull = consumedItems == maxItems;
            switch (ver)
            {
                case 1:
                    buffer.Count = Unsafe.SizeOf<StepMoveV1>() * consumedItems;
                    MemoryMarshal.AsBytes(steps.Slice(0, consumedItems)).CopyTo(buffer.Span);
                    _queueStepsV1Cmd[1] = buffer.Segment;
                    return _queueStepsV1Cmd;
                case 2:
                    buffer.Count = Unsafe.SizeOf<StepMoveV2>() * consumedItems;
                    var v2 = MemoryMarshal.Cast<byte, StepMoveV2>(buffer.Span);
                    for (int i = 0; i < consumedItems; i++)
                        v2[i] = StepMoveV2.Create(steps[i]);
                    _queueStepsV2Cmd[1] = buffer.Segment;
                    return _queueStepsV2Cmd;
                case 3:
                    buffer.Count = Unsafe.SizeOf<StepMoveV3>() * consumedItems;
                    var v3 = MemoryMarshal.Cast<byte, StepMoveV3>(buffer.Span);
                    for (int i = 0; i < consumedItems; i++)
                        v3[i] = StepMoveV3.Create(steps[i]);
                    _queueStepsV3Cmd[1] = buffer.Segment;
                    return _queueStepsV3Cmd;
                default:
                    throw new InvalidOperationException($"Invalid step move version {ver}");
            }
        }

        private void FlushStepsInner(in McuOccasion occasion)
        {
            var usedStepCommandsIds = _homingStepCommandIds;
            while (true)
            {
                if (_cmdSteps.Count == 0)
                    return;
                var cmd = PrepareCommand(out var consumedItems, out var isFull, out var ver);
                if (_cmdValuesSentCount == 0 || !_mcu.TryReplace(_cmdValuesId, cmd))
                {
#if DEBUG
                    Interlocked.Increment(ref _verSentCount[_cmdValuesVer]);
#endif
                    if (_cmdValuesSentCount != 0) // remove previously sent moves from buffer
                    {
                        _cmdSteps.RemoveFromBeginning(_cmdValuesSentCount);
                        _cmdValuesSentCount = 0;
                        continue;
                    }

                    _cmdValuesId = _mcu.Send(cmd, McuCommandPriority.Printing, occasion);
                    usedStepCommandsIds?.Add(_cmdValuesId);
                }

                Debug.Assert(_cmdSteps.Count <= 100 /* sane value */);
                if (isFull || consumedItems < _cmdSteps.Count) // we cant cram anything else in there or we have still some steps left
                {
                    _cmdSteps.RemoveFromBeginning(consumedItems);
                    _cmdValuesSentCount = 0;
                    _cmdValuesVer = ver;
                    continue;
                }
                else
                {
                    _cmdValuesSentCount = consumedItems;
                    _cmdValuesVer = ver;
                    break;
                }
            }
        }

        private void QueueFlushInner()
        {
            if (_cmdValuesSentCount > 0)
            {
#if DEBUG
                Interlocked.Increment(ref _verSentCount[_cmdValuesVer]);
#endif
                _cmdSteps.RemoveFromBeginning(_cmdValuesSentCount);
                _cmdValuesSentCount = 0;
            }
            if (_dumpWriter != null)
                _dumpWriter.Flush();
        }

        private void SendStepInner(in StepMoveV1 move, in McuOccasion occasion)
        {
            if (_dumpWriter != null)
                _dumpWriter.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<StepMoveV1>(in move)));
            _cmdSteps.Add() = move;
            FlushStepsInner(occasion);
        }

        private void EnsureEnabledInner(McuTimestamp timestamp, bool enabled = true)
        {
            if (_enabled != enabled)
            {
                if (timestamp.IsImmediate || timestamp.IsEmpty)
                    _enablePin?.Set(enabled, McuCommandPriority.Printing, McuTimestamp.Immediate(_enablePin.Mcu));
                else
                    _enablePin?.Set(enabled, McuCommandPriority.Printing, timestamp);
                _enabled = enabled;
            }
        }

        public McuTimestamp Reset(McuTimestamp timestamp, McuStepperResetFlags flags, out bool hasReset, SystemTimestamp now = default, double advance = 0, TimeSpan minQueueAheadDuration = default)
        {
            lock (_queueStepLock)
            {
                return ResetInner(timestamp, flags, out hasReset, null, now, false, advance, minQueueAheadDuration);
            }
        }

        private McuTimestamp ResetInner(McuTimestamp timestamp, McuStepperResetFlags flags, out bool hasReset, McuMinClockFunc? minClock, SystemTimestamp now, bool dryRun, double advance, TimeSpan minQueueAheadDuration)
        {
            Debug.Assert(Monitor.IsEntered(_queueStepLock));
            if (advance < 0)
                throw new ArgumentOutOfRangeException(nameof(advance));
            if (now.IsEmpty)
                now = SystemTimestamp.Now;
            if ((flags & McuStepperResetFlags.ThrowIfResetNecessary) != 0)
            {
                if (timestamp.IsEmpty || timestamp.Precision != _intervalPrecision)
                    throw new InvalidOperationException($"Timestamp is empty or has invalid precision and reset was not enabled in the call for stepper {this}, timestamp={timestamp}, now = {now}");
            }
            else
                timestamp = timestamp.WithPrecision(_intervalPrecision);
            Debug.Assert(_lastClock.IsEmpty || _lastClock.Precision == _intervalPrecision);

            if (!flags.HasFlag(McuStepperResetFlags.Force) && !_lastClock.IsEmpty && !timestamp.IsEmpty && _lastClock.ToSystem() >= now + _underflowDuration)
            {
                if (_lastClock > timestamp)
                    throw new InvalidOperationException($"Stepper {this} clock has jumped to the past. LastClock={_lastClock}, timestamp={timestamp}, now={McuTimestamp.FromSystem(_mcu, now).Clock}, flags={flags})");
                timestamp += advance;
                if (_lastClock < timestamp)
                {
                    timestamp = QueueDwell(
                        timestamp.GetClockWithPrecision(_intervalPrecision) - _lastClock.GetClockWithPrecision(_intervalPrecision),
                        _lastClock,
                        minClock: minClock,
                        dryRun: dryRun);
                }
                hasReset = false;
            }
            else
            {
                var requestedTimestamp = timestamp;

                if (flags.HasFlag(McuStepperResetFlags.ThrowIfResetNecessary))
                    throw new McuStepperResetNecessaryException($"Reset was neccessary and was not enabled in the call for stepper {this}. Is the CPU having trouble preparing moves in time, or is this an indication of bug? (lastClock={_lastClock}, now={McuTimestamp.FromSystem(_mcu, now).Clock}, requested={requestedTimestamp}, flags={flags})");

                if (flags.HasFlag(McuStepperResetFlags.Force) || _lastClock.IsEmpty || timestamp.IsEmpty || timestamp.ToSystem() < now + _sendAheadDuration) // timestamp is empty or in the past
                {
                    timestamp = McuTimestamp.FromSystem(_mcu, now, _intervalPrecision) + Math.Max(_queueAheadDuration.TotalSeconds, minQueueAheadDuration.TotalSeconds);
                    if (!requestedTimestamp.IsEmpty && timestamp < requestedTimestamp)
                        timestamp = requestedTimestamp;
                    if (timestamp < _lastClock)
                        throw new InvalidOperationException($"New timestamp for reset {timestamp.Clock} (from now) is behind lastClock={_lastClock}");
                    _logger.LogDebug($"Resetting stepper {this} clock moved from {_lastClock} to {timestamp.Clock} (from now={McuTimestamp.FromSystem(_mcu, now).Clock}, flags={flags}, requested={requestedTimestamp}, advance={advance}, minQueueAheadDuration={minQueueAheadDuration})");
                }
                else
                {
                    if (timestamp < _lastClock)
                        throw new InvalidOperationException($"New timestamp for reset {timestamp.Clock} is behind lastClock={_lastClock}");
                    _logger.LogDebug($"Resetting stepper {this} clock moved from {_lastClock} to {timestamp.Clock} (from external timestamp, flags={flags}, requested={requestedTimestamp}, advance={advance})");
                }

                if (advance != 0)
                {
                    timestamp += advance;
                    _logger.LogDebug($"Advanced stepper {this} during reset by {advance} to {timestamp}");
                }

                // give MCU some time to finish all moves before reset
                if (!_lastClock.IsEmpty && timestamp.ToRelativeSeconds() - _lastClock.ToRelativeSeconds() <= _sendAheadDuration.TotalSeconds)
                {
                    timestamp = timestamp + _sendAheadDuration;
                    _logger.LogDebug($"Moving timestamp for stepper {this} even bit further in to the future to {timestamp.Clock} since it was too close to {nameof(_lastClock)}");
                }

                // send
                if (minClock != null)
                    throw new InvalidOperationException($"Cannot use minClock while resetting stepper {this}");

                QueueFlushInner();
                _resetCmd.Cmd[_resetCmd.Clock] = (uint)timestamp.ClockPrecise;
                _resetCmd.Cmd[_resetCmd.ClockHi] = (uint)(timestamp.ClockPrecise >> 32);
                _mcu.Send(_resetCmd.Cmd, McuCommandPriority.Printing, new McuOccasion((timestamp - _sendAheadDuration).Clock, timestamp.Clock));
                SetLastClockInner(timestamp);
                hasReset = true;
            }

            if (hasReset)
            {
                var options = _options.Value;
                if (options.DumpCommandsFileFormat != null && options.DumpCommandsToFile)
                {
                    if (_dumpWriter != null)
                    {
                        _dumpWriter.Dispose();
                        _dumpWriter = null;
                    }
                    if (options.DumpCommandsFileFormat != null && options.DumpCommandsToFile)
                    {
                        var dumpCounter = Interlocked.Increment(ref _dumpCounter) - 1;
                        var dumpFilename = string.Format(options.DumpCommandsFileFormat, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"), dumpCounter);
                        var dumpDirectory = Path.GetDirectoryName(dumpFilename);
                        if (!string.IsNullOrEmpty(dumpDirectory))
                            Directory.CreateDirectory(dumpDirectory);
                        _dumpWriter = new BinaryWriter(File.Open(dumpFilename, FileMode.Create, FileAccess.Write, FileShare.Read));
                    }
                }
            }

            return timestamp;
        }

        private McuOccasion GetOrUpdateQueueOccasion(McuMinClockFunc? minClock, in McuTimestamp timestamp)
        {
            var minClockSuggested = (timestamp - _sendAheadDuration).Clock;
            if (minClock == null)
                return new McuOccasion(minClockSuggested, timestamp.Clock);
            else
                return new McuOccasion(minClock(minClockSuggested, timestamp.Clock), timestamp.Clock);
        }

        public override McuTimestamp QueueStep(
            bool positive,
            long precisionInterval,
            long count,
            long add,
            McuTimestamp timestamp,
            McuMinClockFunc? minClock = null,
            SystemTimestamp now = default,
            bool dryRun = false)
            => QueueStep(positive, precisionInterval, count, add, timestamp, true, minClock, now, dryRun);

        private McuTimestamp QueueStep(
            bool positive,
            long precisionInterval,
            long count,
            long add,
            McuTimestamp timestamp,
            bool throwIfResetNecessary,
            McuMinClockFunc? minClock,
            SystemTimestamp now,
            bool dryRun)
        {
            if (precisionInterval <= 0)
                throw new ArgumentException($"{nameof(precisionInterval)} must be positive.", nameof(precisionInterval));
            if (count < 0)
                throw new ArgumentException($"{nameof(count)} must not be negative.", nameof(count));
            if (_stepPinDesc == null || _dirPinDesc == null)
                throw new InvalidOperationException($"Cannot step stepper {this}, step and/or dir pin was not configured");
            if (_dirPinDesc.Invert)
                positive = !positive;
            if (count <= 1)
                add = 0;

            lock (_queueStepLock)
            {
                timestamp = ResetInner(timestamp, (throwIfResetNecessary ? McuStepperResetFlags.ThrowIfResetNecessary : 0), out _, minClock, now, dryRun, 0, TimeSpan.Zero);
                if (!dryRun)
                    EnsureEnabledInner(timestamp);

                if (count > 0)
                {
                    var maxIntervalV1 = Math.Min(_maxInterval32, StepMoveV1.MaxInterval);
                    var occasion = GetOrUpdateQueueOccasion(minClock, timestamp);

                    if (precisionInterval <= maxIntervalV1 && add == 0) // simplest case, short time no acceleration
                    {
                        var innerSteps = (count + MaxStepsInCmd - 1) / MaxStepsInCmd;
                        for (int i = 0; i < innerSteps; i++)
                        {
                            var steps = (int)(count * (i + 1) / innerSteps - count * i / innerSteps);

                            if (!dryRun)
                                SendStepInner(StepMoveV1.Move(checked((uint)precisionInterval), checked((short)(positive ? steps : -steps)), 0), occasion);

                            timestamp = AdvancePrecise(timestamp, steps * precisionInterval);
                            occasion = GetOrUpdateQueueOccasion(minClock, timestamp);
                        }
                    }
                    else if (precisionInterval <= maxIntervalV1) // more complex case, solvable even with accelleration
                    {
                        var finalPrecisionInterval = precisionInterval + count * add;
                        if (finalPrecisionInterval > maxIntervalV1 || finalPrecisionInterval < 0)
                            throw new InvalidOperationException($"Step interval for stepper {this} is too large or goes below zero, this can be possibly solved by increasing {nameof(McuStepperOptions.MinVelocity)}");

                        var remainder = count;
                        do
                        {
                            var steps = Math.Min(remainder, MaxStepsInCmd);
                            var pticks = add * steps * (steps - 1) / 2 + precisionInterval * steps;

                            if (!dryRun)
                                SendStepInner(StepMoveV1.Move(checked((uint)precisionInterval), checked((short)(positive ? steps : -steps)), checked((short)add)), occasion);

                            remainder -= steps;
                            timestamp = AdvancePrecise(timestamp, pticks);
                            occasion = GetOrUpdateQueueOccasion(minClock, timestamp);
                        }
                        while (remainder > 0);
                    }
                    else // case with long intervals or long dwells, no acceleration supported
                    {
                        if (add != 0)
                            throw new InvalidOperationException($"Step interval for stepper {this} is too large and acceleration is enabled, this can be possibly solved by increasing {nameof(McuStepperOptions.MinVelocity)}");

                        var remainder = count;
                        do
                        {
                            var innerSteps = (precisionInterval + maxIntervalV1 - 1) / maxIntervalV1;
                            for (int i = 0; i < innerSteps; i++)
                            {
                                var pticks = (int)(precisionInterval * (i + 1) / innerSteps - precisionInterval * i / innerSteps);

                                if (!dryRun)
                                {
                                    if (i == 0)
                                        SendStepInner(StepMoveV1.Move((uint)pticks, positive ? (short)1 : (short)-1, 0), occasion);
                                    else
                                        SendStepInner(StepMoveV1.Dwell((uint)pticks), occasion);
                                }

                                timestamp = AdvancePrecise(timestamp, pticks);
                                occasion = GetOrUpdateQueueOccasion(minClock, timestamp);
                            }

                            remainder--;
                        }
                        while (remainder > 0);
                    }
                }

                SetLastClockInner(timestamp);
                return timestamp;
            }
        }

        private McuTimestamp AdvancePrecise(in McuTimestamp timestamp, long pticks)
        {
            Debug.Assert(!timestamp.IsEmpty && timestamp.Precision == _intervalPrecision);
            return new McuTimestamp(timestamp.Mcu, timestamp.ClockPrecise + pticks, _intervalPrecision);
        }

        public McuTimestamp QueueDwell(long precisionInterval, McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false)
        {
            if (precisionInterval < 0)
                throw new ArgumentException($"{nameof(precisionInterval)} must not be negative.", nameof(precisionInterval));

            lock (_queueStepLock)
            {
                timestamp = ResetInner(timestamp, McuStepperResetFlags.ThrowIfResetNecessary, out _, minClock, now, dryRun, 0, TimeSpan.Zero);
                if (!dryRun)
                    EnsureEnabledInner(timestamp);

                if (precisionInterval > 0)
                {
                    long maxInterval;
                    if (precisionInterval > StepMoveV3.MaxInterval * 2) // allow 2x V3 max intervals, since we can cram almost twice data to V3, otherwise dont bother and use even larger values
                        maxInterval = StepMoveV1.MaxInterval;
                    else
                        maxInterval = StepMoveV3.MaxInterval;
                    if (maxInterval > _maxInterval32)
                        maxInterval = _maxInterval32;
                    var innerSteps = (precisionInterval + maxInterval - 1) / maxInterval;
                    for (int i = 0; i < innerSteps; i++)
                    {
                        var pticks = (int)(precisionInterval * (i + 1) / innerSteps - precisionInterval * i / innerSteps);
                        var occasion = GetOrUpdateQueueOccasion(minClock, timestamp);
                        if (!dryRun)
                            SendStepInner(StepMoveV1.Dwell((uint)pticks), occasion);

                        timestamp = AdvancePrecise(timestamp, pticks);
                    }
                }

                SetLastClockInner(timestamp);
                return timestamp;
            }
        }

        public McuTimestamp QueuePwm(float value, McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false)
        {
            var pwm = (McuHardPwmPin?)_pwmPin;
            if (pwm == null)
                throw new InvalidOperationException($"Pwm pin was not setup for stepper {this}");

            lock (_queueStepLock)
            {
                timestamp = ResetInner(timestamp, McuStepperResetFlags.ThrowIfResetNecessary, out _, minClock, now, dryRun, 0, TimeSpan.Zero);
                if (!dryRun)
                    EnsureEnabledInner(timestamp);

                var occasion = GetOrUpdateQueueOccasion(minClock, timestamp);
                if (!dryRun)
                    SendStepInner(StepMoveV1.Pwm(checked((ushort)Math.Clamp(MathF.Round(value * pwm.PwmMax), 0, pwm.PwmMax))), occasion);

                SetLastClockInner(timestamp);
                return timestamp;
            }
        }

        public McuTimestamp QueueNextStepWaketimeVerify(McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false)
        {
            lock (_queueStepLock)
            {
                timestamp = ResetInner(timestamp, McuStepperResetFlags.ThrowIfResetNecessary, out _, minClock, now, dryRun, 0, TimeSpan.Zero);
                if (!dryRun)
                    EnsureEnabledInner(timestamp);

                var occasion = GetOrUpdateQueueOccasion(minClock, timestamp);
                if (!dryRun)
                {
                    _verifyNextStepWaketimeCmd.Cmd[_verifyNextStepWaketimeCmd.Clock] = (uint)timestamp.ClockPrecise;
                    _verifyNextStepWaketimeCmd.Cmd[_verifyNextStepWaketimeCmd.ClockHi] = (uint)(timestamp.ClockPrecise >> 32);
                    _mcu.Send(_verifyNextStepWaketimeCmd.Cmd, McuCommandPriority.Printing, occasion);
                }

                SetLastClockInner(timestamp);
                return timestamp;
            }
        }

        public void QueueFlush()
        {
            lock (_queueStepLock)
            {
                QueueFlushInner();
            }
        }

        public McuTimestamp Enable(bool enabled, McuTimestamp timestamp)
        {
            lock (_queueStepLock)
            {
                timestamp = ResetInner(timestamp, McuStepperResetFlags.ThrowIfResetNecessary, out _, null, default, false, 0, TimeSpan.Zero);
                EnsureEnabledInner(timestamp, enabled);
            }
            return timestamp;
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            var options = _options.Value;
            _criticalMinInterval = GetPrecisionIntervalFromVelocity(_criticalVelocity);
            _stepperOid = commands.CreateOid();
            if (_stepPinDesc != null)
            {
                commands.Add(
                    _mcu.LookupCommand("config_stepper oid=%c step_pin=%c dir_pin=%c min_interval=%u invert_step=%c no_delay=%c interval_precision=%c")
                    .Bind(
                        _stepperOid,
                        _mcu.Config.GetPin(_stepPinDesc!.Pin),
                        _mcu.Config.GetPin(_dirPinDesc!.Pin),
                        _criticalMinInterval,
                        _stepPinDesc.Invert ? 1 : 0,
                        options.NoDelay ? 1 : 0,
                        options.IntervalPrecision));
            }
            else
            {
                var pwm = (McuHardPwmPin)_pwmPin!;
                commands.Add(
                    _mcu.LookupCommand("config_pwm_stepper oid=%c pwm_oid=%c interval_precision=%c")
                    .Bind(
                        _stepperOid,
                        pwm.Oid,
                        options.IntervalPrecision));
            }

            if (_endstopPinDesc != null)
            {
                _endstopOid = commands.CreateOid();
                commands.Add(
                    _mcu.LookupCommand("config_endstop oid=%c pin=%c pull_up=%c stepper_count=%c")
                    .Bind(
                        _endstopOid.Value,
                        _mcu.Config.GetPin(_endstopPinDesc.Pin),
                        _endstopPinDesc.Pullup,
                        1));
                commands.Add(
                    _mcu.LookupCommand("endstop_set_stepper oid=%c pos=%c stepper_oid=%c")
                    .Bind(_endstopOid.Value, 0, _stepperOid));
                commands.Add(
                    _mcu.LookupCommand("endstop_home oid=%c clock=%u sample_ticks=%u sample_count=%c rest_ticks=%u pin_value=%c")
                    .Bind(_endstopOid.Value, 0, 0, 0, 0, 0),
                   onRestart: true);
            }

            _queueStepsV1Cmd = _mcu.LookupCommand("queue_steps_v1 oid=%c steps=%*s")
                .Bind("oid", _stepperOid);
            _queueStepsV1Cmd.IsTimingCritical = true;
            _queueStepsV2Cmd = _mcu.LookupCommand("queue_steps_v2 oid=%c steps=%*s")
                .Bind("oid", _stepperOid);
            _queueStepsV2Cmd.IsTimingCritical = true;
            _queueStepsV3Cmd = _mcu.LookupCommand("queue_steps_v3 oid=%c steps=%*s")
                .Bind("oid", _stepperOid);
            _queueStepsV3Cmd.IsTimingCritical = true;
            _queueStepsV4Cmd = _mcu.LookupCommand("queue_steps_v4 oid=%c steps=%*s")
                .Bind("oid", _stepperOid);
            _queueStepsV4Cmd.IsTimingCritical = true;

            _resetDacCmd = _mcu.LookupCommand("reset_stepper_dac oid=%c").Bind(
                _stepperOid);
            _updateDacCmd.Cmd = _mcu.LookupCommand("update_stepper_dac oid=%c value=%u").Bind(
                _stepperOid,
                0);
            _updateDacCmd.Value = _updateDacCmd.Cmd.GetArgumentIndex("value");

            _resetCmd.Cmd = _mcu.LookupCommand("reset_step_clock oid=%c clock=%u clockhi=%u").Bind(
                _stepperOid,
                0,
                0);
            _resetCmd.Cmd.IsTimingCritical = true;
            _resetCmd.Clock = _resetCmd.Cmd.GetArgumentIndex("clock");
            _resetCmd.ClockHi = _resetCmd.Cmd.GetArgumentIndex("clockhi");

            if (_endstopOid != null)
            {
                _homeCmd = _mcu.LookupCommand("endstop_home oid=%c clock=%u sample_ticks=%u sample_count=%c rest_ticks=%u pin_value=%c")
                    .Bind("oid", _endstopOid.Value);
                _homeCmd.IsTimingCritical = true;
                _endstopQueryCmd = _mcu.LookupCommand("endstop_query_state oid=%c")
                    .Bind("oid", _endstopOid.Value);
            }
            _endstopStateResponse = _mcu.LookupCommand("endstop_state oid=%c homing=%c pin_value=%c");

            _verifyNextStepWaketimeCmd.Cmd = _mcu.LookupCommand("verify_next_step_waketime oid=%c clock=%u clockhi=%u")
                    .Bind("oid", _stepperOid);
            _verifyNextStepWaketimeCmd.Cmd.IsTimingCritical = true;
            _verifyNextStepWaketimeCmd.Clock = _verifyNextStepWaketimeCmd.Cmd.GetArgumentIndex("clock");
            _verifyNextStepWaketimeCmd.ClockHi = _verifyNextStepWaketimeCmd.Cmd.GetArgumentIndex("clockhi");

            commands.Add(
                _resetCmd.Cmd.Clone(),
                onRestart: true);
            return ValueTask.CompletedTask;
        }

        public async Task<bool> QueryEndstop(CancellationToken cancel)
        {
            Task<McuCommand> task;
            lock (_endstopQueryCmd)
            {
                task = _mcu.SendWithResponse(
                    _endstopQueryCmd,
                    _endstopStateResponse,
                    response => response["oid"].Int32 == _endstopOid!.Value,
                    McuCommandPriority.Printing,
                    McuOccasion.Now,
                    cancel: cancel);
            }
            var response = await task;
            return response["pin_value"].Boolean;
        }

        public override string ToString()
            => $"{_name} (pin={(_stepPinDesc?.Key.ToString() ?? _pwmPin?.Pin.Key.ToString())!})";
    }
}
