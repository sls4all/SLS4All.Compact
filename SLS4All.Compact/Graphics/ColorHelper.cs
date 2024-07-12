// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Graphics
{
    public static class ColorHelper
    {
        private static readonly ConcurrentDictionary<RgbaF, (string filter, RgbaF result)> _cssColorizeFilterCache = new();
        private static readonly RgbaF s_colorizeInit = RgbaFColors.White.WebBrightness(0.0f).WebInvert(1.0f).WebBrightness(0.5f).WebSepia();
        private static readonly Vector3[] s_fullColors = new Vector3[]{
            new(0,0,0),
            new(0,0,1),
            new(0,1,0),
            new(1,1,0),
            new(1,0,0),
            new(1,0,1),
            new(1,1,1),
        };
        private static readonly Vector3[] s_redOrangeWhiteColors = new Vector3[]{
            new(1,0,0),
            new(1,0.6470588235294118f,0),
            new(1,1,1)
        };
        private static readonly Vector3[] s_greenOrangeRedColors = new Vector3[]{
            new(0,1,0),
            new(1,0.6470588235294118f,0),
            new(1,0,0)
        };

        public static RgbB GetGreenOrangeRedHeatmapColor(float v, float vmin = 0, float vrange = 1)
            => GetHeatmapColor(v, vmin, vrange, s_greenOrangeRedColors);

        public static RgbB GetWhiteOrangeRedHeatmapColor(float v, float vmin = 0, float vrange = 1)
            => GetHeatmapColor(v, vmin, vrange, s_redOrangeWhiteColors);

        public static RgbB GetFullHeatmapColor(float v, float vmin = 0, float vrange = 1)
            => GetHeatmapColor(v, vmin, vrange, s_fullColors);

        public static RgbB GetHeatmapColor(float v, float vmin, float vrange, Vector3[] colors)
        {
            // Heatmap code borrowed from: http://www.andrewnoske.com/wiki/Code_-_heatmaps_and_color_gradients
            int idx1, idx2;
            var fractBetween = 0.0f;
            v = vrange > 0 ? (v - vmin) / vrange : 0;
            if (v <= 0)
                idx1 = idx2 = 0;
            else if (v >= 1)
                idx1 = idx2 = colors.Length - 1;
            else
            {
                v *= colors.Length - 1;
                idx1 = (int)v;
                idx2 = idx1 + 1;
                fractBetween = v - idx1;
            }
            ref var firstColor = ref MemoryMarshal.GetArrayDataReference(colors);
            ref var c1 = ref Unsafe.Add(ref firstColor, idx1);
            ref var c2 = ref Unsafe.Add(ref firstColor, idx2);
            var c = (((c2 - c1) * fractBetween) + c1) * 255.0f;
            var ir = (byte)MathF.Round(c.X);
            var ig = (byte)MathF.Round(c.Y);
            var ib = (byte)MathF.Round(c.Z);
            return new RgbB(ir, ig, ib);
        }

        public static uint To32Bits(this RgbB value)
            => (uint)value.R | ((uint)value.G << 8) | ((uint)value.B << 16) | 0xFF000000U;

        public static uint To32Bits(this RgbaB value)
            => (uint)value.R | ((uint)value.G << 8) | ((uint)value.B << 16) | ((uint)value.A << 24);

        public static ushort To24Bits(this RgbB value)
            => (ushort)((value.R >> (8 - 5) << (6 + 5)) | (value.G >> (8 - 6) << (5)) | (value.B >> (8 - 5)));

        public static ushort To24Bits(this RgbaB value)
            => (ushort)((value.R >> (8 - 5) << (6 + 5)) | (value.G >> (8 - 6) << (5)) | (value.B >> (8 - 5)));

        public static string GetCssColorizeFilter(this RgbaF target)
            => GetCssColorizeFilter(target, out _);

        /// <summary>
        /// Tries to guess best CSS filter that transforms any color to specified color.
        /// Can be used to overlay color of transparent images. 
        /// The applied filter effect is that only alpha will be untouched, all color values is replaced with <paramref name="target"/> color.
        /// Result is then multiplied with <paramref name="target"/> alpha component.
        /// </summary>
        public static string GetCssColorizeFilter(this RgbaF target, out RgbaF result)
        {
            if (_cssColorizeFilterCache.TryGetValue(target, out var existing))
            {
                result = existing.result;
                return existing.filter;
            }

            var init = s_colorizeInit;
            const float hueMax = MathF.PI * 2;
            const float saturationMax = 1000;
            const float brightnessMax = 1;

            var random = new Random();
            var best = (hue1: 0f, sat1: 0f, br: 0f, color: default(RgbaF));
            var bestError = float.MaxValue;
            const float maxError = (10.0f / 255.0f) * (10.0f / 255.0f);
            for (int i = 0; i < 1000000; i++)
            {
                var candidate = (
                    hue1: random.NextSingle() * hueMax,
                    sat1: random.NextSingle() * saturationMax,
                    br: random.NextSingle() * brightnessMax,
                    color: default(RgbaF));
                candidate.color = init
                    .WebHueRotate(candidate.hue1)
                    .WebSaturate(candidate.sat1)
                    .WebBrightness(candidate.br);
                var error = RgbaF.SquaredRgbError(candidate.color, target);
                if (error < bestError)
                {
                    bestError = error;
                    best = candidate;
                    if (error < maxError)
                        break;
                }
            }

            var sb = new StringBuilder("brightness(0) invert(1) brightness(0.5) sepia(1)");
            sb.Append(" hue-rotate(");
            sb.Append((best.hue1 / Math.PI * 180).ToString("0.0000", CultureInfo.InvariantCulture));
            sb.Append("deg) saturate(");
            sb.Append((best.sat1).ToString("0.0000", CultureInfo.InvariantCulture));
            sb.Append(')');
            if (best.br != 1)
            {
                sb.Append(" brightness(");
                sb.Append(best.br.ToString("0.0000", CultureInfo.InvariantCulture));
                sb.Append(')');
            }
            if (target.A != 1)
            {
                sb.Append(" opacity(");
                sb.Append(target.A.ToString("0.0000", CultureInfo.InvariantCulture));
                sb.Append(')');
            }
            var filter = sb.ToString();
            result = best.color;
            _cssColorizeFilterCache[target] = (filter, result);
            return filter;
        }

        public static RgbaB ToRgbaB(this RgbB color, byte a)
            => new RgbaB(color.R, color.G, color.B, a);
    }
}
