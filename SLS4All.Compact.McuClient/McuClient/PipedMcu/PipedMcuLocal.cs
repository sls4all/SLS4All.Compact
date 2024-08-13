// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Storage.PrinterSettings;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static SLS4All.Compact.McuClient.PipedMcu.PipedMcuCodec;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    public sealed class PipedMcuLocal : McuAbstract
    {
        private readonly ConcurrentDictionary<long, IDisposable> _responseHandlers;
        private readonly PriorityScheduler _streamToDeviceScheduler;
        private readonly object _commandFactoriesLock = new();
        private PipedCommandFactory[] _commandFactories = [];
        private long _lastSendWaitId;
        private long _lastResponseHandlerId;
        private volatile Stream? _streamFromDevice;

        public override int CreatedArenas
        {
            get
            {
                var count = base.CreatedArenas;
                foreach (var factory in _commandFactories)
                    count += factory.CreatedArenas;
                return count;
            }
        }

        public override long CreatedCommands
        {
            get
            {
                var count = base.CreatedCommands;
                foreach (var factory in _commandFactories)
                    count += factory.CreatedCommands;
                return count;
            }
        }

        public PipedMcuLocal(
            ILoggerFactory loggerFactory, 
            IAppDataWriter appDataWriter, 
            McuManager manager, 
            IOptions<McuOptions> options, 
            IMcuClockSync clockSync, 
            IEnumerable<IMcuDeviceFactory> deviceFactories,
            IPrinterSettingsStorage settingsStorage) 
            : base(loggerFactory, loggerFactory.CreateLogger<PipedMcuLocal>(), appDataWriter, manager, options, clockSync, deviceFactories)
        {
            _responseHandlers = new();
            _streamToDeviceScheduler = new PriorityScheduler(
                $"MCU {Name} piped StreamToDevice scheduler", 
                ThreadPriority.AboveNormal, 
                Environment.ProcessorCount);
        }

        protected override async Task RunAfterClockSync(Func<Task> innerRun, CancellationToken cancel)
        {
            var mcuCancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            var mcuCancel = mcuCancelSource.Token;
            var streamsToDevice = new NamedPipeClientStream[Environment.ProcessorCount];
            for (int i = 0; i < streamsToDevice.Length; i++)
            {
                var streamToDevice = new NamedPipeClientStream(
                    ".",
                    $"{typeof(PipedMcuProxy).FullName}[ToDevice-{Name}]",
                    PipeDirection.InOut,
                    PipeOptions.WriteThrough);
                streamsToDevice[i] = streamToDevice;
            }
            var streamFromDevice = new NamedPipeClientStream(
                ".",
                $"{typeof(PipedMcuProxy)}[FromDevice-{Name}]",
                PipeDirection.InOut,
                PipeOptions.WriteThrough);

            void DisposeStreams()
            {
                foreach (var streamToDevice in streamsToDevice)
                    streamToDevice.Dispose();
                lock (streamFromDevice) // ensure everything is sent
                    streamFromDevice.Dispose();
            }
            try
            {
                using (cancel.Register(DisposeStreams))
                {
                    var tasks = new List<Task>();
                    try
                    {
                        // connect
                        _logger.LogInformation($"MCU {Name} proxy streams are connecting");
                        foreach (var streamToDevice in streamsToDevice)
                            await streamToDevice.ConnectAsync(mcuCancel);
                        await streamFromDevice.ConnectAsync(mcuCancel);
                        _streamFromDevice = streamFromDevice;
                        _logger.LogInformation($"MCU {Name} proxy streams have connected");

                        // process stream
                        foreach (var streamToDevice in streamsToDevice)
                        {
                            tasks.Add(Task.Factory.StartNew(() => ProcessStream(streamToDevice, streamFromDevice, mcuCancel),
                                mcuCancel,
                                TaskCreationOptions.LongRunning,
                                _streamToDeviceScheduler).Unwrap());
                        }
                        tasks.Add(innerRun());

                        var finishedTask = await Task.WhenAny(tasks);
                        await finishedTask;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Unhandled exception while running MCU {Name}, cancelling");
                        SendException(streamFromDevice, ex);
                    }
                    finally
                    {
                        mcuCancelSource.Cancel();
                        DisposeStreams();
                    }
                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Unhandled exception while running MCU {Name}, already cancelled");
                    }
                }
            }
            finally
            {
                _streamFromDevice = null;
            }
        }

        private void SendException(Stream streamFromDevice, Exception ex)
        {
            try
            {
                Span<byte> buffer = stackalloc byte[MinMessageLength + Measure(ex)];
                var bufferSpan = buffer;
                Initialize(ref bufferSpan, MessageType.ExceptionEvent);
                Write(ref bufferSpan, ex);
                Finish(buffer, bufferSpan);
                WriteEvent(streamFromDevice, buffer);
            }
            catch
            {
                // swallow
            }
        }

        private Task ProcessStream(Stream streamToDevice, Stream streamFromDevice, CancellationToken cancel)
        {
            while (true)
            {
                var commandFactory = new PipedCommandFactory(this);
                lock (_commandFactoriesLock)
                {
                    _commandFactories = _commandFactories.Append(commandFactory).ToArray();
                }
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    Read(commandFactory, streamToDevice, streamFromDevice, cancel);
                }
            }
        }

        private void Read(PipedCommandFactory commandFactory, Stream streamToDevice, Stream streamFromDevice, CancellationToken cancel)
        {
            Span<byte> header = stackalloc byte[MinMessageLength];
            streamToDevice.ReadExactly(header);
            var message = DecodeHeader(header);
            Span<byte> body = message.Length - MinMessageLength != 0 ? stackalloc byte[message.Length - MinMessageLength] : [];
            if (body.Length > 0)
                streamToDevice.ReadExactly(body);

            // process
            switch (message.Type)
            {
                case MessageType.SendCommand:
                    OnSendCommand(body, streamToDevice, commandFactory);
                    break;
                case MessageType.SendCancelCommand:
                    OnSendCancelCommand(body);
                    break;
                case MessageType.SendCancelManyCommand:
                    OnSendCancelManyCommand(body);
                    break;
                case MessageType.TryReplaceCommand:
                    OnTryReplaceCommand(body, commandFactory, streamToDevice);
                    break;
                case MessageType.SendWaitCommand:
                    OnSendWaitCommand(body, commandFactory, streamToDevice, streamFromDevice, cancel);
                    break;
                case MessageType.RegisterResponseHandlerCommand:
                    OnRegisterResponseHandlerCommand(body, streamToDevice, streamFromDevice, cancel);
                    break;
                case MessageType.UnregisterResponseHandlerCommand:
                    OnUnregisterResponseHandlerCommand(body, cancel);
                    break;
                case MessageType.HasTimingCriticalCommandsScheduleCommand:
                    HasTimingCriticalCommandsScheduleCommand(streamToDevice, cancel);
                    break;
                case MessageType.GetCurrentErrorCommand:
                    GetCurrentErrorCommand(streamToDevice, cancel);
                    break;
                case MessageType.CollectGarbageCommand:
                    CollectGarbageCommand(streamToDevice, cancel);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid command received for MCU {Name}: {message.Type}");
            }
        }

        private void CollectGarbageCommand(Stream streamToDevice, CancellationToken cancel)
        {
            var hasCollected = false;
            var hasTimingCriticalCommandsScheduled = _manager.HasTimingCriticalCommandsScheduled;
            if (!hasTimingCriticalCommandsScheduled)
            {
                _logger.LogDebug($"Collect garbage - begin");
                var start = SystemTimestamp.Now;
                hasCollected = _manager.TryCollectGarbageBlocking();
                _logger.LogDebug($"Collect garbage - end. GC duration = {start.ElapsedFromNow}. CreatedCommands={_manager.CreatedCommands}, CreatedArenas={_manager.CreatedArenas}");
            }
            else
                _logger.LogDebug($"Collect garbage - critical commands scheduled.");
            PrinterGC.LogCollectionCount(_logger);
            streamToDevice.WriteByte(hasCollected ? (byte)1 : (byte)0);
        }

        private void HasTimingCriticalCommandsScheduleCommand(Stream streamToDevice, CancellationToken cancel)
        {
            streamToDevice.WriteByte(this.HasTimingCriticalCommandsScheduled ? (byte)1 : (byte)0);
        }

        private void GetCurrentErrorCommand(Stream streamToDevice, CancellationToken cancel)
        {
            var ex = CurrentError;
            var exLength = ex != null ? Measure(ex) : 0;
            var length = 4 + exLength;
            Span<byte> buffer = stackalloc byte[length];
            var write = buffer;
            Write(ref write, exLength);
            if (ex != null)
                Write(ref write, ex);
            streamToDevice.Write(buffer);
        }

        private void OnRegisterResponseHandlerCommand(Span<byte> body, Stream streamToDevice, Stream streamFromDevice, CancellationToken cancel)
        {
            var request = ReadBoolean(ref body) ? ReadCommand(ref body, this, null) : null;
            var response = ReadCommand(ref body, this, null);
            var handlerId = Interlocked.Increment(ref _lastResponseHandlerId);
            _responseHandlers[handlerId] = RegisterResponseHandler(
                request,
                response,
                (Exception? exception, McuCommand? command, CancellationToken cancel) =>
                {
                    if (exception != null)
                    {
                        Span<byte> buffer = stackalloc byte[MinMessageLength + 8 + 1 + Measure(exception)];
                        var bufferSpan = buffer;
                        Initialize(ref bufferSpan, MessageType.ResponseHandlerEvent);
                        Write(ref bufferSpan, handlerId);
                        Write(ref bufferSpan, false);
                        Write(ref bufferSpan, exception);
                        Finish(buffer, bufferSpan);
                        WriteEvent(streamFromDevice, buffer);
                    }
                    else if (command != null)
                    {
                        Span<byte> buffer = stackalloc byte[MinMessageLength + 8 + 1 + Measure(command)];
                        var bufferSpan = buffer;
                        Initialize(ref bufferSpan, MessageType.ResponseHandlerEvent);
                        Write(ref bufferSpan, handlerId);
                        Write(ref bufferSpan, true);
                        Write(ref bufferSpan, command);
                        Finish(buffer, bufferSpan);
                        WriteEvent(streamFromDevice, buffer);
                    }
                    return ValueTask.CompletedTask;
                });

            Span<byte> sentResponse = stackalloc byte[8];
            var responseSpan = sentResponse;
            Write(ref responseSpan, handlerId);
            streamToDevice.Write(sentResponse);
        }

        private void WriteEvent(Stream streamFromDevice, Span<byte> buffer)
        {
            try
            {
                lock (streamFromDevice)
                    streamFromDevice.Write(buffer);
            }
            catch (Exception ex) when (IsClosedPipeException(ex))
            {
                throw new McuException("Pipe is closed, event write failed", ex);
            }
        }

        private void OnUnregisterResponseHandlerCommand(Span<byte> body, CancellationToken cancel)
        {
            var handlerId = ReadInt64(ref body);
            if (_responseHandlers.TryRemove(handlerId, out var disposable))
                disposable.Dispose();
        }

        private void OnSendCommand(Span<byte> body, Stream streamToDevice, PipedCommandFactory commandFactory)
        {
            var command = ReadCommand(ref body, null, commandFactory);
            try
            {
                var priority = ReadInt32(ref body);
                var clock = ReadMcuOccasion(ref body);
                var cancelFirst = ReadMcuSendResultNullable(ref body);
                var sendResult = Send(command, priority, clock, cancelFirst);
                Span<byte> response = stackalloc byte[Measure(sendResult)];
                var responseSpan = response;
                Write(ref responseSpan, sendResult);
                streamToDevice.Write(response);
            }
            finally
            {
                commandFactory.ReturnCommand(command);
            }
        }

        private void OnSendCancelCommand(Span<byte> body)
        {
            var id = ReadMcuSendResult(ref body);
            SendCancel(id);
        }

        private void OnSendCancelManyCommand(Span<byte> body)
        {
            var length = ReadInt32(ref body);
            var ids = new McuSendResult[length];
            for (int i = 0; i < length; i++)
                ids[i] = ReadMcuSendResult(ref body);
            SendCancel(ids);
        }

        private void OnTryReplaceCommand(Span<byte> body, PipedCommandFactory commandFactory, Stream streamToDevice)
        {
            var id = ReadMcuSendResult(ref body);
            var command = ReadCommand(ref body, null, commandFactory);
            try
            {
                var result = TryReplace(id, command);
                streamToDevice.WriteByte(result ? (byte)1 : (byte)0);
            }
            finally
            {
                commandFactory.ReturnCommand(command);
            }
        }

        private void OnSendWaitCommand(Span<byte> body, PipedCommandFactory commandFactory, Stream streamToDevice, Stream streamFromDevice, CancellationToken cancel)
        {
            var id = Interlocked.Increment(ref _lastSendWaitId);
            var command = ReadCommand(ref body, null, commandFactory);
            try
            {
                var priority = ReadInt32(ref body);
                var clock = ReadMcuOccasion(ref body);
                var cancelFirst = ReadMcuSendResultNullable(ref body);
                var task = SendWait(command, priority, clock, cancel);

                // NOTE: we cannot block and throw, we are on pipe read task
                task.ContinueWith(_ =>
                {
                    try
                    {
                        Span<byte> buffer = stackalloc byte[MinMessageLength + 8];
                        var bufferSpan = buffer;
                        Initialize(ref bufferSpan, MessageType.SendWaitEvent);
                        Write(ref bufferSpan, id);
                        Finish(buffer, bufferSpan);
                        WriteEvent(streamFromDevice, buffer);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to write response for id: {id}");
                    }
                    return Task.CompletedTask;
                });

                Span<byte> response = stackalloc byte[8];
                var responseSpan = response;
                Write(ref responseSpan, id);
                streamToDevice.Write(response);
            }
            finally
            {
                commandFactory.ReturnCommand(command);
            }
        }

        protected override Task<bool> SendConfigCommands(CancellationToken cancel)
        {
            // NOTE: do nothing, delegated to proxy
            cancel.ThrowIfCancellationRequested();
            return Task.FromResult(false); // return false, does not set WasReady to allow seamless restart (other states are delegated to proxy)
        }

        public override void Dispose()
        {
            base.Dispose();
            _streamToDeviceScheduler.Dispose();
        }

        public void SendClockSyncEvent(double sampleTime, double clock, double freq, bool isReady)
        {
            var streamFromDevice = _streamFromDevice;
            if (streamFromDevice == null)
                return;
            try
            {
                Span<byte> buffer = stackalloc byte[MinMessageLength + 8 + 8 + 8 + 1];
                var bufferSpan = buffer;
                Initialize(ref bufferSpan, MessageType.ClockSyncEvent);
                Write(ref bufferSpan, sampleTime);
                Write(ref bufferSpan, clock);
                Write(ref bufferSpan, freq);
                Write(ref bufferSpan, isReady);
                Finish(buffer, bufferSpan);
                WriteEvent(streamFromDevice, buffer);
            }
            catch
            {
                // swallow
            }
        }

        internal void SendClockSyncUnreachableEvent()
        {
            var streamFromDevice = _streamFromDevice;
            if (streamFromDevice == null)
                return;
            try
            {
                Span<byte> buffer = stackalloc byte[MinMessageLength];
                var bufferSpan = buffer;
                Initialize(ref bufferSpan, MessageType.ClockSyncUnreachableEvent);
                Finish(buffer, bufferSpan);
                WriteEvent(streamFromDevice, buffer);
            }
            catch
            {
                // swallow
            }
        }

        public void SendClockSyncExceptionEvent(Exception ex)
        {
            var streamFromDevice = _streamFromDevice;
            if (streamFromDevice == null)
                return;
            try
            {
                Span<byte> buffer = stackalloc byte[MinMessageLength + Measure(ex)];
                var bufferSpan = buffer;
                Initialize(ref bufferSpan, MessageType.ClockSyncExceptionEvent);
                Write(ref bufferSpan, ex);
                Finish(buffer, bufferSpan);
                WriteEvent(streamFromDevice, buffer);
            }
            catch
            {
                // swallow
            }
        }

        protected override void OnHasLostCommunication()
        {
            base.OnHasLostCommunication();
            var streamFromDevice = _streamFromDevice;
            if (streamFromDevice == null)
                return;
            try
            {
                Span<byte> buffer = stackalloc byte[MinMessageLength];
                var bufferSpan = buffer;
                Initialize(ref bufferSpan, MessageType.LostCommunicationEvent);
                Finish(buffer, bufferSpan);
                WriteEvent(streamFromDevice, buffer);
            }
            catch
            {
                // swallow
            }
        }

        protected override ValueTask OnMcuShutdown(Exception? exception, McuCommand? command, CancellationToken cancel)
        {
            // NOTE: base class would shutdown manager and all MCUs, we wont do that here, it is the proxy caller responsibility
            return ValueTask.CompletedTask;
        }
    }
}