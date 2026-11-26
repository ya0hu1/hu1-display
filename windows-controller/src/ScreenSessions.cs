using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DeskMateLearning
{
    // A dedicated owner thread holds the legacy interprocess mutex while any local route
    // exists. Releasing the first of two sessions must not let an older controller in.
    internal static class ScreenSessions
    {
        internal const string DisplayMutex=@"Local\Huiying.DisplayLayout";
        private static readonly Registry production=new Registry(@"Local\DeskMateLearning.UsbSession");
        internal static IDisposable Reserve(string serial,DisplayInfo d,DisplayMode mode){return production.Reserve(serial,d,mode);}
        internal static EventWaitHandle LivePermit(string nonce,string monitor,bool issue){return production.LivePermit(nonce,monitor,issue);}
        internal static string Identity(DisplayInfo d){return d==null ? "" : (d.Description??"").Split(' ')[0];}
        internal static bool MayAdd(IEnumerable<string> devices,IEnumerable<string> displays,string device,string screen,bool mirror,bool existingMirror){
            return !String.IsNullOrEmpty(device) && !String.IsNullOrEmpty(screen) && !devices.Contains(device,StringComparer.OrdinalIgnoreCase) && !displays.Contains(screen,StringComparer.OrdinalIgnoreCase) && !existingMirror && (!mirror || !devices.Any());
        }
        internal sealed class Registry {
            readonly object gate=new object();readonly Dictionary<string,string> routes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            ManualResetEvent finished;Thread owner;internal readonly string MutexName;
            internal Registry(string mutexName){MutexName=mutexName;}
            internal IDisposable Reserve(string serial,DisplayInfo d,DisplayMode mode){
                lock(gate){var id=d==null ? null : d.RouteKey;bool mirror=mode==DisplayMode.Mirror;
                    if(!MayAdd(routes.Keys,routes.Values,serial,id,mirror,routes.ContainsValue("#mirror")))throw new InvalidOperationException(UiText.Get("设备或屏幕已有会话。"));
                    if(routes.Count==0){finished=new ManualResetEvent(false);bool acquired=false;
                        using(var ready=new ManualResetEvent(false)){
                            owner=new Thread(delegate(){using(var m=new Mutex(false,MutexName)){
                                try{try{acquired=m.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}}
                                finally{ready.Set();}if(acquired){try{finished.WaitOne();}finally{m.ReleaseMutex();}}}});
                            owner.IsBackground=true;owner.Start();ready.WaitOne();
                        }
                        if(!acquired){owner.Join();finished.Dispose();throw new InvalidOperationException(UiText.Get("另一个会话正在运行，请先在其窗口中停止。"));}
                    }
                    routes.Add(serial,mirror?"#mirror":id);return new Ticket(this,serial);
                }
            }
            sealed class Ticket:IDisposable {
                readonly Registry registry;string serial;
                internal Ticket(Registry registry,string serial){this.registry=registry;this.serial=serial;}
                public void Dispose(){lock(registry.gate){if(serial==null)return;registry.routes.Remove(serial);serial=null;
                    if(registry.routes.Count==0){registry.finished.Set();registry.owner.Join();registry.finished.Dispose();}}}
            }
            // Only our own reserved route can invite a watchdog to move that monitor. A saved
            // request is not a reusable bypass for the legacy (whole-session) interprocess lock.
            internal EventWaitHandle LivePermit(string nonce,string monitor,bool issue){
                Guid id;if(!Guid.TryParseExact(nonce,"N",out id) || String.IsNullOrEmpty(monitor))throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                string key;using(var sha=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(nonce+"|"+monitor))).Replace("-","");
                string name=@"Local\Huiying.Position."+key;
                if(!issue)return EventWaitHandle.OpenExisting(name);
                lock(gate){if(!routes.Values.Contains(monitor,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                    bool created;var signal=new EventWaitHandle(true,EventResetMode.ManualReset,name,out created);
                    if(!created){signal.Dispose();throw new InvalidOperationException("Layout request already exists.");}return signal;}
            }
        }
        // Other virtual screens may independently start/stop a different refresh rate.
        // Identity, position and orientation of every screen, and all physical/owned modes, still matter.
        internal static bool SameRouteLayout(DesktopDisplay[] before,DesktopDisplay[] after,string owned,bool allowVddPosition=false){
            // A peer route may add/remove its own VDD altogether. Never discount changes
            // to the selected display (including its adapter) or to a physical/primary screen.
            Func<DesktopDisplay,bool> relevant=d=>!d.Virtual || d.Primary || d.Name==owned;
            var a=before.Where(relevant).ToArray();var b=after.Where(relevant).ToArray();
            if(allowVddPosition){b=LayoutPolicy.Clone(b);foreach(var d in b.Where(x=>x.Virtual && !x.Primary && x.Name==owned)){
                var prior=a.FirstOrDefault(x=>x.Key==d.Key);if(prior!=null){d.X=prior.X;d.Y=prior.Y;}
            }}
            return LayoutPolicy.Same(a,b,true) && a.All(d=>b.Any(x=>x.Key==d.Key && x.AdapterId==d.AdapterId));
        }
    }
}
