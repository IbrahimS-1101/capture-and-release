using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameplayRecorder;
// Ensure the screen-capture subprocess cannot keep recording if the app crashes.
internal sealed class CaptureJob : IDisposable
{
    readonly SafeFileHandle handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic {
        public long ProcessTime,JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet,MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass,SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] struct Extended {
        public Basic Basic;public IoCounters Io;
        public nuint ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(SafeFileHandle job,int infoClass,ref Extended info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(SafeFileHandle job,nint process);
    internal CaptureJob(Process process)
    {
        handle=CreateJobObject(0,null);
        var info=new Extended{Basic=new Basic{Flags=0x2000}};
        if(handle.IsInvalid || !SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf<Extended>()) || !AssignProcessToJobObject(handle,process.Handle)) {
            int error=Marshal.GetLastWin32Error();handle.Dispose();throw new System.ComponentModel.Win32Exception(error,"Could not set up capture-process cleanup");
        }
    }
    public void Dispose()=>handle.Dispose();
}
