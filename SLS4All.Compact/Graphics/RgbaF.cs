// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SLS4All.Compact.Graphics
{
    [DebuggerDisplay("{Dump}")]    
    public struct RgbaF : IEquatable<RgbaF>
    {
        public Vector4 Vector;

        public float R
        {
            get => Vector.X;
            set => Vector.X = value;
        }
        public float G
        {
            get => Vector.Y;
            set => Vector.Y = value;
        }
        public float B
        {
            get => Vector.Z;
            set => Vector.Z = value;
        }
        public float A
        {
            get => Vector.W;
            set => Vector.W = value;
        }
        public float RGBMax
        {
            get
            {
                var res = R;
                if (G > res)
                    res = G;
                if (B > res)
                    res = B;
                return res;
            }
        }
        public float RGBMin
        {
            get
            {
                var res = R;
                if (G < res)
                    res = G;
                if (B < res)
                    res = B;
                return res;
            }
        }
        internal string Dump
        {
            get
            {
                var b = ToRgbaByte();
                return ($"#{b.A:X2}{b.R:X2}{b.G:X2}{b.B:X2} ({b.R:0.000},{b.G:0.000},{b.B:0.000},{b.A:0.000}) [{R:0.000},{G:0.000},{B:0.000},{A:0.000}]");
            }
        }

        public RgbaF(float r, float g, float b, float a)
        {
            Vector = new Vector4(r, g, b, a);
        }

        public RgbaF(float r, float g, float b)
        {
            Vector = new Vector4(r, g, b, 1);
        }

        public RgbaF(string value)
        {
            if (RgbaFColors._webColors != null && // may be null due to dependecies between the two classes
                RgbaFColors._webColors.TryGetValue(value, out var webColor))
            {
                Vector = webColor.Vector;
                return;
            }
            static int HalfByteFromHex(char ch, bool throwOnError = true)
            {
                if (ch >= '0' && ch <= '9')
                    return (ch - '0');
                else if (ch >= 'a' && ch <= 'f')
                    return (ch - 'a' + 10);
                else if (ch >= 'A' && ch <= 'F')
                    return (ch - 'A' + 10);
                else if (throwOnError)
                    throw new ArgumentException("value is not a valid hex color");
                else
                    return -1;
            }
            static int ByteFromHex(string value, int index, bool throwOnError = true)
                => (HalfByteFromHex(value[index], throwOnError) << 4) | HalfByteFromHex(value[index + 1], throwOnError);

            int start = value.StartsWith('#') ? 1 : 0;
            var r = ByteFromHex(value, start + 0);
            var g = ByteFromHex(value, start + 2);
            var b = ByteFromHex(value, start + 4);
            int a;
            if (value.Length - start == 8)
                a = ByteFromHex(value, start + 6);
            else if (value.Length - start == 6)
                a = 255;
            else
                throw new ArgumentException("value is not a valid hex color");
            Vector = new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
        }

        public RgbaF(Vector4 vec)
        {
            Vector = vec;
        }

        public RgbaF(RgbB value)
        {
            Vector = new Vector4(value.R / 255.0f, value.G / 255.0f, value.B / 255.0f, 1);
        }

        public RgbaF(RgbaB value)
        {
            Vector = new Vector4(value.R / 255.0f, value.G / 255.0f, value.B / 255.0f, value.A / 255.0f);
        }

        public static RgbaF FromBytes(int r, int g, int b, int a)
            => new RgbaF(new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f));

        public static RgbaF FromBytes(int r, int g, int b)
            => new RgbaF(new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, 1));

        public RgbaB ToRgbaByte()
            => new RgbaB(ClampByte(R), ClampByte(G), ClampByte(B), ClampByte(A));

        public RgbB ToRgbByte()
            => new RgbB(ClampByte(R), ClampByte(G), ClampByte(B));

        public RgbaF WebHueRotate(float radians)
        {
            (var sinHue, var cosHue) = MathF.SinCos(radians);
            var rotate = new Matrix4x4(
                0.213f + cosHue * 0.787f - sinHue * 0.213f, 0.213f - cosHue * 0.213f + sinHue * 0.143f, 0.213f - cosHue * 0.213f - sinHue * 0.787f, 0,
                0.715f - cosHue * 0.715f - sinHue * 0.715f, 0.715f + cosHue * 0.285f + sinHue * 0.140f, 0.715f - cosHue * 0.715f + sinHue * 0.715f, 0,
                0.072f - cosHue * 0.072f + sinHue * 0.928f, 0.072f - cosHue * 0.072f - sinHue * 0.283f, 0.072f + cosHue * 0.928f + sinHue * 0.072f, 0,
                0, 0, 0, 0);
            var vec = Vector4.Transform(Vector, rotate);
            var res = new RgbaF(Clamp(vec.X), Clamp(vec.Y), Clamp(vec.Z), A);
            return res;
        }

        public RgbaF WebSaturate(float amount)
        {
            var saturate = new Matrix4x4(
                0.213f + 0.787f * amount, 0.213f - 0.213f * amount, 0.213f - 0.213f * amount, 0,
                0.715f - 0.715f * amount, 0.715f + 0.285f * amount, 0.715f - 0.715f * amount, 0,
                0.072f - 0.072f * amount, 0.072f - 0.072f * amount, 0.072f + 0.928f * amount, 0,
                0, 0, 0, 0);
            var vec = Vector4.Transform(Vector, saturate);
            var res = new RgbaF(Clamp(vec.X), Clamp(vec.Y), Clamp(vec.Z), A);
            return res;
        }

        private static float Clamp(float v)
        {
            if (v < 0)
                return 0;
            if (v > 1)
                return 1;
            return v;
        }

        private static byte ClampByte(float v)
        {
            if (v < 0)
                return 0;
            if (v > 1)
                return 255;
            return (byte)MathF.Round(v * 255);
        }

        public RgbaF WebSepia()
        {
            var res = new RgbaF(
                Clamp(R * 0.393f + G * 0.769f + B * 0.189f),
                Clamp(R * 0.349f + G * 0.686f + B * 0.168f),
                Clamp(R * 0.272f + G * 0.534f + B * 0.131f),
                A);
            return res;
        }

        public RgbaF WebContrast(float amount)
        {
            float intercept = -(0.5f * amount) + 0.5f;

            var res = new RgbaF(
                Clamp(intercept + amount * R),
                Clamp(intercept + amount * G),
                Clamp(intercept + amount * B),
                A);
            return res;
        }

        public RgbaF WebBrightness(float amount)
        {
            var res = new RgbaF(
                Clamp(amount * R),
                Clamp(amount * G),
                Clamp(amount * B),
                A);
            return res;
        }

        public RgbaF WebInvert(float amount)
        {
            var oneMinusAmount = 1.0f - amount;
            var res = new RgbaF(
                1.0f - (oneMinusAmount + R * (amount - oneMinusAmount)),
                1.0f - (oneMinusAmount + G * (amount - oneMinusAmount)),
                1.0f - (oneMinusAmount + B * (amount - oneMinusAmount)),
                A);
            return res;
        }

        public static float SquaredRgbError(RgbaF x, RgbaF y)
        {
            var r = x.R - y.R;
            var g = x.G - y.G;
            var b = x.B - y.B;
            return r * r + g * g + b * b;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is RgbaF other && Equals(other);

        public bool Equals(RgbaF other)
            => Vector == other.Vector;

        public override int GetHashCode()
            => Vector.GetHashCode();

        public static bool operator ==(RgbaF x, RgbaF y)
            => x.Vector == y.Vector;

        public static bool operator !=(RgbaF x, RgbaF y)
            => x.Vector != y.Vector;

        public RgbaF WithA(float a)
            => new RgbaF(new Vector4(R, G, B, a));

        public override string ToString()
        {
            var b = ToRgbaByte();
            return $"#{b.A:X2}{b.R:X2}{b.G:X2}{b.B:X2}";
        }
    }
}
