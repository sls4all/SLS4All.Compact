// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.McuClient.Pins;

namespace SLS4All.Compact.McuClient
{
    public enum McuPinType
    {
        NotSet = 0,
        Endstop,
        Digital,
        SoftPwm,
        HardPwm,
        Dimmer,
        DimmerSensor,
        Adc,
        Stepper,
        ChipSelect,
        Button,
    }

    public readonly record struct McuPinKey(string McuAlias, string Pin)
    {
        public override string ToString()
            => $"{McuAlias}:{Pin}";
    }

    public sealed record class McuPinDescription(
        IMcu Mcu, 
        string Pin, 
        bool Invert, 
        int Pullup, 
        bool AllowInShutdown = false,
        string? ShareType = null, 
        McuPinType Type = McuPinType.NotSet, 
        double? CycleTime = null, 
        McuPinDescription? SensorPin = null,
        TimeSpan? MaxDuration = null)
    {
        public McuPinKey Key => new McuPinKey(Mcu.Name, Pin);
        public IMcuOutputPin SetupPin(string name)
            => Mcu.SetupPin(name, this);

        public static (string McuAlias, string PinName, bool Invert, int Pullup) Parse(string description, bool canInvert = false, bool canPullup = false)
        {
            var invert = false;
            var pullup = 0;
            description = description.Trim();
            if (description.StartsWith('^'))
            {
                description = description[1..];
                pullup = 1;
            }
            else if (description.StartsWith('~'))
            {
                description = description[1..];
                pullup = -1;
            }
            if (description.StartsWith('!'))
            {
                description = description[1..];
                invert = true;
            }
            if (!canInvert && invert)
                throw new ArgumentException($"Pin was requested with invert '!' specifier but {nameof(canInvert)} was false. Description: {description}");
            if (!canPullup && pullup != 0)
                throw new ArgumentException($"Pin was requested with pullup '^/~' specifier but {nameof(canPullup)} was false. Description: {description}");
            var colon = description.IndexOf(':');
            if (colon == -1)
                return ("mcu", description, invert, pullup);
            else
                return (description[..colon], description[(colon + 1)..], invert, pullup);
        }

    }
}
