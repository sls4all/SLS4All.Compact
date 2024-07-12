// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public readonly record struct TrapezoidValue(double Timestamp, double Velocity, double Position);

    public sealed class TrapezoidCalculator
    {
        public double ClampMinVelocity { get; set; } = 0;
        public double ClampMaxVelocity { get; set; } = double.MaxValue;

        public PrimitiveList<TrapezoidValue> Values { get; } = new();

        public double Move(
            double initialVelocity,
            double finalVelocity,
            double acceleration,
            double decceleration,
            double maxVelocity,
            double startPosition,
            double finalPosition,
            double timestamp)
        {
            if (initialVelocity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialVelocity), $"{nameof(initialVelocity)} cannot be negative");
            if (finalVelocity < 0)
                throw new ArgumentOutOfRangeException(nameof(finalVelocity), $"{nameof(finalVelocity)} cannot be negative");
            if (acceleration < 0)
                throw new ArgumentOutOfRangeException(nameof(acceleration), $"{nameof(acceleration)} cannot be negative");
            if (decceleration < 0)
                throw new ArgumentOutOfRangeException(nameof(decceleration), $"{nameof(decceleration)} cannot be negative");
            if (maxVelocity <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxVelocity), $"{nameof(maxVelocity)} cannot be zero or negative");

            const double largeAccelerationMultiplier = 1000000;
            if (acceleration == 0 && decceleration == 0)
                return Move(maxVelocity, maxVelocity, startPosition, finalPosition, timestamp);
            else if (acceleration == 0)
            {
                initialVelocity = maxVelocity;
                acceleration = maxVelocity * largeAccelerationMultiplier;
            }
            else if (decceleration == 0)
            {
                finalVelocity = maxVelocity;
                decceleration = maxVelocity * largeAccelerationMultiplier;
            }

            var distance = Math.Abs(finalPosition - startPosition);
            var direction = Math.Sign(finalPosition - startPosition);

            var distance1 = (finalVelocity * finalVelocity - initialVelocity * initialVelocity) / (-2 * decceleration);
            if (distance1 >= distance)
                timestamp = Move(initialVelocity, finalVelocity, startPosition, finalPosition, timestamp);
            else
            {
                var midDistance1 = (maxVelocity * maxVelocity - initialVelocity * initialVelocity) / (2 * acceleration);
                var midPosition1 = startPosition + direction * midDistance1;
                var midDistance2 = (finalVelocity * finalVelocity - maxVelocity * maxVelocity) / (-2 * decceleration);
                var midPosition2 = finalPosition - direction * midDistance2;
                if ((midPosition2 >= midPosition1 && finalPosition >= startPosition) ||
                    (midPosition2 <= midPosition1 && finalPosition <= startPosition))
                {
                    timestamp = Move(initialVelocity, maxVelocity, startPosition, midPosition1, timestamp);
                    timestamp = Move(maxVelocity, maxVelocity, midPosition1, midPosition2, timestamp);
                    timestamp = Move(maxVelocity, finalVelocity, midPosition2, finalPosition, timestamp);
                }
                else
                {
                    var newMaxVelocity = Math.Sqrt(
                        (acceleration + decceleration) *
                        (2 * acceleration * decceleration * distance + acceleration * finalVelocity * finalVelocity + decceleration * initialVelocity * initialVelocity)) /
                        (acceleration + decceleration);
                    Debug.Assert(newMaxVelocity > 0 && newMaxVelocity <= maxVelocity);
                    var midDistance = (newMaxVelocity * newMaxVelocity - initialVelocity * initialVelocity) / (2 * acceleration);
                    var midPosition = startPosition + direction * midDistance;
                    timestamp = Move(initialVelocity, newMaxVelocity, startPosition, midPosition, timestamp);
                    timestamp = Move(newMaxVelocity, finalVelocity, midPosition, finalPosition, timestamp);
                }
            }
            return timestamp;
        }

        public double Move(
            double initialVelocity,
            double finalVelocity,
            double startPosition,
            double finalPosition,
            double timestamp)
        {
            if (initialVelocity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialVelocity), $"{nameof(initialVelocity)} cannot be negative");
            if (finalVelocity < 0)
                throw new ArgumentOutOfRangeException(nameof(finalVelocity), $"{nameof(finalVelocity)} cannot be negative");

            initialVelocity = Math.Clamp(initialVelocity, ClampMinVelocity, ClampMaxVelocity);
            finalVelocity = Math.Clamp(finalVelocity, ClampMinVelocity, ClampMaxVelocity);

            var time = Math.Abs(finalPosition - startPosition) * 2 / (initialVelocity + finalVelocity);
            var finalTimestamp = timestamp + time;

            Add(new(timestamp, initialVelocity, startPosition));
            Add(new(finalTimestamp, finalVelocity, finalPosition));
            return finalTimestamp;
        }

        private void Add(in TrapezoidValue value)
        {
            if (Values.Count == 0 || Values[^1] != value)
                Values.Add(value);
        }
    }
}
