using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace DeskMateLearning
{
    internal static class RelayLoopbackTests
    {
        private sealed class Fixture : IDisposable
        {
            public TcpListener PhoneListener;
            public ReadOnlyRelay Relay;
            public TcpClient Phone,Host;
            public NetworkStream PhoneStream,HostStream;
            public Fixture()
            {
                PhoneListener=new TcpListener(IPAddress.Loopback,0); PhoneListener.Start(1);
                Relay=new ReadOnlyRelay(((IPEndPoint)PhoneListener.LocalEndpoint).Port);
                Host=new TcpClient(); Host.Connect(IPAddress.Loopback,Relay.Port);
                IAsyncResult ar=PhoneListener.BeginAcceptTcpClient(null,null);
                using(ar.AsyncWaitHandle) { if(!ar.AsyncWaitHandle.WaitOne(4000)) throw new TimeoutException("Fake phone accept timeout."); }
                Phone=PhoneListener.EndAcceptTcpClient(ar); PhoneListener.Stop();
                Phone.ReceiveTimeout=3000; Phone.SendTimeout=3000; Host.ReceiveTimeout=3000; Host.SendTimeout=3000;
                PhoneStream=Phone.GetStream(); HostStream=Host.GetStream();
            }
            public void Hello()
            {
                byte[] client=Packet(0,1,HelloBody(true,0)); PhoneStream.Write(client,0,client.Length);
                Assert(Read(HostStream).SequenceEqual(client));
                byte[] host=Packet(0,2,HelloBody(false,0)); HostStream.Write(host,0,host.Length);
                Assert(Read(PhoneStream).SequenceEqual(host));
            }
            public void Dispose()
            {
                if(Relay!=null) Relay.Dispose();
                if(Host!=null) Host.Close(); if(Phone!=null) Phone.Close();
                if(PhoneListener!=null) PhoneListener.Stop();
            }
        }
        private static void Assert(bool ok) { if(!ok) throw new Exception("Assertion failed"); }
        private static byte[] Packet(int channel,int type,byte[] body)
        {
            var s=new MemoryStream(); var w=new BinaryWriter(s);
            w.Write(body.Length+2); w.Write((byte)channel); w.Write((byte)type); w.Write(body); return s.ToArray();
        }
        private static byte[] HelloBody(bool client,int mode)
        {
            var s=new MemoryStream(); var w=new BinaryWriter(s);
            w.Write(Encoding.ASCII.GetBytes("ANYD"));w.Write((ushort)1);w.Write((ushort)0);w.Write(new byte[16]);
            w.Write((ushort)1);w.Write((byte)'T');
            if(client) {w.Write((byte)0);w.Write((byte)0);} else {w.Write((ushort)1);w.Write((byte)'W');w.Write((byte)mode);}
            w.Write(new byte[65]);w.Write(new byte[32]);return s.ToArray();
        }
        private static byte[] Read(Stream input)
        {
            byte[] prefix=new byte[4]; FrameCodec.ReadExactly(input,prefix,4,false);
            int n=BitConverter.ToInt32(prefix,0); if(n<2 || n>FrameCodec.MaxFrameBytes) throw new Exception("Invalid fake frame");
            byte[] rest=new byte[n];FrameCodec.ReadExactly(input,rest,n,false);return prefix.Concat(rest).ToArray();
        }
        public static int Run(string logPath)
        {
            var lines=new List<string>();int failed=0;
            Action<string,Action> test=delegate(string name,Action action) {
                try {action();lines.Add("PASS "+name);} catch(Exception e){failed++;lines.Add("FAIL "+name+": "+e.Message);}
            };
            test("real loopback HELLO forwarded both directions",delegate {using(var f=new Fixture()){f.Hello();Assert(f.Relay.Error==null);}});
            test("reverse input blocked without corrupting stats",delegate {using(var f=new Fixture()){
                f.Hello();byte[] input=Packet(4,0x40,new byte[100]),stats=Packet(0,0x15,new byte[20]);
                f.PhoneStream.Write(input,0,input.Length);f.PhoneStream.Write(stats,0,stats.Length);
                Assert(Read(f.HostStream).SequenceEqual(stats));Assert(Interlocked.Read(ref f.Relay.InputBlocked)==1);
            }});
            test("large video packet streams across loopback",delegate {using(var f=new Fixture()){
                f.Hello();byte[] video=Packet(1,0x20,Enumerable.Range(0,200000).Select(i=>(byte)i).ToArray());
                Task t=Task.Run(delegate {f.HostStream.Write(video,0,video.Length);});
                Assert(Read(f.PhoneStream).SequenceEqual(video));Assert(t.Wait(3000));
            }});
            test("clipboard blocked while keepalive survives",delegate {using(var f=new Fixture()){
                f.Hello();byte[] clipboard=Packet(0,0x18,new byte[100]),ping=Packet(0,0x13,new byte[12]);
                f.PhoneStream.Write(clipboard,0,clipboard.Length);f.PhoneStream.Write(ping,0,ping.Length);
                Assert(Read(f.HostStream).SequenceEqual(ping));Assert(Interlocked.Read(ref f.Relay.ClipboardBlocked)==1);
            }});
            test("graceful stop injects valid shutdown BYE",delegate {using(var f=new Fixture()){
                f.Hello();Thread.Sleep(20);f.Relay.RequestGracefulStop();Assert(Read(f.HostStream).SequenceEqual(Packet(0,0x19,new byte[]{1})));
            }});
            test("partial input stall cannot block stop indefinitely",delegate {using(var f=new Fixture()){
                f.Hello();byte[] incomplete={102,0,0,0,4,0x40};f.PhoneStream.Write(incomplete,0,incomplete.Length);Thread.Sleep(100);
                var watch=Stopwatch.StartNew();f.Relay.RequestGracefulStop();Assert(watch.ElapsedMilliseconds<1500);
            }});
            test("encrypted negotiation closes instead of bypassing filter",delegate {using(var f=new Fixture()){
                byte[] client=Packet(0,1,HelloBody(true,0));f.PhoneStream.Write(client,0,client.Length);Read(f.HostStream);
                byte[] encrypted=Packet(0,2,HelloBody(false,1));f.HostStream.Write(encrypted,0,encrypted.Length);
                Assert(SpinWait.SpinUntil(delegate{return f.Relay.Error!=null;},3000));Assert(f.Relay.Error.Contains("Encrypted"));
            }});
            test("relay readiness requires both valid HELLO packets",delegate {using(var f=new Fixture()){
                Assert(!f.Relay.HandshakeComplete);f.Hello();Assert(SpinWait.SpinUntil(()=>f.Relay.HandshakeComplete,1000));
            }});
            test("control heartbeat updates liveness without any video",delegate {using(var f=new Fixture()){
                f.Hello();Thread.Sleep(150);double before=f.Relay.ClientIdleSeconds;
                byte[] ping=Packet(0,0x13,new byte[12]);f.PhoneStream.Write(ping,0,ping.Length);Read(f.HostStream);
                Assert(SpinWait.SpinUntil(()=>f.Relay.ClientIdleSeconds<before,1000));
                f.HostStream.Write(ping,0,ping.Length);Read(f.PhoneStream);Assert(f.Relay.HostIdleSeconds<1);
            }});
            lines.Add("RESULT passed="+(lines.Count-failed)+" failed="+failed);
            lines.Add("Scope: synthetic endpoints on 127.0.0.1 only; no ADB, engine, phone, GUI or drivers.");
            File.WriteAllLines(logPath,lines.ToArray(),Encoding.UTF8);foreach(string line in lines)Console.WriteLine(line);
            return failed==0 ? 0 : 1;
        }
    }
}
