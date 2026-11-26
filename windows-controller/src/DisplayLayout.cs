using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DeskMateLearning
{
    public sealed class DesktopDisplay
    {
        public string Name,MonitorId,AdapterId,Title;
        public bool Primary,Virtual;
        public int[] AvailableRates; // At the active VDD size; informational, never used for layout writes.
        public int X,Y,Width,Height,Orientation,RefreshHz,BitsPerPixel;
        public DesktopDisplay Copy(){return (DesktopDisplay)MemberwiseClone();}
        public string Key {get{return Name+"|"+MonitorId;}}
        public string DisplayTitle {get{return Virtual ? UiText.Get("手机虚拟屏 · VDD") : Title;}}
        public string Label {get{return DisplayTitle+" · "+Width+"×"+Height+" @ "+RefreshHz+"Hz";}}
    }
    public static class LayoutPolicy
    {
        public static DesktopDisplay[] Clone(IEnumerable<DesktopDisplay> list){return list.Select(d=>d.Copy()).ToArray();}
        public static bool Same(IEnumerable<DesktopDisplay> a,IEnumerable<DesktopDisplay> b,bool positions)
        {
            var x=a.OrderBy(d=>d.Key).ToArray();var y=b.OrderBy(d=>d.Key).ToArray();
            if(x.Length!=y.Length)return false;
            for(int i=0;i<x.Length;i++)if(x[i].Key!=y[i].Key || x[i].Primary!=y[i].Primary || x[i].Virtual!=y[i].Virtual ||
                x[i].Width!=y[i].Width || x[i].Height!=y[i].Height || x[i].Orientation!=y[i].Orientation ||
                x[i].RefreshHz!=y[i].RefreshHz || x[i].BitsPerPixel!=y[i].BitsPerPixel ||
                (positions && (x[i].X!=y[i].X || x[i].Y!=y[i].Y)))return false;
            return true;
        }
        public static bool Overlap(DesktopDisplay a,DesktopDisplay b)
        {return (long)a.X<b.X+b.Width && (long)b.X<a.X+a.Width && (long)a.Y<b.Y+b.Height && (long)b.Y<a.Y+a.Height;}
        private static bool Touch(DesktopDisplay a,DesktopDisplay b)
        {return ((long)a.X+a.Width==b.X || (long)b.X+b.Width==a.X) && Math.Max(a.Y,b.Y)<Math.Min((long)a.Y+a.Height,(long)b.Y+b.Height) ||
                ((long)a.Y+a.Height==b.Y || (long)b.Y+b.Height==a.Y) && Math.Max(a.X,b.X)<Math.Min((long)a.X+a.Width,(long)b.X+b.Width);}
        public static void Validate(DesktopDisplay[] original,DesktopDisplay[] draft)
        {
            if(original==null || draft==null || original.Length==0 || original.Length>16 || !Same(original,draft,false))throw new InvalidOperationException(UiText.Get("显示器身份或模式发生变化，请重新读取Windows布局。"));
            if(draft.Select(d=>d.Key).Distinct().Count()!=draft.Length || draft.Count(d=>d.Primary)!=1)throw new InvalidOperationException(UiText.Get("主屏或显示器身份不唯一。"));
            foreach(var d in draft){if(d.Width<=0 || d.Height<=0 || d.Width>32768 || d.Height>32768 || Math.Abs((long)d.X)>30000 || Math.Abs((long)d.Y)>30000)throw new InvalidOperationException(UiText.Get("布局超出允许范围。"));
                var old=original.Single(x=>x.Key==d.Key);if(d.Primary && (d.X!=old.X || d.Y!=old.Y))throw new InvalidOperationException(UiText.Get("主屏锚定原位；只能拖动其他屏幕。"));}
            for(int i=0;i<draft.Length;i++)for(int j=i+1;j<draft.Length;j++)if(Overlap(draft[i],draft[j]))throw new InvalidOperationException(UiText.Get("屏幕不能重叠；复制桌面请在Windows设置中配置。"));
            var visited=new HashSet<string>();visited.Add(draft.Single(d=>d.Primary).Key);
            bool progress=true;while(progress){progress=false;foreach(var d in draft)if(!visited.Contains(d.Key) && draft.Any(x=>visited.Contains(x.Key) && Touch(d,x))){visited.Add(d.Key);progress=true;}}
            if(visited.Count!=draft.Length)throw new InvalidOperationException(UiText.Get("屏幕之间必须有边缘相接，请拖动吸附或使用快捷方向。"));
        }
        public static DesktopDisplay[] Place(DesktopDisplay[] draft,string key,string direction)
        {
            var result=Clone(draft);var d=result.Single(x=>x.Key==key);var p=result.Single(x=>x.Primary);
            if(d.Primary)throw new InvalidOperationException(UiText.Get("不能快捷移动主屏。"));
            if(direction=="left"){d.X=p.X-d.Width;d.Y=p.Y;}
            else if(direction=="right"){d.X=p.X+p.Width;d.Y=p.Y;}
            else if(direction=="top"){d.X=p.X;d.Y=p.Y-d.Height;}
            else if(direction=="bottom"){d.X=p.X;d.Y=p.Y+p.Height;}
            else throw new InvalidOperationException(UiText.Get("未知摆放方向。"));
            return result;
        }
        public static void Snap(DesktopDisplay moving,DesktopDisplay[] all,int threshold)
        {
            int bestX=threshold+1,bestY=threshold+1,dx=0,dy=0;
            foreach(var b in all.Where(x=>x.Key!=moving.Key)){
                foreach(int x in new[]{b.X,b.X+b.Width,b.X-moving.Width,b.X+b.Width-moving.Width}){
                    long delta=(long)x-moving.X;if(Math.Abs(delta)<bestX){bestX=(int)Math.Abs(delta);dx=(int)delta;}}
                foreach(int y in new[]{b.Y,b.Y+b.Height,b.Y-moving.Height,b.Y+b.Height-moving.Height}){
                    long delta=(long)y-moving.Y;if(Math.Abs(delta)<bestY){bestY=(int)Math.Abs(delta);dy=(int)delta;}}
            }
            if(bestX<=threshold)moving.X+=dx;if(bestY<=threshold)moving.Y+=dy;
        }
    }
    public static class WindowsDisplays
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Device
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Mode
        {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DeviceName;
            public ushort SpecVersion,DriverVersion,Size,Extra;
            public uint Fields;
            public int X,Y;
            public uint Orientation,FixedOutput;
            public short Color,Duplex,YResolution,TTOption,Collate;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string FormName;
            public ushort LogPixels;
            public uint BitsPerPel,Width,Height,Flags,Frequency,ICMMethod,ICMIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplayDevices(string name,uint index,ref Device device,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplaySettingsEx(string name,int index,ref Mode mode,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int ChangeDisplaySettingsEx(string name,ref Mode mode,IntPtr hwnd,uint flags,IntPtr param);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="ChangeDisplaySettingsExW")]private static extern int CommitDisplaySettings(string name,IntPtr mode,IntPtr hwnd,uint flags,IntPtr param);
        private static Mode Current(string name){var m=new Mode();m.Size=(ushort)Marshal.SizeOf(typeof(Mode));if(m.Size!=220 || !EnumDisplaySettingsEx(name,-1,ref m,0))throw new InvalidOperationException(UiText.Get("无法读取原生显示模式：")+name);return m;}
        private static int[] RatesAtSize(string name,Mode current)
        {
            var rates=new SortedSet<int>();
            for(int index=0;index<2048;index++){
                var mode=new Mode();mode.Size=current.Size;
                if(!EnumDisplaySettingsEx(name,index,ref mode,0))break;
                if(mode.Width==current.Width && mode.Height==current.Height && mode.Orientation==current.Orientation &&
                   mode.BitsPerPel==current.BitsPerPel && mode.Frequency>1)rates.Add((int)mode.Frequency);
            }
            return rates.ToArray();
        }
        private static string Friendly(string id,string fallback)
        {
            var match=Regex.Match(id ?? "",@"DISPLAY#([^#]+)#([^#]+)#");if(!match.Success)return fallback;
            try{using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\"+match.Groups[1].Value+@"\"+match.Groups[2].Value+@"\Device Parameters")){
                var bytes=key==null ? null : key.GetValue("EDID") as byte[];
                if(bytes!=null && bytes.Length>=128)for(int pos=54;pos<=108;pos+=18)if(bytes[pos]==0 && bytes[pos+1]==0 && bytes[pos+3]==0xfc){string name=System.Text.Encoding.ASCII.GetString(bytes,pos+5,13).Trim('\0','\r','\n',' ');if(name.Length>0)return name;}
            }}catch(Exception){}
            return match.Groups[1].Value;
        }
        public static DesktopDisplay[] Capture()
        {
            var result=new List<DesktopDisplay>();
            for(uint i=0;i<64;i++){
                var a=new Device();a.cb=Marshal.SizeOf(typeof(Device));if(!EnumDisplayDevices(null,i,ref a,0))break;
                if((a.Flags&1)==0 || (a.Flags&8)!=0)continue;
                var m=Current(a.Name);var monitor=new Device();monitor.cb=Marshal.SizeOf(typeof(Device));
                if(!EnumDisplayDevices(a.Name,0,ref monitor,1))throw new InvalidOperationException(UiText.Get("无法识别显示器：")+a.Name);
                bool vdd=(monitor.Id ?? "").IndexOf("MTT1337",StringComparison.OrdinalIgnoreCase)>=0 || (a.Id ?? "").IndexOf("mttvdd",StringComparison.OrdinalIgnoreCase)>=0;
                result.Add(new DesktopDisplay{Name=a.Name,MonitorId=monitor.Id,AdapterId=a.Id,AvailableRates=vdd ? RatesAtSize(a.Name,m) : null,Title=vdd ? "VDD" : Friendly(monitor.Id,monitor.Description),Primary=(a.Flags&4)!=0,Virtual=vdd,
                    X=m.X,Y=m.Y,Width=(int)m.Width,Height=(int)m.Height,Orientation=(int)m.Orientation,RefreshHz=(int)m.Frequency,BitsPerPixel=(int)m.BitsPerPel});
            }
            if(result.Count==0)throw new InvalidOperationException(UiText.Get("未读到活动桌面。请在本地Windows交互会话运行。"));return result.ToArray();
        }
        // Called only by the watchdog helper after an explicit Apply action. All original settings
        // are journalled first; no primary/mode/adapter enable state is changed here.
        internal static void StageAndCommit(DesktopDisplay[] targets)
        {
            foreach(var d in targets){var m=Current(d.Name);m.X=d.X;m.Y=d.Y;m.Fields=0x20; // DM_POSITION only
                int code=ChangeDisplaySettingsEx(d.Name,ref m,IntPtr.Zero,0x10000001,IntPtr.Zero); // UPDATEREGISTRY | NORESET
                if(code!=0)throw new InvalidOperationException(UiText.Get("布局准备失败：")+d.Name+" / code="+code);}
            int applied=CommitDisplaySettings(null,IntPtr.Zero,IntPtr.Zero,0,IntPtr.Zero);
            if(applied!=0)throw new InvalidOperationException(UiText.Get("应用Windows布局失败 / code=")+applied);
        }
    }
}
