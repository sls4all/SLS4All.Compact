// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.McuClient;

namespace SLS4All.Compact.Printer
{
    public sealed class McuInitializeCommandContext : IPrinterClientCommandContext
    {
        public McuManager Manager { get; }

        public McuInitializeCommandContext(McuManager manager)
            => Manager = manager;

        public static McuManager GetManagerEvenInShutdown(McuPrinterClient printerClient, IPrinterClientCommandContext? context)
        {
            if (context is PrinterShutdownCommandContext)
                return printerClient.ManagerEvenInShutdown;
            else if (context is McuInitializeCommandContext initialize)
                return initialize.Manager;
            else
                return printerClient.ManagerEvenInShutdown;
        }

        public static McuManager GetManager(McuPrinterClient printerClient, IPrinterClientCommandContext? context)
        {
            if (context is PrinterShutdownCommandContext)
                return printerClient.ManagerEvenInShutdown;
            else if (context is McuInitializeCommandContext initialize)
                return initialize.Manager;
            else
                return printerClient.Manager;
        }

        public static McuManager? GetManagerIfReady(McuPrinterClient printerClient, IPrinterClientCommandContext? context)
        {
            if (context is PrinterShutdownCommandContext)
                return printerClient.ManagerEvenInShutdown;
            else if (context is McuInitializeCommandContext initialize)
                return initialize.Manager;
            else
                return printerClient.ManagerIfReady;
        }
    }
}
