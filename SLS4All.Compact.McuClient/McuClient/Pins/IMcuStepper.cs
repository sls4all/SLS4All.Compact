// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;

namespace SLS4All.Compact.McuClient.Pins
{
    public delegate long McuMinClockFunc(long minClock, long reqClock);

    [Flags]
    public enum McuStepperResetFlags
    {
        None = 0,
        Force = 1,
        NoDwell = 2,
        ThrowIfResetNecessary = 4,
    }

    public interface IMcuStepper
    {
        IMcu Mcu { get; }
        long MinPrecisionInterval { get; }
        double MinVelocity { get; }
        double MaxVelocity { get; }
        double CriticalVelocity { get; }
        double? MinPwmCycleTime { get; }
        TimeSpan SendAheadDuration { get; }
        TimeSpan QueueAheadDuration { get; }
        double FullStepDistance { get; }
        double MicrostepDistance { get; }
        long GetPrecisionIntervalFromSeconds(double seconds);
        double GetPrecisionIntervalFromSecondsDouble(double seconds);
        long GetPrecisionIntervalFromVelocity(double velocity);
        double GetPrecisionIntervalFromVelocityDouble(double velocity);
        Task<bool> EndstopMove(
            EndstopSensitivity sensitivity,
            double velocity,
            double startPosition,
            double finalPosition,
            double? clearanceSteps,
            bool expectedEndstopState,
            CancellationToken cancel);
        Task<bool> EndstopMove(
            EndstopSensitivity sensitivity,
            double maxVelocity,
            Func<McuTimestamp, McuTimestamp> queueSteps,
            double? clearanceSteps,
            bool expectedEndstopState,
            CancellationToken cancel);

        McuTimestamp Move(double velocity, double startPosition, double endPosition, McuTimestamp timestamp);
        McuTimestamp Move(double initialVelocity, double finalVelocity, double startPosition, double endPosition, McuTimestamp timestamp);
        McuTimestamp Move(double initialVelocity, double finalVelocity, double acceleration, double decceleration, double maxVelocity, double startPosition, double finalPosition, McuTimestamp timestamp);
        McuTimestamp Enable(bool enable, McuTimestamp timestamp);
        McuTimestamp Reset(McuTimestamp timestamp, McuStepperResetFlags flags, out bool hasReset, SystemTimestamp now = default, double advance = 0, TimeSpan minQueueAheadDuration = default);
        McuTimestamp QueueStep(bool positive, long precisionInterval, long count, long add, McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false);
        McuTimestamp QueueDwell(long precisionInterval, McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false);
        McuTimestamp QueuePwm(float value, McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false);
        McuTimestamp QueueNextStepWaketimeVerify(McuTimestamp timestamp, McuMinClockFunc? minClock = null, SystemTimestamp now = default, bool dryRun = false);
        void QueueFlush();
        Task<bool> QueryEndstop(CancellationToken cancel);
        (long PrecisionInterval, long Count, long Ticks, double PrecisionRemainder) GetSteps(double velocity, double startPosition, double endPosition, double precisionRemainder);
        McuTimestamp DacReset(McuTimestamp timestamp, double maxDistance);
        McuTimestamp DacHome(McuTimestamp timestamp, double position, double maxDistance);
    }
}