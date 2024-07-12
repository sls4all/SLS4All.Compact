// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public class MemoryClipboardProvider : IClipboardProvider
    {
        private volatile object? _storedValue;

        protected static bool TryClone(object? value, out object? clone)
        {
            if (value == null)
            {
                clone = null;
                return true;
            }
            var type = value.GetType();
            if (Type.GetTypeCode(type) != TypeCode.Object || type.IsValueType)
            {
                clone = value;
                return true;
            }
            try
            {
                dynamic? dyn = value;
                clone = dyn?.Clone();
                return true;
            }
            catch
            {
                // swallow
                clone = null;
                return false;
            }
        }

        public virtual ValueTask Copy(object? obj, string? str, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (TryClone(obj, out var clone))
                _storedValue = clone;
            return ValueTask.CompletedTask;
        }

        public virtual ValueTask<(bool Succeeded, object? Value)> Paste(Type type, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var storedValue = _storedValue;
            if (storedValue == null)
            {
                return ValueTask.FromResult((true, (object?)null));
            }
            if (!type.IsAssignableFrom(storedValue.GetType()))
            {
                return ValueTask.FromResult((false, (object?)null));
            }
            var succeeded = TryClone(storedValue, out var value);
            return ValueTask.FromResult((succeeded, (object?)value));
        }
    }
}
