// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using SLS4All.Compact.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.UpdateModel
{
    public static class CompactMemberIdGenerator
    {
        /// <summary>
        /// 24 characters long charset inspired by Windows keys
        /// </summary>
        private const string _charset = "BCDFGHJKMPQRTVWXY2346789";

        private const int _memberIdLengthBytes = 9;
        private const int _crcLengthBytes = 2;
        private const int _groupLength = 5;
        private readonly static int s_significantCharCount = (int)Math.Ceiling((_memberIdLengthBytes + _crcLengthBytes) * 8 / Math.Log2(_charset.Length));
        private readonly static int s_totalCharCount = s_significantCharCount + (s_significantCharCount - 1) / _groupLength;

        public static string FormatMemberId(byte[] id)
        {
            var crc = Crc16.Hash(id);
            var remainder = new BigInteger(id.Concat(crc).ToArray(), true, false);
            var sb = new StringBuilder(s_totalCharCount);
            for (int i = 0; i < s_significantCharCount; i++)
            {
                if (i != 0 && i % _groupLength == 0)
                    sb.Append('-');
                var value = (int)(remainder % _charset.Length);
                remainder /= _charset.Length;
                sb.Append(_charset[value]);
            }
            Debug.Assert(remainder == 0);
            return sb.ToString();
        }

        public static string GenerateRandomMemberId(out byte[] id)
        {
            id = RandomNumberGenerator.GetBytes(_memberIdLengthBytes);
            return FormatMemberId(id);
        }

        public static bool TryExtractMemberId(string str, [MaybeNullWhen(false)] out byte[] id)
        {
            str = str.Trim().ToUpperInvariant();
            id = null;
            if (str.Length != s_totalCharCount)
                return false;
            var accumulator = new BigInteger();
            var p = str.Length - 1;
            for (int i = 0; i < s_significantCharCount; i++)
            {
                if (i != 0 && i % _groupLength == 0)
                {
                    if (str[p--] is not ('-' or ' '))
                        return false;
                }
                var value = _charset.IndexOf(str[p--]);
                if (value == -1)
                    return false;
                accumulator = accumulator * _charset.Length + value;
            }
            Debug.Assert(p == -1);
            var data = accumulator.ToByteArray();
            var idPart = data.AsSpan(0, _memberIdLengthBytes);
            var crcPart = data.AsSpan(_memberIdLengthBytes, _crcLengthBytes);
            var crcCalculated = Crc16.Hash(idPart);
            if (!crcPart.SequenceEqual(crcCalculated))
                return false;
            id = idPart.ToArray();
            return true;
        }
    }
}
