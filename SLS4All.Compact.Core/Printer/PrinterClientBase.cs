// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Threading;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

namespace SLS4All.Compact.Printer
{
    public class PrinterClientBaseOptions
    {
        public int StreamingBatchSize { get; set; } = 100;
        /// <summary>
        /// How many seconds of code commands to stream before throttling down to real-time for more
        /// </summary>
        public TimeSpan StreamingBufferTime { get; set; } = TimeSpan.MaxValue;
        public bool DisableStreamPublishSynchronization { get; set; } = false;
        public TimeSpan ExecuteDiagTaskPeriod { get; set; } = TimeSpan.FromMinutes(5);
    }

    public abstract class PrinterClientBase : IPrinterClient
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<PrinterClientBaseOptions> _options;
        private long _lastIndex;
        private readonly IObjectFactory<IMovementClient, object> _movementFactory;
        private readonly PriorityScheduler _scriptAndExecuteScheduler;

        public abstract bool SupportsSustainedLowLatencyGCMode { get; }
        public abstract bool IsConnected { get; }

        public abstract long ConnectionIndex { get; }

        public abstract bool IsShutdown { get; }
        public abstract string? ShutdownReason { get; }

        public abstract bool ShouldSendSafeCheckpoints { get; }

        public abstract bool HasLostCommunication { get; }

        public AsyncEvent ConnectedEvent { get; } = new();

        public AsyncEvent<string> FirmwareShutdownEvent { get; } = new();

        public event PrinterClientEntryEvent<PrinterCommand>? CommandEvent;
        public event PrinterClientEntryEvent<PrinterResponse>? ResponseEvent;

        public event PrinterClientEntryEvent<PrinterLog>? LogEvent
        {
            add { }
            remove { }
        }

        public PrinterClientBase(
            ILogger logger,
            IOptionsMonitor<PrinterClientBaseOptions> options,
            IObjectFactory<IMovementClient, object> movementFactory)
        {
            _logger = logger;
            _options = options;
            _movementFactory = movementFactory;
            _scriptAndExecuteScheduler = new PriorityScheduler("PrinterClientExecute", ThreadPriority.AboveNormal, 2 /* script + execute */);
        }


        public async Task<PrinterResponse> Send(CodeCommand cmd, bool hidden, bool throwOnError = true, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            if (cmd.Value is DelegatedCodeFormatter formatter)
            {
                await formatter.Execute(cmd, hidden, context, cancel);
            }

            return Publish(cmd, allowSynchronous: false, hidden, needsResponse: true);
        }

        public async Task Stream(
            PrinterStream script, 
            bool hidden, 
            IPrinterClientCommandContext? context = null, 
            CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var executeChannel = Channel.CreateUnbounded<CodeCommand>(new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
            });
            var publishChannel = Channel.CreateUnbounded<(ArraySegment<CodeCommand> Segment, SystemTimestamp StartAt, SystemTimestamp EndAt)>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
            });

            var scriptTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await script(executeChannel.Writer, cancel);
                    executeChannel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception in script task");
                    executeChannel.Writer.TryComplete(ex);
                }
                finally
                {
                    _logger.LogDebug($"Script task completed");
                }
            }, default, TaskCreationOptions.LongRunning, _scriptAndExecuteScheduler).Unwrap();

            var publishTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await foreach (var batch in publishChannel.Reader.ReadAllAsync(cancel))
                    {
                        var batchDuration = Math.Max(batch.EndAt.Timestamp - batch.StartAt.Timestamp, 0);
                        var segment = batch.Segment;
                        for (int i = 0; i < segment.Count; i++)
                        {
                            Publish(segment[i], allowSynchronous: true, hidden, needsResponse: false);
                            if (!options.DisableStreamPublishSynchronization)
                            {
                                var executeAt = new SystemTimestamp(batch.StartAt.Timestamp + batchDuration * i / segment.Count);
                                var delay = executeAt - SystemTimestamp.Now;
                                if (delay > TimeSpan.Zero)
                                    await Task.Delay(delay, cancel);
                            }
                        }
                        ArrayPool<CodeCommand>.Shared.Return(segment.Array!);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception in publish task");
                    throw;
                }
                finally
                {
                    _logger.LogDebug($"Publish task completed");
                }
            }, default, TaskCreationOptions.LongRunning, TaskScheduler.Current).Unwrap();

            var executePhase = -1;
            var executeCount = 0;
            var executeBatchCount = 0;

            var executeTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    using (var movement = _movementFactory.CreateDisposable())
                    {
                        var streamingBatchSize = Math.Max(options.StreamingBatchSize, 1);
                        var remainingInBatch = streamingBatchSize;
                        var publishBatchBuffer = new List<CodeCommand>();
                        SystemTimestamp publishBatchStart = default;
                        var queueAheadDelay = movement.Instance.GetQueueAheadDuration(context);
                        var publishBatchStartFirst = SystemTimestamp.Now + queueAheadDelay;
                        await foreach (var cmd in executeChannel.Reader.ReadAllAsync(cancel))
                        {
                            Volatile.Write(ref executePhase, 0);
                            if (remainingInBatch == 0)
                            {
                                Debug.Assert(!publishBatchStart.IsEmpty);
                                Volatile.Write(ref executePhase, 1);
                                var remaining = await movement.Instance.GetRemainingPrintTime(context, cancel);
                                var publishBatchSegment = new ArraySegment<CodeCommand>(ArrayPool<CodeCommand>.Shared.Rent(publishBatchBuffer.Count), 0, publishBatchBuffer.Count);
                                publishBatchBuffer.CopyTo(publishBatchSegment.Array!, 0);
                                publishBatchBuffer.Clear();
                                Volatile.Write(ref executePhase, 2);
                                await publishChannel.Writer.WriteAsync((publishBatchSegment, publishBatchStart, remaining.Timestamp), cancel);
                                Interlocked.Increment(ref executeBatchCount);
                                if (remaining.Duration > options.StreamingBufferTime)
                                {
                                    Volatile.Write(ref executePhase, 3);
                                    await Task.Delay(remaining.Duration - options.StreamingBufferTime, cancel);
                                }
                                remainingInBatch = streamingBatchSize;
                                publishBatchStart = default;
                            }
                            else
                                remainingInBatch--;

                            if (publishBatchStart.IsEmpty)
                            {
                                Volatile.Write(ref executePhase, 4);
                                var remaining = await movement.Instance.GetRemainingPrintTime(context, cancel);
                                if (remaining.Timestamp > publishBatchStartFirst)
                                    publishBatchStart = remaining.Timestamp;
                                else
                                    publishBatchStart = publishBatchStartFirst;
                            }

                            publishBatchBuffer.Add(cmd);
                            if (cmd.Value is DelegatedCodeFormatter formatter)
                            {
                                Volatile.Write(ref executePhase, 5);
                                await formatter.Execute(cmd, hidden, context, cancel);
                            }
                            Volatile.Write(ref executePhase, 6);
                            Interlocked.Increment(ref executeCount);
                        }
                        Volatile.Write(ref executePhase, 7);

                        if (publishBatchBuffer.Count > 0)
                        {
                            Debug.Assert(!publishBatchStart.IsEmpty);
                            Volatile.Write(ref executePhase, 8);
                            var remaining = await movement.Instance.GetRemainingPrintTime(context, cancel);
                            var publishBatchSegment = new ArraySegment<CodeCommand>(ArrayPool<CodeCommand>.Shared.Rent(publishBatchBuffer.Count), 0, publishBatchBuffer.Count);
                            publishBatchBuffer.CopyTo(publishBatchSegment.Array!, 0);
                            Volatile.Write(ref executePhase, 9);
                            await publishChannel.Writer.WriteAsync((publishBatchSegment, publishBatchStart, remaining.Timestamp), cancel);
                            Interlocked.Increment(ref executeBatchCount);
                        }
                    }
                    publishChannel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception in execute task (phase = {Volatile.Read(ref executePhase)}, count = {executeCount}, batchCount={executeBatchCount})");
                    publishChannel.Writer.TryComplete(ex);
                }
                finally
                {
                    _logger.LogDebug($"Execute task completed (phase = {Volatile.Read(ref executePhase)}, count = {executeCount}, batchCount={executeBatchCount})");
                }
            }, default, TaskCreationOptions.LongRunning, _scriptAndExecuteScheduler).Unwrap();

            var executeDiagTask = Task.Factory.StartNew(async () =>
            {
                if (options.ExecuteDiagTaskPeriod == TimeSpan.Zero)
                    return;
                try
                {
                    while (true)
                    {
                        try
                        {
                            await executeTask.WaitAsync(options.ExecuteDiagTaskPeriod, cancel);
                        }
                        catch (TimeoutException)
                        {
                            // swallow
                        }
                        if (executeTask.IsCompleted)
                            break;
                        _logger.LogDebug($"Streaming execute phase: {Volatile.Read(ref executePhase)}, count: {Volatile.Read(ref executeCount)}, batchCount: {Volatile.Read(ref executeBatchCount)}");
                    }
                }
                catch
                {
                    // swallow
                }
            }, default, TaskCreationOptions.LongRunning, TaskScheduler.Current).Unwrap();

            await Task.WhenAll(scriptTask, executeTask, publishTask, executeDiagTask);
        }

        private PrinterResponse Publish(in CodeCommand cmd, bool allowSynchronous, bool hidden, bool needsResponse)
        {
            var commandEvent = CommandEvent;
            var responseEvent = ResponseEvent;
            if (!needsResponse)
            {
                if (commandEvent == null && responseEvent == null)
                    return default;
            }
            var commandIndex = GetNewIndex();
            var responseIndex = GetNewIndex();
            var now = SystemTimestamp.Now;
            var fakeResponse = new PrinterResponse(responseIndex, now, "ok", "ok", PrinterResult.OKResponse, hidden, commandIndex);
            if (commandEvent != null)
            {
                var fakeCmd = new PrinterCommand(commandIndex, now, cmd, PrinterResult.Command, hidden, fakeResponse, null);
                commandEvent?.Invoke(this, allowSynchronous, fakeCmd);
            }
            responseEvent?.Invoke(this, allowSynchronous, fakeResponse);
            return fakeResponse;
        }

        private long GetNewIndex()
            => Interlocked.Increment(ref _lastIndex);

        public abstract Task WaitForConnection(CancellationToken cancel = default);

        public abstract Task Restart(PrinterClientRestartFlags type, CancellationToken cancel = default);

        public abstract void Shutdown(string reason, Exception? ex, IPrinterClientCommandContext? context);

        public abstract (string Key, string Message)[] GetConnectionStatus();

        public abstract Task EnterPrintingMode(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        public abstract Task ExitPrintingMode(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
    }
}