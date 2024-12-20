// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Diagnostics
{
    public class ThreadStackTraceDumper : IThreadStackTraceDumper
    {
        public string DumpThreads(params IEnumerable<int> managedThreadIds)
        {
            try
            {
                var buf = new StringBuilder();
                var managedThreadIdsSet = managedThreadIds.ToHashSet();
                var pid = Process.GetCurrentProcess().Id;
                using (var dataTarget = DataTarget.AttachToProcess(pid, false))
                {
                    var runtimeInfo = dataTarget.ClrVersions[0];
                    var runtime = runtimeInfo.CreateRuntime();
                    foreach (var thread in runtime.Threads)
                    {
                        if (managedThreadIdsSet.Count != 0 && !managedThreadIdsSet.Contains(thread.ManagedThreadId))
                            continue;
                        buf.AppendLine($"Thread #{thread.ManagedThreadId}, OSThreadId={thread.OSThreadId}, IsAlive={thread.IsAlive}, IsFinalizer={thread.IsFinalizer}, IsGc={thread.IsGc}, LockCount={thread.LockCount}");
                        foreach (var frame in thread.EnumerateStackTrace())
                            buf.AppendLine($"   {frame}");
                    }
                }
                return buf.ToString();
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }
    }
}
