// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Diagnostics;

namespace SLS4All.Compact.McuClient.Devices
{
    public abstract class McuDeviceBase : IMcuDevice
    {
        private readonly byte[] _buffer = new byte[(McuCodec.MaxBlockSize + 1) * 2];
        private readonly PrimitiveList<int> _indexes = new();
        private int _bufferLen = 0;

        public abstract IMcuDeviceFactory Factory { get; }

        public abstract McuDeviceInfo Info { get; }

        public abstract ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancel = default);

        public abstract ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default);

        public async ValueTask<int> ReadBlock(McuCodec codec, CancellationToken cancel = default)
        {
            var buffer = _buffer;
            while (true)
            {
                if (_bufferLen > 0)
                {
                    var block = TryIdentifyFirstValidBlock(codec, out var dataSize);
                    if (block.length != 0)
                    {
                        var start = block.length + block.start;
                        buffer.AsSpan(start, _bufferLen - start).CopyTo(buffer);
                        _bufferLen -= start;
                        return dataSize;
                    }
                }

                if (buffer.Length - _bufferLen > 0)
                {
                    var read = await Read(buffer.AsMemory(_bufferLen), cancel);
                    if (read == 0)
                        throw new EndOfStreamException($"End of stream encountered for {Info}");
                    _bufferLen += read;
                }
                else // filled the original(input) buffer, no sync byte found
                {
                    // trim at first thing looking like a sync byte
                    var syncPos = Array.IndexOf(buffer, McuCodec.SyncByte);
                    if (syncPos == -1) // no sync found, discard everything
                        _bufferLen = 0;
                    else // split at sync
                    {
                        buffer.AsSpan(syncPos + 1, _bufferLen - syncPos - 1).CopyTo(buffer);
                        _bufferLen -= syncPos + 1;
                    }
                }
            }
        }

        private (int start, int length) TryIdentifyFirstValidBlock(McuCodec codec, out int dataSize)
        {
            var indexes = _indexes;
            var start = 0;
            var buffer = _buffer;
            var bufferLen = _bufferLen;
            dataSize = 0;
            indexes.Clear();
            indexes.Add(0);
            while (start < bufferLen)
            {
                var end = buffer.AsSpan(start, bufferLen - start).IndexOf(McuCodec.SyncByte);
                if (end == -1)
                    break;
                end += start;
                start = end + 1;
                indexes.Add(start);
            }
            if (indexes.Count > 0)
            {
                for (int i = 0; i < indexes.Count; i++)
                {
                    for (int q = i + 1; q < indexes.Count; q++)
                    {
                        var bstart = indexes[i];
                        var blen = indexes[q] - bstart;
                        if (codec.TryResetRead(new ArraySegment<byte>(buffer, bstart, blen), out dataSize))
                        {
                            indexes.Clear();
                            return (bstart, blen);
                        }
                    }
                }
            }
            return default;
        }

        public abstract Task Flush(CancellationToken cancel = default);

        public abstract void Dispose();
    }
}