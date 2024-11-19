// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    public sealed class PipedMcuClockSyncProxy : IMcuClockSync
    {
        private record class State
        {
            public double McuFreq;
            public (double SampleTime, double Clock, double Freq) ClockEst;
            public bool IsReady;

            public State(double mcuFreq, double sampleTime, double clock, double freq, bool isReady)
            {
                McuFreq = mcuFreq;
                ClockEst = (sampleTime, clock, freq);
                IsReady = isReady;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowNotReady()
                => throw new InvalidOperationException("ClockSync is not yet ready");

            public long GetClockDuration(double seconds)
                => (long)GetClockDurationDouble(seconds);

            public double GetClockDurationDouble(double seconds)
            {
                if (!IsReady)
                    ThrowNotReady();
                return seconds * McuFreq;
            }

            public double GetSecondsDuration(long clock)
            {
                if (!IsReady)
                    ThrowNotReady();
                return clock / McuFreq;
            }

            public double GetSecondsDurationDouble(double clock)
            {
                if (!IsReady)
                    ThrowNotReady();
                return clock / McuFreq;
            }

            public long GetClock(SystemTimestamp timestamp)
                => (long)GetClockDouble(timestamp);

            public double GetClockDouble(SystemTimestamp timestamp)
            {
                if (!IsReady)
                    ThrowNotReady();
                (var sampleTime, var clock, var freq) = ClockEst;
                return clock + (timestamp.TotalSeconds - sampleTime) * freq;
            }

            public SystemTimestamp ClockToTimestamp(long inputClock)
            {
                if (!IsReady)
                    ThrowNotReady();
                (var sampleTime, var clock, var freq) = ClockEst;
                var seconds = sampleTime - (clock - inputClock) / freq;
                return SystemTimestamp.FromTotalSeconds(seconds);
            }

            public SystemTimestamp ClockToTimestampDouble(double inputClock)
            {
                if (!IsReady)
                    ThrowNotReady();
                (var sampleTime, var clock, var freq) = ClockEst;
                var seconds = sampleTime - (clock - inputClock) / freq;
                return SystemTimestamp.FromTotalSeconds(seconds);
            }
        }


        private readonly ILogger _logger;
        private readonly AutoResetEvent _updatedEvent;
        private readonly CancellationTokenSource _unreachableCancelSource;
        private readonly Lock _sync = new();
        private volatile State _state;
        private IMcu? _mcu;
        private long _updatedCount;
        private TaskCompletionSource _initializedSource;

        private State CurrentState => _state;

        public bool IsReady => CurrentState.IsReady;
        public CancellationToken UnreachableCancel => _unreachableCancelSource.Token;
        public long UpdatedCount => Interlocked.Read(ref _updatedCount);
        public WaitHandle UpdatedEvent => _updatedEvent;

        public PipedMcuClockSyncProxy(ILogger<PipedMcuClockSyncProxy> logger)
        {
            _logger = logger;
            _updatedEvent = new AutoResetEvent(false);
            _state = new State(1, 0, 0, 1, false);
            _initializedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _unreachableCancelSource = new();
        }

        public double GetSecondsDuration(long clock)
            => CurrentState.GetSecondsDuration(clock);

        public double GetSecondsDurationDouble(double clock)
            => CurrentState.GetSecondsDurationDouble(clock);

        public TimeSpan GetSpanDuration(long clock)
            => TimeSpan.FromSeconds(CurrentState.GetSecondsDuration(clock));

        public long GetClock(SystemTimestamp timestamp)
            => CurrentState.GetClock(timestamp);

        public double GetClockDouble(SystemTimestamp timestamp)
            => CurrentState.GetClockDouble(timestamp);

        public SystemTimestamp GetTimestamp(long clock)
            => CurrentState.ClockToTimestamp(clock);

        public SystemTimestamp GetTimestampDouble(double clock)
            => CurrentState.ClockToTimestampDouble(clock);

        public long GetClockDuration(double seconds)
            => CurrentState.GetClockDuration(seconds);

        public double GetClockDurationDouble(double seconds)
            => CurrentState.GetClockDurationDouble(seconds);

        public long GetClockDuration(TimeSpan duration)
            => GetClockDuration(duration.TotalSeconds);

        public async Task Start(IMcu mcu, TaskScheduler scheduler, CancellationToken cancel)
        {
            lock (_sync)
            {
                var mcuFreq = mcu.Config.GetConstInt32("CLOCK_FREQ");
                _state = new State(mcuFreq, 0, 0, mcuFreq, false);
                _mcu = mcu; // NOTE: after _state, will unblock Update()
            }
            await _initializedSource.Task.WaitAsync(cancel);
            Debug.Assert(_state.IsReady);
        }

        public void Stop()
        {
            lock (_sync)
            {
                _state = _state with { IsReady = false };
                _mcu = null;
                _initializedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Update(double sampleTime, double clock, double freq, bool isReady)
        {
            lock (_sync)
            {
                var mcu = _mcu;
                if (mcu == null)
                    return;
                Debug.Assert(!_state.IsReady || _state.IsReady == isReady);
                _state = new State(_state.McuFreq, sampleTime, clock, freq, isReady);
                Interlocked.Increment(ref _updatedCount);
                _updatedEvent.Set();
                if (isReady)
                {
                    if (_initializedSource.TrySetResult())
                        _logger.LogInformation($"Mcu {mcu} is ready");
                }
            }
        }

        public void SetUnreachable()
        {
            if (!_unreachableCancelSource.IsCancellationRequested)
            {
                _logger.LogError($"Mcu {_mcu} is set as unreachable");
                _unreachableCancelSource.Cancel();
            }
        }

        public void SetException(Exception ex)
        {
            if (_initializedSource.TrySetException(ex))
            {
                _logger.LogError(ex, $"Mcu {_mcu} is has got an exception");
            }
        }
    }
}
