// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public enum PrinterClientRestartFlags
    {
        NotSet = 0,
        Restart,
        FirmwareRestart,
    }

    public interface IPrinterClientCommandContext
    {
        bool HasDelay => false;
        Task Delay(TimeSpan duration, CancellationToken cancel = default)
            => throw new NotSupportedException($"Delay not supported in this context: {this}");
    }

    public delegate void PrinterClientEntryEvent<T>(IPrinterClient client, bool allowSynchronous, in T entry)
        where T : struct, IPrinterEntry;

    public interface IPrinterClient
    {
        bool IsConnected { get; }
        long ConnectionIndex { get; }
        bool IsShutdown { get; }
        string? ShutdownReason { get; }
        bool HasLostCommunication { get; }
        AsyncEvent ConnectedEvent { get; }
        AsyncEvent<string> FirmwareShutdownEvent { get; }
        bool ShouldSendSafeCheckpoints { get; }

        event PrinterClientEntryEvent<PrinterCommand>? CommandEvent;
        event PrinterClientEntryEvent<PrinterResponse>? ResponseEvent;
        event PrinterClientEntryEvent<PrinterLog>? LogEvent;

        Task WaitForConnection(CancellationToken cancel = default);
        Task Restart(PrinterClientRestartFlags type, CancellationToken cancel = default);
        Task<PrinterResponse> Send(CodeCommand cmd, bool hidden, bool throwOnError = true, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        Task Stream(PrinterStream script, bool hidden, IPrinterClientCommandContext? context = default, CancellationToken cancel = default);
        void Shutdown(string reason, Exception? ex, IPrinterClientCommandContext? context = default);
        (string Key, string Message)[] GetConnectionStatus();
        Task EnterPrintingMode(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        Task ExitPrintingMode(IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
    }
}
