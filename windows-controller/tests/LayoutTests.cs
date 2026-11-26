using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace DeskMateLearning
{
    public static class LayoutTests
    {
        public static DesktopDisplay[] Sample()
        {return new[]{
            new DesktopDisplay{Name=@"\\.\DISPLAY1",MonitorId="test-primary",Title="Panel A",Primary=true,X=0,Y=0,Width=2560,Height=1440,RefreshHz=180,BitsPerPixel=32},
            new DesktopDisplay{Name=@"\\.\DISPLAY2",MonitorId="test-portrait",Title="Panel B",X=-1080,Y=150,Width=1080,Height=1920,Orientation=1,RefreshHz=60,BitsPerPixel=32},
            new DesktopDisplay{Name=@"\\.\DISPLAY5",MonitorId="test-vdd",Title="VDD",Virtual=true,X=2560,Y=0,Width=1920,Height=1080,RefreshHz=60,BitsPerPixel=32}};}
        private static void Check(bool v){if(!v)throw new Exception("Assertion failed");}
        private static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
        public static int Run(string report)
        {
            var lines=new List<string>();int failed=0;
            Action<string,Action> test=delegate(string name,Action action){try{action();lines.Add("PASS "+name);}catch(Exception e){failed++;lines.Add("FAIL "+name+": "+e.Message);}};
            test("three real-style displays accepted",delegate {LayoutPolicy.Validate(Sample(),Sample());});
            test("existing portrait secondary preserved",delegate {var s=Sample();Check(s[1].X<0 && s[1].Height>s[1].Width && !s[1].Virtual);});
            test("clone is independent draft",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].X=0;Check(a[2].X==2560);});
            foreach(string side in new[]{"left","right","top","bottom"}){string direction=side;test("quick placement "+side,delegate {var a=Sample();if(direction=="left")a=new[]{a[0],a[2]};var b=LayoutPolicy.Place(a,a.Last().Key,direction);LayoutPolicy.Validate(a,b);Check(LayoutPolicy.Same(a,b,false));});}
            test("primary cannot be moved",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[0].X=20;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("primary quick direction rejected",delegate {var a=Sample();Reject(()=>LayoutPolicy.Place(a,a[0].Key,"left"));});
            test("overlapping desktop rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].X=500;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("disconnected desktop rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].X=3000;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("mode edits rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].Width=1280;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("rotation edits rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[1].Orientation=0;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("refresh edits rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[0].RefreshHz=60;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("hotplug stale draft rejected",delegate {Reject(()=>LayoutPolicy.Validate(Sample(),Sample().Take(2).ToArray()));});
            test("identity replacement rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].MonitorId="other";Reject(()=>LayoutPolicy.Validate(a,b));});
            test("extreme coordinates rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].X=Int32.MinValue;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("negative coordinates legal",delegate {var a=Sample();var b=LayoutPolicy.Place(a,a[2].Key,"top");LayoutPolicy.Validate(a,b);Check(b[2].Y==-1080);});
            test("snap brings edge to exact desktop boundary",delegate {var a=Sample();a[2].X=2570;LayoutPolicy.Snap(a[2],a,20);Check(a[2].X==2560);});
            test("corner-only contact rejected",delegate {var a=Sample();var b=LayoutPolicy.Clone(a);b[2].Y=1440;Reject(()=>LayoutPolicy.Validate(a,b));});
            test("external position changes detected",delegate {var a=Sample();var b=LayoutPolicy.Place(a,a[2].Key,"bottom");Check(!LayoutPolicy.Same(a,b,true) && LayoutPolicy.Same(a,b,false));});
            lines.Add("RESULT passed="+(lines.Count-failed)+" failed="+failed);
            lines.Add("Scope: synthetic layout policy only. No Windows display mutation, helper, phone, driver or session started.");
            File.WriteAllLines(report,lines);return failed==0 ? 0 : 1;
        }
    }
}
