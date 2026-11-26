using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DeskMateLearning
{
    internal static class SelfTests
    {
        private static int passed,failed;
        private static readonly List<string> lines=new List<string>();
        private static void Check(bool value) { if(!value) throw new Exception("Assertion failed"); }
        private static void Reject(Action action)
        {
            bool rejected=false; try { action(); } catch(InvalidOperationException) { rejected=true; }
            catch(InvalidDataException) { rejected=true; } catch(ArgumentOutOfRangeException) { rejected=true; }
            catch(EndOfStreamException) { rejected=true; }
            Check(rejected);
        }
        private static void Test(string name,Action action)
        {
            try { action(); passed++; lines.Add("PASS "+name); }
            catch(Exception e) { failed++; lines.Add("FAIL "+name+": "+e.Message); }
        }
        private static Settings Config()
        {
            return new Settings { ReadOnly=true,StreamingSeconds=30,WallClockSeconds=75,
                StateDirectory=Path.GetTempPath(),AdbPath="adb.exe",
                ApprovedDevices=new[]{new ApprovedDevice {Serial="SPARE-TEST",Model="PFJM10",Name="Test spare"}} };
        }
        private static byte[] Stats(uint frames,uint dropped,uint decode,uint rtt,uint bytes)
        {
            var m=new MemoryStream();var w=new BinaryWriter(m);w.Write(frames);w.Write(dropped);w.Write(decode);w.Write(rtt);w.Write(bytes);return m.ToArray();
        }
        private static byte[] Frame(int channel,int type,byte[] body)
        {
            var b=new MemoryStream(); uint n=(uint)(body.Length+2);
            b.WriteByte((byte)n); b.WriteByte((byte)(n>>8)); b.WriteByte((byte)(n>>16)); b.WriteByte((byte)(n>>24));
            b.WriteByte((byte)channel); b.WriteByte((byte)type); b.Write(body,0,body.Length); return b.ToArray();
        }
        private static void Str(Stream s,string text)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(text); s.WriteByte((byte)bytes.Length); s.WriteByte(0); s.Write(bytes,0,bytes.Length);
        }
        private static byte[] Hello(bool client,int mode,int link)
        {
            var s=new MemoryStream(); s.Write(Encoding.ASCII.GetBytes("ANYD"),0,4);
            s.Write(new byte[]{1,0,0,0},0,4); s.Write(new byte[16],0,16); Str(s,"Test");
            if(client) { s.WriteByte((byte)link); s.WriteByte((byte)(link==1 ? 1 : 0)); }
            else { Str(s,"Windows"); s.WriteByte((byte)mode); }
            s.Write(new byte[65],0,65); s.Write(new byte[32],0,32); return s.ToArray();
        }
        private static byte[] Relay(byte[] packet,bool client,bool first,out int blocked)
        {
            int count=0; var output=new MemoryStream();
            FrameCodec.CopyPacket(new MemoryStream(packet),output,client,first,delegate(int kind){count++;},new object());
            blocked=count; return output.ToArray();
        }
        private sealed class FragmentedStream : Stream
        {
            private readonly MemoryStream inner;
            public FragmentedStream(byte[] b) { inner=new MemoryStream(b); }
            public override int Read(byte[] b,int offset,int count) { return inner.Read(b,offset,Math.Min(count,1)); }
            public override bool CanRead { get{return true;} } public override bool CanSeek {get{return false;} }
            public override bool CanWrite {get{return false;} } public override long Length {get{return inner.Length;} }
            public override long Position {get{return inner.Position;} set{throw new NotSupportedException();}}
            public override void Flush() {} public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}
            public override void SetLength(long value){throw new NotSupportedException();}
            public override void Write(byte[] b,int offset,int count){throw new NotSupportedException();}
        }
        public static int Run(string path)
        {
            passed=0; failed=0; lines.Clear();
            Test("default configuration bounded and read-only",delegate {SafetyPolicy.ValidateSettings(Config());});
            Test("read-only cannot be disabled",delegate {var c=Config();c.ReadOnly=false;Reject(()=>SafetyPolicy.ValidateSettings(c));});
            Test("zero duration rejected",delegate {var c=Config();c.StreamingSeconds=0;Reject(()=>SafetyPolicy.ValidateSettings(c));});
            Test("excess duration rejected",delegate {var c=Config();c.StreamingSeconds=61;Reject(()=>SafetyPolicy.ValidateSettings(c));});
            Test("unbounded wall time rejected",delegate {var c=Config();c.WallClockSeconds=91;Reject(()=>SafetyPolicy.ValidateSettings(c));});
            Test("wall time must leave exit margin",delegate {var c=Config();c.WallClockSeconds=30;Reject(()=>SafetyPolicy.ValidateSettings(c));});
            Test("empty approvals allowed for onboarding but no access granted",delegate {var c=Config();c.ApprovedDevices=new ApprovedDevice[0];SafetyPolicy.ValidateSettings(c);Reject(()=>SafetyPolicy.ValidateDevice(c,new DeviceInfo{Serial="NEW",Model="Android",State="device"}));});
            Test("serial shell metacharacters rejected",delegate {Reject(()=>SafetyPolicy.ValidateSerial("SPARE & other"));});
            Test("empty serial rejected",delegate {Reject(()=>SafetyPolicy.ValidateSerial(""));});
            Test("authorized spare accepted",delegate {SafetyPolicy.ValidateDevice(Config(),new DeviceInfo{Serial="SPARE-TEST",Model="PFJM10",State="device"});});
            Test("main phone rejected even if authorized",delegate {Reject(()=>SafetyPolicy.ValidateDevice(Config(),new DeviceInfo{Serial="MAIN",Model="PFJM10",State="device"}));});
            Test("wrong model on spare serial rejected",delegate {Reject(()=>SafetyPolicy.ValidateDevice(Config(),new DeviceInfo{Serial="SPARE-TEST",Model="OTHER",State="device"}));});
            Test("unauthorized device rejected",delegate {Reject(()=>SafetyPolicy.ValidateDevice(Config(),new DeviceInfo{Serial="SPARE-TEST",Model="PFJM10",State="unauthorized"}));});
            Test("physical primary mirror accepted",delegate {SafetyPolicy.ValidateDisplay(DisplayMode.Mirror,new DisplayInfo{Index=0,Primary=true,Virtual=false});});
            Test("physical secondary cannot silently become mirror",delegate {Reject(()=>SafetyPolicy.ValidateDisplay(DisplayMode.Mirror,new DisplayInfo{Index=1,Primary=false}));});
            Test("virtual primary mirror rejected",delegate {Reject(()=>SafetyPolicy.ValidateDisplay(DisplayMode.Mirror,new DisplayInfo{Index=0,Primary=true,Virtual=true}));});
            Test("extend cannot fall back to physical primary",delegate {Reject(()=>SafetyPolicy.ValidateDisplay(DisplayMode.Extend,new DisplayInfo{Index=0,Primary=true}));});
            Test("extend cannot use physical secondary",delegate {Reject(()=>SafetyPolicy.ValidateDisplay(DisplayMode.Extend,new DisplayInfo{Index=1,Primary=false}));});
            Test("non-primary VDD accepted for extend",delegate {SafetyPolicy.ValidateDisplay(DisplayMode.Extend,new DisplayInfo{Index=2,Primary=false,Virtual=true});});
            Test("VDD primary rejected for extend",delegate {Reject(()=>SafetyPolicy.ValidateDisplay(DisplayMode.Extend,new DisplayInfo{Index=0,Primary=true,Virtual=true}));});
            Test("device parser preserves unauthorized state",delegate {var d=SafetyPolicy.ParseDevices("List of devices attached\nSPARE-TEST unauthorized transport_id:1\n");Check(d.Count==1 && d[0].State=="unauthorized");});
            Test("device parser ignores daemon messages",delegate {var d=SafetyPolicy.ParseDevices("* daemon started *\nSPARE-TEST device product:X model:PFJM10\n");Check(d.Count==1 && d[0].Model=="PFJM10");});
            Test("display parser preserves primary and VDD flags",delegate {var d=SafetyPolicy.ParseDisplays("monitors:\n  0: physical [primary]\n  2: VDD [Virtual Display Driver]\n");Check(d.Count==2 && d[0].Primary && !d[0].Virtual && d[1].Virtual);});
            Test("cleanup command scoped to one owned port",delegate {var a=SafetyPolicy.RemoveOwnedForward("SPARE-TEST",50001);Check(String.Join(" ",a)=="-s SPARE-TEST forward --remove tcp:50001");});
            Test("cleanup refuses unknown zero port",delegate {Reject(()=>SafetyPolicy.RemoveOwnedForward("SPARE-TEST",0));});
            Test("cleanup refuses overflow port",delegate {Reject(()=>SafetyPolicy.RemoveOwnedForward("SPARE-TEST",65536));});
            Test("shell metacharacters remain quoted process argument",delegate {Check(ProcessRunner.StartInfo("nothing",new[]{"usb","SPARE & other"},null).Arguments=="usb \"SPARE & other\"");});
            Test("child environment does not alter parent",delegate {string old=Environment.GetEnvironmentVariable("ANYDISPLAY_STORE");var c=Config();var p=ProcessRunner.StartInfo("nothing",new[]{"list"},c);Check(Environment.GetEnvironmentVariable("ANYDISPLAY_STORE")==old && p.EnvironmentVariables["ANYDISPLAY_STORE"].EndsWith("host.json"));});
            Test("plain client HELLO preserved",delegate {int n;var f=Frame(0,1,Hello(true,0,0));Check(Relay(f,true,true,out n).SequenceEqual(f) && n==0);});
            Test("plain host HELLO preserved",delegate {int n;var f=Frame(0,2,Hello(false,0,0));Check(Relay(f,false,true,out n).SequenceEqual(f) && n==0);});
            Test("encrypted mode 1 rejected not downgraded",delegate {int n;Reject(()=>Relay(Frame(0,2,Hello(false,1,0)),false,true,out n));});
            Test("encrypted mode 2 rejected not downgraded",delegate {int n;Reject(()=>Relay(Frame(0,2,Hello(false,2,0)),false,true,out n));});
            Test("network HELLO rejected",delegate {int n;Reject(()=>Relay(Frame(0,1,Hello(true,0,1)),true,true,out n));});
            Test("accessory HELLO rejected for ADB-only relay",delegate {int n;Reject(()=>Relay(Frame(0,1,Hello(true,0,2)),true,true,out n));});
            Test("non-HELLO first frame rejected",delegate {int n;Reject(()=>Relay(Frame(4,0x40,new byte[8]),true,true,out n));});
            Test("unsupported protocol major rejected",delegate {int n;var h=Hello(true,0,0);h[4]=2;Reject(()=>Relay(Frame(0,1,h),true,true,out n));});
            Test("invalid handshake string bounds rejected",delegate {int n;var h=Hello(true,0,0);h[24]=255;h[25]=255;Reject(()=>Relay(Frame(0,1,h),true,true,out n));});
            Test("all reverse input types blocked",delegate {for(int t=0;t<256;t++){int n;Check(Relay(Frame(4,t,new byte[]{1,2}),true,false,out n).Length==0 && n==1);}});
            Test("client clipboard blocked",delegate {int n;Check(Relay(Frame(0,0x18,new byte[16]),true,false,out n).Length==0 && n==1);});
            Test("host clipboard blocked",delegate {int n;Check(Relay(Frame(0,0x18,new byte[16]),false,false,out n).Length==0 && n==1);});
            Test("audio blocked",delegate {int n;Check(Relay(Frame(2,1,new byte[32]),false,false,out n).Length==0 && n==1);});
            Test("video preserved byte for byte",delegate {int n;var f=Frame(1,0x20,Enumerable.Range(0,90000).Select(i=>(byte)i).ToArray());Check(Relay(f,false,false,out n).SequenceEqual(f) && n==0);});
            Test("client stats preserved",delegate {int n;var f=Frame(0,0x15,new byte[20]);Check(Relay(f,true,false,out n).SequenceEqual(f));});
            Test("keyframe request preserved",delegate {int n;var f=Frame(0,0x16,new byte[0]);Check(Relay(f,true,false,out n).SequenceEqual(f));});
            Test("short network reads preserved",delegate {var f=Frame(1,0x20,new byte[4096]);var o=new MemoryStream();FrameCodec.CopyPacket(new FragmentedStream(f),o,false,false,null,new object());Check(o.ToArray().SequenceEqual(f));});
            Test("empty stream closes cleanly",delegate {Check(!FrameCodec.CopyPacket(new MemoryStream(),new MemoryStream(),false,false,null,new object()));});
            Test("truncated length rejected",delegate {Reject(()=>FrameCodec.CopyPacket(new MemoryStream(new byte[]{2,0}),new MemoryStream(),false,false,null,new object()));});
            Test("invalid low frame length rejected",delegate {Reject(()=>FrameCodec.CopyPacket(new MemoryStream(new byte[]{1,0,0,0}),new MemoryStream(),false,false,null,new object()));});
            Test("oversize frame rejected before body allocation",delegate {int n=FrameCodec.MaxFrameBytes+1;byte[] b={(byte)n,(byte)(n>>8),(byte)(n>>16),(byte)(n>>24)};Reject(()=>FrameCodec.CopyPacket(new MemoryStream(b),new MemoryStream(),false,false,null,new object()));});
            Test("truncated frame body rejected",delegate {int n;Reject(()=>Relay(new byte[]{8,0,0,0,1,0x20,1},false,false,out n));});
            Test("unknown channel rejected",delegate {int n;Reject(()=>Relay(Frame(5,1,new byte[0]),false,false,out n));});
            Test("input drops do not lose following stats",delegate {
                var hello=Frame(0,1,Hello(true,0,0));var input=Frame(4,0x40,new byte[100]);var stats=Frame(0,0x15,new byte[20]);
                var source=new MemoryStream(hello.Concat(input).Concat(stats).ToArray());var dest=new MemoryStream();bool first=true;int count=0;
                while(FrameCodec.CopyPacket(source,dest,true,first,delegate(int k){count++;},new object())) first=false;
                Check(count==1 && dest.ToArray().SequenceEqual(hello.Concat(stats)));
            });
            Test("extend smoke selects virtual only",delegate {
                var physical=new DisplayInfo {Index=0,Primary=true};
                var secondary=new DisplayInfo {Index=1};
                var vdd=new DisplayInfo {Index=2,Virtual=true};
                Check(SafetyPolicy.SelectSmokeDisplay(DisplayMode.Extend,new[]{physical,secondary,vdd})==vdd);
            });
            Test("extend smoke missing VDD never mirrors",delegate {Reject(()=>SafetyPolicy.SelectSmokeDisplay(DisplayMode.Extend,new[]{new DisplayInfo {Index=0,Primary=true}}));});
            Test("extend smoke rejects primary VDD",delegate {Reject(()=>SafetyPolicy.SelectSmokeDisplay(DisplayMode.Extend,new[]{new DisplayInfo {Index=2,Virtual=true,Primary=true}}));});
            Test("extend smoke rejects ambiguous VDD",delegate {Reject(()=>SafetyPolicy.SelectSmokeDisplay(DisplayMode.Extend,new[]{new DisplayInfo {Index=2,Virtual=true},new DisplayInfo {Index=3,Virtual=true}}));});
            Test("extend smoke rejects empty list",delegate {Reject(()=>SafetyPolicy.SelectSmokeDisplay(DisplayMode.Extend,new DisplayInfo[0]));});
            Test("mirror smoke preserves physical primary",delegate {
                var primary=new DisplayInfo {Index=0,Primary=true};
                Check(SafetyPolicy.SelectSmokeDisplay(DisplayMode.Mirror,new[]{new DisplayInfo {Index=2,Virtual=true},primary})==primary);
            });
            Test("smoke rejects unknown mode",delegate {Reject(()=>SafetyPolicy.SelectSmokeDisplay((DisplayMode)99,new[]{new DisplayInfo {Index=2,Virtual=true}}));});
            Test("display validation rejects unknown mode",delegate {Reject(()=>SafetyPolicy.ValidateDisplay((DisplayMode)99,new DisplayInfo {Index=0,Primary=true}));});
            Test("daily args omit seconds entirely",delegate {var a=SessionRuntime.EngineArguments(Config(),12345,2,true);Check(!a.Contains("--seconds") && !a.Contains("--verbose"));});
            Test("timed args retain finite seconds",delegate {var a=SessionRuntime.EngineArguments(Config(),12345,2,false);Check(a.Contains("--seconds") && a[Array.IndexOf(a,"--seconds")+1]=="30");});
            Test("daily args retain selected display and read-only options",delegate {var a=SessionRuntime.EngineArguments(Config(),12345,2,true);Check(a[Array.IndexOf(a,"--monitor")+1]=="2" && a.Contains("--no-audio") && a.Contains("--no-clipboard") && a.Contains("h264"));});
            Test("daily args reject invalid relay port",delegate {Reject(()=>SessionRuntime.EngineArguments(Config(),1,2,true));});
            Test("daily args reject negative display",delegate {Reject(()=>SessionRuntime.EngineArguments(Config(),12345,-1,true));});
            Test("daily session survives old wall limit",delegate {Check(SessionRuntime.StopReason(true,false,36000,75,true,1,1)==null);});
            Test("daily manual stop has priority",delegate {Check(SessionRuntime.StopReason(true,true,1,75,false,100,100)=="user");});
            Test("daily handshake timeout is bounded",delegate {Check(SessionRuntime.StopReason(true,false,31,75,false,0,0)=="handshake-timeout");});
            Test("daily handshake allowed below timeout",delegate {Check(SessionRuntime.StopReason(true,false,29,75,false,0,0)==null);});
            Test("daily missing client heartbeat stops",delegate {Check(SessionRuntime.StopReason(true,false,100,75,true,16,1)=="transport-idle-timeout");});
            Test("daily missing host heartbeat stops",delegate {Check(SessionRuntime.StopReason(true,false,100,75,true,1,16)=="transport-idle-timeout");});
            Test("static desktop with control traffic remains alive",delegate {Check(SessionRuntime.StopReason(true,false,7200,75,true,1,1)==null);});
            Test("timed session still has outer watchdog",delegate {Check(SessionRuntime.StopReason(false,false,76,75,true,0,0)=="timed-test-wall-clock");});
            Test("bounded tail preserves small output",delegate {var t=new BoundedTextTail(10);t.Append("hello".ToCharArray(),5);Check(t.Snapshot()=="hello" && !t.Truncated);});
            Test("bounded tail keeps newest chars across chunks",delegate {var t=new BoundedTextTail(5);t.Append("abcd".ToCharArray(),4);t.Append("efgh".ToCharArray(),4);Check(t.Snapshot()=="defgh" && t.Truncated && t.TotalCharacters==8);});
            Test("bounded tail handles huge chunk",delegate {var t=new BoundedTextTail(4);t.Append("0123456789".ToCharArray(),10);Check(t.Snapshot()=="6789" && t.Truncated);});
            Test("bounded tail exact capacity is not truncated",delegate {var t=new BoundedTextTail(4);t.Append("abcd".ToCharArray(),4);Check(t.Snapshot()=="abcd" && !t.Truncated);});
            Test("bounded drain handles unbroken long line",delegate {var t=new BoundedTextTail(64);var task=t.DrainAsync(new StringReader(new string('x',300000)));Check(task.Wait(5000) && t.Snapshot().Length==64 && t.TotalCharacters==300000);});
            Test("bounded tail rejects invalid capacity",delegate {Reject(()=>new BoundedTextTail(0));});
            Test("bounded tail rejects invalid append count",delegate {var t=new BoundedTextTail(4);Reject(()=>t.Append(new char[1],2));});
            Test("quote APK path with spaces",delegate {Check(ProcessRunner.QuoteArgument(@"C:\My Apps\client.apk")=="\"C:\\My Apps\\client.apk\"".Replace("\\\\","\\"));});
            Test("quote empty argument",delegate {Check(ProcessRunner.QuoteArgument("")=="\"\"");});
            Test("telemetry starts without fabricated metrics",delegate {var t=new TelemetryStore().SnapshotAt(0);Check(!t.HasStats && !t.Fps.HasValue && !t.HasConfig);});
            Test("telemetry first sample has no guessed FPS",delegate {var t=new TelemetryStore();t.ObserveAt(0x15,Stats(60,0,10000,2000,1000000),true,1);Check(!t.SnapshotAt(1).Fps.HasValue);});
            Test("telemetry uses actual interval not target Hz",delegate {var t=new TelemetryStore();t.ObserveAt(0x15,Stats(60,0,10000,2000,1000000),true,1);t.ObserveAt(0x15,Stats(60,2,8000,2500,1000000),true,3);var v=t.SnapshotAt(3);Check(v.Fps==30 && v.Mbps==4 && v.RttMs==2.5 && v.DecodeMs==8 && v.TotalDropped==2);});
            Test("telemetry stale after three seconds",delegate {var t=new TelemetryStore();t.ObserveAt(0x15,Stats(1,0,10,10,20),true,1);Check(!t.SnapshotAt(5).Fresh);});
            Test("telemetry ignores malformed stats",delegate {var t=new TelemetryStore();t.ObserveAt(0x15,new byte[19],true,1);Check(!t.SnapshotAt(1).HasStats);});
            Test("telemetry ignores wrong stats direction",delegate {var t=new TelemetryStore();t.ObserveAt(0x15,Stats(1,0,10,10,20),false,1);Check(!t.SnapshotAt(1).HasStats);});
            Test("telemetry static screen reports zero FPS",delegate {var t=new TelemetryStore();t.ObserveAt(0x15,Stats(1,0,10,10,20),true,1);t.ObserveAt(0x15,Stats(0,0,0,10,20),true,2);Check(t.SnapshotAt(2).Fps==0 && !t.SnapshotAt(2).DecodeMs.HasValue);});
            Test("telemetry history is bounded",delegate {var t=new TelemetryStore();for(int i=0;i<200;i++)t.ObserveAt(0x15,Stats(60,0,1,1,20),true,i);Check(t.SnapshotAt(199).History.Length==60);});
            Test("telemetry config reads negotiated size",delegate {var t=new TelemetryStore();byte[] b=new byte[24];b[4]=128;b[5]=7;b[6]=56;b[7]=4;b[8]=60;b[10]=1;t.ObserveAt(0x12,b,false,1);var v=t.SnapshotAt(1);Check(v.Width==1920 && v.Height==1080 && v.TargetHz==60 && v.Codec=="H.264");});
            Test("frame observer preserves stats bytes",delegate {byte[] b=Stats(60,0,10000,2000,1000000);var packet=Frame(0,0x15,b);var output=new MemoryStream();int count=0;FrameCodec.CopyPacket(new MemoryStream(packet),output,true,false,null,new object(),delegate(int type,byte[] body,bool client){Check(type==0x15 && client && body.SequenceEqual(b));count++;});Check(count==1 && output.ToArray().SequenceEqual(packet));});
            Test("frame observer does not inspect clipboard",delegate {var output=new MemoryStream();int count=0;FrameCodec.CopyPacket(new MemoryStream(Frame(0,0x18,new byte[20])),output,true,false,null,new object(),delegate {count++;});Check(count==0 && output.Length==0);});
            lines.Add("RESULT passed="+passed+" failed="+failed);
            lines.Add("Scope: pure policy/framing tests. No ADB, engine, GUI, phone, sockets or drivers executed.");
            File.WriteAllLines(path,lines.ToArray(),Encoding.UTF8);
            foreach(string line in lines) Console.WriteLine(line);
            return failed==0 ? 0 : 1;
        }
    }
}
