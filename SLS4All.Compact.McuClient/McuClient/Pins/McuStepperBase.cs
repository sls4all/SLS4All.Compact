// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using System.Diagnostics;

namespace SLS4All.Compact.McuClient.Pins
{
    public abstract class McuStepperBase
    {
        private readonly TrapezoidCalculator _trapezoid;
        private long _minPrecisionIntervalLazy;

        public abstract int IntervalPrecisionMul { get; }
        public abstract double FullStepDistance { get; }
        public abstract double MicrostepDistance { get; }
        public abstract double MinVelocity { get; }
        public abstract double MaxVelocity { get; }
        public abstract double CriticalVelocity { get; }
        protected abstract TimeSpan AccelerationStepDuration { get; }
        protected abstract IMcuClockSync Clock { get; }
        public long MinPrecisionInterval
        {
            get
            {
                var minPrecisionInterval = _minPrecisionIntervalLazy;
                if (minPrecisionInterval == 0)
                    _minPrecisionIntervalLazy = minPrecisionInterval = GetPrecisionIntervalFromVelocity(MaxVelocity);
                return minPrecisionInterval;
            }
        }

        protected McuStepperBase()
        {
            _trapezoid = new();
        }

        private void InitTrapezoid()
        {
            _trapezoid.Values.Clear();
            _trapezoid.ClampMinVelocity = MinVelocity;
            _trapezoid.ClampMaxVelocity = MaxVelocity;
        }

        private McuTimestamp QueueTrapezoid(McuTimestamp timestamp)
        {
            var values = _trapezoid.Values.Span;
            for (int i = 1; i < values.Length; i++)
            {
                ref var prev = ref values[i - 1];
                ref var cur = ref values[i];

                var startCount = (long)Math.Round(prev.Position / MicrostepDistance);
                var endCount = (long)Math.Round(cur.Position / MicrostepDistance);
                var count = Math.Abs(endCount - startCount);
                var positive = endCount >= startCount;
                if (prev.Velocity == cur.Velocity || count == 0)
                {
                    var pinterval = GetPrecisionIntervalFromVelocity(cur.Velocity);
                    timestamp = QueueStep(positive, pinterval, count, 0, timestamp);
                }
                else
                {
                    var remainingCount = count;
                    var estimatedTotalSeconds = Math.Abs(cur.Position - prev.Position) * 2 / (cur.Velocity + prev.Velocity);
                    var estimatedTotalPTicks = Clock.GetClockDuration(estimatedTotalSeconds) * IntervalPrecisionMul;
                    var totalPTicks = 0L;
                    var accelerationStepPTicks = Clock.GetClockDurationDouble(AccelerationStepDuration.TotalSeconds) * IntervalPrecisionMul;
                    while (remainingCount != 0)
                    {
                        var stepPInterval = GetPrecisionIntervalFromVelocity(prev.Velocity + (cur.Velocity - prev.Velocity) * Math.Min(1.0, (double)totalPTicks / estimatedTotalPTicks));
                        var stepCount = remainingCount;
                        var stepPTicks = stepPInterval * stepCount;
                        if (stepPTicks > accelerationStepPTicks)
                        {
                            stepCount = Math.Min((long)Math.Ceiling(accelerationStepPTicks / stepPInterval), remainingCount);
                            stepPTicks = stepPInterval * stepCount;
                        }
                        var nextStepPInterval = GetPrecisionIntervalFromVelocity(prev.Velocity + (cur.Velocity - prev.Velocity) * Math.Min(1.0, (double)(totalPTicks + stepPTicks) / estimatedTotalPTicks));
                        var add = (nextStepPInterval - stepPInterval) / stepCount;
                        add = Math.Clamp(add, short.MinValue, short.MaxValue); // NOTE: clamp to short, should not matter much if add is too low in this case
                        timestamp = QueueStep(positive, stepPInterval, stepCount, add, timestamp);
                        remainingCount -= stepCount;
                        totalPTicks += stepPTicks;
                    }
                }
            }
            return timestamp;
        }

        public McuTimestamp Move(
            double initialVelocity,
            double finalVelocity,
            double acceleration,
            double decceleration,
            double maxVelocity,
            double startPosition,
            double finalPosition,
            McuTimestamp timestamp)
        {
            InitTrapezoid();
            _trapezoid.Move(
                initialVelocity,
                finalVelocity,
                acceleration,
                decceleration,
                maxVelocity,
                startPosition,
                finalPosition,
                0);
            return QueueTrapezoid(timestamp);
        }

        public McuTimestamp Move(
            double initialVelocity,
            double finalVelocity,
            double startPosition,
            double finalPosition,
            McuTimestamp timestamp)
        {
            InitTrapezoid();
            _trapezoid.Move(
                initialVelocity,
                finalVelocity,
                startPosition,
                finalPosition,
                0);
            return QueueTrapezoid(timestamp);
        }

        public McuTimestamp Move(
            double velocity,
            double startPosition,
            double finalPosition,
            McuTimestamp timestamp)
            => Move(velocity, velocity, startPosition, finalPosition, timestamp);

        public long GetPrecisionIntervalFromSeconds(double seconds)
        {
            return Clock.GetClockDuration(IntervalPrecisionMul * seconds);
        }

        public double GetPrecisionIntervalFromSecondsDouble(double seconds)
        {
            return Clock.GetClockDurationDouble(IntervalPrecisionMul * seconds);
        }

        public long GetPrecisionIntervalFromVelocity(double velocity)
        {
            velocity = Math.Clamp(velocity, MinVelocity, MaxVelocity);
            return Clock.GetClockDuration(IntervalPrecisionMul * MicrostepDistance / velocity);
        }

        public double GetPrecisionIntervalFromVelocityDouble(double velocity)
        {
            velocity = Math.Clamp(velocity, MinVelocity, MaxVelocity);
            return Clock.GetClockDurationDouble(IntervalPrecisionMul * MicrostepDistance / velocity);
        }

        public (long Count, double FinalPosition) GetSteps(double position)
        {
            var count = (long)Math.Round(position / MicrostepDistance);
            return (count, count * MicrostepDistance);
        }

        public (long PrecisionInterval, long Count, long Ticks, double PrecisionRemainder, double FinalPosition) GetSteps(double velocity, double startPosition, double endPosition, double precisionRemainder)
        {
            if (velocity < 0)
                throw new ArgumentOutOfRangeException(nameof(velocity), $"{nameof(velocity)} cannot be negative");

            velocity = Math.Clamp(velocity, MinVelocity, MaxVelocity);

            var startCount = (long)Math.Round(startPosition / MicrostepDistance);
            var endCount = (long)Math.Round(endPosition / MicrostepDistance);
            var count = Math.Abs(endCount - startCount);
            if (count == 0)
            {
                return (0, 0, 0, precisionRemainder, startPosition);
            }
            else
            {
                var minPInterval = MinPrecisionInterval;
                var pintervalf = GetPrecisionIntervalFromVelocityDouble(velocity) + precisionRemainder / count;
                var pinterval = (long)Math.Round(pintervalf);
                var pticks = pinterval * count;
                if (pticks < minPInterval)
                    return (0, 0, 0, pintervalf * count, startPosition);
                if (pinterval < minPInterval)
                {
                    pinterval = minPInterval;
					pintervalf = minPInterval;
                    pticks = pinterval * count;
                }
                var newPRemainder = pintervalf * count - pticks;
                var finalPosition = endCount * MicrostepDistance;
                return (pinterval, count, pticks, newPRemainder, finalPosition);
            }
        }

        public abstract McuTimestamp QueueStep(
            bool positive,
            long interval,
            long count,
            long add,
            McuTimestamp timestamp,
            SystemTimestamp now = default,
            bool dryRun = false);
    }
}