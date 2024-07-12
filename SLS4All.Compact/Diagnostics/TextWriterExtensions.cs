// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.IO;

namespace SLS4All.Compact.Diagnostics
{
    /// <remarks>
    /// Taken from .NET sources to implement custom <see cref="CompactSimpleConsoleFormatter"/>
    /// </remarks>
    internal static class TextWriterExtensions
    {
        public readonly struct ColorScope : IDisposable
        {
            private readonly TextWriter _textWriter;
            private readonly ConsoleColor? _background;
            private readonly ConsoleColor? _foreground;

            public ColorScope(TextWriter textWriter, ConsoleColor? background, ConsoleColor? foreground)
            {
                _textWriter = textWriter;
                _background = background;
                _foreground = foreground;
            }

            public void Dispose()
            {
                // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
                if (_foreground.HasValue)
                {
                    _textWriter.Write(AnsiParser.DefaultForegroundColor); // reset to default foreground color
                }
                if (_background.HasValue)
                {
                    _textWriter.Write(AnsiParser.DefaultBackgroundColor); // reset to the background color
                }
            }
        }

        public static ColorScope CreateColorScope(this TextWriter textWriter, ConsoleColor? background, ConsoleColor? foreground)
        {
            // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
            if (background.HasValue)
            {
                textWriter.Write(AnsiParser.GetBackgroundColorEscapeCode(background.Value));
            }
            if (foreground.HasValue)
            {
                textWriter.Write(AnsiParser.GetForegroundColorEscapeCode(foreground.Value));
            }
            return new ColorScope(textWriter, background, foreground);
        }


        public static void WriteColoredMessage(this TextWriter textWriter, string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
            if (background.HasValue)
            {
                textWriter.Write(AnsiParser.GetBackgroundColorEscapeCode(background.Value));
            }
            if (foreground.HasValue)
            {
                textWriter.Write(AnsiParser.GetForegroundColorEscapeCode(foreground.Value));
            }
            textWriter.Write(message);
            if (foreground.HasValue)
            {
                textWriter.Write(AnsiParser.DefaultForegroundColor); // reset to default foreground color
            }
            if (background.HasValue)
            {
                textWriter.Write(AnsiParser.DefaultBackgroundColor); // reset to the background color
            }
        }
    }
}
