// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins
{
    public class TmcFieldCollection<TRegister>
        where TRegister : struct, Enum
    {
        private readonly List<(TRegister Reg, uint Value)> _values;

        public List<(TRegister Reg, uint Value)> Values => _values;

        public TmcFieldCollection()
        {
            _values = new();
        }

        public long GetField(string field, uint? registerValue = null, TRegister? register = null)
        {
            var reg = register ?? TmcFields<TRegister>.FieldToRegister[field];
            var regValue = registerValue ?? GetRegister(reg);
            var info = TmcFields<TRegister>.AllFields[reg][field];
            long fieldValue = (regValue & info.Mask) >> info.Shift;
            if (info.IsSigned)
            {
                var counter = 32 - info.Shift;
                fieldValue = ((fieldValue << counter) >> counter);
            }
            return fieldValue;
        }

        public void SetField(string field, long fieldValue, uint? registerValue = null, TRegister? register = null)
        {
            var reg = register ?? TmcFields<TRegister>.FieldToRegister[field];
            var regValue = registerValue ?? GetRegister(reg);
            var info = TmcFields<TRegister>.AllFields[reg][field];
            var regValueNew = (regValue & ~info.Mask) | (((uint)fieldValue << info.Shift) & info.Mask);
            SetRegister(reg, regValueNew);
        }

        private void SetRegister(TRegister reg, uint value)
        {
            var span = CollectionsMarshal.AsSpan(_values);
            for (int i = 0; i < span.Length; i++)
            {
                ref var item = ref span[i];
                if (EqualityComparer<TRegister>.Default.Equals(item.Reg, reg))
                {
                    item.Value = value;
                    return;
                }
            }
            _values.Add((reg, value));
        }

        public uint GetRegister(TRegister reg, uint defaultValue = 0)
        {
            var span = CollectionsMarshal.AsSpan(_values);
            for (int i = 0; i < span.Length; i++)
            {
                ref var item = ref span[i];
                if (EqualityComparer<TRegister>.Default.Equals(item.Reg, reg))
                    return item.Value;
            }
            return defaultValue;
        }

        public void SetField(string field, long defaultFieldValue, Dictionary<string, long?>? fields)
        {
            if (fields != null && fields.TryGetValue(field, out var fieldValue) && fieldValue >= 0)
                SetField(field, fieldValue.Value);
            else
                SetField(field, defaultFieldValue);
        }
    }
}
