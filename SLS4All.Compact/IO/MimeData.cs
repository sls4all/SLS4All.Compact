// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace SLS4All.Compact.IO
{
    public readonly record struct MimeData(string ContentType, Memory<byte> Data, bool IsReturnable = false)
    {
        public bool IsEmpty => ContentType == null;

        public static MimeData BlackPng { get; } 
            = new MimeData("image/png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAA1JREFUGFdjYGBg+A8AAQQBAHAgZQsAAAAASUVORK5CYII="));

        public static MimeData BlackJpeg { get; }
            = new MimeData("image/jpeg", Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/4gIoSUNDX1BST0ZJTEUAAQEAAAIYAAAAAAQwAABtbnRyUkdCIFhZWiAAAAAAAAAAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAAHRyWFlaAAABZAAAABRnWFlaAAABeAAAABRiWFlaAAABjAAAABRyVFJDAAABoAAAAChnVFJDAAABoAAAAChiVFJDAAABoAAAACh3dHB0AAAByAAAABRjcHJ0AAAB3AAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAFgAAAAcAHMAUgBHAEIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFhZWiAAAAAAAABvogAAOPUAAAOQWFlaIAAAAAAAAGKZAAC3hQAAGNpYWVogAAAAAAAAJKAAAA+EAAC2z3BhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABYWVogAAAAAAAA9tYAAQAAAADTLW1sdWMAAAAAAAAAAQAAAAxlblVTAAAAIAAAABwARwBvAG8AZwBsAGUAIABJAG4AYwAuACAAMgAwADEANv/bAEMAAwICAgICAwICAgMDAwMEBgQEBAQECAYGBQYJCAoKCQgJCQoMDwwKCw4LCQkNEQ0ODxAQERAKDBITEhATDxAQEP/bAEMBAwMDBAMECAQECBALCQsQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEP/AABEIAAEAAQMBIgACEQEDEQH/xAAVAAEBAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AlUAD/9k="));
        
        public MimeData RentCopy()
            => new MimeData(ContentType, Data.Span.ToBorrowedArrayMemory(), IsReturnable: true);

        public void Return()
        {
            if (IsReturnable)
                PrinterMemoryExtensions.ReturnArrayMemory<byte>(Data);
        }

        public void WriteAsDataUrl(Stream stream)
        {
            stream.Write("data:"u8);
            var array = ArrayPool<byte>.Shared.Rent(ContentType.Length);
            var count = Encoding.ASCII.GetBytes(ContentType, array);
            stream.Write(array.AsSpan(0, count));
            ArrayPool<byte>.Shared.Return(array);
            stream.Write(";base64, "u8);
            array = ArrayPool<byte>.Shared.Rent(Base64.GetMaxEncodedToUtf8Length(Data.Length));
            Base64.EncodeToUtf8(Data.Span, array, out _, out count);
            stream.Write(array.AsSpan(0, count));
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
