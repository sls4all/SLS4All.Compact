
namespace SLS4All.Compact.Diagnostics
{
    public sealed class NullThreadStackTraceDumper : IThreadStackTraceDumper
    {
        public static NullThreadStackTraceDumper Instance { get; } = new();

        public string DumpThreads(params IEnumerable<int> managedThreadIds) => "<null>";
    }
}