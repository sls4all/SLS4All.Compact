using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public class FakeMcu : McuBase
    {
        private sealed class InflightItems : IMcuInflightItems
        {
            public static InflightItems Instance { get; } = new();
            public void Clear()
            {
            }

            public void Dispose()
            {
            }
        }

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<McuResponseHandler, string>> _responseHandlers;
        private readonly FakeMcuDeviceFactory _fakeDeviceFactory;
        private readonly TaskQueue _responseTaskQueue;
        private readonly McuConfigCommands _configCommands;

        public override bool HasTimingCriticalCommandsScheduled => false;
        public override bool IsFake => true;

        public FakeMcu(
            ILoggerFactory loggerFactory, 
            ILogger<FakeMcu> logger, 
            IAppDataWriter appDataWriter, 
            McuManager manager, 
            IOptions<McuOptions> options, 
            IMcuClockSync clockSync,
            FakeMcuDeviceFactory fakeDeviceFactory) 
            : base(loggerFactory, logger, appDataWriter, manager, options, clockSync)
        {
            _fakeDeviceFactory = fakeDeviceFactory;
            _responseHandlers = new();
            _responseTaskQueue = new();
            _configCommands = new();
        }

        public override void RegisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler)
            => _configCommands.InitializingEvent.AddHandler(handler);

        public override void UnregisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler)
            => _configCommands.InitializingEvent.RemoveHandler(handler);

        protected override async Task<bool> SendConfigCommands(CancellationToken cancel)
        {
            await SendConfigCommands(_configCommands, ignoreCrc: true, cancel);
            return true;
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

        public override void SendCancel(McuSendResult sendId)
        {
        }

        public override void SendCancel(IEnumerable<McuSendResult> sendIds)
        { 
        }

        public override McuTimestamp MovementCancel()
            => default;

        public override bool TryReplace(McuSendResult id, McuOccasion occasion, McuCommand command)
            => false;

        protected override Task CheckFirmwareUpdate(IDisposable device, CancellationToken cancel)
            => Task.CompletedTask;

        protected override void ClearCommandQueue()
        {
        }

        protected override async ValueTask<IDisposable> CreateDevice(CancellationToken cancel)
        {
            var options = _options.Value;
            if (options.Device != null)
            {
                var info = FakeMcuDeviceFactory.CreateDeviceInfo(options.Device.Alias ?? options.Name);
                var device = await _fakeDeviceFactory.Open(info, cancel);
                return device;
            }
            else
            {
                List<(McuDeviceInfo Info, IMcuDeviceFactory Factory)> deviceNames = new();
                var factoryDeviceNames = await _fakeDeviceFactory.GetDeviceNames(cancel);
                foreach (var item in factoryDeviceNames)
                {
                    if (item.Name == _name)
                        deviceNames.Add((item, _fakeDeviceFactory));
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

        protected override IMcuInflightItems CreateInflightItems()
            => InflightItems.Instance;

        protected override (Task ReadTask, Task WriteTask) CreateReadWriteTask(
            IDisposable device_, 
            ulong initSeq, 
            IDisposable inflightItems, 
            PriorityScheduler runScheduler, 
            CancellationToken cancel)
        {
            var task = Task.Delay(-1, cancel);
            return (task, task);
        }

        protected override Task<McuConfig> IdentifyDevice(IDisposable device_, CancellationToken cancel)
        {
            var device = (FakeMcuDeviceFactory.FakeDevice)device_;
            var config = device.Config;
            return Task.FromResult(config);
        }

        protected override ValueTask<ulong> SynchronizeDevice(IDisposable device, CancellationToken cancel)
            => ValueTask.FromResult(0UL);

        public override McuSendResult Send(McuCommand command, int priority, McuOccasion clock, McuSendResult? cancelFirst = null)
        {
            var now = SystemTimestamp.Now;
            switch (command.CommandName)
            {
                case "get_uptime":
                case "get_clock":
                    {
                        var freq = Config.GetConstInt32("CLOCK_FREQ");
                        var uptime = (long)(now.TotalSeconds * freq);
                        var response = command.CommandName == "get_uptime"
                            ? this.LookupCommand("uptime")
                                .Bind("high", uptime >> 32)
                                .Bind("clock", uptime & 0xFFFFFFFF)
                            : this.LookupCommand("clock")
                                .Bind("clock", uptime & 0xFFFFFFFF);
                        SendResponse(
                            now,
                            response,
                            default);
                        return default;
                    }
                case "get_config":
                    {
                        var response = this.LookupCommand("config")
                            .Bind("is_config", 1);
                        SendResponse(
                            now,
                            response,
                            default);
                        return default;
                    }
            }
            return default;
        }

        public override Task SendWait(McuCommand command, int priority, McuOccasion clock, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            Send(command, priority, clock);
            return Task.CompletedTask;
        }

        protected void SendResponse(SystemTimestamp now, McuCommand response, CancellationToken cancel)
        {
            if (_responseHandlers.TryGetValue(response.CommandId, out var handlers) && handlers.Count > 0)
            {
                foreach (var handler_ in handlers)
                {
                    var handler = handler_;
                    response.SentTimestamp = now.TotalSeconds;
                    response.ReceiveTimestamp = now.TotalSeconds + 0.0001;
                    _responseTaskQueue.EnqueueValue(() => handler.Key(null, response, cancel), null, true);
                }
            }
        }
    }
}
