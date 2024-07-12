// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public enum McuCommandArgumentType
    {
        NotSet = 0,
        Number,
        String,
    }

    public readonly record struct McuCommandArgument(McuCommandArgumentType Type, string Name, int Start, int Length);
    public readonly record struct McuCommandArgumentValue(long Int64, ArraySegment<byte> ArraySegment, ArenaBuffer<byte> ArenaBuffer)
    {
        public int Int32 => (int)Int64;
        public uint UInt32 => (uint)Int64;
        public bool Boolean => Int64 != 0;
        public ArraySegment<byte> Buffer
        {
            get
            {
                if (ArraySegment.Array != null)
                    return ArraySegment;
                else if (ArenaBuffer.Arena != null)
                    return ArenaBuffer.Segment;
                else
                    return default;
            }
        }

        public static implicit operator McuCommandArgumentValue(long int64)
            => new McuCommandArgumentValue(int64, default, default);

        public static implicit operator McuCommandArgumentValue(ArraySegment<byte> value)
            => new McuCommandArgumentValue(0, value, default);

        public static implicit operator McuCommandArgumentValue(ArenaBuffer<byte> value)
            => new McuCommandArgumentValue(0, default, value);

        public override string? ToString()
        {
            if (this.Buffer.Array != null)
                return Convert.ToHexString(this.Buffer.AsSpan());
            else
                return this.Int64.ToString(CultureInfo.InvariantCulture);
        }

        public McuCommandArgumentValue CloneIncludingBuffer()
        {
            if (Buffer != default)
                return new McuCommandArgumentValue(Int64, Buffer.ToArray(), default);
            else
                return new McuCommandArgumentValue(Int64, default, default);
        }
    }

    public sealed class McuCommand
    {
        private readonly static Regex s_argumentRegex = new Regex("(?<name>[a-zA-Z0-9_]+)=(?<format>%[-+ #]*(\\d+|\\*)?(.\\d+|.\\*)?(hh|h|l|ll|j|z|t|L|I|I32|I64|w)?[%csdioxXufFeEaAgGp]|%n)", RegexOptions.Compiled);

        private readonly int _commandId;
        private readonly string _commandName;
        private readonly string _messageFormat;
        private readonly McuCommandArgumentValue[] _argumentValues;
        private readonly McuCommandArgument[] _arguments;
        private double _sentTimestamp = default;
        private double _receiveTimestamp = default;
        private bool _isTimingCritical;

        public string MessageFormat => _messageFormat;
        public int CommandId => _commandId;
        public string CommandName => _commandName;
        public int ArgumentCount => _arguments.Length;
        public McuCommandArgument[] Arguments => _arguments;
        public double SentTimestamp
        {
            get => _sentTimestamp;
            set => _sentTimestamp = value;
        }
        public double ReceiveTimestamp
        {
            get => _receiveTimestamp;
            set => _receiveTimestamp = value;
        }

        public McuCommandArgumentValue this[int index]
        {
            get => _argumentValues[index];
            set => _argumentValues[index] = value;
        }

        public McuCommandArgumentValue this[string name]
        {
            get => _argumentValues[GetArgumentIndex(name)];
            set => _argumentValues[GetArgumentIndex(name)] = value;
        }

        public bool IsTimingCritical
        {
            get => _isTimingCritical;
            set => _isTimingCritical = value;
        }

        /// <remarks>
        /// Since we commonly C# `lock` on command instance, we set a placeholder instance before real command is assigned.
        /// </remarks>
        public static McuCommand PlaceholderCommand { get; } = new McuCommand(-1, "placeolder", "placeolder", []);

        private McuCommand(int commandId, string commandName, string messageFormat, McuCommandArgument[] arguments)
        {
            _commandId = commandId;
            _commandName = commandName;
            _messageFormat = messageFormat;
            _argumentValues = arguments.Length == 0 ? Array.Empty<McuCommandArgumentValue>() : new McuCommandArgumentValue[arguments.Length];
            _arguments = arguments;
        }

        public McuCommandArgumentValue? TryGetArgument(string name)
        {
            if (TryGetArgumentIndex(name, out var index))
                return _argumentValues[index];
            else
                return null;
        }

        public string? TryGetArgumentString(string name, McuConfig config)
        {
            if (TryGetArgumentIndex(name, out var index))
            {
                var arg = _argumentValues[index].Int32;
                if (config.IdToStaticString.TryGetValue(arg, out var res))
                    return res;
                else
                    return null;
            }
            else
                return null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(_messageFormat);
            if (_argumentValues.Length > 0)
            {
                for (int i = _arguments.Length - 1; i >= 0; i--)
                {
                    var value = _argumentValues[i].ToString();
                    if (value != null)
                    {
                        var arg = _arguments[i];
                        sb.Remove(arg.Start, arg.Length);
                        sb.Insert(arg.Start, value);
                    }
                }
            }
            return sb.ToString();
        }

        public bool TryGetArgumentIndex(string name, out int index)
        {
            for (int i = 0; i < _arguments.Length; i++)
            {
                var argument = _arguments[i];
                if (argument.Name == name)
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        public int GetArgumentIndex(string name)
        {
            if (!TryGetArgumentIndex(name, out var index))
                throw new ArgumentException($"Arhument {name} was not found", nameof(name));
            return index;
        }

        public static McuCommand Parse(int commandId, string messageFormat)
        {
            var percentCount = messageFormat.Count(x => x == '%');
            var formatMatches = s_argumentRegex.Matches(messageFormat);
            if (formatMatches.Count != percentCount)
                throw new FormatException($"Invalid format specifiers for command: {messageFormat}");
            var list = new List<McuCommandArgument>();
            foreach (Match match in formatMatches)
            {
                var name = match.Groups["name"].Value;
                var format = match.Groups["format"];
                var type = format.Value.Contains('s') ? McuCommandArgumentType.String : McuCommandArgumentType.Number;
                list.Add(new McuCommandArgument(type, name, format.Index, format.Length));
            }

            var parts = messageFormat.Split(' ');
            var commandName = parts[0];

            return new McuCommand(commandId, commandName, messageFormat, list.ToArray());
        }

        public McuCommand Clone()
        {
            var clone = new McuCommand(_commandId, _commandName, _messageFormat, _arguments);
            for (int i = 0; i < _argumentValues.Length; i++)
                clone._argumentValues[i] = _argumentValues[i].CloneIncludingBuffer();
            clone._isTimingCritical = _isTimingCritical;
            return clone;
        }

        public McuCommand Bind(params long[] values)
        {
            if (values.Length != _arguments.Length)
                throw new ArgumentException($"Number of arguments do not match");
            for (int i = 0; i < values.Length; i++)
                _argumentValues[i] = values[i];
            return this;
        }

        public McuCommand Bind(string name, long value)
        {
            var index = GetArgumentIndex(name);
            _argumentValues[index] = new McuCommandArgumentValue(value, default, default);
            return this;
        }

        public McuCommand Bind(string name, ArraySegment<byte> value)
        {
            var index = GetArgumentIndex(name);
            _argumentValues[index] = new McuCommandArgumentValue(0, value, default);
            return this;
        }
        
        public McuCommand Bind(string name, ArenaBuffer<byte> value)
        {
            var index = GetArgumentIndex(name);
            _argumentValues[index] = new McuCommandArgumentValue(0, default, value);
            return this;
        }
    }
}
