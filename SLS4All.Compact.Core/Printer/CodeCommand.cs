// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using static SLS4All.Compact.Printer.CodeCommand;

namespace SLS4All.Compact.Printer
{
    public interface ITypedCodeCommand
    {
    }

    public interface ICodeFormatter
    {
        void ToString(StringBuilder buf, in CodeCommand cmd);
        string ToString(in CodeCommand cmd);
        ValueTask Execute(CodeCommand cmd, bool hidden, IPrinterClientCommandContext? context, CancellationToken cancel = default);
    }

    public readonly struct CodeCommand : IEquatable<CodeCommand>
    {
        [InterpolatedStringHandler]
        public ref struct CodeInterpolatedStringHandler
        {
            private DefaultInterpolatedStringHandler _handler;

            public CodeInterpolatedStringHandler(
              int literalLength,
              int formattedCount)
            {
                _handler = new DefaultInterpolatedStringHandler(literalLength, formattedCount, CultureInfo.InvariantCulture);
            }

            public void AppendLiteral(string value)
                => _handler.AppendLiteral(value);

            public void AppendFormatted(ReadOnlySpan<char> value)
                => _handler.AppendFormatted(value);

            public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
                => _handler.AppendFormatted(value, alignment, format);

            public void AppendFormatted<T>(T value)
                => _handler.AppendFormatted<T>(value);

            public void AppendFormatted<T>(T value, string? format)
                => _handler.AppendFormatted<T>(value, format);

            public void AppendFormatted<T>(T value, int alignment)
                => _handler.AppendFormatted<T>(value, alignment);

            public void AppendFormatted<T>(T value, int alignment, string? format)
                => _handler.AppendFormatted<T>(value, alignment, format);

            public void AppendFormatted(object? value, int alignment = 0, string? format = null)
                => _handler.AppendFormatted(value, alignment, format);

            public void AppendFormatted(string? value)
                => _handler.AppendFormatted(value);

            public void AppendFormatted(string? value, int alignment = 0, string? format = null)
                => _handler.AppendFormatted(value, alignment, format);

            public override string ToString()
                => _handler.ToString();

            public string ToStringAndClear()
                => _handler.ToStringAndClear();
        }


        private readonly object _value;
        private readonly float _arg1;
        private readonly float _arg2;
        private readonly float _arg3;
        private readonly float _arg4;
        public object Value => _value;
        public float Arg1 => _arg1;
        public float? Arg1Nullable => _arg1 != float.MinValue ? _arg1 : null;
        public float Arg2 => _arg2;
        public float? Arg2Nullable => _arg2 != float.MinValue ? _arg2 : null;
        public float Arg3 => _arg3;
        public float? Arg3Nullable => _arg3 != float.MinValue ? _arg3 : null;
        public float Arg4 => _arg4;
        public float? Arg4Nullable => _arg4 != float.MinValue ? _arg4 : null;

        public CodeCommand(ref CodeInterpolatedStringHandler command)
            => _value = command.ToStringAndClear();

        public CodeCommand(string value)
        {
            _value = value;
        }

        public CodeCommand(ICodeFormatter formatter, float arg1 = 0, float arg2 = 0, float arg3 = 0, float arg4 = 0)
        {
            _value = formatter;
            _arg1 = arg1;
            _arg2 = arg2;
            _arg3 = arg3;
            _arg4 = arg4;
        }

        public CodeCommand(ITypedCodeCommand value)
            => _value = value;

        public bool Equals(CodeCommand other)
            => Equals(_value, other._value) && _arg1 == other._arg1 && _arg2 == other._arg2 && _arg3 == other._arg3;

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is CodeCommand other && Equals(other);

        public override int GetHashCode() 
            => (_value, _arg1, _arg2, _arg3).GetHashCode();

        public override string ToString()
        {
            if (_value is ICodeFormatter formattable)
                return formattable.ToString(this);
            else
                return _value.ToString()!;
        }

        public void ToString(StringBuilder buf)
        {
            if (_value is ICodeFormatter formattable)
                formattable.ToString(buf, this);
            else
                buf.Append(_value.ToString());
        }
    }
}
