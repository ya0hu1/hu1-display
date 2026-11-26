using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DeskMateLearning
{
    public sealed class TelemetrySnapshot
    {
        public bool HasConfig,HasStats,Fresh;
        public int Width,Height,TargetHz;
        public string Codec;
        public double? Fps,Mbps,RttMs,DecodeMs;
        public uint LastDecoded,LastDropped;
        public ulong TotalDropped,TotalDecoded;
        public double[] History=new double[0];
    }
    // Values come from protocol CONFIG/STATS, not the negotiated refresh rate or fabricated UI data.
    public sealed class TelemetryStore
    {
        private readonly object gate=new object();
        private readonly Queue<double> history=new Queue<double>();
        private bool config,stats;
        internal bool Configured {get{lock(gate)return config;}}
        private int width,height,hz;
        private string codec;
        private double lastAt;
        private double? fps,mbps,rtt,decode;
        private uint decoded,dropped;
        private ulong totalDropped,totalDecoded;
        private static uint U32(byte[] b,int n) {return (uint)(b[n]|b[n+1]<<8|b[n+2]<<16|b[n+3]<<24);}
        private static int U16(byte[] b,int n) {return b[n]|b[n+1]<<8;}
        public static double Now {get{return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}}
        public void Observe(int type,byte[] body,bool fromClient) {ObserveAt(type,body,fromClient,Now);}
        internal void ObserveAt(int type,byte[] body,bool fromClient,double now)
        {
            if(body==null)return;
            lock(gate)
            {
                if(!fromClient && type==0x12 && body.Length==24)
                {
                    width=U16(body,4);height=U16(body,6);hz=U16(body,8);
                    codec=body[10]==1 ? "H.264" : body[10]==2 ? "HEVC" : body[10]==3 ? "AV1" : UiText.Get("未知");
                    config=width>0 && height>0 && hz>0;
                }
                if(fromClient && type==0x15 && body.Length==20)
                {
                    double interval=now-lastAt;
                    fps=stats && interval>=0.1 && interval<=10 ? (double?) (U32(body,0)/interval) : null;
                    mbps=stats && interval>=0.1 && interval<=10 ? (double?) (U32(body,16)*8.0/interval/1000000) : null;
                    decoded=U32(body,0);totalDecoded+=decoded;dropped=U32(body,4);totalDropped+=dropped;
                    decode=decoded>0 ? (double?)(U32(body,8)/1000.0) : null;
                    rtt=U32(body,12)>0 ? (double?)(U32(body,12)/1000.0) : null;
                    lastAt=now;stats=true;
                    if(fps.HasValue){history.Enqueue(fps.Value);while(history.Count>60)history.Dequeue();}
                }
            }
        }
        public TelemetrySnapshot Snapshot() {return SnapshotAt(Now);}
        internal TelemetrySnapshot SnapshotAt(double now)
        {
            lock(gate){return new TelemetrySnapshot {HasConfig=config,HasStats=stats,Fresh=stats && now-lastAt<=3,
                Width=width,Height=height,TargetHz=hz,Codec=codec,Fps=fps,Mbps=mbps,RttMs=rtt,DecodeMs=decode,
                LastDecoded=decoded,LastDropped=dropped,TotalDropped=totalDropped,TotalDecoded=totalDecoded,History=history.ToArray()};}
        }
    }
}
