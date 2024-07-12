// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class ConcatStream : Stream
    {
        private readonly Stream[] _streams;
        private readonly bool _ownsStreams;
        private int _streamIndex;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public ConcatStream(bool ownsStreams, params Stream[] streams)
        {
            _ownsStreams = ownsStreams;
            _streams = streams;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (true)
            {
                if (_streamIndex >= _streams.Length)
                    return 0;
                var stream = _streams[_streamIndex];
                var read = stream.Read(buffer, offset, count);
                if (read != 0)
                    return read;
                else
                    _streamIndex++;
            }
        }

        public override int Read(Span<byte> buffer)
        {
            while (true)
            {
                if (_streamIndex >= _streams.Length)
                    return 0;
                var stream = _streams[_streamIndex];
                var read = stream.Read(buffer);
                if (read != 0)
                    return read;
                else
                    _streamIndex++;
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_streamIndex >= _streams.Length)
                    return 0;
                var stream = _streams[_streamIndex];
                var read = await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                if (read != 0)
                    return read;
                else
                    _streamIndex++;
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                if (_streamIndex >= _streams.Length)
                    return 0;
                var stream = _streams[_streamIndex];
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read != 0)
                    return read;
                else
                    _streamIndex++;
            }
        }

        public override int ReadByte()
        {
            while (true)
            {
                if (_streamIndex >= _streams.Length)
                    return 0;
                var stream = _streams[_streamIndex];
                var read = stream.ReadByte();
                if (read >= 0)
                    return read;
                else
                    _streamIndex++;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override void Close()
        {
            base.Close();
            if (_ownsStreams)
            {
                foreach (var stream in _streams)
                    stream.Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_ownsStreams)
            {
                foreach (var stream in _streams)
                    stream.Dispose();
            }
        }
    }
}
