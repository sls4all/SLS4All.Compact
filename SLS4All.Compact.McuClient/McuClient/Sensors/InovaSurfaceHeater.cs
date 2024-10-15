// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
using System.Collections.Frozen;
using SLS4All.Compact.Storage.PrinterSettings;
using System.Security.AccessControl;

namespace SLS4All.Compact.McuClient.Sensors
{
    public enum InovaSurfaceHeaterMode
    {
        NotSet = 0,
        Max,
        Min,
        Avg,
        Perc,
        PercMax,
    }

    public class InovaSurfaceHeaterBox
    {
        public required int MinX { get; set; }
        public required int MaxX { get; set; }
        public required int MinY { get; set; }
        public required int MaxY { get; set; }

        public TemperatureBox Box
            => new TemperatureBox(MinX, MinY, MaxX, MaxY);
    }

    public class InovaSurfaceHeaterSection : InovaSurfaceHeaterBox, IOptionsItemEnable
    {
        public bool IsEnabled { get; set; } = true;
        public List<string> HeaterPins { get; set; } = new();
    }

    public class InovaSurfaceHeaterOptions
    {
        public required float OverDelta { get; set; }
        public required float UnderDelta { get; set; }
        public required InovaSurfaceHeaterMode Mode { get; set; }
        public float ModePercValue { get; set; } = 0.5f;
        public float ModePercMaxValue { get; set; } = 5f;
        public required float MinPwm { get; set; }
        public required float MaxPwm { get; set; }
        public required float FactorMinPwm { get; set; }
        public required float FactorMaxPwm { get; set; }
        public required float FactorPwm { get; set; }
        public float TargetReachedTolerance { get; set; } = 0;
        public float PwmCycleFrequency { get; set; } = 50;
        public required McuPinType PinType { get; set; }
        public string? DimmerSensorPin { get; set; }
        public required float LightsPwm { get; set; }
        public List<string> LightsPins { get; set; } = new();
        public TimeSpan MinHeatPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public float MinPwmChange { get; set; } = 0.01f;
        public TimeSpan MaxHeatTime { get; set; } = TimeSpan.FromSeconds(10);
        public required InovaSurfaceHeaterSection Quadrant1 { get; set; }
        public required InovaSurfaceHeaterSection Quadrant2 { get; set; }
        public required InovaSurfaceHeaterSection Quadrant3 { get; set; }
        public required InovaSurfaceHeaterSection Quadrant4 { get; set; }
        public double PowerConsumptionPerLight { get; set; } = 0;
        public int PowerManagerPriority { get; set; } = 0;
        public int AverageCount { get; set; } = 1;
        public int KeepWarmCount { get; set; } = 1; // TODO: remove
        public int SlowingDownCount { get; set; } = 2;
        public InovaSurfaceHeaterMode TriangleMode { get; set; } = InovaSurfaceHeaterMode.NotSet;
        public float LeftRightFactor { get; set; } = 1.0f;
        public float TopBottomFactor { get; set; } = 1.0f;
        public bool SmallerTriangles { get; set; } = false;
    }

    public sealed class InovaSurfaceHeater : IMcuHeater, IMcuLightsControl
    {
        private enum Action
        {
            NotSet = 0,
            TargetExceeded,
            WarmingUp,
            SlowingDown,
            KeepWarm,
            Lights,
            Off,
        }

        private sealed class Pin
        {
            public required int Index { get; init; }
            public required McuPinDescription Desc { get; init; }
            public IMcuOutputPin Output { get; set; } = default!;
            public float LastPwmValue { get; set; }
            public SystemTimestamp LastPwmTime { get; set; }
            public bool IsLight { get; set; }
        }

        private sealed class Section
        {
            public required InovaSurfaceHeaterSection Source { get; init; }
            public required int LogicalX { get; init; }
            public required int LogicalY { get; init; }
            public required int Index { get; init; }
            public List<Section> Neighbours { get; } = new();
            public List<Pin> Pins { get; } = new();
            public float Temperature { get; set; }
            public bool TargetReached { get; set; }
        }

        private readonly record struct SectionPair(Section A, Section B, float Temperature, Pin Pin, float Pwm) : IComparable<SectionPair>
        {
            public int CompareTo(SectionPair other)
                => Comparer<float>.Default.Compare(this.Temperature, other.Temperature);
        }

        private readonly IOptions<InovaSurfaceHeaterOptions> _options;
        private readonly object _locker = new object();
        private readonly McuManager _manager;
        private readonly ITemperatureCamera _camera;
        private readonly IPrinterSettingsStorage _settingsStorage;
        private readonly List<float> _calcTemperatureTemp;
        private readonly HashSet<Pin> _pinsTemp;
        private readonly List<SectionPair> _sectionPairsTemp;
        private readonly Section[] _sections;
        private readonly FrozenDictionary<McuPinKey, Pin> _pins;
        private readonly TaskQueue _readEventQueue;
        private readonly Dictionary<int, float> _lightPowers;
        private readonly (float value, SystemTimestamp timestamp, float prevNonZeroValue, SystemTimestamp prevZeroingTimestamp)[] _lightStates;
        private readonly string _name;
        private readonly ReferenceCounter _validationSupressor;
        private readonly Queue<float[]> _matrices;
        private volatile StrongBox<float>? _target;
        private volatile McuTemperatureSensorData? _current;
        private volatile PrinterPowerSettingsSnapshot _powerSettings;
        private float[]? _matrix;
        private bool _isReady;

        public McuTemperatureSensorData? CurrentValue => _current;
        public AsyncEvent<McuTemperatureSensorData> ReadEvent { get; } = new();
        public bool IsValidationSupressed => _validationSupressor.IsIncremented;

        public float? Target
        {
            get => _target?.Value;
            set
            {
                if (value == Target)
                    return;
                lock (_locker)
                {
                    _target = value != null ? new StrongBox<float>(value.Value) : null;
                    foreach (var section in _sections)
                        section.TargetReached = false;
                    Update();
                }
            }
        }

        public (float? Target, bool Reached) TargetReached
        {
            get
            {
                lock (_locker)
                {
                    var target = _target?.Value;
                    if (target == null)
                        return (null, false);
                    var reached = true;
                    foreach (var section in _sections)
                    {
                        if (!section.TargetReached)
                        {
                            reached = false;
                            break;
                        }
                    }
                    return (target, reached);
                }
            }
        }

        public bool HasLightsEnabled
        {
            get
            {
                lock (_locker)
                {
                    for (int i = 0; i < _lightStates.Length; i++)
                        if (_lightStates[i].value != 0)
                            return true;
                }
                return false;
            }
        }

        public int LightCount => _pins.Count;

        public float MaxLightFactor => (float)_powerSettings.HalogenMaxPercent / 100;

        public InovaSurfaceHeater(
            IOptions<InovaSurfaceHeaterOptions> options,
            McuManager manager,
            ITemperatureCamera camera,
            IPrinterSettingsStorage settingsStorage,
            string name)
        {
            _options = options;
            _manager = manager;
            _camera = camera;
            _settingsStorage = settingsStorage;
            _name = name;

            _powerSettings = settingsStorage.Power;
            _validationSupressor = new();
            _readEventQueue = new();
            _calcTemperatureTemp = new();
            _pinsTemp = new();
            _lightPowers = new();
            _matrices = new();
            _sectionPairsTemp = new();

            var o = options.Value;
            var sensorPin = o.PinType == McuPinType.Dimmer
                ? manager.ClaimPin(
                    McuPinType.DimmerSensor,
                    o.DimmerSensorPin ?? throw new InvalidOperationException($"{nameof(o.DimmerSensorPin)} must be set when dimmer pin type is selected"))
                : null;
            var sourceSections = new (InovaSurfaceHeaterSection Source, int X, int Y, int Index)[]
            {
                (o.Quadrant2, 1, 0, 2),
                (o.Quadrant3, 1, 1, 3),
                (o.Quadrant4, 0, 1, 4),
                (o.Quadrant1, 0, 0, 1),
            };
            var sections = new List<Section>();
            var pins = new Dictionary<McuPinKey, Pin>();
            foreach (var section in sourceSections)
            {
                var sectionItem = new Section { Source = section.Source, LogicalX = section.X, LogicalY = section.Y, Index = section.Index };
                foreach (var pin in section.Source.HeaterPins)
                {
                    var parsed = manager.ParsePin(pin);
                    if (!pins.TryGetValue(parsed.Key, out var pinItem))
                    {
                        var desc = manager.ClaimPin(o.PinType, pin, canInvert: true) with
                        {
                            SensorPin = sensorPin,
                            CycleTime = 1.0 / o.PwmCycleFrequency,
                        };
                        pinItem = new Pin { Index = pins.Count, Desc = desc };
                        pins.Add(parsed.Key, pinItem);
                    }
                    sectionItem.Pins.Add(pinItem);
                }
                sections.Add(sectionItem);
            }

            foreach (var section in sections)
            {
                foreach (var other in sections)
                {
                    if (other == section)
                        continue;
                    if (section.Pins.Any(pin => other.Pins.Any(otherPin => pin == otherPin)))
                        section.Neighbours.Add(other);
                }
            }

            foreach (var pin in o.LightsPins)
            {
                var desc = manager.ParsePin(pin);
                pins[desc.Key].IsLight = true;
            }

            foreach (var pin in pins.Values)
            {
                pin.Output = pin.Desc.SetupPin($"heater-{_name}-{pin.Index}");
                pin.Output.SetupMaxDuration(o.MaxHeatTime);
                _manager.PowerManager.SetupPin(pin.Output, o.PowerConsumptionPerLight, o.PowerManagerPriority, PowerPinType.Halogen);
            }

            // finalize
            _pins = pins.ToFrozenDictionary();
            _lightStates = new (float value, SystemTimestamp timestamp, float prevNonZeroValue, SystemTimestamp prevZeroingTimestamp)[pins.Count];

            // hook
            _sections = sections.ToArray();
            _manager.RegisterSetup(null, OnSetup);
            _camera.CurrentPixelsChanged.AddHandler(OnPixelsChanged);
            manager.RunningCancel.Register(() => _camera.CurrentPixelsChanged.RemoveHandler(OnPixelsChanged));
        }

        private void Update()
        {
            var options = _options.Value;
            lock (_locker)
            {
                if (!_isReady || _matrix == null)
                    return;
                var target = _target?.Value;
                var time = SystemTimestamp.Now;
                var maxTemperature = float.MinValue;
                var minTemperature = float.MaxValue;
                var sumTemperature = 0.0f;
                foreach (var section in _sections)
                {
                    var temperature = CalcTemperature(section, null, null);
                    section.Temperature = temperature;
                    if (temperature < minTemperature)
                        minTemperature = temperature;
                    if (temperature > maxTemperature)
                        maxTemperature = temperature;
                    sumTemperature += temperature;

                    if (temperature >= target + options.TargetReachedTolerance)
                        section.TargetReached = true;
                    else if (temperature < target - options.TargetReachedTolerance)
                        section.TargetReached = false;
                }
                var avgTemperature = sumTemperature / _sections.Length;
                var current = new McuTemperatureSensorData(avgTemperature, time);
                _powerSettings = _settingsStorage.Power;
                _current = current;

                Action action;
                float warmingPwm = 0.0f;
                var pins = _pinsTemp;
                pins.Clear();
                if (_lightPowers.Count > 0)
                    action = Action.Lights;
                else if (target == null)
                    action = Action.Off;
                else if (maxTemperature >= target.Value + options.OverDelta)
                {
                    action = Action.TargetExceeded;
                }
                else if (maxTemperature <= target.Value - options.UnderDelta)
                {
                    action = Action.WarmingUp;
                }
                else if (maxTemperature > target.Value)
                {
                    action = Action.SlowingDown;
                    //if (options.TriangleMode == InovaSurfaceHeaterMode.NotSet)
                    {
                        Section maxSection = default!;
                        foreach (var section in _sections)
                        {
                            if (section.Temperature == maxTemperature)
                            {
                                maxSection = section;
                                break;
                            }
                        }
                        var maxNeighbourTemperature = float.MinValue;
                        Section maxNeighbour = default!;
                        foreach (var neighbour in maxSection.Neighbours)
                        {
                            if (neighbour.Temperature > maxNeighbourTemperature)
                            {
                                maxNeighbourTemperature = neighbour.Temperature;
                                maxNeighbour = neighbour;
                            }
                        }
                        if (options.SlowingDownCount == 0)
                        {
                            // add 4 to pins (to disable)
                            foreach (var pin in _pins.Values)
                                pins.Add(pin);
                        }
                        else if (options.SlowingDownCount == 1)
                        {
                            // add 3 to pins (to disable)
                            foreach (var pin in maxSection.Pins)
                                pins.Add(pin);
                            foreach (var pin in maxNeighbour.Pins)
                                pins.Add(pin);
                        }
                        else if (options.SlowingDownCount == 2)
                        {
                            // add 2 to pins (to disable)
                            foreach (var pin in maxSection.Pins)
                                pins.Add(pin);
                        }
                        else if (options.SlowingDownCount == 3)
                        {
                            // add 1 to pins (to disable)
                            foreach (var pin in maxSection.Pins)
                            {
                                foreach (var other in maxNeighbour.Pins)
                                {
                                    if (pin == other)
                                        pins.Add(pin);
                                }
                            }
                        }
                        else
                        {
                            // add 0 to pins (to disable)
                        }
                    }
                    //else
                    //{
                    //    // slightly over, turn off the max triangles
                    //    var sectionPairs = _sectionPairsTemp;
                    //    action = Action.KeepWarm;
                    //    sectionPairs.Clear();
                    //    foreach (var section in _sections)
                    //    {
                    //        foreach (var neighbour in section.Neighbours)
                    //        {
                    //            if (neighbour.Index < section.Index)
                    //                continue;
                    //            Pin commonPin = null!;
                    //            foreach (var pin in section.Pins)
                    //            {
                    //                foreach (var other in neighbour.Pins)
                    //                {
                    //                    if (pin == other)
                    //                    {
                    //                        commonPin = pin;
                    //                        break;
                    //                    }
                    //                }
                    //            }

                    //            var pairTemp = CalcTemperature(section, neighbour, options.TriangleMode);
                    //            sectionPairs.Add(new SectionPair(section, neighbour, pairTemp, commonPin, 0));
                    //        }
                    //    }
                    //    sectionPairs.Sort();
                    //    if (sectionPairs.Count != 4)
                    //        throw new InvalidOperationException("Something horrible has happened, number of section pairs is not 4");

                    //    for (int i = sectionPairs.Count; i > sectionPairs.Count - options.SlowingDownCount; i--)
                    //        pins.Add(sectionPairs[i].Pin);
                    //}
                }
                else
                {
                    //var sectionPairs = _sectionPairsTemp;
                    //action = Action.KeepWarm;
                    //sectionPairs.Clear();
                    //foreach (var section in _sections)
                    //{
                    //    foreach (var neighbour in section.Neighbours)
                    //    {
                    //        if (neighbour.Index < section.Index)
                    //            continue;
                    //        Pin commonPin = null!;
                    //        foreach (var pin in section.Pins)
                    //        {
                    //            foreach (var other in neighbour.Pins)
                    //            {
                    //                if (pin == other)
                    //                {
                    //                    commonPin = pin;
                    //                    break;
                    //                }
                    //            }
                    //        }

                    //        var pairTemp = CalcTemperature(section, neighbour, InovaSurfaceHeaterMode.Avg);
                    //        var pairPwm = Math.Min(
                    //            options.FactorMinPwm + Math.Clamp((target.Value - pairTemp) / options.HardDelta * options.FactorPwm, 0, 1) * (options.FactorMaxPwm - options.FactorMinPwm),
                    //            options.FactorMaxPwm);
                    //        sectionPairs.Add(new SectionPair(section, neighbour, pairTemp, commonPin, pairPwm));
                    //    }
                    //}
                    //sectionPairs.Sort();
                    //if (sectionPairs.Count != 4)
                    //    throw new InvalidOperationException("Something horrible has happened, number of section pairs is not 4");

                    // keeping warm using single pin
                    action = Action.KeepWarm;
                    if (options.TriangleMode != InovaSurfaceHeaterMode.NotSet)
                    {
                        Section minSection = default!;
                        foreach (var section in _sections)
                        {
                            if (section.Temperature == minTemperature)
                            {
                                minSection = section;
                                break;
                            }
                        }
                        var minNeighbourTemperature = float.MaxValue;
                        Section minNeighbour = default!;
                        foreach (var neighbour in minSection.Neighbours)
                        {
                            var pairTemp = CalcTemperature(minSection, neighbour, options.TriangleMode);
                            if (pairTemp < minNeighbourTemperature)
                            {
                                minNeighbourTemperature = pairTemp;
                                minNeighbour = neighbour;
                            }
                        }
                        // find pin common to minSection and minNeighbour
                        Pin commonPin = null!;
                        foreach (var pin in minSection.Pins)
                        {
                            foreach (var other in minNeighbour.Pins)
                            {
                                if (pin == other)
                                {
                                    commonPin = pin;
                                    break;
                                }
                            }
                        }

                        pins.Add(commonPin);
                        var axisFactor = commonPin.Index is 0 or 2 ? options.TopBottomFactor : options.LeftRightFactor;
                        warmingPwm = Math.Min(
                            options.FactorMinPwm + Math.Clamp((target.Value - minNeighbourTemperature) / options.UnderDelta * options.FactorPwm, 0, 1) * (options.FactorMaxPwm - options.FactorMinPwm) * axisFactor,
                            options.FactorMaxPwm);
                    }
                    else
                    {
                        Section minSection = default!;
                        foreach (var section in _sections)
                        {
                            if (section.Temperature == minTemperature)
                            {
                                minSection = section;
                                break;
                            }
                        }
                        var minNeighbourTemperature = float.MaxValue;
                        Section minNeighbour = default!;
                        foreach (var neighbour in minSection.Neighbours)
                        {
                            if (neighbour.Temperature < minNeighbourTemperature)
                            {
                                minNeighbourTemperature = neighbour.Temperature;
                                minNeighbour = neighbour;
                            }
                        }
                        // find pin common to minSection and minNeighbour
                        Pin commonPin = null!;
                        foreach (var pin in minSection.Pins)
                        {
                            foreach (var other in minNeighbour.Pins)
                            {
                                if (pin == other)
                                {
                                    commonPin = pin;
                                    break;
                                }
                            }
                        }

                        pins.Add(commonPin);
                        var axisFactor = commonPin.Index is 0 or 2 ? options.TopBottomFactor : options.LeftRightFactor;
                        warmingPwm = Math.Min(
                            options.FactorMinPwm + Math.Clamp((target.Value - minTemperature) / options.UnderDelta * options.FactorPwm, 0, 1) * (options.FactorMaxPwm - options.FactorMinPwm) * axisFactor,
                            options.FactorMaxPwm);
                    }
                }

                switch (action)
                {
                    case Action.Lights:
                        {
                            foreach (var pin in _pins.Values)
                            {
                                var enabled = _lightPowers.TryGetValue(pin.Index, out var power);
                                var actualPower = enabled ? power : 0.0f;
                                SetPwm(pin, actualPower);
                                UpdatePowerStatesInner(pin.Index, actualPower, time);
                            }
                            break;
                        }
                    case Action.Off:
                    case Action.TargetExceeded:
                        {
                            foreach (var pin in _pins.Values)
                            {
                                SetPwm(pin, 0);
                                UpdatePowerStatesInner(pin.Index, 0, time);
                            }
                            break;
                        }
                    case Action.WarmingUp:
                        {
                            foreach (var pin in _pins.Values)
                            {
                                SetPwm(pin, 1);
                                UpdatePowerStatesInner(pin.Index, 1, time);
                            }
                            break;
                        }
                    case Action.SlowingDown:
                        {
                            foreach (var pin in _pins.Values)
                            {
                                var enabled = !pins.Contains(pin);
                                var actualPower = enabled ? options.MinPwm : 0;
                                SetPwm(pin, actualPower);
                                UpdatePowerStatesInner(pin.Index, actualPower, time);
                            }
                            break;
                        }
                    case Action.KeepWarm:
                        {
                            //for (int i = 0; i < _sectionPairsTemp.Count; i++)
                            //{
                            //    var item = _sectionPairsTemp[i];
                            //    var enabled = i < options.KeepWarmCount;
                            //    SetPwm(item.Pin, enabled ? item.Pwm : options.MinPwm);
                            //}
                            //break;

                            foreach (var pin in _pins.Values)
                            {
                                var enabled = pins.Contains(pin);
                                var actualPower = enabled ? warmingPwm : options.MinPwm;
                                SetPwm(pin, actualPower);
                                UpdatePowerStatesInner(pin.Index, actualPower, time);
                            }
                            break;
                        }
                    default:
                        throw new InvalidOperationException($"Invalid action: {action}");
                }

                _readEventQueue.EnqueueValue(() => ReadEvent.Invoke(current, _manager.RunningCancel), null);
            }
        }

        private ValueTask OnPixelsChanged(CancellationToken cancel)
        {
            var options = _options.Value;
            var source = _camera.CurrentPixels.AsSpan();
            if (_matrices.Count < options.AverageCount)
                _matrices.Enqueue(source.ToArray());
            else
            {
                var oldValues = _matrices.Dequeue();
                source.CopyTo(oldValues);
                _matrices.Enqueue(oldValues);
            }
            if (_matrix == null)
                _matrix = new float[source.Length];
            var span = _matrix.AsSpan();
            span.Clear();
            foreach (var sample in _matrices)
            {
                for (int i = 0; i < span.Length; i++)
                    span[i] += sample[i];
            }
            for (int i = 0; i < span.Length; i++)
                span[i] /= _matrices.Count;

            Update();
            return ValueTask.CompletedTask;
        }

        private void SetPwm(Pin pin, float value)
        {
            var options = _options.Value;
            var time = SystemTimestamp.Now;
            var elapsed = time - pin.LastPwmTime;
            if (elapsed < TimeSpan.FromSeconds(1.0f / options.PwmCycleFrequency))
                return; // too fast for pwm
            if (elapsed < options.MinHeatPeriod &&
                (pin.LastPwmValue == 0) == (value == 0) &&
                Math.Abs(pin.LastPwmValue - value) < options.MinPwmChange)
                return; // no significant change in value
            pin.LastPwmTime = time;
            pin.LastPwmValue = value;
            _manager.PowerManager.Set(pin.Output, value);
        }

        private float CalcTemperature(Section section1, Section? section2, InovaSurfaceHeaterMode? mode)
        {
            var options = _options.Value;
            var width = _camera.Width;
            var height = _camera.Height;
            var values = _calcTemperatureTemp;
            var matrix = _matrix!;
            var mainBox = _camera.MainBox;

            values.Clear();
            if (section2 == null)
            {
                var box1 = mainBox.OffsetInTopLeft(section1.Source.Box);
                var minX = box1.MinX; // leftmost
                var maxX = box1.MaxX; // rightmost
                var minY = box1.MinY; // inner top
                var maxY = box1.MaxY; // inner bottom
                for (var y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (x >= 0 && x < width && y >= 0 && y < height)
                            values.Add(matrix[x + y * width]);
                    }
                }
            }
            else
            {
                var box1 = mainBox.OffsetInTopLeft(section1.Source.Box);
                var box2 = mainBox.OffsetInTopLeft(section2.Source.Box);

                // get values from triangular shape getting smaller to the center of the box, constructed from first/second row/column
                if (section1.LogicalY == section2.LogicalY) // row
                {
                    var minX = Math.Min(box1.MinX, box2.MinX); // leftmost
                    var maxX = Math.Max(box1.MaxX, box2.MaxX); // rightmost
                    var minY = Math.Max(box1.MinY, box2.MinY); // inner top
                    var maxY = Math.Min(box1.MaxY, box2.MaxY); // inner bottom

                    if (options.SmallerTriangles)
                    {
                        // remove one row from the outside to ensure that the average value is no affected by chamber pixel alignment
                        if (section1.LogicalY == 0)
                            minY++;
                        else
                            maxY--;
                    }

                    for (var y = minY; y <= maxY; y++)
                    {
                        var border = section1.LogicalY == 0
                            ? (y - minY) * (maxX - minX + 1) / (2 * (maxY - minY)) // on first row: start with smallest border 
                            : (maxY - y) * (maxX - minX + 1) / (2 * (maxY - minY)); // on second row: start with largest border
                        for (int x = minX + border, xe = maxX - border; x <= xe; x++)
                        {
                            if (x >= 0 && x < width && y >= 0 && y < height)
                                values.Add(matrix[x + y * width]);
                        }
                    }
                }
                else // column
                {
                    var minX = Math.Max(box1.MinX, box2.MinX); // inner left 
                    var maxX = Math.Min(box1.MaxX, box2.MaxX); // inner right
                    var minY = Math.Min(box1.MinY, box2.MinY); // topmost
                    var maxY = Math.Max(box1.MaxY, box2.MaxY); // bottommost

                    if (options.SmallerTriangles)
                    {
                        // remove one column from the outside to ensure that the average value is no affected by chamber pixel alignment
                        if (section1.LogicalX == 0)
                            minX++;
                        else
                            maxX--;
                    }

                    for (var x = minX; x <= maxX; x++)
                    {
                        var border = section1.LogicalX == 0
                            ? (x - minX) * (maxY - minY + 1) / (2 * (maxX - minX)) // on first column: start with smallest border 
                            : (maxX - x) * (maxY - minY + 1) / (2 * (maxX - minX)); // on second column: start with largest border
                        for (int y = minY + border, ye = maxY - border; y <= ye; y++)
                        {
                            if (x >= 0 && x < width && y >= 0 && y < height)
                                values.Add(matrix[x + y * width]);
                        }
                    }
                }
            }
            if (values.Count == 0)
                values.Add(0);
            var actualMode = mode ?? options.Mode;
            switch (actualMode)
            {
                case InovaSurfaceHeaterMode.Max:
                    {
                        var max = float.MinValue;
                        foreach (var value in values)
                            if (value > max)
                                max = value;
                        return max;
                    }
                case InovaSurfaceHeaterMode.Min:
                    {
                        var min = float.MaxValue;
                        foreach (var value in values)
                            if (value < min)
                                min = value;
                        return min;
                    }
                case InovaSurfaceHeaterMode.Avg:
                    {
                        var sum = 0.0f;
                        foreach (var value in values)
                            sum += value;
                        return sum / values.Count;
                    }
                case InovaSurfaceHeaterMode.Perc:
                case InovaSurfaceHeaterMode.PercMax:
                    {
                        values.Sort();
                        var index = options.ModePercValue * (values.Count - 1);
                        var index0 = (int)Math.Floor(index);
                        var index1 = (int)Math.Ceiling(index);
                        var temp0 = values[index0];
                        var temp1 = values[index1];
                        var temp = temp0 + (temp1 - temp0) * (index - index0);
                        if (actualMode == InovaSurfaceHeaterMode.PercMax)
                        {
                            var max = values[^1];
                            if (max - temp < options.ModePercMaxValue)
                                temp = max;
                        }
                        return temp;
                    }
                default:
                    throw new InvalidOperationException($"Invalid mode {actualMode}");
            }
        }

        private ValueTask OnSetup(CancellationToken token)
        {
            lock (_locker)
            {
                _isReady = true;
                Update();
            }
            return ValueTask.CompletedTask;
        }

        public void SetLights(bool enabled, int? mask = null, float? power = null, bool forceMax = false)
        {
            lock (_locker)
            {
                var options = _options.Value;
                var maxFactor = MaxLightFactor;
                _lightPowers.Clear();
                if (enabled && !(power <= 0))
                {
                    foreach (var pin in _pins.Values)
                    {
                        if ((mask != null && (mask.Value & (1 << pin.Index)) != 0) || (mask == null && pin.IsLight))
                        {
                            var setPower = power ?? options.LightsPwm;
                            if (!forceMax && setPower > maxFactor)
                                setPower = maxFactor;
                            _lightPowers.Add(pin.Index, setPower);
                        }
                    }
                }
                Update();
            }
        }

        public void SetLights(Span<(bool Enabled, int Index, float? Power)> items, bool forceMax = false)
        {
            lock (_locker)
            {
                var options = _options.Value;
                var maxFactor = MaxLightFactor;
                _lightPowers.Clear();
                for (int i = 0; i < items.Length; i++)
                {
                    ref var item = ref items[i];
                    if (item.Enabled && !(item.Power <= 0))
                    {
                        var setPower = item.Power ?? options.LightsPwm;
                        if (!forceMax && setPower > maxFactor)
                            setPower = maxFactor;
                        _lightPowers.Add(item.Index, setPower);
                    }
                }
                Update();
            }
        }

        private bool UpdatePowerStatesInner(int index, float power, SystemTimestamp timestamp)
        {
            ref var value = ref _lightStates[index];
            var powersChanged = value.value != power;
            if (power != 0)
                value.prevNonZeroValue = power;
            else if (value.value != 0)
                value.prevZeroingTimestamp = timestamp;
            value.value = power;
            value.timestamp = timestamp;
            return powersChanged;
        }

        public void GetEnabledLights(ICollection<KeyValuePair<int, float>> res)
        {
            lock (_locker)
            {
                foreach (var pair in _lightPowers)
                    res.Add(pair);
            }
        }

        public IDisposable SupressValidation()
            => _validationSupressor.Increment();

        public bool TryGetRecentLightPower(int index, out bool isCurrent, out float power, SystemTimestamp now, TimeSpan? duration)
        {
            (float value, SystemTimestamp timestamp, float prevNonZeroValue, SystemTimestamp prevZeroingTimestamp) entry = default;
            lock (_locker)
            {
                entry = _lightStates[index];
            }
            if (now.IsEmpty)
                now = SystemTimestamp.Now;
            var threshold = now - (duration ?? TimeSpan.FromSeconds(2));
            if (entry.value != 0)
            {
                isCurrent = true;
                power = entry.value;
            }
            else if (!entry.prevZeroingTimestamp.IsEmpty && entry.prevZeroingTimestamp > threshold)
            {
                isCurrent = false;
                power = entry.prevNonZeroValue;
            }
            else
            {
                isCurrent = true;
                power = 0;
            }
            return true;
        }

        public bool HasRecentLightPower(int index, SystemTimestamp now = default, TimeSpan? duration = null)
            => TryGetRecentLightPower(index, out _, out var power, now, duration) && power != 0;

        public override string ToString()
            => $"{_name} [InovaSurfaceHeater]";
    }
}
