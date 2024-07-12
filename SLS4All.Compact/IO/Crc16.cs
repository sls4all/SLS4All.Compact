// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public static class Crc16
    {
        private readonly static ushort[] _table = new ushort[256];
        private const ushort _polynomial = 0xA001;

        static Crc16()
        {
            ushort value;
            ushort temp;
            for (ushort i = 0; i < _table.Length; ++i)
            {
                value = 0;
                temp = i;
                for (byte j = 0; j < 8; ++j)
                {
                    if (((value ^ temp) & 0x0001) != 0)
                    {
                        value = (ushort)((value >> 1) ^ _polynomial);
                    }
                    else
                    {
                        value >>= 1;
                    }
                    temp >>= 1;
                }
                _table[i] = value;
            }
        }

        public static ushort ComputeChecksum(Span<byte> bytes)
        {
            ushort crc = 0;
            for (int i = 0; i < bytes.Length; ++i)
            {
                var index = (byte)(crc ^ bytes[i]);
                crc = (ushort)((crc >> 8) ^ _table[index]);
            }
            return crc;
        }

        public static byte[] Hash(Span<byte> bytes)
        {
            var res = new byte[2];
            var sum = ComputeChecksum(bytes);
            res[0] = (byte)sum;
            res[1] = (byte)(sum >> 8);
            return res;
        }
    }
}
