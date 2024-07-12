// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public class McuDeviceOptions
    {
        public string? FactoryName { get; set; }
        public string? Alias { get; set; }
        public required string Path { get; set; }
    }

    public class McuOptions
    {
        public McuDeviceOptions? Device { get; set; }
        public string Name { get; set; } = "mcu";
        public int MaxInflightBlocks { get; set; } = 15;
        public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan StatsPeriod { get; set; } = TimeSpan.FromSeconds(5);
        public bool DumpCommandQueueToFile { get; set; } = false;
        public string? DumpCommandQueueFileFormat { get; set; }
        public int IORetries { get; set; } = 10;
        public int? RequestedMoveCount { get; set; }
        public TimeSpan InitialErrorSupressionDuration { get; set; } = TimeSpan.FromSeconds(60);
        public McuFirmwareUpdaterOptions? FirmwareUpdate { get; set; }
    }

    /// <summary>
    /// MCU that is "locally" accessible using <see cref="IMcuDeviceFactory"/>
    /// </summary>
    public abstract partial class McuAbstract : McuBase
    {
        private readonly static TimeSpan _spinWaitThreshold = TimeSpan.FromMilliseconds(100); // maximum OS time-slice
        private readonly static TimeSpan _synchronizeTimeout = TimeSpan.FromSeconds(11.0);
        private readonly static TimeSpan _retransmitTimeout = TimeSpan.FromSeconds(0.025);
        private readonly static TimeSpan _retransmitMaxPeriod = TimeSpan.FromSeconds(0.250);
        private readonly static TimeSpan _maxSendWaitPeriod = TimeSpan.FromSeconds(1);
        private readonly IEnumerable<IMcuDeviceFactory> _deviceFactories;
        private readonly McuCommandQueue _commandQueueNeedsLock;
        private readonly int _maxInflightBlocks;
        private readonly TimeSpan _statsPeriod;
        private readonly TaskQueue _responseTaskQueue;
        private readonly McuConfigCommands _configCommands;
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<McuResponseHandler, string>> _responseHandlers;

        public ArenaAllocator<byte> CommandArena => _commandQueueNeedsLock.Arena;
        public override bool HasTimingCriticalCommandsScheduled
        {
            get
            {
                lock (_commandQueueNeedsLock)
                {
                    return _commandQueueNeedsLock.TimingCriticalCount != 0;
                }
            }
        }
        public virtual int CreatedArenas => _commandQueueNeedsLock.Arena.CreatedArenas;
        public virtual long CreatedCommands => 0;

        public McuAbstract(
            ILoggerFactory loggerFactory,
            ILogger logger,
            IAppDataWriter appDataWriter,
            McuManager manager,
            IOptions<McuOptions> options,
            IMcuClockSync clockSync,
            IEnumerable<IMcuDeviceFactory> deviceFactories)
            : base(loggerFactory, logger, appDataWriter, manager, options, clockSync)
        {
            _deviceFactories = deviceFactories;

            var o = options.Value;
            _responseHandlers = new();
            _responseTaskQueue = new();
            _maxInflightBlocks = Math.Min(o.MaxInflightBlocks, McuCodec.SeqMask);
            _statsPeriod = o.StatsPeriod;
            _commandQueueNeedsLock = new McuCommandQueue(this, o);
            _configCommands = new();
        }

        protected override async Task CheckFirmwareUpdate(IDisposable device_, CancellationToken cancel)
        {
            var device = (IMcuDevice)device_;
            var options = _options.Value;
            if (options.FirmwareUpdate == null)
                return;
            var firmwareUpdater = new McuFirmwareUpdater(
                _loggerFactory,
                ConstantOptionsMonitor.Create(options.FirmwareUpdate),
                _appDataWriter,
                this);
            try
            {
                firmwareUpdater.PreUpdateEvent.AddHandler(PreUpdateHanlder);
                await firmwareUpdater.CheckFirmwareUpdate(device, cancel);
            }
            finally
            {
                _isUpdatingFirmware = false;
            }
        }

        private async ValueTask PreUpdateHanlder(CancellationToken cancel)
        {
            await PreUpdatingEvent.Invoke(cancel);
            _isUpdatingFirmware = true;
            _hasTriedToUpdateFirmware = true;
        }

        public override void SendCancel(McuSendResult sendId)
        {
            lock (_commandQueueNeedsLock)
            {
                _commandQueueNeedsLock.Remove(sendId);
            }
        }

        public override void SendCancel(IEnumerable<McuSendResult> sendIds)
        {
            lock (_commandQueueNeedsLock)
            {
                foreach (var sendId in sendIds)
                    _commandQueueNeedsLock.Remove(sendId);
            }
        }

        public override async Task SendWait(McuCommand command, int priority, McuOccasion clock, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancel.Register(() => source.TrySetCanceled(cancel)))
            {
                ulong id = 0;
                EventHandler<ulong> handler = (sender, ackedId) =>
                {
                    if (ackedId == Volatile.Read(ref id))
                        source.TrySetResult();
                };
                try
                {
                    _commandQueueNeedsLock.AckedIdEvent += handler;
                    lock (_commandQueueNeedsLock)
                    {
                        Volatile.Write(ref id, _commandQueueNeedsLock.Enqueue(command, priority, clock.MinClock, clock.ReqClock).Id);
                    }
                    await source.Task.WaitAsync(_responseTimeout, cancel);
                }
                finally
                {
                    _commandQueueNeedsLock.AckedIdEvent -= handler;
                }
            }
        }

        public override McuSendResult Send(McuCommand command, int priority, McuOccasion clock, McuSendResult? cancelFirst = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            lock (_commandQueueNeedsLock)
            {
                if (cancelFirst != null)
                    _commandQueueNeedsLock.Remove(cancelFirst.Value);
                return _commandQueueNeedsLock.Enqueue(command, priority, clock.MinClock, clock.ReqClock);
            }
        }

        public override bool TryReplace(McuSendResult id, McuCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            lock (_commandQueueNeedsLock)
            {
                return _commandQueueNeedsLock.TryReplace(id, command);
            }
        }

        protected override async Task<bool> SendConfigCommands(CancellationToken cancel)
        {
            await SendConfigCommands(_configCommands, cancel);
            return true; // true -> set WasReady
        }

        private async Task CommandWriteProc(
            IMcuDevice device,
            ulong initSeq,
            McuInflightItems inflightItems,
            CancellationToken cancel)
        {
            try
            {
                var options = _options.Value;
                var lastStats = Stopwatch.StartNew();
                var encoder = new McuCodec { Seq = initSeq };
                var lastReorderCount = long.MinValue;
                var lastClockSyncUpdatedCount = long.MinValue;
                var waitHandles = new[]
                {
                    _commandQueueNeedsLock.ReorderEvent,
                    _clockSync.UpdatedEvent,
                    cancel.WaitHandle,
                };
                var waitCondition = () =>
                {
                    var res = false;
                    var reorderCount = _commandQueueNeedsLock.ReorderCount;
                    if (lastReorderCount != reorderCount)
                    {
                        lastReorderCount = reorderCount;
                        res = true;
                    }
                    var clockSyncUpdatedCount = _clockSync.UpdatedCount;
                    if (clockSyncUpdatedCount != lastClockSyncUpdatedCount)
                    {
                        lastClockSyncUpdatedCount = clockSyncUpdatedCount;
                        res = true;
                    }
                    if (cancel.IsCancellationRequested)
                        res = true;
                    return res;
                };
                SystemTimestamp feedFrom = default;
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    encoder.ResetWrite();

                    // NOTE: we want to feed with fixed timestamp each iteration, until all data for it is dequeued and only then
                    //       we want to advance the timestamp. This is to ensure that we explicitely flush to the device once in a while.
                    //       Otherwise we might feed "infinite" data to the device and flush not very often (too much data might end up in cache)
                    if (feedFrom.IsEmpty)
                        feedFrom = SystemTimestamp.Now;
                    var toWait = inflightItems.FeedCommandsFromQueue(feedFrom, encoder, out var count);

                    if (count > 0)
                    {
                        var block = encoder.FinalizeBlock();
                        for (int itry = 0; ; itry++)
                        {
                            try
                            {
                                await device.Write(block, cancel);
                                break;
                            }
                            catch (IOException ex)
                            {
                                if (itry < options.IORetries)
                                {
                                    _logger.Log(GetLogErrorLevel(), ex, $"Writing to device for MCU {_name} has failed with IOException, will try again and eventually throw");
                                    Thread.Yield();
                                }
                                else
                                    throw;
                            }
                        }
                    }
                    if (count == 0 || toWait > TimeSpan.Zero)
                    {
                        // flush only when we have written everything we can for now
                        for (int itry = 0; ; itry++)
                        {
                            try
                            {
                                await device.Flush(cancel);
                                break;
                            }
                            catch (IOException ex)
                            {
                                if (itry < options.IORetries)
                                {
                                    _logger.Log(GetLogErrorLevel(), ex, $"Flushing to device for MCU {_name} has failed with IOException, will try again and eventually throw");
                                    Thread.Yield();
                                }
                                else
                                    throw;
                            }
                        }
                        // reset next feed to current time
                        feedFrom = default;
                    }
                    if (toWait > TimeSpan.Zero)
                    {
                        // recalc time, some time has elapsed due to writing
                        var next = inflightItems.GetNextWaitTimestamp();
                        Wait(waitCondition, waitHandles, next.Now, next.Next);
                    }
                    if (_logger.IsEnabled(LogLevel.Trace) && lastStats.Elapsed > _statsPeriod)
                    {
                        lastStats.Restart();
                        McuCommandQueueStats stats;
                        lock (_commandQueueNeedsLock)
                            stats = _commandQueueNeedsLock.GetStats();
                        _logger.LogTrace($"MCU {_name}: inflight={stats.InflightCount}({stats.InflightBytes}B), stalled={stats.StalledCount}({stats.StalledBytes}B), allocated={stats.AllocatedCount}, sent={stats.SentCount}({stats.SentBytes}B), usedArenaItems={stats.UsedArenaItemCount}, usedArenas={stats.UsedArenaCount}({stats.UsedArenaBytes}B), createdArenas={stats.CreatedArenaCount}({stats.CreatedArenaBytes}B), timingCriticalCount={stats.TimingCriticalCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    _logger.Log(GetLogErrorLevel(), ex, $"Unhandled exception in MCU {_name} command write thread, will rethrow");
                    throw;
                }
            }
        }

        private static bool Wait(Func<bool> condition, WaitHandle[] waitHandles, SystemTimestamp now, SystemTimestamp next)
        {
            var timeout = next - now;
            if (timeout <= TimeSpan.Zero)
                return false;
            // NOTE: synchronous wait is most performant here, albeit ugly, we also have our own scheduler with dedicated threads
            if (timeout > _spinWaitThreshold)
            {
                // NOTE: any remainder will be spinned after looping
                return WaitHandle.WaitAny(waitHandles, timeout - _spinWaitThreshold, false) != WaitHandle.WaitTimeout;
            }
            else
            {
                SpinWait spinner = default;
                while (!condition())
                {
                    spinner.SpinOnce();
                    if (next.Timestamp <= Stopwatch.GetTimestamp())
                        return false;
                }
                return true;
            }
        }

        private async Task ResponseReadProc(
            IMcuDevice device,
            ulong receiveSeq,
            McuInflightItems inflightItems,
            CancellationToken cancel)
        {
            var decoder = new McuCodec();
            var lastSendTimes = new Dictionary<string, SystemTimestamp>();
            try
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();

                    // read next block
                    var dataSize = await device.ReadBlock(decoder, cancel);
                    var timestamp = SystemTimestamp.Now.TotalSeconds;

                    // calculate receive sequence number
                    var newReceiveSeq = (receiveSeq & ~(ulong)McuCodec.SeqMask) | decoder.Seq;
                    if (newReceiveSeq < receiveSeq)
                        newReceiveSeq += McuCodec.SeqMask + 1;

                    // ack messages
#if DEBUG
                    if (McuCommandQueue.IsTracing)
                        Trace.WriteLine($"Received SEQ {Name} #{newReceiveSeq} ({decoder.Seq}), dataSize={dataSize}");
#endif
                    if (receiveSeq != newReceiveSeq)
                    {
                        inflightItems.AckItems(newReceiveSeq, lastSendTimes);
                        receiveSeq = newReceiveSeq;
                    }

                    while (decoder.DataPostition < dataSize)
                    {
                        var responseId = (int)decoder.ReadVLQ();
                        if (!Config.IdToCommand.TryGetValue(responseId, out var responseTemplate))
                        {
                            _logger.LogDebug($"Received unknown response id {responseId} on MCU {_name}");
                            break; // discard whole block
                        }

                        // read response
                        var response = responseTemplate.Clone();
                        response.ReceiveTimestamp = timestamp;
                        for (int i = 0; i < response.Arguments.Length; i++)
                        {
                            var arg = response.Arguments[i];
                            switch (arg.Type)
                            {
                                case McuCommandArgumentType.Number:
                                    {
                                        var value = decoder.ReadVLQ();
                                        response[i] = value;
                                        break;
                                    }
                                case McuCommandArgumentType.String:
                                    {
                                        var stringLength = (int)decoder.ReadVLQ();
                                        var bytes = decoder.ReadBytes(stringLength);
                                        response[i] = new McuCommandArgumentValue(int.MinValue, bytes, default);
                                        break;
                                    }
                                default:
                                    throw new InvalidOperationException($"Invalid argument type {arg.Type} for response {response}");
                            }
                        }

                        // process response
                        if (_responseHandlers.TryGetValue(response.CommandId, out var handlers) && handlers.Count > 0)
                        {
                            foreach (var handler_ in handlers)
                            {
                                var handler = handler_;
                                if (lastSendTimes.TryGetValue(handler.Value, out var lastSendTime))
                                    response.SentTimestamp = lastSendTime.TotalSeconds;
                                _responseTaskQueue.EnqueueValue(() => handler.Key(null, response, cancel), null, true);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    _logger.Log(GetLogErrorLevel(), ex, $"Unhandled exception in MCU {_name} response read thread, will rethrow");
                    throw;
                }
            }
        }

        protected override async ValueTask<ulong> SynchronizeDevice(IDisposable device_, CancellationToken cancel)
        {
            var device = (IMcuDevice)device_;
            var decoder = new McuCodec();
            await device.ReadBlock(decoder, cancel).AsTask().WaitAsync(_synchronizeTimeout);
            _logger.LogInformation($"Synchronized protocol for MCU {_name}, seq = {decoder.Seq}");
            return decoder.Seq;
        }

        protected override async ValueTask<IDisposable> CreateDevice(CancellationToken cancel)
        {
            var options = _options.Value;
            if (options.Device != null)
            {
                var factories = _deviceFactories.ToArray();
                if (factories.Length != 1 || !string.IsNullOrEmpty(options.Device.FactoryName))
                {
                    if (string.IsNullOrEmpty(options.Device.FactoryName))
                        throw new FileNotFoundException($"{nameof(options.Device)} is set, {nameof(options.Device.FactoryName)} has to be set also if there is other than single factory");
                    factories = _deviceFactories.Where(x => x.FactoryName == options.Device.FactoryName).ToArray();
                    if (factories.Length == 0)
                        throw new FileNotFoundException($"No factory named {options.Device.FactoryName} for alias {_name} was found");
                    else if (factories.Length > 1)
                        throw new FileNotFoundException($"Multiple factories named {options.Device.FactoryName} for alias {_name} were found");
                }
                var pathAndBaud = McuAliasesOptions.GetEndpointAndBaud(options.Device.Path);
                var info = new McuDeviceInfo(options.Device.Alias ?? options.Name, options.Name, pathAndBaud.Endpoint, pathAndBaud.Baud);
                var device = await factories[0].Open(info, cancel);
                return device;
            }
            else
            {
                List<(McuDeviceInfo Info, IMcuDeviceFactory Factory)> deviceNames = new();
                foreach (var factory in _deviceFactories)
                {
                    var factoryDeviceNames = await factory.GetDeviceNames(cancel);
                    foreach (var item in factoryDeviceNames)
                    {
                        if (item.Name == _name)
                            deviceNames.Add((item, factory));
                    }
                }
                if (deviceNames.Count == 1)
                {
                    var item = deviceNames.Single();
                    var device = await item.Factory.Open(item.Info, cancel);
                    return device;
                }
                else if (deviceNames.Count == 0)
                {
                    throw new FileNotFoundException($"Device with alias {_name} was not found");
                }
                else
                    throw new FileNotFoundException($"Multiple devices with alias {_name} were found");
            }
        }

        public override void RegisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler)
            => _configCommands.InitializingEvent.AddHandler(handler);

        public override void UnregisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler)
            => _configCommands.InitializingEvent.RemoveHandler(handler);

        protected override void ClearCommandQueue()
        {
            lock (_commandQueueNeedsLock)
            {
                _commandQueueNeedsLock.Clear();
            }
        }

        protected override IMcuInflightItems CreateInflightItems()
            => new McuInflightItems(this);

        protected override (Task ReadTask, Task WriteTask) CreateReadWriteTask(
            IDisposable device_, 
            ulong initSeq, 
            IDisposable inflightItems_, 
            PriorityScheduler runScheduler, 
            CancellationToken cancel)
        {
            var device = (IMcuDevice)device_;
            var inflightItems = (McuInflightItems)inflightItems_;
            var writeTask = Task.Factory.StartNew(
                () => CommandWriteProc(device, initSeq, inflightItems, cancel),
                default,
                TaskCreationOptions.LongRunning,
                runScheduler).Unwrap();
            var readTask = Task.Factory.StartNew(
                () => ResponseReadProc(device, initSeq, inflightItems, cancel),
                default,
                TaskCreationOptions.LongRunning,
                runScheduler).Unwrap();
            return (readTask, writeTask);
        }

        protected override async ValueTask CancelResponseHandlers(Exception responseException, CancellationToken cancel)
        {
            foreach (var handler in _responseHandlers)
            {
                foreach (var item in handler.Value.Keys)
                {
                    try
                    {
                        await item(responseException, null, cancel);
                    }
                    catch (Exception ex)
                    {
                        Config.IdToCommand.TryGetValue(handler.Key, out var command);
                        _logger.LogError(ex, $"Failed to call response '{command?.CommandName}' handler for MCU {Name}: {ex.Message}");
                    }
                }
            }
            _responseHandlers.Clear();
        }

        public override IDisposable RegisterResponseHandler(McuCommand? request, McuCommand response, McuResponseHandler handler)
        {
            var handlers = _responseHandlers.GetOrAdd(response.CommandId, x => new());
            handlers[handler] = request?.MessageFormat ?? ""; // overwrite
            return new RegisterResponseHandlerDisposable(this, response, handler);
        }

        protected override void UnregisterResponseHandler(McuCommand response, McuResponseHandler handler)
        {
            if (_responseHandlers.TryGetValue(response.CommandId, out var handlers))
                handlers.TryRemove(handler, out _);
        }
    }

    public sealed partial class Mcu : McuAbstract
    {
        public Mcu(
            ILoggerFactory loggerFactory, 
            IAppDataWriter appDataWriter, 
            McuManager manager, 
            IOptions<McuOptions> options, 
            IMcuClockSync clockSync, 
            IEnumerable<IMcuDeviceFactory> deviceFactories) 
            : base(loggerFactory, loggerFactory.CreateLogger<Mcu>(), appDataWriter, manager, options, clockSync, deviceFactories)
        {
        }
    }
}
