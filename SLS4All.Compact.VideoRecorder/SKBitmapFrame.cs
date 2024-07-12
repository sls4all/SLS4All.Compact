// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using FFMpegCore.Pipes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.VideoRecorder
{
    /// <summary>
    /// <see cref="https://swharden.com/csdv/skiasharp/video/"/>
    /// </summary>
    public sealed class SKBitmapFrame : IVideoFrame, IDisposable
    {
        private readonly SKBitmap _source;

        public int Width => _source.Width;
        public int Height => _source.Height;
        public string Format => "bgra";

        public SKBitmapFrame(SKBitmap bmp)
        {
            if (bmp.ColorType != SKColorType.Bgra8888)
                throw new NotImplementedException("only 'bgra' color type is supported");
            _source = bmp;
        }

        public void Serialize(Stream stream)
            => stream.Write(_source.GetPixelSpan());

        public Task SerializeAsync(Stream stream, CancellationToken token)
        {
            stream.Write(_source.GetPixelSpan());
            return Task.CompletedTask;
        }

        public void Dispose() =>
            _source.Dispose();
    }
}
