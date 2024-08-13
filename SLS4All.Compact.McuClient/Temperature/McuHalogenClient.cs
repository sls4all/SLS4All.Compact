// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient.Sensors;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SLS4All.Compact.Storage.PrinterSettings;
using SLS4All.Compact.Configuration;

namespace SLS4All.Compact.Temperature
{
    public class McuHalogenClientOptions
    {
        public TimeSpan LowFrequencyPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public string SurfaceId { get; set; } = "surface";
    }

    public sealed class McuHalogenClient : BackgroundThreadService, IHalogenClient, ILightsClient
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<McuHalogenClientOptions> _options;
        private readonly IMediator _mediator;
        private readonly McuPrinterClient _printerClient;
        private readonly IPrinterSettingsStorage _settingsStorage;
        private readonly PeriodicTimer _lowFrequencyTimer;
        private readonly string _surfaceId;

        private volatile LightsState _lowFrequencyState;
        private volatile int _lightCount;
        private volatile PrinterPowerSettings _powerSettings;

        public LightsState CurrentState => _lowFrequencyState;
        public AsyncEvent<LightsState> StateChangedLowFrequency { get; } = new();
        public AsyncEvent<LightsState> StateChangedHighFrequency { get; } = new();
        public string SurfaceId => _surfaceId;
        public int LightCount => _lightCount;

        public McuHalogenClient(
            ILogger<McuHalogenClient> logger,
            IOptionsMonitor<McuHalogenClientOptions> options,
            IMediator mediator,
            McuPrinterClient printerClient,
            IPrinterSettingsStorage settingsStorage)
            : base(logger)
        {
            _logger = logger;
            _options = options;
            _mediator = mediator;
            _printerClient = printerClient;
            _settingsStorage = settingsStorage;

            var o = options.CurrentValue;
            _surfaceId = o.SurfaceId;
            _lowFrequencyState = new(false);
            _lowFrequencyTimer = new PeriodicTimer(o.LowFrequencyPeriod);
            _powerSettings = _settingsStorage.GetPowerSettings();
        }

        public IDisposable SupressValidation()
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, null);
            var heater = manager.Heaters[_surfaceId];
            return heater.SupressValidation();
        }

        public float GetMaxHalogenFactor(IPrinterClientCommandContext? context = null)
        {
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var heater = (IMcuLightsControl)manager.Heaters[_surfaceId];
            return heater.MaxLightFactor;
        }

        ValueTask ILightsClient.SetLights(bool enabled, int? mask, float? power, bool hidden, IPrinterClientCommandContext? context, CancellationToken cancel)
            => SetHalogens(enabled, mask, power, hidden, false, context, cancel);

        public ValueTask SetHalogens(bool enabled, int? mask = null, float? power = null, bool hidden = false, bool forceMax = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var heater = (IMcuLightsControl)manager.Heaters[_surfaceId];
            heater.SetLights(enabled, mask, power, forceMax: forceMax);

            var state = GetState(heater);
            return StateChangedHighFrequency.Invoke(state, cancel);
        }

        public ValueTask SetHalogens(Memory<(bool enabled, int index, float? power)> values, bool hidden, bool forceMax = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var heater = (IMcuLightsControl)manager.Heaters[_surfaceId];
            heater.SetLights(values.Span, forceMax: forceMax);
            var state = GetState(heater);
            return StateChangedHighFrequency.Invoke(state, cancel);
        }

        private LightsState GetState(IMcuLightsControl heater)
        {
            return heater.HasLightsEnabled
                ? LightsState.LightsStateEnabled
                : LightsState.LightsStateDisabled;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            while (true)
            {
                try
                {
                    _powerSettings = _settingsStorage.GetPowerSettings();
                    var manager = _printerClient.ManagerIfReady;
                    if (manager != null)
                    {
                        if (manager.Heaters.TryGetValue(_surfaceId, out var surface))
                        {
                            var heater = (IMcuLightsControl)surface;
                            var state = GetState(heater);
                            _lightCount = heater.LightCount;
                            _lowFrequencyState = state;
                            await _mediator.Publish(state, cancel);
                            await StateChangedLowFrequency.Invoke(state, cancel);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to get/process low frequency data");
                }
                try
                {
                    await _lowFrequencyTimer.WaitForNextTickAsync(cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to wait for next period");
                }
            }
        }

        public bool TryGetRecentLightPower(int index, out bool isCurrent, out float power, SystemTimestamp now, TimeSpan? duration, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManagerIfReady(_printerClient, context);
            if (manager == null)
            {
                isCurrent = true;
                power = 0;
                return false;
            }
            var heater = (IMcuLightsControl)manager.Heaters[_surfaceId];
            return heater.TryGetRecentLightPower(index, out isCurrent, out power, now, duration);
        }

        public bool HasRecentLightPower(int index, SystemTimestamp now = default, TimeSpan? duration = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManagerIfReady(_printerClient, context);
            if (manager == null)
                return false;
            var heater = (IMcuLightsControl)manager.Heaters[_surfaceId];
            return heater.HasRecentLightPower(index, now, duration);
        }

        public bool HasRecentLightPower(SystemTimestamp now = default, TimeSpan? duration = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManagerIfReady(_printerClient, context);
            if (manager == null)
                return false;
            var heater = (IMcuLightsControl)manager.Heaters[_surfaceId];
            for (int i = 0; i < _lightCount; i++)
                if (heater.HasRecentLightPower(i, now, duration))
                    return true;
            return false;
        }
    }
}
