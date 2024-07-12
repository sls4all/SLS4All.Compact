// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public sealed class McuCodec
    {
        public const byte SyncByte = 0x7e;
        public const int DataStart = 2;
        public const int MinBlockSize = 5;
        public const int MaxBlockSize = 64;
        public const int MaxDataSize = MaxBlockSize - 5;
        public const int SeqMask = 0x0f;
        private readonly byte[] _buffer = new byte[MaxBlockSize + 1];
        private ulong _seq;
        private int _pos;

        public ArraySegment<byte> Buffer => _buffer;
        public int DataPostition => _pos - DataStart;
        public int Position => _pos;
        public ulong Seq
        {
            get => _seq;
            set => _seq = value;
        }

        public void ResetWrite()
        {
            _pos = DataStart;
        }

        public bool TryResetRead(ArraySegment<byte> buffer, out int dataSize)
        {
            var length = buffer.Count;
            dataSize = 0;
            if (length < MinBlockSize)
                return false;
            if (buffer[0] != length)
                return false;
            if (buffer[length - 1] != SyncByte)
                return false;
            var crcPresent = (ushort)((buffer[length - 3] << 8) | buffer[length - 2]);
            var crcCalculated = Crc16(buffer.AsSpan(0, length - 3));
            if (crcPresent != crcCalculated)
                return false;
            buffer.AsSpan().CopyTo(_buffer);
            _pos = DataStart;
            _seq = (ulong)buffer[1] & SeqMask;
            dataSize = length - MinBlockSize;
            return true;
        }

        public bool TryWrite(Span<byte> data)
        {
            if (_pos + data.Length > MaxDataSize + DataStart)
                return false;
            data.CopyTo(_buffer.AsSpan(_pos));
            _pos += data.Length;
            return true;
        }

        private static ushort Crc16(Span<byte> buf)
        {
            ushort crc = 0xffff;
            for (int ii = 0; ii < buf.Length; ii++)
            {
                var data = buf[ii];
                data = (byte)(data ^ (crc & 0xff));
                data = (byte)(data ^ (data << 4));
                crc = (ushort)(((data << 8) | (crc >> 8)) ^ (byte)(data >> 4) ^ (data << 3));
            }
            return crc;
        }

        public Memory<byte> FinalizeBlock()
        {
            _buffer[0] = (byte)(_pos + 3);
            _buffer[1] = (byte)((_seq & SeqMask) | 0x10);
            var crc = Crc16(_buffer.AsSpan(0, _pos));
            _buffer[_pos++] = (byte)(crc >> 8);
            _buffer[_pos++] = (byte)(crc);
            _buffer[_pos++] = (byte)SyncByte;
            _seq++;
            return _buffer.AsMemory(0, _pos);
        }

        public Memory<byte> FinalizeCommand()
        {
            return _buffer.AsMemory(2, _pos - 2);
        }

        public void WriteVLQ(long value)
        {
            if (value > uint.MaxValue)
                WriteVLQInner((uint)value);
            else if (value < int.MinValue)
                WriteVLQInner((int)value);
            else
                WriteVLQInner(value);
        }

        private void WriteVLQInner(long value)
        {
            if (!(value < (3L << 5) && value >= -(1L << 5)))
            {
                if (!(value < (3L << 12) && value >= -(1L << 12)))
                {
                    if (!(value < (3L << 19) && value >= -(1L << 19)))
                    {
                        if (!(value < (3L << 26) && value >= -(1L << 26)))
                        {
                            _buffer[_pos++] = (byte)((value >> 28) | 0x80);
                        }
                        _buffer[_pos++] = (byte)(((value >> 21) & 0x7f) | 0x80);
                    }
                    _buffer[_pos++] = (byte)(((value >> 14) & 0x7f) | 0x80);
                }
                _buffer[_pos++] = (byte)(((value >> 7) & 0x7f) | 0x80);
            }
            _buffer[_pos++] = (byte)(value & 0x7f);
        }

        public long ReadVLQ()
        {
            var item = _buffer[_pos++];
            var value = (long)item & ~0x80;
            while ((item & 0x80) != 0)
            {
                item = _buffer[_pos++];
                value = (value << 7) | (byte)(item & ~0x80);
            }
            return value;
        }

        public byte[] ReadBytes(int length)
        {
            var res = _buffer.AsSpan(_pos, length).ToArray();
            _pos += length;
            return res;
        }
    }
}
