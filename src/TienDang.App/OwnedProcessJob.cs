using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TienDang.App;

// One private job per owned process. Descendants join automatically. Closing
// the non-inheritable handle terminates this job, never unrelated user processes.
internal sealed class OwnedProcessJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        internal long PerProcessTime, PerJobTime;
        internal uint Flags;
        internal nuint MinWorkingSet, MaxWorkingSet;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int cls, ref ExtendedLimits info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    private readonly SafeFileHandle _job;
    private OwnedProcessJob(SafeFileHandle job) => _job = job;
    internal static OwnedProcessJob Attach(Process process)
    {
        var job = CreateJobObject(0, null);
        try
        {
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) ||
                !AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(job);
        }
        catch { job.Dispose(); throw; }
    }
    public void Dispose() => _job.Dispose();
}