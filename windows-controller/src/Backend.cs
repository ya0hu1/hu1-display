using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DeskMateLearning
{
    public sealed class CommandResult { public int ExitCode; public string Output,Error; }
    public static class ProcessRunner
    {
        public static string QuoteArgument(string value)
        {
            if(value==null || value.IndexOf('"')>=0 || value.IndexOf('\r')>=0 || value.IndexOf('\n')>=0)
                throw new InvalidOperationException(UiText.Get("进程参数无效。"));
            if(value.Length>0 && !value.Any(Char.IsWhiteSpace))return value;
            int tail=0;for(int i=value.Length-1;i>=0 && value[i]=='\\';i--)tail++;
            return "\""+value+new string('\\',tail)+"\"";
        }
        public static ProcessStartInfo StartInfo(string exe,string[] arguments,Settings settings)
        {
            var si=new ProcessStartInfo(exe,String.Join(" ",arguments.Select(QuoteArgument).ToArray()));
            // An adb daemon may inherit its caller's working directory. Neither the
            // image nor its cwd should hold a handle to the extracted package.
            si.WorkingDirectory=Environment.SystemDirectory;
            si.UseShellExecute=false; si.CreateNoWindow=true;
            si.RedirectStandardOutput=true; si.RedirectStandardError=true;
            if(settings!=null)
            {
                si.EnvironmentVariables["ANYDISPLAY_ADB"]=settings.AdbPath;
                si.EnvironmentVariables["ANYDISPLAY_STORE"]=Path.Combine(settings.StateDirectory,"host.json");
            }
            return si;
        }
        public static CommandResult Run(string exe,string[] arguments,int timeout,Settings settings)
        {
            using(var p=new Process())
            {
                p.StartInfo=StartInfo(exe,arguments,settings); p.Start();
                Task<string> output=p.StandardOutput.ReadToEndAsync(),error=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(timeout)) { p.Kill(); p.WaitForExit(3000); throw new TimeoutException(UiText.Get("命令执行超时。")); }
                if(!output.Wait(2000) || !error.Wait(2000)) throw new TimeoutException(UiText.Get("命令输出未正常结束。"));
                return new CommandResult { ExitCode=p.ExitCode, Output=output.Result, Error=error.Result };
            }
        }
    }
    public sealed class Backend
    {
        private readonly Settings settings;
        private TelemetryStore telemetry=new TelemetryStore();
        internal volatile DesktopDisplay SessionTarget;
        internal volatile bool LayoutReady;
        public TelemetrySnapshot CurrentTelemetry {get{return telemetry.Snapshot();}}
        public Backend(Settings settings) { SafetyPolicy.ValidateSettings(settings); this.settings=settings; }
        private static string Sha256(string file)
        {
            using(var sha=SHA256.Create()) using(var input=File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
        }
        public void VerifyTools()
        {
            if(!File.Exists(settings.EnginePath) || !File.Exists(settings.AdbPath)) throw new FileNotFoundException(UiText.Get("缺少指定版本的视频引擎或 ADB。工作台不会自动下载或安装。"));
            if(Sha256(settings.EnginePath)!=settings.EngineSha256 || Sha256(settings.AdbPath)!=settings.AdbSha256)
                throw new InvalidOperationException(UiText.Get("引擎或 ADB 的校验值不符。"));
            string folder=Path.GetDirectoryName(settings.AdbPath);
            if(Sha256(Path.Combine(folder,"AdbWinApi.dll"))!="ed20abfb8b423029a2278d3947c1a1235774afd1b0e0e08405591e6f1d237a49" ||
                Sha256(Path.Combine(folder,"AdbWinUsbApi.dll"))!="ac658211ac7991ce581f4f2efff86358abdca8eb1bc0f38d7686cbf4215f7c1f")
                throw new InvalidOperationException(UiText.Get("ADB 组件校验失败。"));
            settings.AdbPath=PortableAdb.Stage(settings.AdbPath,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
        private string Adb(params string[] args)
        {
            CommandResult r=ProcessRunner.Run(settings.AdbPath,args,15000,null);
            if(r.ExitCode!=0) throw new InvalidOperationException(UiText.Get("ADB 操作失败：")+r.Error+r.Output);
            return r.Output.Trim();
        }
        public ScanResult Scan()
        {
            VerifyTools(); Directory.CreateDirectory(settings.StateDirectory);
            var result=new ScanResult();
            foreach(DeviceInfo d in SafetyPolicy.ParseDevices(Adb("devices","-l")))
            {
                var approved=settings.ApprovedDevices.FirstOrDefault(a=>a.Serial==d.Serial && a.Model==d.Model);
                d.Trusted=approved!=null;d.Name=approved==null ? null : approved.Name;
                result.Devices.Add(d);
            }
            result.Displays.AddRange(EngineDisplays());
            try{VddPaths.AddChoices(result.Displays);}catch(System.ComponentModel.Win32Exception e){result.PathWarning=e.Message;}
            return result;
        }
        private List<DisplayInfo> EngineDisplays(){
            var monitors=ProcessRunner.Run(settings.EnginePath,new[]{"list","--seconds","0"},20000,settings);
            if(monitors.ExitCode!=0) throw new InvalidOperationException(UiText.Get("引擎读取显示器失败：")+monitors.Error);
            return SafetyPolicy.ParseDisplays(monitors.Output);
        }
        private int BatteryTemperature(string serial)
        {
            string battery=Adb("-s",serial,"shell","dumpsys","battery");
            var m=System.Text.RegularExpressions.Regex.Match(battery,@"(?m)^\s*temperature:\s*(\d+)");
            if(!m.Success) throw new InvalidOperationException(UiText.Get("无法确认设备温度。"));
            return Int32.Parse(m.Groups[1].Value);
        }
        public SessionResult RunSession(DeviceInfo selected,DisplayInfo display,DisplayMode mode,ManualResetEvent stop)
        {
            return RunSession(selected,display,mode,stop,false);
        }
        public SessionResult RunSession(DeviceInfo selected,DisplayInfo display,DisplayMode mode,ManualResetEvent stop,bool continuous)
        {
            return RunSession(selected,display,mode,stop,continuous,StreamProfile.ClientDefault);
        }
        public SessionResult RunSession(DeviceInfo selected,DisplayInfo display,DisplayMode mode,ManualResetEvent stop,bool continuous,StreamProfile profile)
        {
            return RunRoute(selected,display,mode,stop,continuous,profile,false,false);
        }
        internal SessionResult RunRoute(DeviceInfo selected,DisplayInfo display,DisplayMode mode,ManualResetEvent stop,bool continuous,StreamProfile profile,bool retire,bool returnWindows,Action beforeDesktopCleanup=null)
        {
            if(profile==null || (!profile.IsDefault && mode!=DisplayMode.Extend))throw new ArgumentException("Custom stream profile requires extend mode.");
            SafetyPolicy.ValidateDevice(settings,selected);
            if(display==null || !display.Dormant)SafetyPolicy.ValidateDisplay(mode,display);
            else if(!retire || mode!=DisplayMode.Extend || !display.Virtual || display.Primary || display.MonitorPath==null)throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
            using(ScreenSessions.Reserve(selected.Serial,display,mode)){
                var lease=new DesktopLease(display,retire && mode==DisplayMode.Extend,returnWindows && mode==DisplayMode.Extend,beforeDesktopCleanup);
                try{
                    // Engine v0.1.4 takes an index at CONFIG_REQ, not an immutable ID.
                    // Serialize our path changes until CONFIG, not for the lifetime of the stream.
                    return RunOwnedSession(selected,display,mode,stop,continuous,profile,lease);
                }finally{lease.Unlock();}
            }
        }
        sealed class DesktopLease {
            readonly DisplayInfo choice; readonly bool retire,returnWindows; Mutex transition;bool attached;readonly Action beforeCleanup;
            internal DesktopLease(DisplayInfo choice,bool retire,bool returnWindows,Action beforeCleanup){this.choice=choice;this.retire=retire;this.returnWindows=returnWindows;this.beforeCleanup=beforeCleanup;}
            internal void Lock(){if(transition!=null)return;
                var m=new Mutex(false,ScreenSessions.DisplayMutex);
                try{try{m.WaitOne();}catch(AbandonedMutexException){}transition=m;}catch{m.Dispose();throw;}
            }
            internal void Unlock(){if(transition!=null){transition.ReleaseMutex();transition.Dispose();transition=null;}}
            internal void Attach(){if(choice.Dormant){VddPaths.Change(choice.MonitorPath,true);attached=true;}}
            internal void Finish(DesktopDisplay target,bool streamed,SessionResult result){
                // Hide the HUD before Windows can relocate it to a physical screen.
                try{if(beforeCleanup!=null)beforeCleanup();}catch(Exception e){result.CleanupErrors.Add(e.Message);}
                if(!attached && !streamed)return; // A failed start must not retire a previously active desktop.
                if(returnWindows && target!=null){try{int skipped;result.WindowsMoved=WindowHandoff.Move(target,IntPtr.Zero,out skipped);result.WindowsSkipped=skipped;}catch(Exception e){result.CleanupErrors.Add(e.Message);}}
                if(retire && choice.MonitorPath!=null){
                    if(target!=null && !String.Equals(target.MonitorId,choice.MonitorPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                    if(!VddPaths.IsDormant(choice.MonitorPath)){
                        if(target!=null)VddPaths.VerifyOwner(target);
                        VddPaths.Change(choice.MonitorPath,false);
                    }
                    var deadline=Stopwatch.StartNew();
                    while(!VddPaths.IsDormant(choice.MonitorPath)){if(deadline.ElapsedMilliseconds>=2000)throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));Thread.Sleep(100);}
                    result.DesktopRetired=true;
                }
            }
        }
        private SessionResult RunOwnedSession(DeviceInfo selected,DisplayInfo display,DisplayMode mode,ManualResetEvent stop,bool continuous,StreamProfile profile,DesktopLease lease)
        {
            if(stop==null) throw new ArgumentNullException("stop");
            if(stop.WaitOne(0)) throw new OperationCanceledException(UiText.Get("访问手机前已停止。"));
            telemetry=new TelemetryStore();
            SafetyPolicy.ValidateDevice(settings,selected);
            VerifyTools();
            if(Adb("-s",selected.Serial,"get-state")!="device" || Adb("-s",selected.Serial,"shell","getprop","ro.product.model")!=selected.Model)
                throw new InvalidOperationException(UiText.Get("设备身份已变化，已取消连接。"));
            int api=Int32.Parse(Adb("-s",selected.Serial,"shell","getprop","ro.build.version.sdk"));
            if(api<26 || BatteryTemperature(selected.Serial)>=400) throw new InvalidOperationException(UiText.Get("Android 版本或当前设备温度不满足连接条件。"));
            var client=AndroidClient.Installed(pkg=>Adb("-s",selected.Serial,"shell","pm","list","packages",pkg));
            Directory.CreateDirectory(settings.LogDirectory); Directory.CreateDirectory(settings.StateDirectory);
            DesktopDisplay[] before=null;DisplayInfo fresh=null;VddRefreshSession rate=null;
            var result=new SessionResult { StartedAt=DateTimeOffset.Now.ToString("o"), Mode=mode.ToString(),Continuous=continuous };
            Process engine=null; ReadOnlyRelay relay=null; OwnedEngineJob engineJob=null;
            Task stdout=null,stderr=null;
            var outTail=new BoundedTextTail(BoundedTextTail.DefaultLimit);
            var errTail=new BoundedTextTail(BoundedTextTail.DefaultLimit);
            int ownedPort=0; bool started=false,clientOwned=false,requestedStop=false;
            var watch=Stopwatch.StartNew();
            try
            {
                if(stop.WaitOne(0)) throw new OperationCanceledException(UiText.Get("启动手机客户端前已停止。"));
                lease.Lock();
                if(stop.WaitOne(0))throw new OperationCanceledException();
                lease.Attach();
                for(int i=0;i<12;i++){
                    before=WindowsDisplays.Capture();
                    var native=display.MonitorPath==null ? null : before.SingleOrDefault(x=>String.Equals(x.MonitorId,display.MonitorPath,StringComparison.OrdinalIgnoreCase) && x.Virtual && !x.Primary);
                    fresh=EngineDisplays().SingleOrDefault(d=>display.MonitorPath!=null ? native!=null && ScreenSessions.Identity(d)==native.Name : d.Description==display.Description);
                    if(fresh!=null || !display.Dormant)break;
                    if(stop.WaitOne(200))throw new OperationCanceledException();
                }
                SafetyPolicy.ValidateDisplay(mode,fresh);
                SessionTarget=WindowHandoff.FindTarget(before,fresh,mode);
                if(mode==DisplayMode.Extend && SessionTarget==null)throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                rate=new VddRefreshSession(before,fresh,mode,profile.Fps,WindowsDisplays.Capture,WindowsDisplays.SetTemporaryVddRate);
                rate.Apply();
                clientOwned=true;
                Adb("-s",selected.Serial,"shell","am","start","-W","-n",client.Activity);
                string port=Adb("-s",selected.Serial,"forward","tcp:0",client.Endpoint);
                if(!Int32.TryParse(port,out ownedPort) || ownedPort<1024 || ownedPort>65535)
                    throw new InvalidOperationException(UiText.Get("返回的转发端口无效，未清理其他端口。"));
                engine=new Process();
                if(stop.WaitOne(0))throw new OperationCanceledException();
                var launchInventory=WindowsDisplays.Capture();
                if(SessionTarget!=null && !launchInventory.Any(x=>x.Key==SessionTarget.Key && x.AdapterId==SessionTarget.AdapterId && x.Virtual && !x.Primary))throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                var launch=EngineDisplays().SingleOrDefault(d=>ScreenSessions.Identity(d)==ScreenSessions.Identity(fresh));
                SafetyPolicy.ValidateDisplay(mode,launch);result.SelectedDisplay=launch.Description;result.SelectedDisplayIndex=launch.Index;
                relay=new ReadOnlyRelay(ownedPort,profile,mode,delegate {
                    // No CLI/ADB process in a relay callback: it must not outlive portable exit.
                    // Negotiation can change resolution, not the set of attached outputs.
                    var actual=WindowsDisplays.Capture();
                    if(actual.Length!=launchInventory.Length || !launchInventory.All(d=>actual.Any(x=>x.Key==d.Key && x.AdapterId==d.AdapterId && x.Virtual==d.Virtual && x.Primary==d.Primary)))
                        throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                });
                telemetry=relay.Telemetry;
                engine.StartInfo=ProcessRunner.StartInfo(settings.EnginePath,
                    SessionRuntime.EngineArguments(settings,relay.Port,launch.Index,continuous),settings);
                engineJob=new OwnedEngineJob();
                engine.Start(); started=true;
                engineJob.Attach(engine);
                stdout=outTail.DrainAsync(engine.StandardOutput); stderr=errTail.DrainAsync(engine.StandardError);
                watch.Restart();
                while(!engine.WaitForExit(200))
                {
                    if(telemetry.Configured){lease.Unlock();LayoutReady=true;}
                    string reason=SessionRuntime.StopReason(continuous,stop.WaitOne(0),watch.Elapsed.TotalSeconds,
                        settings.WallClockSeconds,relay.HandshakeComplete,relay.ClientIdleSeconds,relay.HostIdleSeconds);
                    if(reason==null && !telemetry.Configured && watch.Elapsed.TotalSeconds>=30)reason="config-timeout";
                    if(!requestedStop && reason!=null)
                    {
                        result.StopReason=reason; result.UserStopped=reason=="user"; result.TimedOut=!result.UserStopped;
                        requestedStop=true; relay.RequestGracefulStop();
                        if(!engine.WaitForExit(5000)) { engine.Kill(); engine.WaitForExit(3000); }
                        break;
                    }
                }
                result.ExitCode=engine.ExitCode;
            }
            catch(Exception e) { result.Error=e.Message; if(e is OperationCanceledException) result.UserStopped=true; }
            finally
            {
                LayoutReady=false;
                try {
                    try{if(started && !engine.HasExited){
                        if(relay!=null) relay.RequestGracefulStop();
                        if(!engine.WaitForExit(5000)) { engine.Kill(); engine.WaitForExit(3000); }
                    }}catch(Exception e){result.CleanupErrors.Add(e.Message);}
                    try{if(engineJob!=null)engineJob.Dispose();}catch(Exception e){result.CleanupErrors.Add(e.Message);}
                    if(relay!=null)
                    {
                        result.BlockedInputFrames=Interlocked.Read(ref relay.InputBlocked);
                        result.BlockedClipboardFrames=Interlocked.Read(ref relay.ClipboardBlocked);
                        if(relay.Error!=null && result.Error==null) result.Error="Relay: "+relay.Error;
                        relay.Dispose();
                    }
                } finally {
                    lease.Lock();
                    try{if(rate!=null)rate.Restore();
                        var now=WindowsDisplays.Capture();
                        result.DisplayLayoutUnchanged=before!=null && fresh!=null && ScreenSessions.SameRouteLayout(before,now,ScreenSessions.Identity(fresh));
                        result.DisplayModesRestored=result.DisplayLayoutUnchanged || (mode==DisplayMode.Extend && before!=null && fresh!=null && ScreenSessions.SameRouteLayout(before,now,ScreenSessions.Identity(fresh),true));
                    }catch(Exception e){result.CleanupErrors.Add(e.Message);}
                    try{lease.Finish(SessionTarget,telemetry.Configured,result);}catch(Exception e){result.CleanupErrors.Add(e.Message);}
                    lease.Unlock();
                    if(clientOwned) try { Adb("-s",selected.Serial,"shell","am","force-stop",client.Package); }
                        catch(Exception e) { result.CleanupErrors.Add(e.Message); }
                    if(ownedPort>=1024 && ownedPort<=65535) try { Adb(SafetyPolicy.RemoveOwnedForward(selected.Serial,ownedPort)); }
                        catch(Exception e) { result.CleanupErrors.Add(e.Message); }
                }
            }
            try {
                if(stdout!=null && !stdout.Wait(2000)) result.CleanupErrors.Add("stdout drain did not finish within timeout.");
                if(stderr!=null && !stderr.Wait(2000)) result.CleanupErrors.Add("stderr drain did not finish within timeout.");
            } catch(Exception e) {result.CleanupErrors.Add("Log drain: "+e.Message);}
            result.HostStdout=outTail.Snapshot(); result.HostStderr=errTail.Snapshot();
            result.Telemetry=telemetry.Snapshot();
            result.LogTailTruncated=outTail.Truncated || errTail.Truncated;
            result.ElapsedSeconds=watch.Elapsed.TotalSeconds;
            if(result.StopReason==null) result.StopReason=result.Error!=null ? "error" : "engine-ended";
            result.EndedAt=DateTimeOffset.Now.ToString("o");
            result.LogFile=Path.Combine(settings.LogDirectory,"controller-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".json");
            File.WriteAllText(result.LogFile,new JavaScriptSerializer().Serialize(result));
            if(engine!=null) engine.Dispose();
            return result;
        }
    }
}
