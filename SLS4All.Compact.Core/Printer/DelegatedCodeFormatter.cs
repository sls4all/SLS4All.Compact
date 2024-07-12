// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Text;

namespace SLS4All.Compact.Printer
{
    public sealed class DelegatedCodeFormatter : ICodeFormatter
    {
        private readonly Delegate _delegate;
        private readonly Func<CodeCommand, string> _toString;

        public object? Tag { get; init; }

        public DelegatedCodeFormatter(
            Func<CodeCommand, bool, IPrinterClientCommandContext?, CancellationToken, ValueTask> func,
            Func<CodeCommand, string> toString,
            object? tag = null)
        {
            _delegate = func;
            _toString = toString;
            Tag = tag;
        }

        public DelegatedCodeFormatter(
            Func<CodeCommand, bool, IPrinterClientCommandContext?, CancellationToken, Task> func,
            Func<CodeCommand, string> toString,
            object? tag = null)
        {
            _delegate = func;
            _toString = toString;
            Tag = tag;
        }

        public CodeCommand Create(float arg1 = 0, float arg2 = 0, float arg3 = 0, float arg4 = 0)
            => new CodeCommand(this, arg1, arg2, arg3, arg4);

        public ValueTask Execute(CodeCommand cmd, bool hidden, IPrinterClientCommandContext? context, CancellationToken cancel)
        {
            if (_delegate is Func<CodeCommand, bool, IPrinterClientCommandContext?, CancellationToken, ValueTask> valueFunc)
                return valueFunc(cmd, hidden, context, cancel);
            else
                return new ValueTask(((Func<CodeCommand, bool, IPrinterClientCommandContext?, CancellationToken, Task>)_delegate)(cmd, hidden, context, cancel));
        }

        public void ToString(StringBuilder buf, in CodeCommand cmd)
            => buf.Append(_toString(cmd));

        public string ToString(in CodeCommand cmd)
            => _toString(cmd);

        public static bool IsWithTag(in CodeCommand cmd, object? tag)
            => cmd.Value is DelegatedCodeFormatter delegated && Equals(delegated.Tag, tag);
    }
}
