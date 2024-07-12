// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.McuClient
{
    public abstract class McuClockSyncAbstract : IDisposable, IMcuClockSync
    {
        protected internal enum ProcessResult
        {
            NotSet = 0,
            Skipped,
            ResetNeeded,
            Success,
        }

        protected internal record class State
        {
            public const double RttAge = 0.000010 / (60.0 * 60.0);
            public const double Decay = 1.0 / 30.0;
            public const double ResetFreqRatio = 0.05;
            public const int ResetFreqRatioCount = 5;

            public readonly McuClockSyncAbstract ClockSync;
            public long LastClock;
            public string McuAlias;
            public double McuFreq;
            public double ClockAvg;
            public double TimeAvg;
            public double MinRttTime;
            public double MinHalfRtt;
            public (double SampleTime, double Clock, double Freq) ClockEstInner;
            public (double SampleTime, double Clock, double Freq) ClockEst;
            public double LastPredictionTime;
            public double PredictionVariance;
            public double TimeVariance;
            public double ClockCovariance;
            public int ResetCounter;
            public bool IsReady;

            public State(McuClockSyncAbstract clockSync)
            {
                ClockSync = clockSync;
                McuAlias = "mcu";
                McuFreq = 1;
                ClockEstInner = ClockEst = (0.0, 0.0, 1);
                // Minimum round-trip-time tracking
                MinHalfRtt = 999_999_999.9;
                MinRttTime = 0.0;
                // Linear regression of mcu clock and system sent_time
                TimeAvg = TimeVariance = 0.0;
                ClockAvg = ClockCovariance = 0.0;
                PredictionVariance = 0.0;
                LastPredictionTime = 0.0;
                ResetCounter = 0;
            }

            public ProcessResult Process(ILogger logger, McuCommand response)
            {
                // Extend clock to 64bit
                var clock32 = response["clock"].UInt32;
                var clock = McuHelpers.Clock32ToClock64(LastClock, clock32);
                LastClock = clock;

                // Check if this is the best round-trip-time seen so far
                if (response.SentTimestamp == 0 || response.ReceiveTimestamp == 0)
                {
                    logger.LogDebug($"MCU {McuAlias}, timestamps missing, skipping");
                    return ProcessResult.Skipped;
                }
                var sentTime = response.SentTimestamp;
                var receiveTime = response.ReceiveTimestamp;
                var halfRtt = 0.5 * (receiveTime - sentTime);
                var agedRtt = (sentTime - MinRttTime) * RttAge;
                if (halfRtt < MinHalfRtt + agedRtt)
                {
                    MinHalfRtt = halfRtt;
                    MinRttTime = sentTime;
                    logger.LogDebug($"MCU {McuAlias}, new minimum rtt {sentTime:0.000}: hrtt={halfRtt:0.000000} freq={ClockEstInner.Freq}");
                }

                // Filter out samples that are extreme outliers
                var expClock = (sentTime - TimeAvg) * ClockEstInner.Freq + ClockAvg;
                var clockDiff2 = (clock - expClock) * (clock - expClock);
                var t1 = clockDiff2 - 25.0 * PredictionVariance;
                var t2 = clockDiff2 - (0.000500 * McuFreq) * (0.000500 * McuFreq);
                if (t1 > 0 && t2 > 0)
                {
                    if (clock > expClock && sentTime < LastPredictionTime + 10.0)
                    {
                        logger.LogDebug($"MCU {McuAlias}, ignoring clock sample {sentTime:0.000}: freq={ClockEstInner.Freq} diff={clock - expClock} stddev={Math.Sqrt(PredictionVariance):0.000}");
                        return ProcessResult.Success;
                    }
                    logger.LogInformation($"MCU {McuAlias}, resetting prediction variance {sentTime:0.000}: freq={ClockEstInner.Freq} diff={clock - expClock} stddev={Math.Sqrt(PredictionVariance):0.000}");
                    PredictionVariance = (0.001 * McuFreq) * (0.001 * McuFreq);
                }
                else
                {
                    LastPredictionTime = sentTime;
                    PredictionVariance = (1.0 - Decay) * (PredictionVariance + clockDiff2 * Decay);
                }

                // Add clock and sent_time to linear regression
                var diffSentTime = sentTime - TimeAvg;
                TimeAvg += Decay * diffSentTime;
                TimeVariance = (1.0 - Decay) * (TimeVariance + diffSentTime * diffSentTime * Decay);
                var diffClock = clock - ClockAvg;
                ClockAvg += Decay * diffClock;
                ClockCovariance = (1.0 - Decay) * (ClockCovariance + diffSentTime * diffClock * Decay);

                // Update prediction from linear regression
                var newFreq = ClockCovariance / TimeVariance;
                var freqRatio = newFreq < McuFreq ? newFreq / McuFreq : 2 - McuFreq / newFreq;
                ClockEstInner = (TimeAvg + MinHalfRtt, ClockAvg, newFreq);
                var predStddev = Math.Sqrt(PredictionVariance);
                logger.LogTrace($"MCU {McuAlias}, regr {sentTime:0.000}: avg={TimeAvg:0.000}({ClockAvg:0.000}) clock={clock} freq={newFreq:0.000} d={clock - expClock}({predStddev:0.000}) rt={receiveTime:0.000} df={diffSentTime:0.000} hrtt={halfRtt:0.000000} t1={t1:0.000} t2={t2:0.000}");

                if (Math.Abs(1 - freqRatio) > ResetFreqRatio) // frequency difference is too large, reset. Was debugger stepping?
                {
                    if (++ResetCounter >= ResetFreqRatioCount)
                        return ProcessResult.ResetNeeded;
                    else
                        return ProcessResult.Success;
                }
                else
                {
                    //self.serial.set_clock_est(new_freq, self.time_avg + TRANSMIT_EXTRA,
                    //                      int(self.clock_avg - 3. * pred_stddev))
                    ClockEst = ClockEstInner;
                    ResetCounter = 0;
                    IsReady = true;
                    return ProcessResult.Success;
                }
            }

            public bool TryInitialize(McuCommand uptimeResponse, double mcuFreq, string mcuAlias)
            {
                if (uptimeResponse.SentTimestamp == 0)
                    return false;
                McuAlias = mcuAlias;
                McuFreq = mcuFreq;
                LastClock = ((long)uptimeResponse["high"].UInt32 << 32) | uptimeResponse["clock"].UInt32;
                ClockAvg = LastClock;
                TimeAvg = uptimeResponse.SentTimestamp;
                ClockEst = (TimeAvg, ClockAvg, McuFreq);
                PredictionVariance = (0.001 * McuFreq) * (0.001 * McuFreq);
                return true;
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

        private const int _maxQueriesPending = 4;
        private readonly ILogger _logger;
        private readonly McuManager _manager;
        private volatile CancellationTokenSource? _cancelSource;
        private CancellationTokenSource? _unreachableCancelSource;
        private Task? _task;
        private McuCommand _cmdGetUptime = McuCommand.PlaceholderCommand, _cmdGetUptimeResponse = McuCommand.PlaceholderCommand;
        private McuCommand _cmdGetClock = McuCommand.PlaceholderCommand, _cmdGetClockResponse = McuCommand.PlaceholderCommand;
        private IDisposable? _getUptimeSubscription;
        private readonly object _lock = new object();
        private volatile State? _stateLazy;
        private int _queriesPending;
        private bool _resetNeeded;
        private long _updatedCount;
        private readonly AutoResetEvent _updatedEvent;

        public long UpdatedCount => Interlocked.Read(ref _updatedCount);
        public WaitHandle UpdatedEvent => _updatedEvent;

        public CancellationToken UnreachableCancel
            => _unreachableCancelSource?.Token ?? default;

        protected internal (double SampleTime, double Clock, double Freq) ClockEst
            => CurrentState.ClockEst;

        public double McuFreq
            => CurrentState.McuFreq;

        public double MinHalfRtt
            => CurrentState.MinHalfRtt;

        protected internal State CurrentState
        {
            get
            {
                if (_stateLazy == null)
                {
                    lock (_lock)
                    {
                        if (_stateLazy == null)
                        {
                            Interlocked.CompareExchange(ref _stateLazy, CreateState(), null);
                            OnStateChanged();
                        }
                    }
                }
                return _stateLazy!;
            }
        }

        public bool IsReady
            => CurrentState.IsReady;

        protected McuClockSyncAbstract(
            ILogger logger,
            McuManager root)
        {
            _logger = logger;
            _manager = root;
            _updatedEvent = new AutoResetEvent(false);
        }

        protected virtual State CreateState()
            => new State(this);

        public virtual async Task Start(IMcu mcu, TaskScheduler scheduler, CancellationToken cancel)
        {
            Stop();
            _cancelSource = new();
            _unreachableCancelSource = new();
            var initializedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (_cancelSource.Token.Register(() => initializedSource.TrySetCanceled(_cancelSource.Token)))
            {
                _task = Task.Factory.StartNew(
                    () => Run(mcu, initializedSource, _unreachableCancelSource, _cancelSource.Token),
                    default,
                    TaskCreationOptions.None,
                    scheduler).Unwrap();
                await initializedSource.Task.WaitAsync(cancel);
            }
        }

        private async Task Run(
            IMcu mcu, 
            TaskCompletionSource initializedSource,
            CancellationTokenSource unreachableCancelSource,
            CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                // Load initial clock and frequency
                var mcuFreq = mcu.Config.GetConstInt32("CLOCK_FREQ");
                await RunInner(mcu, initializedSource, mcuFreq, unreachableCancelSource, cancel);
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    var msg = $"Unhandled exception in ClockSync for MCU {mcu}, shutting down";
                    _logger.LogError(ex, msg);
                    _manager.Shutdown(new Messages.McuShutdownMessage
                    {
                        Mcu = mcu,
                        Exception = ex,
                        Reason = msg,
                    });
                    throw;
                }
            }
        }

        private async Task RunInner(
            IMcu mcu, 
            TaskCompletionSource initializedSource, 
            int mcuFreq,
            CancellationTokenSource unreachableCancelSource,
            CancellationToken cancel)
        {
            try
            {
                _logger.LogInformation($"Starting main loop for MCU {mcu.Name}");

                _getUptimeSubscription?.Dispose();
                _cmdGetUptime = mcu.LookupCommand("get_uptime");
                _cmdGetUptimeResponse = mcu.LookupCommand("uptime");
                _cmdGetClock = mcu.LookupCommand("get_clock");
                _cmdGetClockResponse = mcu.LookupCommand("clock");

                var state = CreateState();
                while (true)
                {
                    var uptimeResponse = await mcu.SendWithResponse(
                        _cmdGetUptime,
                        _cmdGetUptimeResponse,
                        null,
                        McuCommandPriority.ClockSync,
                        McuOccasion.Now,
                        cancel: cancel);
                    if (state.TryInitialize(uptimeResponse, mcuFreq, mcu.Name))
                        break;
                }

                // initial values
                const int initialRequestCount = 8;
                for (int i = 1; i <= initialRequestCount; i++)
                {
                    await Task.Delay(50, cancel);
                    state.LastPredictionTime = -9999.0;
                    var response = await mcu.SendWithResponse(
                        _cmdGetClock,
                        _cmdGetClockResponse,
                        null,
                        McuCommandPriority.ClockSync,
                        McuOccasion.Now,
                        cancel: cancel);
                    var result = state.Process(_logger, response);
                    if (i == initialRequestCount)
                    {
                        if (result == ProcessResult.ResetNeeded)
                            throw new McuAutomatedRestartException($"Reset needed after initial ClockSync requests for MCU {mcu.Name}", 
                                reason: McuAutomatedRestartReason.ClockSync);
                    }
                }

                // set initial state
                try
                {
                    FinalizeInitialState(state);
                    lock (_lock)
                    {
                        _resetNeeded = false;
                        _stateLazy = state;
                        _getUptimeSubscription = mcu.RegisterResponseHandler(_cmdGetClock, _cmdGetClockResponse, OnMcuUptime);
                        SetUpdated();
                        OnStateChanged();
                    }
                    _logger.LogInformation($"ClockSync for MCU {mcu.Name} has initialized");
                    initializedSource.TrySetResult();

                    // run periodic loop
                    while (true)
                    {
                        lock (_lock)
                        {
                            if (_resetNeeded)
                                return;
                            mcu.Send(
                                _cmdGetClock,
                                McuCommandPriority.ClockSync,
                                McuOccasion.Now);
                            if (++_queriesPending > _maxQueriesPending)
                            {
                                _logger.LogError($"MCU {mcu.Name} is unreachable");
                                OnUnreachableChanged();
                                unreachableCancelSource.Cancel();
                            }
                        }

                        // Use an unusual time for the next event so clock messages
                        // don't resonate with other periodic events.
                        await Task.Delay(TimeSpan.FromSeconds(0.9839), cancel);
                    }
                }
                catch (Exception ex)
                {
                    if (!cancel.IsCancellationRequested)
                        _logger.LogError(ex, $"Unhandled exception in ClockSync for MCU {mcu.Name}");
                    throw;
                }
                finally
                {
                    _logger.LogInformation($"Ended ClockSync for MCU {mcu.Name}");
                }
            }
            catch (Exception ex) when (!initializedSource.Task.IsCompleted)
            {
                OnException(ex);
                initializedSource.TrySetException(ex);
                throw;
            }
        }

        protected virtual void FinalizeInitialState(State state)
        {
        }

        protected virtual void FinalizeUpdatedState(State state, State prevState)
        {
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

        private ValueTask OnMcuUptime(Exception? exception, McuCommand? command, CancellationToken cancel)
        {
            if (command != null)
            {
                lock (_lock)
                {
                    var prevState = CurrentState;
                    var state = prevState with { };
                    var result = state.Process(_logger, command);
                    switch (result)
                    {
                        case ProcessResult.Success:
                            FinalizeUpdatedState(state, prevState);
                            _stateLazy = state;
                            _queriesPending = 0;
                            SetUpdated();
                            OnStateChanged();
                            break;
                        case ProcessResult.Skipped:
                            break;
                        case ProcessResult.ResetNeeded:
                            _resetNeeded = true;
                            break;
                        default:
                            throw new InvalidOperationException($"Invalid result: {result}");
                    }
                }
            }
            return ValueTask.CompletedTask;
        }

        protected void SetUpdated()
        {
            Interlocked.Increment(ref _updatedCount);
            _updatedEvent.Set();
        }

        public void Stop()
        {
            _getUptimeSubscription?.Dispose();
            _cancelSource?.Cancel();
        }

        public void Dispose()
        {
            Stop();
        }

        protected virtual void OnException(Exception ex)
        {
        }

        protected virtual void OnUnreachableChanged()
        {
        }

        protected virtual void OnStateChanged()
        {
        }
    }

    public sealed class McuClockSync : McuClockSyncAbstract
    {
        public McuClockSync(ILogger logger, McuManager root) : base(logger, root)
        {
        }
    }
}
