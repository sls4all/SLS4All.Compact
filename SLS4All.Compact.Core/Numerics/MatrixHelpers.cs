// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Numerics
{
    public static class MatrixHelpers
    {
        public static Vector3 MaxPerAxis(Vector3 a, Vector3 b)
            => new Vector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToXY(this Vector3 vec)
            => new Vector2(vec.X, vec.Y);

        /// <summary>
        /// Fixes the transformation matrix, since the voxel buffer swaps axis Y and Z
        /// </summary>
        public static Matrix4x4 FromToMeshTransform(this Matrix4x4 t)
        {
            return new Matrix4x4(
                t.M11, t.M13, t.M12, t.M14,
                t.M31, t.M33, t.M32, t.M34,
                t.M21, t.M23, t.M22, t.M24,
                t.M41, t.M43, t.M42, t.M44
            );
        }

        /// <summary>
        /// Fixes the vector, since the voxel buffer swaps axis Y and Z
        /// </summary>
        public static Vector3 FromToMeshTransform(this Vector3 v)
        {
            return new Vector3(
                v.X,
                v.Z,
                v.Y
            );
        }

        public static Matrix4x4 Round(this Matrix4x4 value, int digits)
            => new Matrix4x4(
                MathF.Round(value.M11, digits), MathF.Round(value.M12, digits), MathF.Round(value.M13, digits), MathF.Round(value.M14, digits),
                MathF.Round(value.M21, digits), MathF.Round(value.M22, digits), MathF.Round(value.M23, digits), MathF.Round(value.M24, digits),
                MathF.Round(value.M31, digits), MathF.Round(value.M32, digits), MathF.Round(value.M33, digits), MathF.Round(value.M34, digits),
                MathF.Round(value.M41, digits), MathF.Round(value.M42, digits), MathF.Round(value.M43, digits), MathF.Round(value.M44, digits));

        private static float RoundZeroOneEpsilon(float value, float epsilon)
        {
            if (Math.Abs(value) < epsilon)
                return 0;
            else if (Math.Abs(1 - value) < epsilon)
                return 1;
            else
                return value;
        }

        public static Matrix4x4 RoundZeroOneEpsilon(this Matrix4x4 value, float epsilon = 0.000_001f)
            => new Matrix4x4(
                RoundZeroOneEpsilon(value.M11, epsilon), RoundZeroOneEpsilon(value.M12, epsilon), RoundZeroOneEpsilon(value.M13, epsilon), RoundZeroOneEpsilon(value.M14, epsilon),
                RoundZeroOneEpsilon(value.M21, epsilon), RoundZeroOneEpsilon(value.M22, epsilon), RoundZeroOneEpsilon(value.M23, epsilon), RoundZeroOneEpsilon(value.M24, epsilon),
                RoundZeroOneEpsilon(value.M31, epsilon), RoundZeroOneEpsilon(value.M32, epsilon), RoundZeroOneEpsilon(value.M33, epsilon), RoundZeroOneEpsilon(value.M34, epsilon),
                RoundZeroOneEpsilon(value.M41, epsilon), RoundZeroOneEpsilon(value.M42, epsilon), RoundZeroOneEpsilon(value.M43, epsilon), RoundZeroOneEpsilon(value.M44, epsilon));

        public static Vector3 Round(this Vector3 value, int digits = 0)
            => new Vector3(MathF.Round(value.X, digits), MathF.Round(value.Y, digits), MathF.Round(value.Z, digits));

        public static Vector3 GetTranslation(Matrix4x4 source)
        {
            return new Vector3(source.M41, source.M42, source.M43);
        }

        public static Vector3 GetScale(Matrix4x4 source)
        {
            var sx = new Vector3(source.M11, source.M21, source.M31).Length();
            var sy = new Vector3(source.M12, source.M22, source.M32).Length();
            var sz = new Vector3(source.M13, source.M23, source.M33).Length();
            return new Vector3(sx, sy, sz);
        }

        public static Matrix4x4 GetRotationMatrix(Matrix4x4 source)
        {
            // NOTE: use double precision transform the matrix for better results
            var sx = Math.Sqrt((double)source.M11 * source.M11 + (double)source.M21 * source.M21 + (double)source.M31 * source.M31);
            var sy = Math.Sqrt((double)source.M12 * source.M12 + (double)source.M22 * source.M22 + (double)source.M32 * source.M32);
            var sz = Math.Sqrt((double)source.M13 * source.M13 + (double)source.M23 * source.M23 + (double)source.M33 * source.M33);
            var rotationMatrix = new Matrix4x4(
                (float)(source.M11 / sx), (float)(source.M12 / sy), (float)(source.M13 / sz), 0,
                (float)(source.M21 / sx), (float)(source.M22 / sy), (float)(source.M23 / sz), 0,
                (float)(source.M31 / sx), (float)(source.M32 / sy), (float)(source.M33 / sz), 0,
                0, 0, 0, 1);
            return rotationMatrix;
        }

        public static Quaternion GetRotationQuaternion(Matrix4x4 source)
            => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(GetRotationMatrix(source)));

        public static (float yaw, float pitch, float roll) GetRotationYawPitchRoll(this Matrix4x4 source)
        {
            // NOTE: use double precision transform the matrix for better results
            var q = GetRotationQuaternion(source);
            var yaw = Math.Atan2(2.0 * ((double)q.Y * q.Z + (double)q.W * q.X), (double)q.W * q.W - (double)q.X * q.X - (double)q.Y * q.Y + (double)q.Z * q.Z);
            var pitch = Math.Asin(-2.0 * ((double)q.X * q.Z - (double)q.W * q.Y));
            var roll = Math.Atan2(2.0 * ((double)q.X * q.Y + (double)q.W * q.Z), (double)q.W * q.W + (double)q.X * q.X - (double)q.Y * q.Y - (double)q.Z * q.Z);
            return ((float)yaw, (float)pitch, (float)roll);
        }

        public static float GetFreedomAroundUp(this Matrix4x4 source)
        {
            var orig = Vector3.UnitY;
            var tran = Vector3.TransformNormal(orig, source);
            return MathF.Acos(Vector3.Dot(orig, tran));
        }
    }
}
