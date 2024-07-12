// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class ReadOnlySequenceStream : Stream
    {
        private ReadOnlySequence<byte> _sequence = new([]);

        public ReadOnlySequence<byte> Sequence
        {
            get => _sequence;
            set => _sequence = value;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var firstSpan = _sequence.FirstSpan;
            var read = Math.Min(firstSpan.Length, count);
            firstSpan.Slice(0, read).CopyTo(buffer.AsSpan(offset));
            _sequence = _sequence.Slice(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var firstSpan = _sequence.FirstSpan;
            var read = Math.Min(firstSpan.Length, buffer.Length);
            firstSpan.Slice(0, read).CopyTo(buffer);
            _sequence = _sequence.Slice(read);
            return read;
        }

        public override int ReadByte()
        {
            var firstSpan = _sequence.FirstSpan;
            if (firstSpan.Length > 0)
            {
                var res = firstSpan[0];
                _sequence = _sequence.Slice(1);
                return res;
            }
            else
                return -1;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
