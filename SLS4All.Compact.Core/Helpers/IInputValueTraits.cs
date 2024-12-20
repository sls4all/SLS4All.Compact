// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.IO;
using SLS4All.Compact.Numerics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public interface IInputValueTraitsActionHandler
    {
        Task<bool> TrySetValue(object? value);
    }

    public readonly record struct InputValueTraitsAction(
        string Name,
        Func<object?, IInputValueTraitsActionHandler, Task> Callback);

    public interface IInputValueTraits
    {
        Type Type { get; }
        bool IsUpperAlphanumeric => false;
        bool IsNumber => false;
        bool IsFloating => false;
        bool IsSigned => false;
        bool IsBoolean => false;
        bool IsNullable => false;
        bool IsTimeSpan => false;
        Func<Task>? Action => null;
        InputValueTraitsAction[]? Actions => null;
        object?[]? Choices => null;
        string[]? FilenameMasks => null;
        string? Directory => null;
        object? InitialValueOverride => null;
        bool HasStringToValue => true;

        string? ValueToString(object? value);
        string? ValueToEditableString(object? value);
        object? StringToValue(string text);
    }

    public sealed class ActionInputValueTraits : IInputValueTraits
    {
        private readonly Func<object?, string?> _valueToString;

        public ActionInputValueTraits(
            Type type,
            Func<object?, string?> valueToString,
            Func<Task> action)
        {
            Type = type;
            _valueToString = valueToString;
            IsNullable = Nullable.GetUnderlyingType(type) != null;
            Action = action;
        }

        public Type Type { get; }

        public Func<Task> Action { get; }

        public bool IsNullable { get; }

        public object? StringToValue(string text)
            => null;

        public string? ValueToString(object? value)
            => _valueToString(value);

        public string? ValueToEditableString(object? value)
            => _valueToString(value);
    }

    public sealed class DelegatedInputValueTraits : IInputValueTraits
    {
        private readonly Func<object?, string?> _valueToString;
        private readonly Func<object?, string?> _valueToEditableString;
        private readonly Func<string, object?> _stringToValue;

        public DelegatedInputValueTraits(
            Type type,
            Func<object?, string?> valueToString,
            Func<string, object?> stringToValue,
            Func<object?, string?>? valueToEditableString = null,
            object?[]? choices = null,
            bool hasStringToValue = true)
        {
            Type = type;
            IsNullable = Nullable.GetUnderlyingType(type) != null;
            Choices = choices;
            _valueToString = valueToString;
            _valueToEditableString = valueToEditableString ?? valueToString;
            _stringToValue = stringToValue;
            HasStringToValue = hasStringToValue;
        }

        public Type Type { get; }

        public bool IsNullable { get; } 

        public object?[]? Choices { get; }

        public bool HasStringToValue { get; }

        public object? StringToValue(string text)
            => _stringToValue(text);

        public string? ValueToString(object? value)
            => _valueToString(value);

        public string? ValueToEditableString(object? value)
            => _valueToEditableString(value);
    }

    public sealed class DelegatedFilenameInputValueTraits : IInputValueTraits
    {
        private readonly Func<object?, string?> _valueToString;
        private readonly Func<object?, string?> _valueToEditableString;
        private readonly Func<string, object?> _stringToValue;

        public string[]? FilenameMasks { get; }
        public string? Directory { get; }
        public Type Type { get; }
        public bool HasStringToValue { get; }

        public DelegatedFilenameInputValueTraits(
            Type type,
            Func<object?, string?> valueToString,
            Func<string, object?> stringToValue,
            string[]? filenameMasks,
            Func<object?, string?>? valueToEditableString = null,
            string? directory = null,
            bool hasStringToValue = true)
        {
            Type = type;
            FilenameMasks = filenameMasks;
            Directory = directory;
            _valueToString = valueToString;
            _valueToEditableString = valueToEditableString ?? valueToString;
            _stringToValue = stringToValue;
            HasStringToValue = hasStringToValue;
        }

        public object? StringToValue(string text)
            => _stringToValue(text);

        public string? ValueToString(object? value)
            => _valueToString(value);

        public string? ValueToEditableString(object? value)
            => _valueToEditableString(value);
    }

    public static class InputValueTraits
    {
        private static readonly ConcurrentDictionary<Type, IInputValueTraits> _cache = new();

        public const string TrueText = "Enabled";
        public const string FalseText = "Disabled";

        public static IInputValueTraits Create(Type type)
        {
            if (type == typeof(CollapsedStringWithTraits))
                return CollapsedStringWithTraits.Traits;
            else if (_cache.TryGetValue(type, out var traits))
                return traits;
            else
            {
                traits = new DefaultInputValueTraits(type);
                if (!_cache.TryAdd(type, traits))
                    traits = _cache[type];
                return traits;
            }
        }
    }

    public sealed class DefaultInputValueTraits : IInputValueTraits
    {
        public Type Type { get; }
        public bool IsNumber { get; }
        public bool IsFloating { get; }
        public bool IsSigned { get; }
        public bool IsBoolean { get; }
        public bool IsTimeSpan { get; }
        public object?[]? Choices { get; }
        public bool IsNullable { get; }
        public InputValueTraitsAction[]? Actions { get; }
        public string? Format { get; }


        public DefaultInputValueTraits(Type type, InputValueTraitsAction[]? actions = null, string? format = null)
        {
            Type = type;
            Actions = actions;
            Format = format;
            IsNullable = Nullable.GetUnderlyingType(type) != null;
            if (type == typeof(SByte) || type == typeof(SByte?) ||
                type == typeof(Int16) || type == typeof(Int16?) ||
                type == typeof(Int32) || type == typeof(Int32?) ||
                type == typeof(Int64) || type == typeof(Int64?))
            {
                IsNumber = true;
                IsSigned = true;
                IsFloating = false;
            }
            else if (type == typeof(Byte) || type == typeof(Byte?) ||
                type == typeof(UInt16) || type == typeof(UInt16?) ||
                type == typeof(UInt32) || type == typeof(UInt32?) ||
                type == typeof(UInt64) || type == typeof(UInt64?))
            {
                IsNumber = true;
                IsSigned = false;
                IsFloating = false;
            }
            else if (type == typeof(Single) || type == typeof(Single?) ||
                type == typeof(Double) || type == typeof(Double?) ||
                type == typeof(Decimal) || type == typeof(Decimal?))
            {
                IsNumber = true;
                IsSigned = true;
                IsFloating = true;
            }
            else if (type == typeof(Boolean) || type == typeof(Boolean?))
            {
                IsBoolean = true;
            }
            else if (type == typeof(TimeSpan) || type == typeof(TimeSpan?))
            {
                IsTimeSpan = true;
            }
        }

        public string? ValueToEditableString(object? value)
            => ValueToString(value);

        public string? ValueToString(object? value)
        {
            if (value == null || value.Equals(""))
                return null;
            if (IsBoolean)
                return value.Equals(true) ? InputValueTraits.TrueText : InputValueTraits.FalseText;
            else if (IsTimeSpan)
            {
                var ts = (TimeSpan)(object)value;
                var negate = false;
                if (ts < TimeSpan.Zero)
                {
                    ts = -ts;
                    negate = true;
                }
                var hours = (int)ts.TotalHours;
                var minutes = ts.Minutes;
                var seconds = (ts.TotalSeconds - hours * 60 * 60 - minutes * 60).RoundToDecimal(3);
                return $"{(negate ? "-" : "")}{hours:00}:{minutes:00}:{(seconds < 10 ? "0" : "")}{seconds}";
            }
            else if (!string.IsNullOrEmpty(Format) && value is IFormattable formattable)
                return formattable.ToString(Format, CultureInfo.CurrentCulture) ?? "";
            else
                return value.ToString() ?? "";
        }

        public object? StringToValue(string text)
        {
            var type = Type;
            var nonNullable = Nullable.GetUnderlyingType(type) ?? type;
            var isNullable = nonNullable != type || !type.IsValueType;
            if (isNullable && string.IsNullOrWhiteSpace(text))
                return type.IsValueType ? Activator.CreateInstance(type) : (object?)null;
            else
            {
                if (IsBoolean)
                    return text.Equals(InputValueTraits.TrueText, StringComparison.CurrentCultureIgnoreCase);
                else if (IsTimeSpan)
                {
                    // NOTE: we use our own parsing of timestamps that does not include days and prefers seconds for plain number (instead of days)
                    var negate = false;
                    var parts = text.Trim().Split(':');
                    if (parts[0].StartsWith('-'))
                    {
                        parts[0] = parts[0].Substring(1);
                        negate = true;
                    }
                    TimeSpan res;
                    switch (parts.Length)
                    {
                        case 1:
                            res = TimeSpan.FromSeconds(double.Parse(FixDecimalSeparator(parts[0])));
                            break;
                        case 2:
                            res = TimeSpan.FromMinutes(double.Parse(FixDecimalSeparator(parts[0]))) +
                                TimeSpan.FromSeconds(double.Parse(FixDecimalSeparator(parts[1])));
                            break;
                        case 3:
                            res = TimeSpan.FromHours(double.Parse(FixDecimalSeparator(parts[0]))) +
                                TimeSpan.FromMinutes(double.Parse(FixDecimalSeparator(parts[1]))) +
                                TimeSpan.FromSeconds(double.Parse(FixDecimalSeparator(parts[2])));
                            break;
                        default:
                            throw new FormatException("Invalid value, expected SS, MM:SS or HH:MM:SS");
                    }
                    if (negate)
                        res = -res;
                    return res;
                }
                else if (IsNumber)
                    return Convert.ChangeType(FixDecimalSeparator(text), nonNullable);
                else if (Type == typeof(IPAddress))
                    return string.IsNullOrWhiteSpace(text) ? null : IPAddress.Parse(text);
                else
                    return Convert.ChangeType(text, nonNullable);
            }
        }

        private static string FixDecimalSeparator(string text)
        {
            var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            if (separator == ".")
                return text.Replace(',', '.');
            else if (separator == ",")
                return text.Replace('.', ',');
            else
                return text;
        }
    }
}
