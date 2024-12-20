using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.VideoRecorder
{
    public static class SkiaHelpers
    {
        public static void DrawWrapLines(string text, float width, SKCanvas canvas, SKPaint paint, float x, float y)
        {
            var wrappedLines = new List<string>();
            var lineLength = 0f;
            var line = "";
            foreach (var word in text.Split(','))
            {
                var wordWithSpace = word + ",";
                var wordWithSpaceLength = paint.MeasureText(wordWithSpace);
                if (lineLength + wordWithSpaceLength > width)
                {
                    wrappedLines.Add(line);
                    line = wordWithSpace;
                    lineLength = wordWithSpaceLength;
                }
                else
                {
                    line += wordWithSpace;
                    lineLength += wordWithSpaceLength;
                }
            }
            if (line.Length != 0)
                wrappedLines.Add(line);
            foreach (var wrappedLine in wrappedLines)
            {
                canvas.DrawText(wrappedLine, x, y, paint);
                y += paint.FontSpacing;
            }
        }

        public static SKBitmap RotateBitmap(SKBitmap original, SKEncodedOrigin origin, bool disposeOriginal)
        {
            SKBitmap? target = null;
            try
            {
                switch (origin)
                {
                    default:
                    case SKEncodedOrigin.TopLeft: // Default
                        return original;
                    case SKEncodedOrigin.TopRight: // Reflected across y-axis.
                        {
                            target = new SKBitmap(original.Width, original.Height, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.Scale(1, -1, original.Width / 2.0f, original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                    case SKEncodedOrigin.BottomRight: // Rotated 180deg
                        {
                            // rotate 180deg
                            target = new SKBitmap(original.Width, original.Height, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.RotateDegrees(180, original.Width / 2.0f, original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                    case SKEncodedOrigin.BottomLeft: // Reflected across x-axis.
                        {
                            // flip along the y-axis
                            target = new SKBitmap(original.Width, original.Height, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.Scale(-1, 1, original.Width / 2.0f, original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                    case SKEncodedOrigin.LeftTop: // Reflected across x-axis. Rotated 90° counter-clockwise.
                        {
                            target = new SKBitmap(original.Height, original.Width, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.Translate(original.Height / 2.0f, original.Width / 2.0f);
                                canvas.Scale(-1, 1);
                                canvas.RotateDegrees(-90);
                                canvas.Translate(-original.Width / 2.0f, -original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                    case SKEncodedOrigin.RightTop: // Rotated 90deg clockwise
                        {
                            target = new SKBitmap(original.Height, original.Width, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.Translate(original.Height / 2.0f, original.Width / 2.0f);
                                canvas.RotateDegrees(90);
                                canvas.Translate(-original.Width / 2.0f, -original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                    case SKEncodedOrigin.RightBottom: // Reflected across x-axis. Rotated 90° clockwise.
                        {
                            target = new SKBitmap(original.Height, original.Width, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.Translate(original.Height / 2.0f, original.Width / 2.0f);
                                canvas.Scale(-1, 1);
                                canvas.RotateDegrees(90);
                                canvas.Translate(-original.Width / 2.0f, -original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                    case SKEncodedOrigin.LeftBottom: // Rotated 90deg counter-clockwise.
                        {
                            target = new SKBitmap(original.Height, original.Width, original.ColorType, original.AlphaType);
                            using (var canvas = new SKCanvas(target))
                            {
                                canvas.Translate(original.Height / 2.0f, original.Width / 2.0f);
                                canvas.RotateDegrees(-90);
                                canvas.Translate(-original.Width / 2.0f, -original.Height / 2.0f);
                                canvas.DrawBitmap(original, 0, 0);
                            }
                            if (disposeOriginal)
                                original.Dispose();
                            return target;
                        }
                }
            }
            catch
            {
                target?.Dispose();
                throw;
            }
        }

        public unsafe static SKBitmap? TryDecodeBitmap(ReadOnlySpan<byte> buffer, bool applyExifOrientation, SKEncodedOrigin? explicitOrigin = null)
        {
            fixed (byte* ptr = buffer)
            {
                using SKData data = SKData.Create((IntPtr)ptr, buffer.Length);
                using SKCodec codec = SKCodec.Create(data);
                if (codec == null)
                    return null;
                else if (!applyExifOrientation && explicitOrigin == null)
                    return SKBitmap.Decode(codec);
                else
                {
                    var original = SKBitmap.Decode(codec);
                    try
                    {
                        return RotateBitmap(original, explicitOrigin ?? codec.EncodedOrigin, true);
                    }
                    catch
                    {
                        original.Dispose();
                        throw;
                    }
                }
            }
        }

        public unsafe static SKBitmap? TryDecodeBitmap(Stream stream, bool applyExifOrientation, SKEncodedOrigin? explicitOrigin = null)
        {
            using SKCodec codec = SKCodec.Create(stream);
            if (codec == null)
                return null;
            else if (!applyExifOrientation && explicitOrigin == null)
                return SKBitmap.Decode(codec);
            else
            {
                var original = SKBitmap.Decode(codec);
                try
                {
                    return RotateBitmap(original, explicitOrigin ?? codec.EncodedOrigin, true);
                }
                catch
                {
                    original.Dispose();
                    throw;
                }
            }
        }
    }
}
