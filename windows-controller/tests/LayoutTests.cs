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
            new DesktopDisplay{Name=@"\\.\DISPLAY5",MonitorId="test-vdd",Title="VDD",AvailableRates=new[]{60},Virtual=true,X=2560,Y=0,Width=1920,Height=1080,RefreshHz=60,BitsPerPixel=32}};}
        const string Mtt=@"\\?\DISPLAY#MTT1337#test";
        static VddPaths.Endpoint End(string monitor,uint src,uint target,bool active){return new VddPaths.Endpoint{
            Monitor=monitor,Path=new VddPaths.PathInfo{Flags=active?1u:0u,Source=new VddPaths.Source{Id=src},Target=new VddPaths.Target{Id=target,Available=1}}};}
        private static void Check(bool v){if(!v)throw new Exception("Assertion failed");}
        private static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
        public static int Run(string report)
        {
            var registry=new ScreenSessions.Registry(@"Local\Hu1Display.LayoutTest."+Guid.NewGuid().ToString("N"));
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
            test("owned VDD refresh 120 is restored; physical and mismatched targets ignored",delegate {
                var actual=Sample();var v=actual[2];v.AvailableRates=new[]{60,90,120};
                var choice=new DisplayInfo{Virtual=true,Description=v.Name+" synthetic"};int writes=0;
                Func<DesktopDisplay[]> read=()=>LayoutPolicy.Clone(actual);
                Action<DesktopDisplay,int> write=(d,hz)=>{if(d.Key!=v.Key)throw new Exception("physical target");writes++;v.RefreshHz=hz;};
                var owner=new VddRefreshSession(read(),choice,DisplayMode.Extend,120,read,write);owner.Apply();Check(v.RefreshHz==120);owner.Restore();Check(v.RefreshHz==60 && writes==2);
                new VddRefreshSession(read(),choice,DisplayMode.Mirror,120,read,write).Apply();choice.Description=actual[0].Name+" synthetic";
                new VddRefreshSession(read(),choice,DisplayMode.Extend,120,read,write).Apply();Check(writes==2);
            });
            test("unsupported rates and external changes never cause a blind restore",delegate {
                var actual=Sample();var v=actual[2];var choice=new DisplayInfo{Virtual=true,Description=v.Name+" synthetic"};int writes=0;
                Func<DesktopDisplay[]> read=()=>LayoutPolicy.Clone(actual);Action<DesktopDisplay,int> write=(d,hz)=>{v.RefreshHz=hz;writes++;};
                new VddRefreshSession(read(),choice,DisplayMode.Extend,90,read,write).Apply();Check(writes==0);
                v.AvailableRates=new[]{60,90};var owner=new VddRefreshSession(read(),choice,DisplayMode.Extend,90,read,write);
                owner.Apply();v.Width+=10;Reject(()=>owner.Restore());Check(writes==1);
            });
            test("return windows selects only matching non-primary VDD",delegate {
                var all=Sample();var display=new DisplayInfo{Description=all[2].Name+" synthetic",Virtual=true};
                Check(WindowHandoff.FindTarget(all,display,DisplayMode.Extend)==all[2]);Check(WindowHandoff.FindTarget(all,display,DisplayMode.Mirror)==null);
                display.Description=all[0].Name+" synthetic";Check(WindowHandoff.FindTarget(all,display,DisplayMode.Extend)==null);
            });
            test("window placement handles negative coordinates, oversized windows and taskbar",delegate {
                var from=new System.Drawing.Rectangle(-3000,150,1920,1080);var to=new System.Drawing.Rectangle(60,20,1200,800);
                var inside=WindowHandoff.Fit(new System.Drawing.Rectangle(-2800,210,400,300),from,to);Check(to.Contains(inside) && inside.Width==400 && inside.Height==300);
                Check(WindowHandoff.Fit(new System.Drawing.Rectangle(-3700,-100,2400,1700),from,to)==to);
            });
            test("HUD stays on the chosen screen at all four anchors; stale stats blank",delegate {
                var work=new System.Drawing.Rectangle(-1600,140,1280,720);for(int n=0;n<4;n++)Check(work.Contains(StudioHud.Place(work,n,330,72)));
                Check(StudioHud.Numbers(new TelemetrySnapshot{Fresh=false,Fps=90}).All(v=>v=="—"));
            });
            test("multiple leases keep legacy guard until last route, reject duplicate phone/screen and mirror",delegate {
                var a=new DisplayInfo{Virtual=true,Description="VDD1 synthetic"};var b=new DisplayInfo{Virtual=true,Description="VDD2 synthetic"};
                using(var one=registry.Reserve("TEST-A",a,DisplayMode.Extend)){
                    Reject(()=>registry.Reserve("TEST-A",b,DisplayMode.Extend));Reject(()=>registry.Reserve("TEST-B",a,DisplayMode.Extend));
                    Reject(()=>registry.Reserve("TEST-B",b,DisplayMode.Mirror));using(var two=registry.Reserve("TEST-B",b,DisplayMode.Extend)){
                        one.Dispose();using(var mutex=new System.Threading.Mutex(false,registry.MutexName)){Check(!mutex.WaitOne(0));}
                    }
                }
            });
            test("CCD attach skips busy sources and preserves both active paths",delegate {
                var a=End("physical",0,0,true);var b=End(Mtt+"B",1,1,true);var spare=End(Mtt,2,2,false);
                var path=VddPaths.Plan(new[]{a,b,End(Mtt,1,2,false),spare},Sample(),Mtt,true);
                Check(path.Length==3 && path[0].Equals(a.Path) && path[1].Equals(b.Path));
                Check(path[2].Flags==1 && path[2].Source.Id==2 && path[2].Source.Mode==UInt32.MaxValue && path[2].Target.Mode==UInt32.MaxValue);
                Reject(()=>VddPaths.Plan(new[]{a,b,End(Mtt,1,2,false)},Sample(),Mtt,true));
            });
            test("CCD detach only removes selected target; primary, clone and ambiguous targets rejected",delegate {
                var all=Sample();all[2].MonitorId=Mtt;var a=End("physical",0,0,true);var b=End(Mtt+"B",1,1,true);var c=End(Mtt,2,2,true);
                var path=VddPaths.Plan(new[]{a,b,c},all,Mtt,false);Check(path.SequenceEqual(new[]{a.Path,b.Path}));
                Reject(()=>VddPaths.Plan(new[]{a,b,c},all,"physical",false));
                Reject(()=>VddPaths.Plan(new[]{a,b,End(Mtt,1,2,true)},all,Mtt,false));
                Reject(()=>VddPaths.Plan(new[]{a,b,c,End(Mtt,3,3,false)},all,Mtt,false));
                all[2].Primary=true;Reject(()=>VddPaths.Plan(new[]{a,b,c},all,Mtt,false));
            });
            test("route checks tolerate independent VDD unplug but not own or physical replacement",delegate {
                var all=Sample();var peer=all[2].Copy();peer.Name="peer";var both=all.Concat(new[]{peer}).ToArray();
                Check(ScreenSessions.SameRouteLayout(both,all,all[2].Name));Check(!ScreenSessions.SameRouteLayout(both,all,"peer"));
                var changed=LayoutPolicy.Clone(all);changed[0].AdapterId="different";Check(!ScreenSessions.SameRouteLayout(both,changed,all[2].Name));
            });
            test("dormant and active aliases reserve the same stable monitor",delegate {
                var a=new DisplayInfo{MonitorPath=Mtt,Dormant=true,Index=-1};var b=new DisplayInfo{MonitorPath=Mtt,Description="DISPLAY8 synthetic"};
                using(registry.Reserve("ONE",a,DisplayMode.Extend)){Reject(()=>registry.Reserve("TWO",b,DisplayMode.Extend));}
            });
            test("capacity draft changes count only; no downsize or writes to original",delegate {
                var bytes=System.Text.Encoding.UTF8.GetBytes("<vdd_settings><monitors><count>2</count></monitors><global><g_refresh_rate>120</g_refresh_rate></global></vdd_settings>");
                var xml=VddCapacity.Draft(new MemoryStream(bytes,false),3);Check(xml.SelectSingleNode("//count").InnerText=="3" && xml.SelectSingleNode("//g_refresh_rate").InnerText=="120");
                var higher=System.Text.Encoding.UTF8.GetBytes(xml.OuterXml);bool refused=false;
                try{VddCapacity.Draft(new MemoryStream(higher,false),2);}catch(InvalidDataException){refused=true;}Check(refused);
            });
            test("move past occupied portrait neighbour without moving physical screens",delegate {
                var a=Sample();var b=LayoutPolicy.Place(a,a[2].Key,"left");Check(b[2].X==a[1].X-a[2].Width && b[2].Y==a[1].Y);
                Check(LayoutPolicy.LiveMove(a,b,a[2].MonitorId)==b[2]);Reject(()=>LayoutPolicy.LiveMove(a,b,a[0].MonitorId));
                b[1].X--;Reject(()=>LayoutPolicy.LiveMove(a,b,a[2].MonitorId));
            });
            test("translation preserves owned rate restore without masking other mode or position changes",delegate {
                var a=Sample();var v=a[2];v.AvailableRates=new[]{60,90};int writes=0;
                var rate=new VddRefreshSession(a,new DisplayInfo{Virtual=true,Description=v.Name+" test"},DisplayMode.Extend,90,()=>LayoutPolicy.Clone(a),(d,h)=>{Check(d.X==v.X);v.RefreshHz=h;writes++;});
                rate.Apply();var baseline=LayoutPolicy.Clone(a);v.X+=42;Check(!ScreenSessions.SameRouteLayout(baseline,a,v.Name) && ScreenSessions.SameRouteLayout(baseline,a,v.Name,true));
                rate.Restore();Check(v.RefreshHz==60 && v.X==baseline[2].X+42 && writes==2);baseline[2].RefreshHz=60;a[1].X--;Check(!ScreenSessions.SameRouteLayout(baseline,a,v.Name,true));
            });
            test("live permits require an owned route and expire with the issuing handle",delegate {
                var id=Guid.NewGuid().ToString("N");Reject(()=>registry.LivePermit(id,Mtt,true));
                using(registry.Reserve("fake",new DisplayInfo{MonitorPath=Mtt},DisplayMode.Extend))
                using(var owner=registry.LivePermit(id,Mtt,true))using(var child=registry.LivePermit(id,Mtt,false)){
                    Check(child.WaitOne(0));owner.Reset();Check(!child.WaitOne(0));Reject(()=>registry.LivePermit(id,Mtt+"other",true));
                }
                bool gone=false;try{registry.LivePermit(id,Mtt,false).Dispose();}catch(System.Threading.WaitHandleCannotBeOpenedException){gone=true;}Check(gone);
            });
            lines.Add("RESULT passed="+(lines.Count-failed)+" failed="+failed);
            lines.Add("Scope: synthetic layout policy only. No Windows display mutation, helper, phone, driver or session started.");
            File.WriteAllLines(report,lines);return failed==0 ? 0 : 1;
        }
    }
}
