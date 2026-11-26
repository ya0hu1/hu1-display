using System;
using System.IO;

namespace DeskMateLearning
{
    // The Windows adb server outlives its CLI. Run the pinned tools from a versioned
    // per-user cache, never leave a server mapping the removable application directory.
    internal static class PortableAdb
    {
        static readonly string[] names={"adb.exe","AdbWinApi.dll","AdbWinUsbApi.dll"};
        static readonly string[] hashes={"bae5ce98846b19caa74a34900850caa8b21d01976932b2ad83baeb5de5651aee","ed20abfb8b423029a2278d3947c1a1235774afd1b0e0e08405591e6f1d237a49","ac658211ac7991ce581f4f2efff86358abdca8eb1bc0f38d7686cbf4215f7c1f"};
        static readonly object gate=new object();
        internal static string Stage(string source,string localAppData){
            lock(gate){
                string dir=Path.Combine(localAppData,"DeskLinkStudio","runtime","adb-"+hashes[0].Substring(0,16));Directory.CreateDirectory(dir);
                for(int i=0;i<names.Length;i++){
                    string input=Path.Combine(Path.GetDirectoryName(source),names[i]),output=Path.Combine(dir,names[i]);
                    if(!File.Exists(output)){
                        string tmp=output+"."+Guid.NewGuid().ToString("N")+".tmp";
                        try{File.Copy(input,tmp,false);if(StudioServices.Hash(tmp)!=hashes[i])throw new InvalidDataException(UiText.Get("ADB 组件校验失败。"));
                            try{File.Move(tmp,output);}catch(IOException){if(!File.Exists(output))throw;}}
                        finally{if(File.Exists(tmp))File.Delete(tmp);}
                    }
                    if(StudioServices.Hash(output)!=hashes[i])throw new InvalidDataException(UiText.Get("ADB 组件校验失败。"));
                }
                return Path.Combine(dir,names[0]);
            }
        }
    }
}
