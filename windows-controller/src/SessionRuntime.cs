using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DeskMateLearning
{
    // Daily sessions omit --seconds entirely: upstream treats --seconds 0 as an immediate deadline.
    public static class SessionRuntime
    {
        public static string[] EngineArguments(Settings settings,int relayPort,int displayIndex,bool continuous)
        {
            SafetyPolicy.ValidateSettings(settings);
            if(relayPort<1024 || relayPort>65535 || displayIndex<0) throw new ArgumentOutOfRangeException();
            var args=new List<string> {"connect","127.0.0.1:"+relayPort,"--monitor",displayIndex.ToString(),
                "--codec","h264","--no-audio","--no-clipboard"};
            if(!continuous) args.AddRange(new[]{"--seconds",settings.StreamingSeconds.ToString(),"--verbose"});
            return args.ToArray();
        }
        public static string StopReason(bool continuous,bool userStop,double elapsed,int wallSeconds,
            bool handshake,double clientIdle,double hostIdle)
        {
            if(userStop) return "user";
            if(!continuous) return elapsed>=wallSeconds ? "timed-test-wall-clock" : null;
            if(!handshake) return elapsed>=30 ? "handshake-timeout" : null;
            // Complete control packets, not video frame rate: a static desktop is valid.
            if(clientIdle>=15 || hostIdle>=15) return "transport-idle-timeout";
            return null;
        }
    }

    // Bounded in-memory log tail. The pump continuously drains the child pipe without retaining
    // hours of DEBUG output or reading an unbounded single line into memory.
    public sealed class BoundedTextTail
    {
        public const int DefaultLimit=65536;
        private readonly int limit;
        private readonly StringBuilder text=new StringBuilder();
        private readonly object gate=new object();
        public long TotalCharacters { get; private set; }
        public BoundedTextTail(int limit)
        {
            if(limit<1 || limit>1024*1024) throw new ArgumentOutOfRangeException("limit");
            this.limit=limit;
        }
        public void Append(char[] buffer,int count)
        {
            if(buffer==null || count<0 || count>buffer.Length) throw new ArgumentOutOfRangeException("count");
            lock(gate)
            {
                TotalCharacters+=count;
                if(count>=limit) {text.Clear();text.Append(buffer,count-limit,limit);}
                else {int excess=text.Length+count-limit;if(excess>0)text.Remove(0,excess);text.Append(buffer,0,count);}
            }
        }
        public string Snapshot() { lock(gate) return text.ToString(); }
        public bool Truncated { get {lock(gate)return TotalCharacters>limit;} }
        public Task DrainAsync(TextReader reader)
        {
            return Task.Run(delegate {
                char[] buffer=new char[4096];int count;
                while((count=reader.Read(buffer,0,buffer.Length))>0) Append(buffer,count);
            });
        }
    }
}
