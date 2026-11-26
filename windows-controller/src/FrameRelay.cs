using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

namespace DeskMateLearning
{
    // Only v1, plain, local ADB sessions. Reject encrypted negotiation; never downgrade it.
    public static class FrameCodec
    {
        public const int MaxFrameBytes = 8 * 1024 * 1024;
        public static bool ReadExactly(Stream s, byte[] b, int count, bool allowEof)
        {
            int n=0;
            while (n<count)
            {
                int got=s.Read(b,n,count-n);
                if (got==0) { if (n==0 && allowEof) return false; throw new EndOfStreamException("Truncated frame."); }
                n+=got;
            }
            return true;
        }
        private static int SkipString(byte[] b,int offset)
        {
            if (offset+2>b.Length) throw new InvalidDataException("Truncated handshake string.");
            int length=b[offset] | b[offset+1]<<8;
            if (offset+2+length>b.Length) throw new InvalidDataException("Handshake string out of bounds.");
            return offset+2+length;
        }
        public static void ValidateHello(byte[] body,bool fromClient)
        {
            if (body.Length<26 || body[0]!='A' || body[1]!='N' || body[2]!='Y' || body[3]!='D' || body[4]!=1 || body[5]!=0)
                throw new InvalidDataException("Unsupported AnyDisplay handshake version.");
            int offset=SkipString(body,24);
            if (fromClient)
            {
                if (offset+2>body.Length || body[offset]!=0 || body[offset+1]!=0)
                    throw new InvalidDataException("Read-only relay accepts ADB loopback, not network/accessory authentication.");
            }
            else
            {
                offset=SkipString(body,offset);
                if (offset>=body.Length || body[offset]!=0)
                    throw new InvalidDataException("Encrypted handshake rejected; cannot safely drop AEAD frames.");
            }
        }
        public static bool CopyPacket(Stream input,Stream output,bool fromClient,bool first,
            Action<int> blocked,object writeLock)
        {
            return CopyPacket(input,output,fromClient,first,blocked,writeLock,null);
        }
        public static bool CopyPacket(Stream input,Stream output,bool fromClient,bool first,
            Action<int> blocked,object writeLock,Action<int,byte[],bool> observed)
        {
            return CopyPacket(input,output,fromClient,first,blocked,writeLock,observed,StreamProfile.ClientDefault,new PacketBuffers());
        }
        // One set per direction; no shared buffer and no frame-sized allocations on the video path.
        internal sealed class PacketBuffers
        {
            internal bool ConfigRequested,ConfigAccepted;
            internal DisplayMode? ExpectedMode;internal Action VerifyTarget;
            internal readonly byte[] Header=new byte[6], Payload=new byte[65536];
        }
        internal static bool CopyPacket(Stream input,Stream output,bool fromClient,bool first,
            Action<int> blocked,object writeLock,Action<int,byte[],bool> observed,StreamProfile profile,PacketBuffers buffers)
        {
            byte[] header=buffers.Header;
            if (!ReadExactly(input,header,6,true)) return false;
            uint size=(uint)(header[0] | header[1]<<8 | header[2]<<16 | header[3]<<24);
            if (size<2 || size>MaxFrameBytes) throw new InvalidDataException("Frame exceeds safe 2..8MiB limits.");
            if (header[4]>4) throw new InvalidDataException("Unknown protocol channel.");
            int remaining=(int)size-2;
            if (first)
            {
                if (header[4]!=0 || header[5]!=(fromClient ? 1 : 2) || remaining>4096)
                    throw new InvalidDataException("Expected bounded HELLO before session data.");
                byte[] hello=new byte[remaining]; ReadExactly(input,hello,remaining,false);
                ValidateHello(hello,fromClient);
                lock(writeLock) { output.Write(header,0,6); output.Write(hello,0,hello.Length); }
                return true;
            }
            if(fromClient && header[4]==0 && header[5]==0x11){
                // An index could refer to a different desktop after a peer disconnected.
                if(buffers.ConfigRequested)throw new InvalidDataException("Reconnect to reconfigure the capture target.");
                buffers.ConfigRequested=true;
            }
            if(!fromClient && buffers.ExpectedMode.HasValue){
                if(header[4]==1 && !buffers.ConfigAccepted)throw new InvalidDataException("Video before verified CONFIG.");
                if(header[4]==0 && header[5]==0x12){
                    if(remaining!=24 || buffers.ConfigAccepted)throw new InvalidDataException("Unexpected CONFIG.");
                    var config=new byte[24];ReadExactly(input,config,24,false);
                    if(((config[15]&8)!=0)!=(buffers.ExpectedMode==DisplayMode.Mirror))throw new InvalidDataException("Engine selected a different display mode; video blocked.");
                    if(buffers.VerifyTarget!=null)buffers.VerifyTarget();
                    lock(writeLock){output.Write(header,0,6);output.Write(config,0,24);}
                    buffers.ConfigAccepted=true;if(observed!=null)observed(0x12,config,false);return true;
                }
            }
            if((buffers.ExpectedMode.HasValue || !profile.IsDefault) && fromClient && header[4]==0 && header[5]==0x11){
                if(remaining!=13)throw new InvalidDataException("Invalid v1 CONFIG_REQ length.");
                var request=new byte[13];ReadExactly(input,request,13,false);profile.Apply(request);
                if(buffers.ExpectedMode.HasValue)request[12]=(byte)((request[12]&~8)|(buffers.ExpectedMode==DisplayMode.Mirror?8:0));
                lock(writeLock){output.Write(header,0,6);output.Write(request,0,13);}return true;
            }
            bool drop=(fromClient && header[4]==4) || (header[4]==0 && header[5]==0x18) || header[4]==2;
            // Only two bounded, public control payloads are observed. Video/input/clipboard are never copied for telemetry.
            bool inspect=observed!=null && header[4]==0 &&
                ((fromClient && header[5]==0x15 && remaining==20) || (!fromClient && header[5]==0x12 && remaining==24));
            byte[] observation=inspect ? new byte[remaining] : null;
            int observationOffset=0;
            byte[] chunk=buffers.Payload;
            lock(writeLock)
            {
                if (!drop) { output.Write(header,0,6); }
                while (remaining>0)
                {
                    int amount=Math.Min(remaining,chunk.Length);
                    ReadExactly(input,chunk,amount,false);
                    if (!drop) output.Write(chunk,0,amount);
                    if(inspect){Buffer.BlockCopy(chunk,0,observation,observationOffset,amount);observationOffset+=amount;}
                    remaining-=amount;
                }
            }
            if(inspect) observed(header[5],observation,fromClient);
            if (drop && blocked!=null) blocked(header[4]==4 ? 4 : (header[4]==2 ? 2 : 0x18));
            return true;
        }
    }
    public sealed class ReadOnlyRelay : IDisposable
    {
        private readonly TcpListener listener;
        private readonly int devicePort;
        private readonly object hostWriteLock=new object(), deviceWriteLock=new object();
        private volatile bool stopped;
        private volatile bool plainHostHello, plainClientHello;
        private long lastClientPacket=Stopwatch.GetTimestamp(), lastHostPacket=Stopwatch.GetTimestamp();
        public bool HandshakeComplete { get { return plainHostHello && plainClientHello; } }
        public double ClientIdleSeconds { get { return (Stopwatch.GetTimestamp()-Interlocked.Read(ref lastClientPacket))/(double)Stopwatch.Frequency; } }
        public double HostIdleSeconds { get { return (Stopwatch.GetTimestamp()-Interlocked.Read(ref lastHostPacket))/(double)Stopwatch.Frequency; } }
        private TcpClient host,device;
        private Thread acceptThread,deviceThread;
        public int Port { get; private set; }
        public string Error { get; private set; }
        public long InputBlocked, ClipboardBlocked;
        public TelemetryStore Telemetry {get;private set;}
        private readonly StreamProfile profile;
        public ReadOnlyRelay(int devicePort) : this(devicePort,StreamProfile.ClientDefault) {}
        private readonly DisplayMode? expectedMode;private readonly Action verifyTarget;
        public ReadOnlyRelay(int devicePort,StreamProfile profile):this(devicePort,profile,null,null){}
        internal ReadOnlyRelay(int devicePort,StreamProfile profile,DisplayMode? mode,Action verifyTarget)
        {
            if (devicePort<1024 || devicePort>65535) throw new ArgumentOutOfRangeException("devicePort");
            expectedMode=mode;this.verifyTarget=verifyTarget;
            this.devicePort=devicePort;this.profile=profile ?? StreamProfile.ClientDefault;
            Telemetry=new TelemetryStore();
            listener=new TcpListener(IPAddress.Loopback,0); listener.Start(1);
            Port=((IPEndPoint)listener.LocalEndpoint).Port;
            acceptThread=new Thread(Accept); acceptThread.IsBackground=true; acceptThread.Start();
        }
        private void Accept()
        {
            try
            {
                host=listener.AcceptTcpClient(); listener.Stop(); host.NoDelay=true; host.SendTimeout=2000;
                device=new TcpClient(); device.NoDelay=true;
                IAsyncResult ar=device.BeginConnect(IPAddress.Loopback,devicePort,null,null);
                using(ar.AsyncWaitHandle) { if (!ar.AsyncWaitHandle.WaitOne(5000)) throw new TimeoutException("Device loopback connect timeout."); }
                device.EndConnect(ar); device.SendTimeout=2000;
                deviceThread=new Thread(delegate(){ Pump(device.GetStream(),host.GetStream(),true,hostWriteLock); });
                deviceThread.IsBackground=true; deviceThread.Start();
                Pump(host.GetStream(),device.GetStream(),false,deviceWriteLock);
            }
            catch(Exception e) { if (!stopped) Error=e.Message; }
            finally { Dispose(); }
        }
        private void Pump(Stream input,Stream output,bool fromClient,object writeLock)
        {
            try
            {
                bool first=true;
                var buffers=new FrameCodec.PacketBuffers{ExpectedMode=expectedMode,VerifyTarget=verifyTarget};
                Action<int> onDrop=delegate(int kind){
                    if(kind==4) Interlocked.Increment(ref InputBlocked);
                    if(kind==0x18) Interlocked.Increment(ref ClipboardBlocked);
                };
                Action<int,byte[],bool> observe=Telemetry.Observe;
                while (!stopped)
                {
                    if(!FrameCodec.CopyPacket(input,output,fromClient,first,onDrop,writeLock,observe,profile,buffers))break;
                    if(fromClient) Interlocked.Exchange(ref lastClientPacket,Stopwatch.GetTimestamp());
                    else Interlocked.Exchange(ref lastHostPacket,Stopwatch.GetTimestamp());
                    if(first) {if(fromClient) plainClientHello=true;else plainHostHello=true;}
                    first=false;
                }
            }
            catch(Exception e) { if (!stopped) Error=e.Message; }
            finally { Dispose(); }
        }
        public void RequestGracefulStop()
        {
            if (!plainHostHello || host==null || stopped) return;
            bool acquired=false;
            try {
                acquired=Monitor.TryEnter(hostWriteLock,250);
                if (!acquired) { Dispose(); return; }
                byte[] bye={3,0,0,0,0,0x19,1}; host.GetStream().Write(bye,0,bye.Length);
            }
            catch(Exception) { Dispose(); }
            finally { if(acquired) Monitor.Exit(hostWriteLock); }
        }
        public void Dispose()
        {
            stopped=true;
            try { listener.Stop(); } catch(Exception) { }
            try { if(host!=null) host.Close(); } catch(Exception) { }
            try { if(device!=null) device.Close(); } catch(Exception) { }
        }
    }
}
