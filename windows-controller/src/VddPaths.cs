using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace DeskMateLearning
{
    // CCD desktop paths, not PnP devices. SETDISPLAYCOUNT on the bundled driver reloads
    // the whole adapter. Never send it for a live session. Never persist a supplied topology.
    internal static class VddPaths
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low,High; }
        [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id,Mode,Status; }
        [StructLayout(LayoutKind.Sequential)] internal struct Target {
            public Luid Adapter; public uint Id,Mode,Technology,Rotation,Scaling,Numerator,Denominator,Scanline,Available,Status;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct PathInfo { public Source Source; public Target Target; public uint Flags; }
        // DISPLAYCONFIG_MODE_INFO including its 48-byte union, copied without interpreting video timing.
        [StructLayout(LayoutKind.Explicit,Size=64)] struct Mode {
            [FieldOffset(0)]public uint Type;[FieldOffset(4)]public uint Id;[FieldOffset(8)]public Luid Adapter;
            [FieldOffset(16)]public ulong A;[FieldOffset(24)]public ulong B;[FieldOffset(32)]public ulong C;
            [FieldOffset(40)]public ulong D;[FieldOffset(48)]public ulong E;[FieldOffset(56)]public ulong F;
            // Only valid for DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE; POINTL follows width, height and pixelFormat.
            [FieldOffset(28)]public int SourceX;[FieldOffset(32)]public int SourceY;
        }
        [StructLayout(LayoutKind.Sequential)] struct Header { public uint Type,Size; public Luid Adapter; public uint Id; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Name {
            public Header Header; public uint Flags,Technology; public ushort Manufacturer,Product; public uint Connector;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Friendly;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string DevicePath;
        }
        [DllImport("user32")] static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
        [DllImport("user32")] static extern int QueryDisplayConfig(uint flags,ref uint n,[Out] PathInfo[] paths,ref uint m,[Out] Mode[] modes,IntPtr topology);
        [DllImport("user32")] static extern int DisplayConfigGetDeviceInfo(ref Name info);
        [DllImport("user32")] static extern int SetDisplayConfig(uint n,[In] PathInfo[] paths,uint m,[In] Mode[] modes,uint flags);
        const uint Active=1,Invalid=UInt32.MaxValue,Supplied=0x20,Validate=0x40,Apply=0x80;
        internal sealed class Endpoint {
            public string Monitor; public PathInfo Path;
            public bool IsActive {get{return (Path.Flags&Active)!=0;}}
        }
        sealed class Snapshot {
            internal readonly Endpoint[] Ends; internal readonly Mode[] Modes;
            internal Snapshot(uint query=1){
                for(int attempt=0;attempt<4;attempt++){
                    uint n,m;int code=GetDisplayConfigBufferSizes(query,out n,out m);if(code!=0)throw new Win32Exception(code);
                    var paths=new PathInfo[n];var modes=new Mode[m];code=QueryDisplayConfig(query,ref n,paths,ref m,modes,IntPtr.Zero);
                    if(code==122)continue;if(code!=0)throw new Win32Exception(code);
                    Modes=modes.Take((int)m).ToArray();Ends=paths.Take((int)n).Select(p=>{
                        var name=new Name{Header=new Header{Type=2,Size=(uint)Marshal.SizeOf(typeof(Name)),Adapter=p.Target.Adapter,Id=p.Target.Id}};
                        return new Endpoint{Path=p,Monitor=p.Target.Available!=0 && DisplayConfigGetDeviceInfo(ref name)==0 ? name.DevicePath : null};
                    }).ToArray();return;
                }
                throw Stale();
            }
        }
        static Exception Stale(){return new InvalidOperationException(UiText.Get("显示路径已变化或不唯一；请刷新或打开 Windows 显示设置。"));}
        static bool Eq(string a,string b){return String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        static bool IsMtt(string id){return id!=null && id.StartsWith(@"\\?\DISPLAY#MTT1337#",StringComparison.OrdinalIgnoreCase);}
        static bool SameSource(PathInfo a,PathInfo b){return a.Source.Adapter.Equals(b.Source.Adapter) && a.Source.Id==b.Source.Id;}
        static bool SameTarget(PathInfo a,PathInfo b){return a.Target.Adapter.Equals(b.Target.Adapter) && a.Target.Id==b.Target.Id;}
        static DesktopDisplay Desktop(DesktopDisplay[] all,string monitor){return all.SingleOrDefault(x=>Eq(x.MonitorId,monitor) && x.Virtual && !x.Primary);}
        internal static PathInfo[] Plan(Endpoint[] ends,DesktopDisplay[] all,string monitor,bool attach){
            if(!IsMtt(monitor) || !all.Any(x=>x.Primary && !x.Virtual))throw Stale();
            var active=ends.Where(e=>e.IsActive).ToArray();var candidates=ends.Where(e=>Eq(monitor,e.Monitor) && e.Path.Target.Available!=0).ToArray();
            if(active.Any(e=>String.IsNullOrEmpty(e.Monitor)))throw Stale();
            if(candidates.Length==0 || candidates.Any(c=>!SameTarget(c.Path,candidates[0].Path)))throw Stale();
            var current=active.Where(e=>Eq(e.Monitor,monitor)).ToArray();
            if(attach){
                if(current.Length!=0 || all.Any(d=>Eq(d.MonitorId,monitor)))throw Stale();
                var free=candidates.FirstOrDefault(e=>!active.Any(a=>SameSource(a.Path,e.Path)));if(free==null)throw Stale();
                var p=free.Path;p.Flags=Active;p.Source.Mode=Invalid;p.Target.Mode=Invalid;
                return active.Select(e=>e.Path).Concat(new[]{p}).ToArray();
            }
            if(current.Length!=1 || Desktop(all,monitor)==null || active.Count(a=>SameSource(a.Path,current[0].Path))!=1 || active.Length<2)throw Stale();
            return active.Where(e=>e!=current[0]).Select(e=>e.Path).ToArray();
        }
        internal static void AddChoices(List<DisplayInfo> choices){
            var snap=new Snapshot();var all=WindowsDisplays.Capture();
            foreach(var group in snap.Ends.Where(e=>IsMtt(e.Monitor)).GroupBy(e=>e.Monitor,StringComparer.OrdinalIgnoreCase)){
                bool active=group.Any(e=>e.IsActive);
                try{Plan(snap.Ends,all,group.Key,!active);}catch(InvalidOperationException){continue;}
                uint slot=group.First().Path.Target.Id;
                if(active){var screen=Desktop(all,group.Key);var item=choices.SingleOrDefault(d=>d.Virtual && !d.Primary && ScreenSessions.Identity(d)==screen.Name);
                    if(item!=null){item.MonitorPath=group.Key;item.Slot=slot;}}
                else choices.Add(new DisplayInfo{Virtual=true,Index=-1,Dormant=true,MonitorPath=group.Key,Slot=slot});
            }
        }
        // Explicit read-only diagnostic: ask Windows to validate the proposed one-slot delta, never apply it.
        internal static int Check(string report){
            var lines=new List<string>();int failed=0;
            try{var snap=new Snapshot();var all=WindowsDisplays.Capture();
                foreach(var g in snap.Ends.Where(e=>IsMtt(e.Monitor)).GroupBy(e=>e.Monitor,StringComparer.OrdinalIgnoreCase)){
                    bool attach=!g.Any(e=>e.IsActive);
                    try{Submit(Plan(snap.Ends,all,g.Key,attach),snap.Modes,Validate);lines.Add("OK SDC_VALIDATE "+(attach?"attach":"detach")+" slot "+g.First().Path.Target.Id);}
                    catch(Exception e){lines.Add("Unavailable: "+e.Message);failed++;}
                }
            }catch(Exception e){lines.Add(e.Message);failed++;}
            if(lines.Count==0)lines.Add("No MttVDD targets found.");
            System.IO.File.WriteAllLines(report,lines);return failed==0?0:1;
        }
        internal static void Change(string monitor,bool attach){
            var snap=new Snapshot();var plan=Plan(snap.Ends,WindowsDisplays.Capture(),monitor,attach);
            Submit(plan,snap.Modes,Validate);
            // A physical hotplug, clone or mode switch during validation invalidates the plan.
            var check=new Snapshot();var checkedPlan=Plan(check.Ends,WindowsDisplays.Capture(),monitor,attach);
            if(!plan.SequenceEqual(checkedPlan) || !snap.Modes.SequenceEqual(check.Modes))throw Stale();
            Submit(checkedPlan,check.Modes,Apply);
        }
        // Edit just POINTL in an active source mode. No topology selection, adapter reload,
        // ALLOW_CHANGES or SAVE_TO_DATABASE (sessions may be using a temporary desktop/rate).
        internal static void Position(DesktopDisplay[] from,DesktopDisplay[] to,string monitor){
            var dest=LayoutPolicy.LiveMove(from,to,monitor);var old=from.Single(d=>d.Key==dest.Key);
            var snap=new Snapshot(2);var paths=snap.Ends.Select(e=>e.Path).ToArray();
            var matches=snap.Ends.Where(e=>e.IsActive && Eq(e.Monitor,monitor)).ToArray();
            if(!IsMtt(monitor) || matches.Length!=1 || snap.Ends.Any(e=>!e.IsActive || String.IsNullOrEmpty(e.Monitor)))throw Stale();
            var p=matches[0].Path;uint i=p.Source.Mode;
            if(paths.Count(q=>SameSource(p,q))!=1 || i>=snap.Modes.Length)throw Stale();
            var modes=(Mode[])snap.Modes.Clone();var m=modes[i];
            if(m.Type!=1 || m.Id!=p.Source.Id || !m.Adapter.Equals(p.Source.Adapter) || m.SourceX!=old.X || m.SourceY!=old.Y)throw Stale();
            modes[i].SourceX=dest.X;modes[i].SourceY=dest.Y;
            Submit(paths,modes,Validate);
            var check=new Snapshot(2);
            if(!paths.SequenceEqual(check.Ends.Select(e=>e.Path)) || !snap.Modes.SequenceEqual(check.Modes) ||
               !snap.Ends.Select(e=>e.Monitor).SequenceEqual(check.Ends.Select(e=>e.Monitor)) || !LayoutPolicy.Same(WindowsDisplays.Capture(),from,true))throw Stale();
            Submit(paths,modes,Apply);
        }
        static void Submit(PathInfo[] plan,Mode[] modes,uint action){
            int code=SetDisplayConfig((uint)plan.Length,plan,(uint)modes.Length,modes,Supplied|action);
            if(code!=0)throw new Win32Exception(code,UiText.Get("Windows 拒绝更改所选虚拟屏：")+code);
        }
        internal static void VerifyOwner(DesktopDisplay target){
            var d=Desktop(WindowsDisplays.Capture(),target.MonitorId);
            if(d==null || d.Key!=target.Key || d.AdapterId!=target.AdapterId)throw Stale();
        }
        internal static bool IsDormant(string monitor){
            var active=new Snapshot().Ends.Where(e=>e.IsActive).ToArray();
            if(active.Any(e=>String.IsNullOrEmpty(e.Monitor)))throw Stale();
            return !active.Any(e=>Eq(e.Monitor,monitor));
        }
    }
}
