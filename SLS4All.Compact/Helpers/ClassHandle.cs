using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public static class ClassHandle
    {
        private sealed record class HandleWrapper(GCHandle Handle)
        {
            ~HandleWrapper()
            {
                Handle.Free();
            }
        }

        private readonly static ConditionalWeakTable<object, HandleWrapper> s_handles = new();

        public static long GetHandle(object? key)
        {
            if (key == null)
                return 0;
            var handle = s_handles.GetValue(key, CreateHandleWrapper);
            return (nint)handle.Handle;
        }

        private static HandleWrapper CreateHandleWrapper(object key)
        {
            var gcHandle = GCHandle.Alloc(key, GCHandleType.Weak);
            return new HandleWrapper(gcHandle);
        }

        public static T? TryGetTarget<T>(long handle)
            where T : class
        {
            if (handle == 0)
                return null;
            var gcHandle = (GCHandle)(nint)handle;
            return gcHandle.Target as T;
        }
    }
}
