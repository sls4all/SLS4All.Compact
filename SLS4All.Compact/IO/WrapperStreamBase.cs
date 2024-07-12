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
    public abstract class WrapperStreamBase : Stream
    {
        private readonly Stream _innerStream = default!;

        protected Stream InnerStream
        {
            get => _innerStream;
            init => _innerStream = value;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanWrite => _innerStream.CanWrite;
        public override bool CanSeek => _innerStream.CanSeek;

        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
        public override int ReadTimeout { get => _innerStream.ReadTimeout; set => _innerStream.ReadTimeout = value; }
        public override int WriteTimeout { get => _innerStream.WriteTimeout; set => _innerStream.WriteTimeout = value; }
        public override bool CanTimeout => _innerStream.CanTimeout;

        protected WrapperStreamBase(Stream stream)
        {
            _innerStream = stream;
        }

        public override int ReadByte()
            => _innerStream.ReadByte();

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
            => _innerStream.BeginRead(buffer, offset, count, callback, state);

        public override int EndRead(IAsyncResult asyncResult)
            => _innerStream.EndRead(asyncResult);

        public override int Read(byte[] buffer, int offset, int count)
            => _innerStream.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer)
            => _innerStream.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _innerStream.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            _innerStream.Dispose();
            base.Dispose(disposing);
        }

        public override void Close()
        {
            _innerStream.Close();
            base.Close();
        }

        public override void Flush()
            => _innerStream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
            => _innerStream.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin)
            => _innerStream.Seek(offset, origin);

        public override void SetLength(long value)
            => _innerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
            => _innerStream.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer)
            => _innerStream.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _innerStream.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _innerStream.WriteAsync(buffer, cancellationToken);

        public override void WriteByte(byte value)
            => _innerStream.WriteByte(value);

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
            => _innerStream.BeginWrite(buffer, offset, count, callback, state);

        public override void EndWrite(IAsyncResult asyncResult)
            => _innerStream.EndWrite(asyncResult);

        public override void CopyTo(Stream destination, int bufferSize)
            => _innerStream.CopyTo(destination, bufferSize);
        
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            => _innerStream.CopyToAsync(destination, bufferSize, cancellationToken);

        public override ValueTask DisposeAsync()
            => _innerStream.DisposeAsync();
    }
}
