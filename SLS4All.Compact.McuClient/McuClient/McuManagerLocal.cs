// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.McuClient.Sensors;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;
using static SLS4All.Compact.McuClient.McuBase;

namespace SLS4All.Compact.McuClient
{
    public class McuManagerOptions
    {
        public class ManagerMcuOptions : McuOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public class ManagerTmc220xDriverOptions : Tmc220xDriverOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public class ManagerInovaGate1TemperatureSensorOptions : InovaGate1TemperatureSensorOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public class ManagerInovaSurfaceBoxSensorOptions : InovaSurfaceBoxSensorOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public class ManagerHysteresisHeaterOptions : HysteresisHeaterOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public class ManagerInovaSurfaceHeaterOptions : InovaSurfaceHeaterOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public class ManagerStepperOptions : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public McuStepperOptions? Stepper { get; set; }
            public ManagerOutputPinOptions? PwmPin { get; set; }
            public ManagerTmc220xDriverOptions? Tmc2209Driver { get; set; }
        }

        public class ManagerOutputPinOptions : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public required McuPinType Type { get; set; }
            public required string Pin { get; set; }
            public double? CycleTime { get; set; }
            public bool AllowInShutdown { get; set; } = false;
            public float? StartValue { get; set; }
            public float? ShutdownValue { get; set; }
            public bool IsStatic { get; set; } = false;
            public TimeSpan? MaxDuration { get; set; } = null;

            public McuPinDescription LookupPin(McuManager manager)
                => manager.ClaimPin(Type, Pin, canInvert: true)
                    with
                {
                    CycleTime = CycleTime,
                    AllowInShutdown = AllowInShutdown,
                    MaxDuration = MaxDuration,
                };
        }

        public class ManagerButtonPinOptions : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public required string Pin { get; set; }

            public McuPinDescription LookupPin(McuManager manager)
                => manager.ClaimPin(McuPinType.Button, Pin, canInvert: true);
        }

        public class ManagerButtonOptions : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public Dictionary<string, ManagerButtonPinOptions?> Pins { get; set; } = new();

            public McuPinDescription[] LookupPins(McuManager manager)
                => Pins.GetOrderedEnabledValues().Select(x => x.LookupPin(manager)).ToArray();
        }

        public class ManagerTemperatureSensor : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public ManagerInovaGate1TemperatureSensorOptions? InovaGate1 { get; set; }
            public ManagerInovaSurfaceBoxSensorOptions? InovaSurfaceBox { get; set; }
            public TemperatureSensorCheckOptions? VerifySensor { get; set; }
        }

        public class ManagerHeater : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public ManagerTemperatureSensor? Sensor { get; set; }
            public ManagerHysteresisHeaterOptions? Hysteresis { get; set; }
            public ManagerInovaSurfaceHeaterOptions? InovaSurface { get; set; }
            public TemperatureSensorCheckOptions? VerifySensor { get; set; }
            public HeaterCheckOptions? VerifyHeater { get; set; }
        }

        public class ManagerMcuSdCardSpi : McuSdCardSpiOptions, IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
        }

        public ManagerOutputPinOptions? DimmerSensorPin { get; set; }
        public Dictionary<string, ManagerMcuOptions?> Mcus { get; set; } = new();
        public Dictionary<string, ManagerStepperOptions?> Steppers { get; set; } = new();
        public Dictionary<string, ManagerOutputPinOptions?> OutputPins { get; set; } = new();
        public Dictionary<string, ManagerButtonOptions?> Buttons { get; set; } = new();
        public Dictionary<string, ManagerTemperatureSensor?> TemperatureSensors { get; set; } = new();
        public Dictionary<string, ManagerHeater?> Heaters { get; set; } = new();
        public Dictionary<string, ManagerMcuSdCardSpi?> SdCardSpi { get; set; } = new();
        public McuPowerManagerOptions PowerManager { get; set; } = new McuPowerManagerOptions();
        public bool DisableTemperatureVerification { get; set; }
        public bool DisableKeepaliveEnable { get; set; }
        public Dictionary<string, string?> KeepaliveEnablePins { get; set; } = new();
        public TimeSpan KeepaliveEnablePeriod { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan KeepaliveEnableMaxDuration { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan QueueMasterLockTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }

    public record class McuManagerShutdownReason(IMcu? Mcu, string? Reason)
    {
        public override string ToString()
        {
            if (Mcu != null && Reason != null)
                return $"Originating MCU {Mcu}: {Reason}";
            else if (Mcu != null)
                return $"Originating MCU {Mcu}";
            else if (Reason != null)
                return Reason;
            else
                return "Unspecified reason";
        }
    }

    public class McuManagerLocal : McuManager
    {
        private readonly IOptionsMonitor<McuStepperGlobalOptions> _optionsStepperGlobal;
        private readonly ITemperatureCamera _temperatureCamera;
        private readonly Dictionary<McuPinKey, McuPinDescription> _activePinsNeedsLock;
        private readonly Dictionary<McuBusKey, McuBusDescription> _activeBusNeedsLock;
        private readonly Dictionary<string, IMcuStepper> _steppersInit;
        private readonly Dictionary<string, IMcuOutputPin> _outputPinsInit;
        private readonly Dictionary<string, IMcuButton> _buttonsInit;
        private readonly Dictionary<string, IMcuTemperatureSensor> _temperatureSensorsInit;
        private readonly Dictionary<string, IMcuHeater> _heatersInit;
        private readonly Dictionary<string, IMcuSdCard> _sdCardsInit;
        private readonly McuPowerManager _powerManager;
        private readonly TimeSpan _stepperQueueHigh;
        private readonly TimeSpan _stepperQueueLow;
        private IMcuOutputPin? _laserPwmPin;

        public override IMcuPowerManager PowerManager => _powerManager;
        public override FrozenDictionary<string, IMcuStepper> Steppers { get; }
        public override FrozenDictionary<string, IMcuOutputPin> OutputPins { get; }
        public override FrozenDictionary<string, IMcuButton> Buttons { get; }
        public override FrozenDictionary<string, IMcuTemperatureSensor> TemperatureSensors { get; }
        public override FrozenDictionary<string, IMcuHeater> Heaters { get; }
        public override FrozenDictionary<string, IMcuSdCard> SdCards { get; }


        public override TimeSpan StepperQueueHigh => _stepperQueueHigh;
        public override TimeSpan StepperQueueLow => _stepperQueueLow;

        protected override bool HasKeepAliveEnablePinsEnabled => true;

        public McuManagerLocal(
            ILoggerFactory loggerFactory,
            IOptionsMonitor<McuManagerOptions> options,
            IAppDataWriter appDataWriter,
            IEnumerable<IMcuDeviceFactory> deviceFactories,
            IPrinterSettings settingsStorage,
            IThreadStackTraceDumper stackTraceDumper,
            IOptionsMonitor<McuStepperGlobalOptions>? optionsStepperGlobal = null,
            ITemperatureCamera? temperatureCamera = null)
            : base(loggerFactory, loggerFactory.CreateLogger<McuManager>(), options, appDataWriter, deviceFactories, settingsStorage, stackTraceDumper)
        {
            _optionsStepperGlobal = optionsStepperGlobal ?? ConstantOptionsMonitor.Create(new McuStepperGlobalOptions());
            _temperatureCamera = temperatureCamera ?? NullTemperatureCamera.Instance;
            _activePinsNeedsLock = new();
            _activeBusNeedsLock = new();

            _powerManager = new McuPowerManager(Options.Create(options.CurrentValue.PowerManager), this, settingsStorage);
            _outputPinsInit = new();
            _buttonsInit = new();
            _steppersInit = new();
            _temperatureSensorsInit = new();
            _heatersInit = new();
            _sdCardsInit = new();
            CreateOutputPins();
            CreateButtons();
            CreateSteppers(); // NOTE: after output pins (laser)
            CreateTemperatureSensors();
            CreateHeaters();
            CreateSdCards();

            Steppers = _steppersInit.ToFrozenDictionary();
            OutputPins = _outputPinsInit.ToFrozenDictionary();
            Buttons = _buttonsInit.ToFrozenDictionary();
            TemperatureSensors = _temperatureSensorsInit.ToFrozenDictionary();
            Heaters = _heatersInit.ToFrozenDictionary();
            SdCards = _sdCardsInit.ToFrozenDictionary();

            (_stepperQueueHigh, _stepperQueueLow) = GetStepperQueueDelays();
        }

        private (TimeSpan High, TimeSpan Low) GetStepperQueueDelays()
        {
            var high = TimeSpan.Zero;
            foreach (var stepper in Steppers.Values)
            {
                if (stepper.QueueAheadDuration > high)
                    high = stepper.QueueAheadDuration;
            }
            var low = high;
            foreach (var stepper in Steppers.Values)
            {
                if (stepper.SendAheadDuration < low)
                    low = stepper.SendAheadDuration;
            }
            return (high, low);
        }

        private void CreateSteppers()
        {
            var options = _options.CurrentValue;
            foreach ((var name, var stepperOptions) in options.Steppers.GetOrderedEnabledKeyValues())
            {
                IStepperDriver? driver;
                if (stepperOptions.Tmc2209Driver?.IsEnabled == true)
                {
                    driver = new Tmc2209Driver(
                        Options.Create(stepperOptions.Tmc2209Driver),
                        this);
                }
                else
                    driver = null;
                McuStepper stepper;
                var optionsGlobal = _optionsStepperGlobal.CurrentValue;
                if (stepperOptions.PwmPin != null)
                {
                    var pwmPin = stepperOptions.PwmPin.LookupPin(this);
                    _laserPwmPin = pwmPin.SetupPin($"{name}-pwm");
                    _laserPwmPin.SetupMaxDuration(TimeSpan.Zero); // TODO: limit laser duration?
                    _laserPwmPin.SetupStartValue(0, 0);
                    _outputPinsInit.Add(name, _laserPwmPin);
                    // placeholder values
                    var o = stepperOptions.Stepper ?? new McuStepperOptions
                    {
                        FullStepDistance = 1,
                        MinVelocity = 0,
                        MaxVelocity = 1,
                    };
                    stepper = new McuStepper(Options.Create(optionsGlobal), Options.Create(o), this, driver, _laserPwmPin, name);
                }
                else if (stepperOptions.Stepper != null)
                    stepper = new McuStepper(Options.Create(optionsGlobal), Options.Create(stepperOptions.Stepper), this, driver, null, name);
                else
                    throw new InvalidOperationException($"Missing stepper or pwm options for {name}");
                _steppersInit.Add(name, stepper);
            }
        }

        private void CreateOutputPins()
        {
            var options = _options.CurrentValue;
            var dimmerSensorPin = options.DimmerSensorPin != null
                ? options.DimmerSensorPin.LookupPin(this)
                : null;
            foreach ((var name, var outputPinOptions) in options.OutputPins.GetOrderedEnabledKeyValues())
            {
                var desc = outputPinOptions.LookupPin(this)
                    with
                {
                    SensorPin = dimmerSensorPin,
                };
                var pin = desc.SetupPin(name);
                pin.SetupMaxDuration(TimeSpan.Zero);
                if (outputPinOptions.StartValue != null || outputPinOptions.ShutdownValue != null || outputPinOptions.IsStatic)
                {
                    if (outputPinOptions.StartValue == null || outputPinOptions.ShutdownValue == null)
                        throw new InvalidOperationException($"{nameof(outputPinOptions.StartValue)} and {nameof(outputPinOptions.ShutdownValue)} must be set both or if {nameof(outputPinOptions.IsStatic)} is enabled for pin {name}");
                    pin.SetupStartValue(
                        outputPinOptions.StartValue.Value,
                        outputPinOptions.ShutdownValue.Value,
                        isStatic: outputPinOptions.IsStatic);
                }
                _outputPinsInit.Add(name, pin);
            }
        }

        private void CreateButtons()
        {
            var options = _options.CurrentValue;
            foreach ((var name, var buttonOptions) in options.Buttons.GetOrderedEnabledKeyValues())
            {
                var descs = buttonOptions.LookupPins(this);
                var button = new McuButton(name, descs);
                _buttonsInit.Add(name, button);
            }
        }

        private void CreateTemperatureSensors()
        {
            var options = _options.CurrentValue;
            foreach ((var name, var sensorOptions) in options.TemperatureSensors.GetOrderedEnabledKeyValues())
            {
                var sensor = CreateSensor(name, options, sensorOptions);
                _temperatureSensorsInit.Add(name, sensor);
            }
        }

        private IMcuTemperatureSensor CreateSensor(string name, McuManagerOptions options, McuManagerOptions.ManagerTemperatureSensor sensorOptions)
        {
            IMcuTemperatureSensor sensor;
            if (sensorOptions.InovaGate1?.IsEnabled == true)
            {
                sensor = new InovaGate1TemperatureSensor(
                    Options.Create(sensorOptions.InovaGate1),
                    this,
                    name);
            }
            else if (sensorOptions.InovaSurfaceBox?.IsEnabled == true)
            {
                sensor = new InovaSurfaceBoxSensor(
                    Options.Create(sensorOptions.InovaSurfaceBox),
                    this,
                    _temperatureCamera,
                    name);
            }
            else
                throw new InvalidOperationException($"Missing sensor options for {name}");
            if (sensorOptions.VerifySensor != null && !options.DisableTemperatureVerification)
                _ = new TemperatureSensorCheck(Options.Create(sensorOptions.VerifySensor), this, sensor);
            return sensor;
        }

        private void CreateHeaters()
        {
            var options = _options.CurrentValue;
            foreach ((var name, var heaterOptions) in options.Heaters.GetOrderedEnabledKeyValues())
            {
                IMcuHeater heater;
                if (heaterOptions.Hysteresis?.IsEnabled == true)
                {
                    if (heaterOptions.Sensor == null)
                        throw new InvalidOperationException($"Missing sensor options for heater {name}");
                    var sensor = CreateSensor(name, options, heaterOptions.Sensor);
                    heater = new HysteresisHeater(
                        Options.Create(heaterOptions.Hysteresis),
                        this,
                        sensor,
                        name);
                }
                else if (heaterOptions.InovaSurface?.IsEnabled == true)
                {
                    heater = new InovaSurfaceHeater(
                        Options.Create(heaterOptions.InovaSurface),
                        this,
                        _temperatureCamera,
                        _settingsStorage,
                        name);
                }
                else
                    throw new InvalidOperationException($"Missing heater options for {name}");
                if (heaterOptions.VerifySensor != null && !options.DisableTemperatureVerification)
                    _ = new TemperatureSensorCheck(Options.Create(heaterOptions.VerifySensor), this, heater);
                if (heaterOptions.VerifyHeater != null && !options.DisableTemperatureVerification)
                    _ = new HeaterCheck(Options.Create(heaterOptions.VerifyHeater), this, heater);
                _heatersInit.Add(name, heater);
            }
        }

        private void CreateSdCards()
        {
            var options = _options.CurrentValue;
            foreach ((var name, var sdCardOptions) in options.SdCardSpi.GetOrderedEnabledKeyValues())
            {
                var sdCard = new McuSdCardSpi(
                    CreateLogger<McuSdCardSpi>(),
                    Options.Create(sdCardOptions),
                    this,
                    name);
                _sdCardsInit.Add(name, sdCard);
            }
        }

        public override McuPinDescription ClaimPin(McuPinType type, string description, bool canInvert = false, bool canPullup = false, string? shareType = null)
        {
            var pin = ParsePin(description, canInvert, canPullup) with
            {
                ShareType = shareType ?? (type == McuPinType.DimmerSensor ? "DimmerSensorPin" : null),
                Type = type
            };
            var key = pin.Key;
            lock (_activePinsNeedsLock)
            {
                if (_activePinsNeedsLock.TryGetValue(key, out var active))
                {
                    if (pin.ShareType == null || active != pin)
                        throw new ArgumentException($"Pin {pin} already shared with different settings {active}");
                }
                else
                    _activePinsNeedsLock[key] = pin;
            }
            return pin;
        }

        public override McuBusDescription ClaimBus(string description, string? shareType = null)
        {
            var bus = ParseBus(description) with
            {
                ShareType = shareType,
            };
            var key = bus.Key;
            lock (_activeBusNeedsLock)
            {
                if (_activeBusNeedsLock.TryGetValue(key, out var active))
                {
                    if (shareType == null || active != bus)
                        throw new ArgumentException($"Bus {bus} already shared with different settings {active}");
                }
                else
                    _activeBusNeedsLock[key] = bus;
            }
            return bus;
        }

        protected override IMcuClockSync CreateClockSync(ILoggerFactory loggerFactory)
            => new McuClockSync(loggerFactory.CreateLogger<McuClockSync>(), this);

        protected override IMcu CreateMcu(ILoggerFactory loggerFactory, IAppDataWriter appDataWriter, McuManager mcuManagerBase, IOptions<McuManagerOptions.ManagerMcuOptions> options, IMcuClockSync clockSync, IEnumerable<IMcuDeviceFactory> deviceFactories)
            => new Mcu(loggerFactory, appDataWriter, this, options, clockSync, _deviceFactories);
    }
}
