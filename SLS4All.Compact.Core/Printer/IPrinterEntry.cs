// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using SLS4All.Compact.Diagnostics;
using System.Text.Json;

namespace SLS4All.Compact.Printer
{
    public interface IPrinterEntry
    {
        /// <summary>
        /// Index of the entry from the start of the client
        /// </summary>
        long Index { get; }
        /// <summary>
        /// Local date/time the entry was created
        /// </summary>
        SystemTimestamp Timestamp { get; }
        /// <summary>
        /// Any associated command, error or information message (e.g. "!!! error") without the confirmation message (e.g. "ok")
        /// </summary>
        string Message { get; }
        /// <summary>
        /// The command, error, informational or confirmation message (e.g. "ok")
        /// </summary>
        string Text { get; }
        /// <summary>
        /// Type of the entry
        /// </summary>
        PrinterResult Type { get; }
        /// <summary>
        /// Gets whether should the item show in terminal/history
        /// </summary>
        bool Hidden { get; }
    }

    public enum PrinterResult
    {
        NotSet = 0,
        Command,
        OKResponse,
        Disconnected,
        Info,
        Error,
        Data,
    }

    public readonly record struct PrinterResponse(long Index, SystemTimestamp Timestamp, string Message, string Text, PrinterResult Type, bool Hidden, long CommandIndex) : IPrinterEntry
    {
        private readonly static char[] _newlineSeparators = new[] { '\r', '\n' };

        public string LastMessage
        {
            get
            {
                var trimmed = Message.Trim();
                var index = trimmed.LastIndexOfAny(_newlineSeparators);
                if (index == -1)
                    return trimmed;
                else
                    return trimmed.Substring(index + 1);
            }
        }

        public bool IsFail => Type is PrinterResult.Error or PrinterResult.Disconnected;
    }
    public readonly record struct PrinterCommand(long Index, SystemTimestamp Timestamp, in CodeCommand Command, PrinterResult Type, bool Hidden, in PrinterResponse Response, TaskCompletionSource<PrinterResponse>? ResponseSource) : IPrinterEntry
    {
        public string Message => Command.ToString();
        public string Text => Command.ToString();
    }
    public readonly record struct PrinterLog(long Index, SystemTimestamp Timestamp, string Message, string Text, PrinterResult Type, bool Hidden) : IPrinterEntry;
}
