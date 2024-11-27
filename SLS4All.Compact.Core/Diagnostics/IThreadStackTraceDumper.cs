
namespace SLS4All.Compact.Diagnostics
{
    public interface IThreadStackTraceDumper
    {
        string DumpThreads(params IEnumerable<int> managedThreadIds);
    }
}