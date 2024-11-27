// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.Disposables.Internals;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Validation;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using static SLS4All.Compact.McuClient.PipedMcu.PipedMcuCodec;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    /// <summary>
    /// MCU that is a proxy for another MCU that connects here trough named pipe
    /// </summary>
    public sealed class PipedMcuProxy : McuBase
    {
        private sealed class NullMcuInflightItems : IMcuInflightItems
        {
            public void Clear()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class PipedDevice : IDisposable
        {
            public struct ToDeviceHelper : IDisposable
            {
                private readonly PipedDevice _device;
                private Stream _stream;

                public ToDeviceHelper(PipedDevice device, Stream stream)
                {
                    _device = device;
                    _stream = stream;
                }

                public void Dispose()
                {
                    if (_stream != null)
                    {
                        _device.AvailableStreamsToDevice.Add(_stream);
                        _stream = null!;
                    }
                }

                public void Send(Span<byte> message)
                {
                    try
                    {
                        _stream.Write(message);
                    }
                    catch (Exception ex) when (IsClosedPipeException(ex))
                    {
                        var receivedException = _device.Proxy._receivedException;
                        if (receivedException != null)
                            throw receivedException;
                        else
                            throw;
                    }
                }

                public void Send(ArraySegment<byte> message)
                {
                    try
                    {
                        _stream.Write(message);
                    }
                    catch (Exception ex) when (IsClosedPipeException(ex))
                    {
                        var receivedException = _device.Proxy._receivedException;
                        if (receivedException != null)
                            throw receivedException;
                        else
                            throw;
                    }
                    finally
                    {
                        ReturnBuffer(message.Array!);
                    }
                }

                public bool ReadBoolean()
                    => _stream.ReadByte() != 0;

                public Exception? ReadExceptionResultNullable()
                {
                    var length = ReadInt32();
                    if (length == 0)
                        return null;
                    Span<byte> buffer = stackalloc byte[length];
                    _stream.ReadExactly(buffer);
                    return PipedMcuCodec.ReadException(ref buffer);
                }

                public McuSendResult ReadSendResult()
                {
                    var length = Measure(default(McuSendResult));
                    Span<byte> buffer = stackalloc byte[length];
                    _stream.ReadExactly(buffer);
                    return PipedMcuCodec.ReadMcuSendResult(ref buffer);
                }

                public long ReadInt64()
                {
                    Span<byte> buffer = stackalloc byte[8];
                    _stream.ReadExactly(buffer);
                    return PipedMcuCodec.ReadInt64(ref buffer);
                }

                public int ReadInt32()
                {
                    Span<byte> buffer = stackalloc byte[4];
                    _stream.ReadExactly(buffer);
                    return PipedMcuCodec.ReadInt32(ref buffer);
                }
            }

            public PipedMcuProxy Proxy { get; }
            public Stream[] StreamsToDevice { get; }
            public BlockingCollection<Stream> AvailableStreamsToDevice { get; } = new();
            public Stream StreamsFromDevice { get; }

            public PipedDevice(PipedMcuProxy proxy, Stream[] streamsToDevice, Stream streamFromDevice)
            {
                Proxy = proxy;
                StreamsToDevice = streamsToDevice;
                AvailableStreamsToDevice = [.. streamsToDevice];
                StreamsFromDevice = streamFromDevice;
            }

            public ToDeviceHelper SendHelper(CancellationToken cancel)
            {
                var stream = AvailableStreamsToDevice.Take(cancel);
                return new ToDeviceHelper(this, stream);
            }

            public void SendResponse(ArraySegment<byte> message)
            {
                StreamsFromDevice.Write(message);
                ReturnBuffer(message.Array!);
            }

            public void Dispose()
            {
                foreach (var stream in StreamsToDevice)
                    stream.Dispose();
                StreamsFromDevice.Dispose();
            }
        }

        private readonly ConcurrentDictionary<long, TaskCompletionSource> _sendWaits;
        private readonly ConcurrentDictionary<McuResponseHandler, (int CommandId, long HandlerId)> _responseHandlersByHandler;
        private readonly ConcurrentDictionary<long, (int CommandId, long HandlerId, McuResponseHandler Handler)> _responseHandlersByHandlerId;
        private readonly TaskQueue _responseTaskQueue;
        private readonly McuConfigCommands _configCommands;
        private PipedDevice? _device;
        private volatile Exception? _receivedException;

        public override bool HasTimingCriticalCommandsScheduled => GetHasTimingCriticalCommandsScheduled();
        public override Exception? CurrentError => GetCurrentError();

        public PipedMcuProxy(
            ILoggerFactory loggerFactory,
            IAppDataWriter appDataWriter,
            McuManager manager,
            IOptions<McuOptions> options,
            IMcuClockSync clockSync)
            : base(loggerFactory, loggerFactory.CreateLogger<PipedMcuProxy>(), appDataWriter, manager, options, clockSync)
        {
            _sendWaits = new();
            _responseHandlersByHandler = new();
            _responseHandlersByHandlerId = new();
            _responseTaskQueue = new();
            _configCommands = new();
        }

        private Exception? GetCurrentError()
        {
            var ex = base.CurrentError;
            if (ex != null)
                return ex;
            try
            {
                using var helper = SendHelper();
                Span<byte> write = stackalloc byte[MinMessageLength];
                var writeSpan = write;
                Initialize(ref writeSpan, MessageType.GetCurrentErrorCommand);
                var message = Finish(write, writeSpan);
                helper.Send(message);
                return helper.ReadExceptionResultNullable();
            }
            catch (Exception ex2)
            {
                return ex2;
            }
        }

        private bool GetHasTimingCriticalCommandsScheduled()
        {
            try
            {
                using var helper = SendHelper();
                Span<byte> write = stackalloc byte[MinMessageLength];
                var writeSpan = write;
                Initialize(ref writeSpan, MessageType.HasTimingCriticalCommandsScheduleCommand);
                var message = Finish(write, writeSpan);
                helper.Send(message);
                return helper.ReadBoolean();
            }
            catch (Exception ex) when (IsClosedPipeException(ex))
            {
                // swallow
                return false;
            }
        }

        public override McuSendResult Send(McuCommand command, int priority, McuOccasion clock, McuSendResult? cancelFirst = null)
        {
            using var helper = SendHelper();
            var length = Measure(command) + 4 + Measure(clock) + Measure(cancelFirst);
            Span<byte> write = stackalloc byte[MinMessageLength + length];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.SendCommand);
            Write(ref writeSpan, command);
            Write(ref writeSpan, priority);
            Write(ref writeSpan, clock);
            Write(ref writeSpan, cancelFirst);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            return helper.ReadSendResult();
        }

        public override bool TryReplace(McuSendResult id, McuCommand command)
        {
            using var helper = SendHelper();
            var length = Measure(id) + Measure(command);
            Span<byte> write = stackalloc byte[MinMessageLength + length];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.TryReplaceCommand);
            Write(ref writeSpan, id);
            Write(ref writeSpan, command);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            return helper.ReadBoolean();
        }

        public override void SendCancel(McuSendResult sendId)
        {
            using var helper = SendHelper();
            var length = Measure(sendId);
            Span<byte> write = stackalloc byte[MinMessageLength + length];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.SendCancelCommand);
            Write(ref writeSpan, sendId);
            var message = Finish(write, writeSpan);
            helper.Send(message);
        }

        public override void SendCancel(IEnumerable<McuSendResult> sendIds)
        {
            var ids = sendIds.ToArray();
            using var helper = SendHelper();
            var length = 4 + Measure(default(McuSendResult)) * ids.Length;
            Span<byte> write = stackalloc byte[MinMessageLength + length];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.SendCancelManyCommand);
            Write(ref writeSpan, ids.Length);
            for (int i = 0; i < ids.Length; i++)
                Write(ref writeSpan, ids[i]);
            var message = Finish(write, writeSpan);
            helper.Send(message);
        }

        private long SendWaitInner(McuCommand command, int priority, McuOccasion clock, McuSendResult? cancelFirst = null)
        {
            using var helper = SendHelper();
            var length = Measure(command) + 4 + Measure(clock) + Measure(cancelFirst);
            Span<byte> write = stackalloc byte[MinMessageLength + length];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.SendWaitCommand);
            Write(ref writeSpan, command);
            Write(ref writeSpan, priority);
            Write(ref writeSpan, clock);
            Write(ref writeSpan, cancelFirst);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            return helper.ReadInt64();
        }

        public override async Task SendWait(McuCommand command, int priority, McuOccasion clock, CancellationToken cancel = default)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancel.Register(() => source.TrySetCanceled()))
            {
                lock (_sendWaits)
                {
                    var id = SendWaitInner(command, priority, clock);
                    var added = _sendWaits.TryAdd(id, source);
                    Debug.Assert(added);
                }
                await source.Task.WaitAsync(_responseTimeout, cancel);
            }
        }

        protected override async ValueTask<IDisposable> CreateDevice(CancellationToken cancel)
        {
            var streamsToDevice = new NamedPipeServerStream[Environment.ProcessorCount];
            for (int i = 0; i < streamsToDevice.Length; i++)
                streamsToDevice[i] = new NamedPipeServerStream(
                    $"{GetType().FullName}[ToDevice-{_name}]",
                    PipeDirection.InOut,
                    streamsToDevice.Length,
                    PipeTransmissionMode.Byte,
                    PipeOptions.WriteThrough);
            var streamFromDevice = new NamedPipeServerStream(
                $"{GetType().FullName}[FromDevice-{_name}]",
                PipeDirection.InOut,
                streamsToDevice.Length,
                PipeTransmissionMode.Byte,
                PipeOptions.WriteThrough);
            try
            {
                // connect
                _logger.LogInformation($"MCU {Name} proxy streams are connecting");
                foreach (var streamToServer in streamsToDevice)
                    await streamToServer.WaitForConnectionAsync(cancel);
                await streamFromDevice.WaitForConnectionAsync(cancel);
                _logger.LogInformation($"MCU {Name} proxy streams have connected");

                // return device
                var device = new PipedDevice(this, streamsToDevice, streamFromDevice);
                _device = device;
                return device;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to wait for connections for MCU {_name}");
                foreach (var streamToServer in streamsToDevice)
                    streamToServer?.Dispose();
                streamFromDevice.Dispose();
                throw;
            }
        }

        protected override (Task ReadTask, Task WriteTask) CreateReadWriteTask(
            IDisposable device_,
            ulong initSeq,
            IDisposable inflightItems,
            PriorityScheduler runScheduler,
            CancellationToken cancel)
        {
            var device = (PipedDevice)device_;
            var readTask = Task.Factory.StartNew(
                () => EventReadProc(device, cancel),
                default,
                TaskCreationOptions.LongRunning,
                runScheduler);

            return (readTask, readTask);
        }

        private Task EventReadProc(
            PipedDevice device,
            CancellationToken cancel)
        {
            try
            {
                using (cancel.Register(device.Dispose))
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Read(device, cancel);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    _logger.Log(GetLogErrorLevel(), ex, $"Unhandled exception in piped MCU {_name} event read thread, will rethrow");
                    var receivedException = _receivedException;
                    if (receivedException != null)
                        throw receivedException;
                    else
                        throw;
                }
                else
                    return Task.CompletedTask;
            }
        }

        private void Read(PipedDevice device, CancellationToken cancel)
        {
            Span<byte> header = stackalloc byte[MinMessageLength];
            device.StreamsFromDevice.ReadExactly(header);
            var message = DecodeHeader(header);
            Span<byte> body = stackalloc byte[message.Length - MinMessageLength];
            device.StreamsFromDevice.ReadExactly(body);

            // process event
            switch (message.Type)
            {
                case MessageType.SendWaitEvent:
                    OnSendWaitEvent(body, cancel);
                    break;
                case MessageType.ResponseHandlerEvent:
                    OnResponseHandlerEvent(body, cancel);
                    break;
                case MessageType.ExceptionEvent:
                    OnExceptionEvent(body, cancel);
                    break;
                case MessageType.ClockSyncEvent:
                    OnClockSyncEvent(body, cancel);
                    break;
                case MessageType.ClockSyncUnreachableEvent:
                    OnClockSyncUnreachableEvent(cancel);
                    break;
                case MessageType.ClockSyncExceptionEvent:
                    OnClockSyncExceptionEvent(body, cancel);
                    break;
                case MessageType.LostCommunicationEvent:
                    OnLostCommunicationEvent(cancel);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid event received for MCU {_name}: {message.Type}, body={Convert.ToHexString(body)}");
            }
        }

        private void OnLostCommunicationEvent(CancellationToken cancel)
        {
            HandleLostCommunication(null);
        }

        private void OnClockSyncExceptionEvent(Span<byte> body, CancellationToken cancel)
        {
            var ex = ReadException(ref body);
            var clockSync = (PipedMcuClockSyncProxy)ClockSync;
            clockSync.SetException(ex);
        }

        private void OnClockSyncUnreachableEvent(CancellationToken cancel)
        {
            var clockSync = (PipedMcuClockSyncProxy)ClockSync;
            clockSync.SetUnreachable();
        }

        private void OnClockSyncEvent(Span<byte> body, CancellationToken cancel)
        {
            var clockSync = (PipedMcuClockSyncProxy)ClockSync;
            clockSync.Update(
                ReadDouble(ref body),
                ReadDouble(ref body),
                ReadDouble(ref body),
                ReadBoolean(ref body));
        }

        private void OnExceptionEvent(Span<byte> body, CancellationToken cancel)
        {
            _receivedException = ReadException(ref body);
            throw _receivedException;
        }

        private void OnSendWaitEvent(Span<byte> body, CancellationToken cancel)
        {
            var id = ReadInt64(ref body);
            if (_sendWaits.TryRemove(id, out var source))
                source.TrySetResult();
        }

        private void OnResponseHandlerEvent(Span<byte> body, CancellationToken cancel)
        {
            var handlerId = ReadInt64(ref body);
            Exception? exception = null;
            McuCommand? response = null;
            if (ReadBoolean(ref body))
                response = ReadCommand(ref body, this, null);
            else
                exception = ReadException(ref body);
            if (_responseHandlersByHandlerId.TryGetValue(handlerId, out var handlerInfo))
                _responseTaskQueue.EnqueueValue(() => handlerInfo.Handler(exception, response, cancel), null, true);
        }

        protected override IMcuInflightItems CreateInflightItems()
            => new NullMcuInflightItems();

        protected override async Task<bool> SendConfigCommands(CancellationToken cancel)
        {
            await SendConfigCommands(_configCommands, cancel);
            return true; // true -> set WasReady
        }

        protected override ValueTask<ulong> SynchronizeDevice(IDisposable device, CancellationToken cancel)
        {
            // NOTE: handled on server Mcu
            return ValueTask.FromResult(0UL);
        }

        protected override void ClearCommandQueue()
        {
            // NOTE: do nothing
        }

        protected override Task CheckFirmwareUpdate(IDisposable device, CancellationToken cancel)
        {
            // NOTE: handled in server Mcu
            return Task.CompletedTask;
        }

        public override void RegisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler)
            => _configCommands.InitializingEvent.AddHandler(handler);
        public override void UnregisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler)
            => _configCommands.InitializingEvent.RemoveHandler(handler);

        private PipedDevice.ToDeviceHelper SendHelper(CancellationToken cancel = default)
        {
            var device = _device;
            if (device == null)
                throw new InvalidOperationException($"Piped MCU {_name} is not connected, stream is not yet ready");
            return device.SendHelper(cancel);
        }

        protected override async ValueTask CancelResponseHandlers(Exception responseException, CancellationToken cancel)
        {
            foreach (var handlerInfo in _responseHandlersByHandlerId.Values)
            {
                try
                {
                    await handlerInfo.Handler(responseException, null, cancel);
                }
                catch (Exception ex)
                {
                    Config.IdToCommand.TryGetValue(handlerInfo.CommandId, out var command);
                    _logger.LogError(ex, $"Failed to call response '{command?.CommandName}' handler for MCU {Name}: {ex.Message}");
                }
            }
            _responseHandlersByHandlerId.Clear();
            _responseHandlersByHandler.Clear();
        }

        public override IDisposable RegisterResponseHandler(McuCommand? request, McuCommand response, McuResponseHandler handler)
        {
            using var helper = SendHelper();
            var length = (request != null ? 1 + Measure(request) : 1) + Measure(response);
            Span<byte> write = stackalloc byte[MinMessageLength + length];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.RegisterResponseHandlerCommand);
            if (request != null)
            {
                Write(ref writeSpan, true);
                Write(ref writeSpan, request);
            }
            else
                Write(ref writeSpan, false);
            Write(ref writeSpan, response);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            var handlerId = helper.ReadInt64();

            var added = _responseHandlersByHandlerId.TryAdd(handlerId, (response.CommandId, handlerId, handler));
            Debug.Assert(added);
            while (!_responseHandlersByHandler.TryAdd(handler, (response.CommandId, handlerId)))
            {
                if (_responseHandlersByHandler.TryGetValue(handler, out var handlerInfo))
                    UnregisterResponseHandler(Config.IdToCommand[handlerInfo.CommandId], handler);
            }
            return new RegisterResponseHandlerDisposable(this, response, handler);
        }

        protected override void UnregisterResponseHandler(McuCommand response, McuResponseHandler handler)
        {
            if (!_responseHandlersByHandler.TryRemove(handler, out var handlerInfo))
                return;
            if (!_responseHandlersByHandlerId.TryRemove(handlerInfo.HandlerId, out _))
                return;

            using var helper = SendHelper();
            Span<byte> write = stackalloc byte[MinMessageLength + 8];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.UnregisterResponseHandlerCommand);
            Write(ref writeSpan, handlerInfo.HandlerId);
            var message = Finish(write, writeSpan);
            helper.Send(message);
        }

        public bool TryCollectGarbageBlocking(bool performMajorCleanup)
        {
            using var helper = SendHelper();
            Span<byte> write = stackalloc byte[MinMessageLength + 1];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.CollectGarbageCommand);
            Write(ref writeSpan, performMajorCleanup);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            return helper.ReadBoolean();
        }

        public bool TryEnterPrintingMode()
        {
            using var helper = SendHelper();
            Span<byte> write = stackalloc byte[MinMessageLength];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.EnterPrintingModeCommand);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            return helper.ReadBoolean();
        }

        public bool TryExitPrintingMode()
        {
            using var helper = SendHelper();
            Span<byte> write = stackalloc byte[MinMessageLength];
            var writeSpan = write;
            Initialize(ref writeSpan, MessageType.ExitPrintingModeCommand);
            var message = Finish(write, writeSpan);
            helper.Send(message);
            return helper.ReadBoolean();
        }
    }
}
