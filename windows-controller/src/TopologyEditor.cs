using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Web.Script.Serialization;

namespace DeskMateLearning
{
    public sealed class TopologyEditor
    {
        private readonly Window owner;
        private readonly Settings settings;
        private readonly Action<string> log;
        private readonly Func<bool> busy,sessionActive;
        private readonly Func<DesktopDisplay,bool> liveOwner;
        private string lastAccess;
        private readonly Canvas canvas;
        private DesktopDisplay[] before=new DesktopDisplay[0],draft=new DesktopDisplay[0];
        private string selected;
        private double scale=1,ox,oy;
        private bool applying,dragging,stale;
        private Point dragOrigin;
        private DesktopDisplay dragDisplay;
        private int dragX,dragY;
        public DesktopDisplay[] CurrentDraft {get{return LayoutPolicy.Clone(draft);}}
        internal DesktopDisplay[] LiveDisplays {get;private set;}
        public bool IsApplying {get{return applying;}}
        public event Action ApplyingChanged;
        public TopologyEditor(Window owner,Settings settings,Action<string> log,Func<bool> busy,Func<bool> sessionActive=null,Func<DesktopDisplay,bool> liveOwner=null)
        {
            LiveDisplays=new DesktopDisplay[0];this.owner=owner;this.settings=settings;this.log=log;this.busy=busy;this.sessionActive=sessionActive ?? (()=>false);this.liveOwner=liveOwner ?? (d=>false);
            canvas=C<Canvas>("TopologyCanvas");canvas.SizeChanged+=delegate {Draw();};
            Bind("LayoutRefresh",Refresh);
            C<Button>("LayoutApply").Click+=async delegate {try{await Apply();}catch(Exception e){log(e.Message);MessageBox.Show(owner,e.Message,UiText.Get("显示布局"),MessageBoxButton.OK,MessageBoxImage.Warning);}};Bind("LayoutReset",delegate {if(stale)Load(WindowsDisplays.Capture());else Load(before);});
            Bind("LayoutIdentify",Identify);
            foreach(string side in new[]{"left","right","top","bottom"}){string s=side;Bind("Layout"+side,delegate {Move(s);});}
            canvas.MouseMove+=Drag;canvas.MouseLeftButtonUp+=EndDrag;
            canvas.LostMouseCapture+=delegate {dragging=false;};
            C<ComboBox>("TopologyPicker").SelectionChanged+=delegate {var d=C<ComboBox>("TopologyPicker").SelectedItem as DesktopDisplay;if(d!=null){selected=d.Key;Draw();}};
        }
        private T C<T>(string id)where T:class{return owner.FindName(id)as T;}
        private void Bind(string id,Action action){C<Button>(id).Click+=delegate {try{action();}catch(Exception e){log(e.Message);MessageBox.Show(owner,e.Message,UiText.Get("显示布局"),MessageBoxButton.OK,MessageBoxImage.Warning);}};}
        public void Refresh()
        {
            LiveDisplays=new DesktopDisplay[0];
            if(applying)return;
            try{LiveDisplays=WindowsDisplays.Capture();}catch(Exception e){log(e.Message);return;}
            if(draft.Length>0 && !LayoutPolicy.Same(before,draft,true) && MessageBox.Show(owner,UiText.Get("丢弃尚未应用的布局草稿，重新读取Windows？"),UiText.Get("重新读取"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            Load(LiveDisplays);log(UiText.Get("已读取Windows真实显示布局：")+before.Length+UiText.Get("块活动桌面，包含实体副屏。"));
        }
        public void Load(DesktopDisplay[] actual)
        {
            LiveDisplays=LayoutPolicy.Clone(actual);stale=false;before=LayoutPolicy.Clone(actual);draft=LayoutPolicy.Clone(actual);
            if(!draft.Any(d=>d.Key==selected))selected=(draft.FirstOrDefault(d=>d.Virtual) ?? draft.FirstOrDefault())==null ? null : (draft.FirstOrDefault(d=>d.Virtual) ?? draft.First()).Key;
            C<ComboBox>("TopologyPicker").ItemsSource=draft;C<ComboBox>("TopologyPicker").SelectedItem=draft.FirstOrDefault(d=>d.Key==selected);Draw();
        }
        public void WindowsChanged()
        {
            try{LiveDisplays=WindowsDisplays.Capture();}catch(Exception e){LiveDisplays=new DesktopDisplay[0];stale=true;CancelDrag();Draw();log(e.Message);return;}
            if(applying || LayoutPolicy.Same(before,LiveDisplays,true))return;
            bool edited=dragging || !LayoutPolicy.Same(before,draft,true);CancelDrag();
            if(edited){stale=true;Draw();return;}Load(LiveDisplays);
        }
        private void CancelDrag(){if(!dragging)return;dragging=false;if(canvas.IsMouseCaptured)canvas.ReleaseMouseCapture();}
        internal void FocusConnected(DesktopDisplay target){
            if(target==null || applying || dragging || stale || !LayoutPolicy.Same(before,draft,true))return;
            if(!draft.Any(d=>d.Key==target.Key))WindowsChanged();
            var d0=draft.FirstOrDefault(d=>d.Key==target.Key && d.AdapterId==target.AdapterId && d.Virtual && !d.Primary);
            if(d0!=null)C<ComboBox>("TopologyPicker").SelectedItem=d0;
        }
        public void ThemeChanged(){Draw();}
        public void LanguageChanged(){C<ComboBox>("TopologyPicker").Items.Refresh();Draw();}
        public void SetBusy(){PollAccess();UpdateButtons();}
        internal void PollAccess(){
            string access=(sessionActive()?"live":"idle")+String.Join("|",draft.Where(CanMove).Select(d=>d.Key));
            if(access==lastAccess)return;lastAccess=access;
            if(dragging && !CanMove(dragDisplay))CancelDrag();Draw();
        }
        private bool CanMove(DesktopDisplay d){return d!=null && !d.Primary && !applying && !busy() && !stale && (!sessionActive() || (d.Virtual && liveOwner(d)));}
        private DesktopDisplay[] Changed(){return draft.Where(d=>before.Any(o=>o.Key==d.Key && (o.X!=d.X || o.Y!=d.Y))).ToArray();}
        private bool CanApply(){var changes=Changed();return changes.Length>0 && (!sessionActive() || changes.Length==1) && changes.All(CanMove);}
        private void UpdateButtons()
        {
            bool editable=!busy() && !applying;
            var v=draft.FirstOrDefault(d=>d.Key==selected && d.Virtual && !d.Primary);
            C<StackPanel>("VddProfileLink").Visibility=v==null ? Visibility.Collapsed : Visibility.Visible;
            C<Button>("ToProfile").IsEnabled=editable && C<ComboBox>("ModePicker").SelectedIndex==0;
            if(v!=null){var rates=v.AvailableRates;
                C<TextBlock>("VddRates").Text=UiText.Format("Windows 当前 {0} Hz · 此分辨率可用：{1}",v.RefreshHz,
                    rates==null || rates.Length==0 ? "—" : String.Join(" / ",rates)+" Hz");}
            C<Button>("LayoutApply").IsEnabled=CanApply();
            foreach(string side in new[]{"left","right","top","bottom"})C<Button>("Layout"+side).IsEnabled=CanMove(draft.FirstOrDefault(d=>d.Key==selected));
            C<Button>("LayoutRefresh").IsEnabled=!applying;C<Button>("LayoutReset").IsEnabled=editable;
            C<Button>("LayoutIdentify").IsEnabled=!applying && before.Length>0;
            C<ComboBox>("TopologyPicker").IsEnabled=!applying && draft.Length!=0;
        }
        private static string Dimensions(DesktopDisplay d){return d.Width+" × "+d.Height;}
        private void Draw()
        {
            if(dragging)return;
            canvas.Children.Clear();double w=canvas.ActualWidth,h=canvas.ActualHeight;
            if(draft.Length==0){C<TextBlock>("SelectedResolution").Text="";C<TextBlock>("LayoutDetails").Text=UiText.Get("选择屏幕查看真实参数。");canvas.Children.Add(new TextBlock{Text=UiText.Get("读取Windows布局后显示真实屏幕，不展示示意配置。"),Margin=new Thickness(20),Foreground=Brushes.SlateGray});UpdateButtons();return;}
            int minX=draft.Min(d=>d.X),minY=draft.Min(d=>d.Y),maxX=draft.Max(d=>d.X+d.Width),maxY=draft.Max(d=>d.Y+d.Height);
            scale=Math.Min(Math.Max(1,w-32)/(maxX-minX+1.0),Math.Max(1,h-24)/(maxY-minY+1.0));
            ox=(w-(maxX-minX)*scale)/2-minX*scale;oy=(h-(maxY-minY)*scale)/2-minY*scale;
            foreach(var d in draft){
                bool active=d.Key==selected;
                var number=System.Text.RegularExpressions.Regex.Match(d.Name,@"\d+$").Value;
                double width=Math.Max(1,d.Width*scale),height=Math.Max(1,d.Height*scale);
                var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                var ink=active ? Brushes.White : (Brush)owner.Resources["TileInk"];
                var id=new TextBlock{Text=number,FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=ink,TextAlignment=TextAlignment.Center};
                stack.Children.Add(id);
                var resolution=new TextBlock{Text=Dimensions(d),FontSize=12,Foreground=ink,TextAlignment=TextAlignment.Center};
                var unlimited=new Size(Double.PositiveInfinity,Double.PositiveInfinity);id.Measure(unlimited);resolution.Measure(unlimited);
                // Map rectangles keep their true aspect. Either the whole label fits, or show only the ID;
                // full resolution is always in the adjacent inspector, tooltip and accessibility name.
                if(resolution.DesiredSize.Width+10<=width && id.DesiredSize.Height+resolution.DesiredSize.Height+8<=height)stack.Children.Add(resolution);
                var card=new Border{Width=width,Height=height,CornerRadius=new CornerRadius(6),Padding=new Thickness(2),
                    Background=(Brush)owner.Resources[active ? "AccentStrong" : d.Primary ? "PrimaryTile" : "TileSurface"],BorderBrush=(Brush)owner.Resources["Accent"],BorderThickness=new Thickness(1),
                    Child=new Viewbox{StretchDirection=StretchDirection.DownOnly,Child=stack},Cursor=CanMove(d) ? Cursors.SizeAll : Cursors.Arrow};
                card.Tag=d;card.ToolTip=d.Label+"\n"+d.Name+"\n"+(d.Primary ? UiText.Get("主屏固定在原位") : UiText.Get("拖动只修改草稿，点击应用才改变Windows"));
                System.Windows.Automation.AutomationProperties.SetName(card,number+" · "+d.Label);
                Canvas.SetLeft(card,ox+d.X*scale);Canvas.SetTop(card,oy+d.Y*scale);canvas.Children.Add(card);
                card.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
                    if(applying)return;var element=(Border)sender;var target=(DesktopDisplay)element.Tag;selected=target.Key;
                    C<ComboBox>("TopologyPicker").SelectedItem=target;
                    if(!CanMove(target)){Draw();return;}
                    dragDisplay=target;dragOrigin=e.GetPosition(canvas);dragX=target.X;dragY=target.Y;dragging=canvas.CaptureMouse();e.Handled=true;
                };
            }
            var chosen=draft.FirstOrDefault(d=>d.Key==selected);
            C<TextBlock>("SelectedResolution").Text=chosen==null ? "" : Dimensions(chosen)+"  ·  "+chosen.RefreshHz+" Hz";
            C<TextBlock>("LayoutDetails").Text=chosen==null ? UiText.Get("选择屏幕查看真实参数。") : chosen.Name+" · "+(chosen.Orientation==0 ? UiText.Get("横向") : chosen.Orientation==1 ? UiText.Get("纵向90°") : chosen.Orientation==2 ? UiText.Get("横向180°") : UiText.Get("纵向270°"))+UiText.Get(" · 坐标 (")+chosen.X+", "+chosen.Y+")";
            C<TextBlock>("LayoutState").Text=stale ? UiText.Get("Windows布局有变更；草稿已保留，应用前必须重新同步。") : LayoutPolicy.Same(before,draft,true) ? (sessionActive() ? UiText.Get("传输中可拖动已连接的虚拟屏；实体屏保持原位。") : UiText.Get("与读取时的Windows布局一致 · 主屏锚定，其他屏可拖动")) : UiText.Get("有未应用的位置草稿 · 确认应用后才同步Windows");
            UpdateButtons();
        }
        private void Drag(object sender,MouseEventArgs e)
        {
            if(!dragging || e.LeftButton!=MouseButtonState.Pressed)return;
            if(!CanMove(dragDisplay)){CancelDrag();Draw();return;}
            var pt=e.GetPosition(canvas);dragDisplay.X=dragX+(int)Math.Round((pt.X-dragOrigin.X)/scale);dragDisplay.Y=dragY+(int)Math.Round((pt.Y-dragOrigin.Y)/scale);
            var card=canvas.Children.OfType<Border>().FirstOrDefault(b=>b.Tag==dragDisplay);if(card!=null){Canvas.SetLeft(card,ox+dragDisplay.X*scale);Canvas.SetTop(card,oy+dragDisplay.Y*scale);}
        }
        private void EndDrag(object sender,MouseButtonEventArgs e)
        {
            if(!dragging)return;dragging=false;canvas.ReleaseMouseCapture();LayoutPolicy.Snap(dragDisplay,draft,(int)Math.Max(10,14/scale));Draw();
        }
        private void Move(string side)
        {
            if(!CanMove(draft.FirstOrDefault(d=>d.Key==selected)))return;
            draft=LayoutPolicy.Place(draft,selected,side);
            C<ComboBox>("TopologyPicker").ItemsSource=draft;C<ComboBox>("TopologyPicker").SelectedItem=draft.Single(d=>d.Key==selected);Draw();
        }
        private void Identify()
        {
            // Labels only; never move user windows or change screen configuration.
            foreach(var d in WindowsDisplays.Capture()){
                var dpi=VisualTreeHelper.GetDpi(owner); // replaced below for .NET Framework availability
                var label=new Window{Width=240,Height=120,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Topmost=true,ShowInTaskbar=false,
                    Background=Brushes.White,Content=new TextBlock{Text=System.Text.RegularExpressions.Regex.Match(d.Name,@"\d+$").Value+"  "+d.Title,FontSize=22,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center},WindowStartupLocation=WindowStartupLocation.Manual};
                label.Left=(d.X+d.Width/2.0)/dpi.DpiScaleX-120;label.Top=(d.Y+d.Height/2.0)/dpi.DpiScaleY-60;label.Show();
                var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=delegate {timer.Stop();label.Close();};timer.Start();
            }
        }
        private async Task Apply()
        {
            if(!CanApply())return;
            var from=LayoutPolicy.Clone(before);var to=LayoutPolicy.Clone(draft);bool live=sessionActive();
            var target=live ? Changed().Single() : null;
            LayoutPolicy.Validate(from,to);
            if(!LayoutPolicy.Same(WindowsDisplays.Capture(),from,true))throw new InvalidOperationException(UiText.Get("Windows布局已变化，请重新读取后再拖动。"));
            string warning=live ? UiText.Get("只移动当前虚拟桌面，不重启视频引擎或更改分辨率。15 秒内未确认将还原。本次位置不写入系统配置。") : UiText.Get("仅修改屏幕位置，保持主屏、方向、分辨率和刷新率不变。\n可能短暂闪屏或重排窗口。应用后15秒内确认保留，否则独立恢复进程还原。\n\n是否应用当前草稿？");
            if(MessageBox.Show(owner,warning,UiText.Get("应用到Windows"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            if(!CanApply() || live!=sessionActive() || !LayoutPolicy.Same(to,draft,true))return;
            string id=Guid.NewGuid().ToString("N"),dir=Path.Combine(settings.StateDirectory,"layout-transactions",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+id);
            string request=Path.Combine(dir,"request.json");
            using(var permit=live ? ScreenSessions.LivePermit(id,target.MonitorId,true) : null){
            StudioServices.AtomicJson(request,new LayoutRequest{Before=from,After=to,Language=UiText.Language,CreatedAt=DateTimeOffset.Now.ToString("o"),LiveMonitor=live?target.MonitorId:null,Permit=live?id:null});
            applying=true;if(ApplyingChanged!=null)ApplyingChanged();UpdateButtons();Process helper=null;Window confirmation=null;
            try{
                var si=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--layout-watchdog "+ProcessRunner.QuoteArgument(request)){UseShellExecute=false,CreateNoWindow=true};
                helper=Process.Start(si);if(helper==null)throw new InvalidOperationException(UiText.Get("恢复进程未启动，未尝试修改Windows。"));
                var began=DateTime.UtcNow;
                while(!helper.HasExited && (DateTime.UtcNow-began).TotalSeconds<40){
                    if(live && !liveOwner(target))permit.Reset();
                    var status=ReadStatus(dir);
                    if(status!=null && status.Phase=="confirm" && confirmation==null){confirmation=ConfirmLayout(dir);confirmation.Show();}
                    if(status!=null && status.Phase!="confirm" && confirmation!=null){confirmation.Close();confirmation=null;}
                    await Task.Delay(150);
                }
                if(confirmation!=null && confirmation.IsVisible)confirmation.Close();
                var final=ReadStatus(dir);if(!helper.HasExited)throw new InvalidOperationException(UiText.Get("恢复进程仍在运行，请勿重复应用。记录：")+dir);
                log(final==null ? UiText.Get("未读到布局结果，请核对Windows显示设置。") : final.Message);
                if(final!=null && final.Phase=="error")MessageBox.Show(owner,final.Message,UiText.Get("布局操作结果"),MessageBoxButton.OK,MessageBoxImage.Warning);
            }
            finally{if(permit!=null)permit.Reset();if(confirmation!=null && confirmation.IsVisible)confirmation.Close();applying=false;if(helper!=null)helper.Dispose();Load(WindowsDisplays.Capture());if(ApplyingChanged!=null)ApplyingChanged();}
            }
        }
        private static LayoutStatus ReadStatus(string dir)
        {try{string p=Path.Combine(dir,"status.json");return File.Exists(p) ? new JavaScriptSerializer().Deserialize<LayoutStatus>(File.ReadAllText(p)) : null;}catch(IOException){return null;}}
        private Window ConfirmLayout(string dir)
        {
            var dialog=new Window{Title=UiText.Get("保留显示位置？"),Width=390,Height=190,Owner=owner,Topmost=true,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var panel=new StackPanel{Margin=new Thickness(20)};var title=new TextBlock{Text=UiText.Get("请在15秒内确认，未确认将自动还原。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};panel.Children.Add(title);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};var keep=new Button{Content=UiText.Get("保留位置"),Padding=new Thickness(18,8,18,8),Margin=new Thickness(0,0,12,0)};var undo=new Button{Content=UiText.Get("立即还原"),Padding=new Thickness(18,8,18,8)};
            System.Windows.Automation.AutomationProperties.SetAutomationId(keep,"KeepLayout");System.Windows.Automation.AutomationProperties.SetAutomationId(undo,"UndoLayout");
            bool decided=false;keep.Click+=delegate {File.WriteAllText(Path.Combine(dir,"keep"),"keep");decided=true;dialog.Close();};undo.Click+=delegate {File.WriteAllText(Path.Combine(dir,"cancel"),"cancel");decided=true;dialog.Close();};buttons.Children.Add(keep);buttons.Children.Add(undo);panel.Children.Add(buttons);dialog.Content=panel;
            dialog.Closed+=delegate {if(!decided)try{File.WriteAllText(Path.Combine(dir,"cancel"),"cancel");}catch(IOException){}};
            return dialog;
        }
    }
}
