// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Diagnostics
{
    public static class DebuggerHelpers
    {
        public static void WaitForDebugger()
        {
            Console.Write("Waiting for debugger...");
            while (!Debugger.IsAttached)
            {
                try
                {
                    if (Console.KeyAvailable)
                        break;
                }
                catch
                {
                    // swallow
                }
                Console.Write(".");
                Thread.Sleep(500);
            }
            Console.WriteLine();
        }
    }
}
