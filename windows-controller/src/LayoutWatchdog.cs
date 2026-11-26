using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace DeskMateLearning
{
    public sealed class LayoutRequest {public DesktopDisplay[] Before,After;public string CreatedAt,Language,LiveMonitor,Permit;}
    public sealed class LayoutStatus {public string Phase,Message,UpdatedAt;public bool LayoutChanged,Restored,Kept;}
    public static class LayoutWatchdog
    {
        private static void Status(string dir,string phase,string message,bool changed,bool restored,bool kept)
        {StudioServices.AtomicJson(Path.Combine(dir,"status.json"),new LayoutStatus{Phase=phase,Message=message,LayoutChanged=changed,Restored=restored,Kept=kept,UpdatedAt=DateTimeOffset.Now.ToString("o")});}
        public static int Run(string requestFile)
        {
            string dir=Path.GetDirectoryName(Path.GetFullPath(requestFile));bool changed=false;
            LayoutRequest request=null;bool live=false;EventWaitHandle grant=null;Action rollback=null;
            using(var session=new Mutex(false,"Local\\DeskMateLearning.UsbSession"))
            using(var layout=new Mutex(false,ScreenSessions.DisplayMutex)){
                bool sessionHeld=false,layoutHeld=false;
                try{
                    request=new JavaScriptSerializer().Deserialize<LayoutRequest>(File.ReadAllText(requestFile));
                    if(request==null)throw new InvalidDataException(UiText.Get("布局请求无效。"));
                    if(request.Language!=null)UiText.SetLanguage(request.Language);
                    live=request.LiveMonitor!=null;
                    try{layoutHeld=layout.WaitOne(0);}catch(AbandonedMutexException){layoutHeld=true;}
                    if(!layoutHeld)throw new InvalidOperationException(UiText.Get("显示布局正在切换，请稍后重试。"));
                    try{sessionHeld=session.WaitOne(0);}catch(AbandonedMutexException){sessionHeld=true;}
                    if(live){
                        LayoutPolicy.LiveMove(request.Before,request.After,request.LiveMonitor);
                        if(sessionHeld)throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                        grant=ScreenSessions.LivePermit(request.Permit,request.LiveMonitor,false);
                        if(!grant.WaitOne(0))throw new InvalidOperationException(UiText.Get("请重新同步虚拟屏。"));
                    }else if(!sessionHeld)throw new InvalidOperationException(UiText.Get("副屏会话正在运行，请先停止再调整布局。"));
                    LayoutPolicy.Validate(request.Before,request.After);
                    if(!LayoutPolicy.Same(WindowsDisplays.Capture(),request.Before,true))throw new InvalidOperationException(UiText.Get("Windows布局已变化，取消过期的操作。"));
                    StudioServices.AtomicJson(Path.Combine(dir,"original.json"),request.Before);
                    Status(dir,"applying",UiText.Get("正在应用位置；不更改模式或主屏。"),false,false,false);
                    rollback=delegate {
                        var actual=WindowsDisplays.Capture();if(LayoutPolicy.Same(actual,request.Before,true))return;
                        if(live){ // Never undo a later external move or write to a replacement target.
                            if(!LayoutPolicy.Same(actual,request.After,true))throw new InvalidOperationException(UiText.Get("设备/模式有变化，拒绝盲目恢复。"));
                            VddPaths.Position(request.After,request.Before,request.LiveMonitor);
                        }else{
                            if(!LayoutPolicy.Same(actual,request.Before,false))throw new InvalidOperationException(UiText.Get("设备/模式有变化，拒绝盲目恢复。"));
                            WindowsDisplays.StageAndCommit(request.Before);
                        }
                    };
                    changed=true;
                    if(live)VddPaths.Position(request.Before,request.After,request.LiveMonitor);else WindowsDisplays.StageAndCommit(request.After);
                    if(!LayoutPolicy.Same(WindowsDisplays.Capture(),request.After,true))throw new InvalidOperationException(UiText.Get("Windows实际位置与草稿不一致，将还原。"));
                    Status(dir,"confirm",UiText.Get("15秒内确认保留，否则自动还原。"),true,false,false);
                    var watch=Stopwatch.StartNew();
                    while(watch.Elapsed.TotalSeconds<15){
                        if((live && !grant.WaitOne(0)) || File.Exists(Path.Combine(dir,"cancel")))break;
                        if(File.Exists(Path.Combine(dir,"keep"))){
                            if(!LayoutPolicy.Same(WindowsDisplays.Capture(),request.After,true))throw new InvalidOperationException(UiText.Get("确认时Windows布局发生变化，不保留过期草稿。"));
                            Status(dir,"kept",UiText.Get("已保留Windows显示位置。"),true,false,true);return 0;
                        }
                        Thread.Sleep(100);
                    }
                    // UI failure/close/no answer all take this route, independently of the UI process.
                    rollback();
                    bool restored=LayoutPolicy.Same(WindowsDisplays.Capture(),request.Before,true);
                    Status(dir,restored ? "restored" : "error",restored ? UiText.Get("未保留，已自动还原原布局。") : UiText.Get("还原后检查不一致，请打开Windows显示设置。"),true,restored,false);
                    return restored ? 0 : 1;
                }
                catch(Exception e){
                    string message=e.Message;bool restored=false;
                    if(changed && request!=null){
                        try{
                            rollback();restored=LayoutPolicy.Same(WindowsDisplays.Capture(),request.Before,true);
                        }catch(Exception restore){message+=UiText.Get("\n还原提示：")+restore.Message;}
                    }
                    try{Status(dir,"error",message,changed,restored,false);}catch(Exception){}
                    return 1;
                }
                finally{if(grant!=null)grant.Dispose();if(layoutHeld)layout.ReleaseMutex();if(sessionHeld)session.ReleaseMutex();}
            }
        }
    }
}
