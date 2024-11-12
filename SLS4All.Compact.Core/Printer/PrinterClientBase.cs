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
        public TimeSpan StreamingBufferTime { get; set; } = TimeSpan.FromSeconds(15);
        public bool DisableStreamPublishSynchronization { get; set; } = false;
    }

    public abstract class PrinterClientBase : IPrinterClient
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<PrinterClientBaseOptions> _options;
        private long _lastIndex;
        private readonly IObjectFactory<IMovementClient, object> _movementFactory;
        private readonly PriorityScheduler _executeScheduler;

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
            _executeScheduler = new PriorityScheduler("PrinterClientExecute", ThreadPriority.AboveNormal, 3 /* script + publish + execute */);
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
            bool synchronousScriptExecution, 
            bool hidden, 
            IPrinterClientCommandContext? context = null, 
            CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var executeChannel = Channel.CreateUnbounded<CodeCommand>(new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = synchronousScriptExecution,
                SingleReader = true,
                SingleWriter = true,
            });
            var publishChannel = Channel.CreateUnbounded<(ArraySegment<CodeCommand> Segment, SystemTimestamp DoneAt, SystemTimestamp StartedAt)>(new UnboundedChannelOptions
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
                    executeChannel.Writer.TryComplete(ex);
                }
                finally
                {
                    _logger.LogDebug($"Script task completed");
                }
            }, default, TaskCreationOptions.LongRunning, _executeScheduler).Unwrap();

            var publishTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await foreach (var item in publishChannel.Reader.ReadAllAsync(cancel))
                    {
                        var startedAt = item.StartedAt;
                        var duration = Math.Max(item.DoneAt.Timestamp - startedAt.Timestamp, 0);
                        var segment = item.Segment;
                        for (int i = 0; i < segment.Count; i++)
                        {
                            var executeAt = new SystemTimestamp(startedAt.Timestamp + duration * i / segment.Count);
                            Publish(segment[i], allowSynchronous: true, hidden, needsResponse: false);
                            if (!options.DisableStreamPublishSynchronization)
                            {
                                var delay = executeAt - SystemTimestamp.Now;
                                if (delay > TimeSpan.Zero)
                                    await Task.Delay(delay, cancel);
                            }
                        }
                        ArrayPool<CodeCommand>.Shared.Return(segment.Array!);
                    }
                }
                finally
                {
                    _logger.LogDebug($"Publish task completed");
                }
            }, default, TaskCreationOptions.LongRunning, TaskScheduler.Current).Unwrap();

            var executeTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    using (var movement = _movementFactory.CreateDisposable())
                    {
                        // delay, to start publishing about the same time the items start executing
                        var queueAheadDelay = movement.Instance.GetQueueAheadDuration(context);
                        var streamingBatchSize = Math.Max(options.StreamingBatchSize, 1);
                        var remainingInBatch = streamingBatchSize;
                        var publishBatchBuffer = new List<CodeCommand>();
                        SystemTimestamp publishBatchStart = default;
                        await foreach (var cmd in executeChannel.Reader.ReadAllAsync(cancel))
                        {
                            if (remainingInBatch == 0)
                            {
                                Debug.Assert(!publishBatchStart.IsEmpty);
                                var remaining = (await movement.Instance.GetRemainingPrintTime(context, cancel));
                                var publishBatchSegment = new ArraySegment<CodeCommand>(ArrayPool<CodeCommand>.Shared.Rent(publishBatchBuffer.Count), 0, publishBatchBuffer.Count);
                                publishBatchBuffer.CopyTo(publishBatchSegment.Array!, 0);
                                publishBatchBuffer.Clear();
                                await publishChannel.Writer.WriteAsync((publishBatchSegment, remaining.Timestamp, publishBatchStart), cancel);
                                if (remaining.Duration > options.StreamingBufferTime)
                                    await Task.Delay(remaining.Duration - options.StreamingBufferTime, cancel);
                                remainingInBatch = streamingBatchSize;
                                publishBatchStart = default;
                            }
                            else
                                remainingInBatch--;

                            if (publishBatchStart.IsEmpty)
                                publishBatchStart = SystemTimestamp.Now + queueAheadDelay;
                            publishBatchBuffer.Add(cmd);
                            if (cmd.Value is DelegatedCodeFormatter formatter)
                                await formatter.Execute(cmd, hidden, context, cancel);
                        }

                        if (publishBatchBuffer.Count > 0)
                        {
                            Debug.Assert(!publishBatchStart.IsEmpty);
                            var remaining = (await movement.Instance.GetRemainingPrintTime(context, cancel));
                            var publishBatchSegment = new ArraySegment<CodeCommand>(ArrayPool<CodeCommand>.Shared.Rent(publishBatchBuffer.Count), 0, publishBatchBuffer.Count);
                            publishBatchBuffer.CopyTo(publishBatchSegment.Array!, 0);
                            await publishChannel.Writer.WriteAsync((publishBatchSegment, remaining.Timestamp, publishBatchStart), cancel);
                        }
                    }
                    publishChannel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    publishChannel.Writer.TryComplete(ex);
                }
                finally
                {
                    _logger.LogDebug($"Execute task completed");
                }
            }, default, TaskCreationOptions.LongRunning, _executeScheduler).Unwrap();

            await Task.WhenAll(scriptTask, executeTask, publishTask);
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
    }
}