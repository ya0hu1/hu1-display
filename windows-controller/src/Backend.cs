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
            var monitors=ProcessRunner.Run(settings.EnginePath,new[]{"list","--seconds","0"},20000,settings);
            if(monitors.ExitCode!=0) throw new InvalidOperationException(UiText.Get("引擎读取显示器失败：")+monitors.Error);
            result.Displays.AddRange(SafetyPolicy.ParseDisplays(monitors.Output));
            return result;
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
            if(profile==null || (!profile.IsDefault && mode!=DisplayMode.Extend))throw new ArgumentException("Custom stream profile requires extend mode.");
            // Cross-process guard protects the one-connection phone client and owned-forward lifecycle.
            using(var lease=new Mutex(false,"Local\\DeskMateLearning.UsbSession"))
            {
                bool acquired=false;
                try {
                    try {acquired=lease.WaitOne(0);} catch(AbandonedMutexException) {acquired=true;}
                    if(!acquired) throw new InvalidOperationException(UiText.Get("另一个会话正在运行，请先在其窗口中停止。"));
                    return RunOwnedSession(selected,display,mode,stop,continuous,profile);
                }
                finally {if(acquired) lease.ReleaseMutex();}
            }
        }
        private SessionResult RunOwnedSession(DeviceInfo selected,DisplayInfo display,DisplayMode mode,ManualResetEvent stop,bool continuous,StreamProfile profile)
        {
            if(stop==null) throw new ArgumentNullException("stop");
            if(stop.WaitOne(0)) throw new OperationCanceledException(UiText.Get("访问手机前已停止。"));
            telemetry=new TelemetryStore();
            SafetyPolicy.ValidateDevice(settings,selected); SafetyPolicy.ValidateDisplay(mode,display);
            VerifyTools();
            if(Adb("-s",selected.Serial,"get-state")!="device" || Adb("-s",selected.Serial,"shell","getprop","ro.product.model")!=selected.Model)
                throw new InvalidOperationException(UiText.Get("设备身份已变化，已取消连接。"));
            int api=Int32.Parse(Adb("-s",selected.Serial,"shell","getprop","ro.build.version.sdk"));
            if(api<26 || BatteryTemperature(selected.Serial)>=400) throw new InvalidOperationException(UiText.Get("Android 版本或当前设备温度不满足连接条件。"));
            var current=Scan();
            DisplayInfo fresh=current.Displays.SingleOrDefault(d=>d.Index==display.Index && d.Description==display.Description);
            SafetyPolicy.ValidateDisplay(mode,fresh);
            if(Adb("-s",selected.Serial,"shell","pm","list","packages","dev.anydisplay.client")!="package:dev.anydisplay.client")
                throw new InvalidOperationException(UiText.Get("未检测到手机客户端。工作台不会自动安装 APK。"));
            Directory.CreateDirectory(settings.LogDirectory); Directory.CreateDirectory(settings.StateDirectory);
            var before=WindowsDisplays.Capture();
            var result=new SessionResult { StartedAt=DateTimeOffset.Now.ToString("o"), Mode=mode.ToString(),
                SelectedDisplay=display.Description, SelectedDisplayIndex=display.Index, Continuous=continuous };
            Process engine=null; ReadOnlyRelay relay=null; OwnedEngineJob engineJob=null;
            Task stdout=null,stderr=null;
            var outTail=new BoundedTextTail(BoundedTextTail.DefaultLimit);
            var errTail=new BoundedTextTail(BoundedTextTail.DefaultLimit);
            int ownedPort=0; bool started=false,clientOwned=false,requestedStop=false;
            var watch=Stopwatch.StartNew();
            try
            {
                if(stop.WaitOne(0)) throw new OperationCanceledException(UiText.Get("启动手机客户端前已停止。"));
                clientOwned=true;
                Adb("-s",selected.Serial,"shell","am","start","-W","-n","dev.anydisplay.client/.ui.MainActivity");
                string port=Adb("-s",selected.Serial,"forward","tcp:0","tcp:47820");
                if(!Int32.TryParse(port,out ownedPort) || ownedPort<1024 || ownedPort>65535)
                    throw new InvalidOperationException(UiText.Get("返回的转发端口无效，未清理其他端口。"));
                relay=new ReadOnlyRelay(ownedPort,profile);
                telemetry=relay.Telemetry;
                engine=new Process();
                engine.StartInfo=ProcessRunner.StartInfo(settings.EnginePath,
                    SessionRuntime.EngineArguments(settings,relay.Port,display.Index,continuous),settings);
                engineJob=new OwnedEngineJob();
                engine.Start(); started=true;
                engineJob.Attach(engine);
                stdout=outTail.DrainAsync(engine.StandardOutput); stderr=errTail.DrainAsync(engine.StandardError);
                watch.Restart();
                while(!engine.WaitForExit(200))
                {
                    string reason=SessionRuntime.StopReason(continuous,stop.WaitOne(0),watch.Elapsed.TotalSeconds,
                        settings.WallClockSeconds,relay.HandshakeComplete,relay.ClientIdleSeconds,relay.HostIdleSeconds);
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
                if(started && !engine.HasExited)
                {
                    if(relay!=null) relay.RequestGracefulStop();
                    if(!engine.WaitForExit(5000)) { engine.Kill(); engine.WaitForExit(3000); }
                }
                if(engineJob!=null) engineJob.Dispose();
                if(relay!=null)
                {
                    result.BlockedInputFrames=Interlocked.Read(ref relay.InputBlocked);
                    result.BlockedClipboardFrames=Interlocked.Read(ref relay.ClipboardBlocked);
                    if(relay.Error!=null && result.Error==null) result.Error="Relay: "+relay.Error;
                    relay.Dispose();
                }
                if(clientOwned) try { Adb("-s",selected.Serial,"shell","am","force-stop","dev.anydisplay.client"); }
                    catch(Exception e) { result.CleanupErrors.Add(e.Message); }
                if(ownedPort>=1024 && ownedPort<=65535) try { Adb(SafetyPolicy.RemoveOwnedForward(selected.Serial,ownedPort)); }
                    catch(Exception e) { result.CleanupErrors.Add(e.Message); }
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
            result.DisplayLayoutUnchanged=LayoutPolicy.Same(before,WindowsDisplays.Capture(),true);
            result.EndedAt=DateTimeOffset.Now.ToString("o");
            result.LogFile=Path.Combine(settings.LogDirectory,"controller-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
            File.WriteAllText(result.LogFile,new JavaScriptSerializer().Serialize(result));
            if(engine!=null) engine.Dispose();
            return result;
        }
    }
}
