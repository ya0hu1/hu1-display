using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DeskMateLearning
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if(args.Length==2 && args[0]=="--layout-watchdog")return LayoutWatchdog.Run(args[1]);
            if(args.Length==2 && args[0]=="--layout-self-test")return LayoutTests.Run(args[1]);
            if(args.Length==2 && args[0]=="--vdd-path-check")return VddPaths.Check(args[1]);
            if(args.Length==2 && args[0]=="--display-inventory") {StudioServices.AtomicJson(args[1],WindowsDisplays.Capture());return 0;}
            if(args.Length==1 && args[0]=="--job-test-child") {Thread.Sleep(60000);return 0;}
            if(args.Length==2 && args[0]=="--job-self-test") return JobLifetimeTests.Run(args[1]);
            if(args.Length==2 && args[0]=="--self-test") return SelfTests.Run(args[1]);
            if(args.Length==2 && args[0]=="--relay-self-test") return RelayLoopbackTests.Run(args[1]);
            string config=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"appsettings.local.json");
            if(args.Length==2 && args[0]=="--config") config=args[1];
            bool smoke=args.Length==3 && (args[0]=="--smoke-test" || args[0]=="--extend-smoke-test" || args[0]=="--continuous-check") && args[1]=="--config";
            bool continuousCheck=smoke && args[0]=="--continuous-check";
            DisplayMode smokeMode=smoke && args[0]!="--smoke-test" ? DisplayMode.Extend : DisplayMode.Mirror;
            if(smoke) config=args[2];
            bool uiTest=args.Length==2 && args[0]=="--ui-self-test";
            try {
                // UI tests can exercise preference persistence, but never with a real trust store.
                Settings settings;
                if(uiTest){
                    string state=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"ui-state-"+Guid.NewGuid().ToString("N"));
                    settings=new Settings {StateDirectory=state,LogDirectory=Path.Combine(state,"logs"),ApprovedDevices=new ApprovedDevice[0]};
                }else settings=StudioServices.Load(config);
                SafetyPolicy.ValidateSettings(settings);
                if(smoke) {
                    var backend=new Backend(settings); var scan=backend.Scan();
                    var device=scan.Devices.Single(d=> d.State=="device" && settings.ApprovedDevices.Any(a=>a.Serial==d.Serial && a.Model==d.Model));
                    var display=SafetyPolicy.SelectSmokeDisplay(smokeMode,scan.Displays);
                    using(var stop=new ManualResetEvent(false)) {
                        using(var timer=new System.Threading.Timer(delegate {if(continuousCheck) stop.Set();},null,
                            continuousCheck ? 95000 : Timeout.Infinite,Timeout.Infinite))
                        {
                        SessionResult r=backend.RunSession(device,display,smokeMode,stop,continuousCheck);
                        bool durationOk=!continuousCheck || (r.UserStopped && r.Continuous && r.ElapsedSeconds>=80);
                        return durationOk && r.ExitCode==0 && r.Error==null && r.DisplayLayoutUnchanged && r.CleanupErrors.Count==0 && !r.TimedOut ? 0 : 1;
                        }
                    }
                }
                bool liveTest=args.Length==2 && args[0]=="--studio-live-check";
                bool livePassed=false;
                if(args.Length!=0 && !(args.Length==2 && args[0]=="--config") && !uiTest && !liveTest) throw new InvalidOperationException(UiText.Get("无法识别启动参数。"));
                var app=new System.Windows.Application();
                app.ShutdownMode=System.Windows.ShutdownMode.OnMainWindowClose;
                var studio=new StudioWindow(settings,uiTest);
                if(uiTest) studio.Window.Loaded+=delegate {studio.Window.Dispatcher.BeginInvoke(new Action(delegate {
                    StudioUiTests.Run(studio,Path.GetFullPath(args[1]),settings.StateDirectory);studio.Window.Close();
                }),System.Windows.Threading.DispatcherPriority.ApplicationIdle);};
                if(liveTest)studio.Window.Loaded+=async delegate {livePassed=await studio.VerifyLive(args[1]);studio.Window.Close();};
                app.Run(studio.Window); return liveTest ? (livePassed ? 0 : 1) : uiTest ? StudioUiTests.LastExitCode : 0;
            }
            catch(Exception e) {
                if(uiTest){File.WriteAllText(args[1],"UI self-test startup failed: "+e);return 1;}
                if(smoke) { File.WriteAllText(config+".error.txt",e.ToString()); return 1; }
                MessageBox.Show(e.Message,UiText.Get("晖映 未执行操作"),MessageBoxButtons.OK,MessageBoxIcon.Warning); return 1;
            }
        }
    }
}
