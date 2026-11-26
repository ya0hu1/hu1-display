using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskMateLearning
{
    // Window placement only: no capture, window titles, process termination, display writes or activation.
    internal static class WindowHandoff
    {
        [StructLayout(LayoutKind.Sequential)] struct Rect {public int L,T,R,B; public Rectangle Box {get{return Rectangle.FromLTRB(L,T,R,B);}} public static Rect From(Rectangle r){return new Rect{L=r.Left,T=r.Top,R=r.Right,B=r.Bottom};}}
        [StructLayout(LayoutKind.Sequential)] struct Placement {public int size,flags,show;public Point min,max;public Rect normal;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Info {public int size;public Rect bounds,work;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;}
        delegate bool Visitor(IntPtr w,IntPtr p);
        [DllImport("user32")]static extern bool EnumWindows(Visitor v,IntPtr p);
        [DllImport("user32")]static extern bool IsWindowVisible(IntPtr w);
        [DllImport("user32")]static extern IntPtr GetShellWindow();
        [DllImport("user32",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr w,StringBuilder name,int n);
        [DllImport("user32")]static extern int GetWindowLong(IntPtr w,int index);
        [DllImport("user32")]static extern IntPtr GetWindow(IntPtr w,uint cmd);
        [DllImport("user32")]static extern IntPtr MonitorFromWindow(IntPtr w,uint flags);
        [DllImport("user32")]static extern IntPtr MonitorFromPoint(Point p,uint flags);
        [DllImport("user32",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr mon,ref Info i);
        [DllImport("user32")]static extern bool GetWindowPlacement(IntPtr w,ref Placement p);
        [DllImport("user32")]static extern bool SetWindowPlacement(IntPtr w,ref Placement p);
        [DllImport("user32")]static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("dwmapi")]static extern int DwmGetWindowAttribute(IntPtr w,int attr,out int value,int size);
        [DllImport("user32")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        static Info MonitorInfo(IntPtr m){var i=new Info{size=Marshal.SizeOf(typeof(Info))};if(m==IntPtr.Zero || !GetMonitorInfo(m,ref i))throw new InvalidOperationException("Monitor unavailable.");return i;}
        internal static DesktopDisplay FindTarget(IEnumerable<DesktopDisplay> all,DisplayInfo choice,DisplayMode mode){return mode!=DisplayMode.Extend || choice==null || !choice.Virtual || choice.Primary ? null : all.SingleOrDefault(d=>d.Virtual && !d.Primary && choice.Description!=null && choice.Description.StartsWith(d.Name+" ",StringComparison.OrdinalIgnoreCase));}
        internal static Rectangle Fit(Rectangle r, Rectangle from, Rectangle to){
            if(from.Width<1 || from.Height<1 || to.Width<1 || to.Height<1)throw new ArgumentException("Empty monitor");
            int w=Math.Min(Math.Max(1,r.Width),to.Width),h=Math.Min(Math.Max(1,r.Height),to.Height);
            Func<int,int,int,int> offset=(n,span,room)=>(int)Math.Round(Math.Max(0,Math.Min(1,n/(double)Math.Max(1,span)))*room);
            return new Rectangle(to.Left+offset(r.Left-from.Left,from.Width-r.Width,to.Width-w),to.Top+offset(r.Top-from.Top,from.Height-r.Height,to.Height-h),w,h);
        }
        static bool Shell(string c){return c=="Progman" || c=="WorkerW" || c=="Shell_TrayWnd" || c=="Shell_SecondaryTrayWnd" || c=="Windows.UI.Core.CoreWindow";}
        // onlyWindow is nonzero exclusively in the isolated interaction test.
        internal static int Move(DesktopDisplay sessionTarget,IntPtr onlyWindow,out int skipped){
            skipped=0;if(sessionTarget==null || !sessionTarget.Virtual || sessionTarget.Primary)return 0;
            var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));if(previous==IntPtr.Zero)throw new InvalidOperationException("DPI context unavailable.");
            try{
                var all=WindowsDisplays.Capture();var source=all.SingleOrDefault(d=>d.Key==sessionTarget.Key && d.AdapterId==sessionTarget.AdapterId && d.Virtual && !d.Primary);
                var dest=all.SingleOrDefault(d=>d.Primary && !d.Virtual);if(source==null || dest==null)return 0;
                var dst=MonitorInfo(MonitorFromPoint(new Point(dest.X+dest.Width/2,dest.Y+dest.Height/2),0));
                if(dst.name!=dest.Name)return 0;int moved=0,failed=0;var shell=GetShellWindow();
                EnumWindows(delegate(IntPtr w,IntPtr unused){
                    if((onlyWindow!=IntPtr.Zero && w!=onlyWindow) || w==shell || !IsWindowVisible(w) || GetWindow(w,4)!=IntPtr.Zero || (GetWindowLong(w,-20)&0x80)!=0)return true;
                    int cloaked;if(DwmGetWindowAttribute(w,14,out cloaked,4)!=0 || cloaked!=0)return true;
                    var cls=new StringBuilder(128);if(GetClassName(w,cls,cls.Capacity)==0 || Shell(cls.ToString()))return true;
                    IntPtr m=MonitorFromWindow(w,0);if(m==IntPtr.Zero)return true;var src=new Info{size=Marshal.SizeOf(typeof(Info))};if(!GetMonitorInfo(m,ref src) || src.name!=source.Name)return true;
                    var p=new Placement{size=Marshal.SizeOf(typeof(Placement))};if(!GetWindowPlacement(w,ref p)){failed++;return true;}
                    // WINDOWPLACEMENT uses workspace coordinates for top-level non-tool windows.
                    var rect=p.normal.Box;rect.Offset(src.work.L-src.bounds.L,src.work.T-src.bounds.T);var next=Fit(rect,src.work.Box,dst.work.Box);
                    bool ok;
                    if(p.show==1){ok=SetWindowPos(w,IntPtr.Zero,next.X,next.Y,next.Width,next.Height,0x4|0x10|0x200|0x4000);}
                    else {
                        var workspace=next;workspace.Offset(dst.bounds.L-dst.work.L,dst.bounds.T-dst.work.T);
                        p.normal=Rect.From(workspace);p.max=new Point(-1,-1);p.flags=(p.flags&2)|4;
                        ok=true;
                        if(p.show==3){
                            // Some windows keep their own normal bounds. Move while restored
                            // (without activation), then maximize on the destination monitor.
                            var normal=p;normal.show=4;normal.flags=4;
                            ok=SetWindowPlacement(w,ref normal) && SetWindowPos(w,IntPtr.Zero,next.X,next.Y,next.Width,next.Height,0x4|0x10|0x200|0x4000);
                        }
                        if(ok)ok=SetWindowPlacement(w,ref p);
                    }
                    if(ok)moved++;else failed++;return true;
                },IntPtr.Zero);skipped=failed;return moved;
            }finally{SetThreadDpiAwarenessContext(previous);}
        }
    }
}
