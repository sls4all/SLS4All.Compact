// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public static class CameraHelpers
    {
        public static void RotateCW<T>(int width, int height, Span<T> pixels)
        {
            var copy = ArrayPool<T>.Shared.Rent(pixels.Length);
            for (int y = 0, i = 0; y < height; y++)
            {
                for (int x = 0, o = height - y - 1; x < width; x++, i++, o += height)
                    copy[o] = pixels[i];
            }
            copy.AsSpan(0, pixels.Length).CopyTo(pixels);
            ArrayPool<T>.Shared.Return(copy);
        }

        public static void Flip<T>(bool flipX, bool flipY, int width, int height, Span<T> pixels)
        {
            if (flipX)
            {
                if (flipY) // x & y
                {
                    for (int i = 0, l = pixels.Length - 1, f = pixels.Length / 2; i < f; i++)
                        Swap(ref pixels[i], ref pixels[l - i]);
                }
                else // x
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0, s = y * width, f = width / 2; x < f; x++)
                            Swap(ref pixels[s + x], ref pixels[s + width - 1 - x]);
                    }
                }
            }
            else if (flipY) // y
            {
                for (int y = 0, f = height / 2; y < f; y++)
                {
                    for (int x = 0, s1 = y * width, s2 = (height - 1 - y) * width; x < width; x++)
                        Swap(ref pixels[s1 + x], ref pixels[s2 + x]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap<T>(ref T value1, ref T value2)
        {
            var temp = value1;
            value1 = value2;
            value2 = temp;
        }
    }
}
