using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeskMateLearning
{
    // An unnamed job contains only the engine we just created. It is not a system-wide process
    // group. If this controller exits unexpectedly, Windows closes its handle and ends that engine.
    internal sealed class OwnedEngineJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        {
            public long ProcessUserTime,JobUserTime;
            public uint Flags;
            public UIntPtr MinimumWorkingSet,MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass,SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters
        {
            public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job,int infoClass,ref ExtendedLimits info,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll")]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
        private IntPtr handle;
        public OwnedEngineJob()
        {
            handle=CreateJobObject(IntPtr.Zero,null);
            if(handle==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create owned engine job.");
            var info=new ExtendedLimits();info.Basic.Flags=0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if(!SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf(typeof(ExtendedLimits))))
            {
                int error=Marshal.GetLastWin32Error();Dispose();
                throw new Win32Exception(error,"Cannot configure owned engine job.");
            }
        }
        public void Attach(Process engine)
        {
            if(handle==IntPtr.Zero) throw new ObjectDisposedException("OwnedEngineJob");
            if(!AssignProcessToJobObject(handle,engine.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot attach own engine to cleanup job; refusing unowned continuous session.");
        }
        public void Dispose()
        {
            IntPtr old=Interlocked.Exchange(ref handle,IntPtr.Zero);
            if(old!=IntPtr.Zero) CloseHandle(old);
            GC.SuppressFinalize(this);
        }
        ~OwnedEngineJob() {Dispose();}
    }
}
