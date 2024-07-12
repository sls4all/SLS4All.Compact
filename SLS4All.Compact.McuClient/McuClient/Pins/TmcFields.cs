// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.ComponentModel;
using System.Reflection;

namespace SLS4All.Compact.McuClient.Pins
{
    public static class TmcFields<TRegister>
        where TRegister : struct, Enum
    {
        public static Dictionary<string, TRegister> FieldToRegister { get; } = new();
        public static Dictionary<TRegister, Dictionary<string, (uint Mask, int Shift, bool IsSigned)>> AllFields { get; set; } = new();
        public static Dictionary<string, TRegister> NameToRegister { get; } = new();
        public static Dictionary<string, int> NameToRegisterValue { get; } = new();

        static TmcFields()
        {
            foreach (var registerField in typeof(TRegister).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                var fieldsDict = new Dictionary<string, (uint, int, bool)>();
                var registerFieldAttr = registerField.GetCustomAttribute<TmcRegisterEnumAttribute>() ?? throw new InvalidOperationException();
                var register = (TRegister)registerField.GetValue(null)!;
                var registerName = register.ToString();
                foreach (var fieldType in registerFieldAttr.EnumTypes)
                {
                    foreach (var field in fieldType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    {
                        var isSigned = field.GetCustomAttribute<TmcSignedFieldAttribute>() != null;
                        var fieldName = field.Name;
                        var mask = Convert.ToUInt32(field.GetValue(null));
                        FieldToRegister[fieldName] = register;
                        fieldsDict[fieldName] = (mask, GetShift(mask), isSigned);
                    }
                }
                AllFields.Add(register, fieldsDict);
                NameToRegister.Add(registerName, register);
                NameToRegisterValue.Add(registerName, Convert.ToInt32(register));
            }
        }

        private static int GetShift(uint mask)
        {
            if (mask == 0)
                throw new ArgumentException("Mask is zero");
            for (int i = 0; ; i++)
            {
                if ((mask & 1) == 1)
                    return i;
                mask >>= 1;
            }
        }
    }
}
