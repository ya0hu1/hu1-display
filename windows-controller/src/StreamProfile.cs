using System;

namespace DeskMateLearning
{
    // Per-session stream requests, not Windows registry settings. Zero means preserve the
    // Android client's choice on that axis. Only used with an explicitly selected VDD.
    public sealed class StreamProfile
    {
        public static readonly StreamProfile ClientDefault = new StreamProfile(0,0,0);
        public readonly int Width, Height, Fps;
        public bool IsDefault { get { return Width==0 && Fps==0; } }
        public StreamProfile(int width,int height,int fps)
        {
            if(!((width==0 && height==0) || (width==1280 && height==720) || (width==1920 && height==1080)))
                throw new ArgumentOutOfRangeException("width");
            if(fps!=0 && fps!=30 && fps!=60 && fps!=90 && fps!=120)throw new ArgumentOutOfRangeException("fps");
            Width=width;Height=height;Fps=fps;
        }
        internal void Apply(byte[] request)
        {
            // v1 CONFIG_REQ: three little-endian u16s followed by codec, bitrate, quality, flags.
            if(request.Length!=13)throw new System.IO.InvalidDataException("Invalid v1 CONFIG_REQ length.");
            if(Width!=0){Put(request,0,Width);Put(request,2,Height);}
            if(Fps!=0)Put(request,4,Fps);
        }
        private static void Put(byte[] b,int offset,int value){b[offset]=(byte)value;b[offset+1]=(byte)(value>>8);}
        public bool Matches(int w,int h,int fps){return (Width==0 || (w==Width && h==Height)) && (Fps==0 || Fps==fps);}
    }
}
