// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿
using System.Text.Json;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Nesting;

namespace SLS4All.Compact.Scripts
{
    public static class JSConverter
    {
        private static readonly JsonSerializerOptions s_serializerOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static object? ToTS(object? arg)
        {
            if (arg == null)
                return null;
            else if (arg is IJSProxy proxy)
                return proxy.Reference;
            else if (arg is IEnumerable<IJSProxy> proxyEnumerable)
                return proxyEnumerable.Select(x => x.Reference).ToArray();
            else if (arg is System.Numerics.Vector2 vec2)
                return new { _type = "Vector2", x = vec2.X, y = vec2.Y };
            else if (arg is System.Numerics.Vector3 vec3)
                return new { _type = "Vector3", x = vec3.X, y = vec3.Y, z = vec3.Z };
            else if (arg is System.Numerics.Quaternion q)
                return new { _type = "Quaternion", x = q.X, y = q.Y, z = q.Z, w = q.W };
            else if (arg is RgbaF rgbaf)
                return new { _type = "Color4", r = rgbaf.R, g = rgbaf.G, b = rgbaf.B, a = rgbaf.A };
            else if (arg is IEnumerable<RgbaF> c4e)
                return new { _type = "Array", items = c4e.Select(x => ToTS(x)).ToArray() };
            else if (arg is NestingTransformState ts)
                return new { _type = "TransformState", position = ToTS(ts.Position), rotation = ToTS(ts.Rotation), quaternion = ToTS(ts.Quaternion), scale = ToTS(ts.Scale) };
            else if (arg is IEnumerable<NestingTransformState> tse)
                return new { _type = "Array", items = tse.Select(x => ToTS(x)).ToArray() };
            else
                return arg;
        }

        public static T ToNet<T>(JsonDocument? doc)
        {
            if (doc == null)
                return default!;
            else
                return JsonSerializer.Deserialize<T>(doc, s_serializerOptions)!;
        }
    }
}
