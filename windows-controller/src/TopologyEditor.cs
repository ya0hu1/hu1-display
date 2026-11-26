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
        private readonly Func<bool> busy;
        private readonly Canvas canvas;
        private DesktopDisplay[] before=new DesktopDisplay[0],draft=new DesktopDisplay[0];
        private string selected;
        private double scale=1,ox,oy;
        private bool applying,dragging,stale;
        private Point dragOrigin;
        private DesktopDisplay dragDisplay;
        private int dragX,dragY;
        public DesktopDisplay[] CurrentDraft {get{return LayoutPolicy.Clone(draft);}}
        public bool IsApplying {get{return applying;}}
        public event Action ApplyingChanged;
        public TopologyEditor(Window owner,Settings settings,Action<string> log,Func<bool> busy)
        {
            this.owner=owner;this.settings=settings;this.log=log;this.busy=busy;
            canvas=C<Canvas>("TopologyCanvas");canvas.SizeChanged+=delegate {Draw();};
            Bind("LayoutRefresh",Refresh);
            C<Button>("LayoutApply").Click+=async delegate {try{await Apply();}catch(Exception e){log(e.Message);MessageBox.Show(owner,e.Message,UiText.Get("显示布局"),MessageBoxButton.OK,MessageBoxImage.Warning);}};Bind("LayoutReset",delegate {draft=LayoutPolicy.Clone(before);Draw();});
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
            if(applying)return;
            if(draft.Length>0 && !LayoutPolicy.Same(before,draft,true) && MessageBox.Show(owner,UiText.Get("丢弃尚未应用的布局草稿，重新读取Windows？"),UiText.Get("重新读取"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            Load(WindowsDisplays.Capture());log(UiText.Get("已读取Windows真实显示布局：")+before.Length+UiText.Get("块活动桌面，包含实体副屏。"));
        }
        public void Load(DesktopDisplay[] actual)
        {
            stale=false;before=LayoutPolicy.Clone(actual);draft=LayoutPolicy.Clone(actual);
            if(!draft.Any(d=>d.Key==selected))selected=(draft.FirstOrDefault(d=>d.Virtual) ?? draft.FirstOrDefault())==null ? null : (draft.FirstOrDefault(d=>d.Virtual) ?? draft.First()).Key;
            C<ComboBox>("TopologyPicker").ItemsSource=draft;C<ComboBox>("TopologyPicker").SelectedItem=draft.FirstOrDefault(d=>d.Key==selected);Draw();
        }
        public void WindowsChanged()
        {
            if(applying)return;
            if(draft.Length>0 && !LayoutPolicy.Same(before,draft,true)){
                stale=true;Draw();return;
            }
            try{Load(WindowsDisplays.Capture());}catch(Exception e){log(UiText.Get("布局更新提示：")+e.Message);}
        }
        public void ThemeChanged(){Draw();}
        public void LanguageChanged(){C<ComboBox>("TopologyPicker").Items.Refresh();Draw();}
        public void SetBusy(){UpdateButtons();}
        private void UpdateButtons()
        {
            bool editable=!busy() && !applying;
            C<Button>("LayoutApply").IsEnabled=editable && !stale && draft.Length>0 && !LayoutPolicy.Same(before,draft,true);
            foreach(string side in new[]{"left","right","top","bottom"})C<Button>("Layout"+side).IsEnabled=editable && draft.Any(d=>d.Key==selected && !d.Primary);
            C<Button>("LayoutRefresh").IsEnabled=!applying;C<Button>("LayoutReset").IsEnabled=editable;
            C<Button>("LayoutIdentify").IsEnabled=!applying && before.Length>0;
            C<ComboBox>("TopologyPicker").IsEnabled=editable;
        }
        private void Draw()
        {
            if(dragging)return;
            canvas.Children.Clear();double w=canvas.ActualWidth,h=canvas.ActualHeight;
            if(draft.Length==0){canvas.Children.Add(new TextBlock{Text=UiText.Get("读取Windows布局后显示真实屏幕，不展示示意配置。"),Margin=new Thickness(20),Foreground=Brushes.SlateGray});UpdateButtons();return;}
            int minX=draft.Min(d=>d.X),minY=draft.Min(d=>d.Y),maxX=draft.Max(d=>d.X+d.Width),maxY=draft.Max(d=>d.Y+d.Height);
            scale=Math.Max(0.008,Math.Min((Math.Max(100,w)-60)/(maxX-minX+1.0),(Math.Max(100,h)-40)/(maxY-minY+1.0)));
            ox=(w-(maxX-minX)*scale)/2-minX*scale;oy=(h-(maxY-minY)*scale)/2-minY*scale;
            foreach(var d in draft){
                bool active=d.Key==selected;
                var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
                var number=System.Text.RegularExpressions.Regex.Match(d.Name,@"\d+$").Value;
                stack.Children.Add(new TextBlock{TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Text=number+(d.Primary ? UiText.Get(" · 主屏") : d.Virtual ? UiText.Get(" · 手机虚拟屏") : UiText.Get(" · 实体副屏")),FontSize=14,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center});
                stack.Children.Add(new TextBlock{Text=d.DisplayTitle,FontSize=10,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,4,4,0)});
                stack.Children.Add(new TextBlock{Text=d.Width+" × "+d.Height+"\n("+d.X+", "+d.Y+")",FontSize=10,TextAlignment=TextAlignment.Center,Foreground=Brushes.SlateGray,Margin=new Thickness(0,4,0,0)});
                var card=new Border{Width=Math.Max(40,d.Width*scale),Height=Math.Max(45,d.Height*scale),CornerRadius=new CornerRadius(10),Background=active ? (Brush)owner.Resources["Tint"] : Brushes.White,
                    BorderBrush=active ? (Brush)owner.Resources["Accent"] : new SolidColorBrush(Color.FromRgb(192,208,223)),BorderThickness=new Thickness(active ? 2 : 1),Child=stack,Cursor=d.Primary ? Cursors.Arrow : Cursors.SizeAll,ClipToBounds=true};
                card.Tag=d;card.ToolTip=d.Label+"\n"+d.Name+"\n"+(d.Primary ? UiText.Get("主屏固定在原位") : UiText.Get("拖动只修改草稿，点击应用才改变Windows"));
                Canvas.SetLeft(card,ox+d.X*scale);Canvas.SetTop(card,oy+d.Y*scale);canvas.Children.Add(card);
                card.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
                    if(applying || busy())return;var element=(Border)sender;var target=(DesktopDisplay)element.Tag;selected=target.Key;
                    C<ComboBox>("TopologyPicker").SelectedItem=target;
                    if(target.Primary){Draw();return;}
                    dragDisplay=target;dragOrigin=e.GetPosition(canvas);dragX=target.X;dragY=target.Y;dragging=true;canvas.CaptureMouse();e.Handled=true;
                };
            }
            var chosen=draft.FirstOrDefault(d=>d.Key==selected);
            C<TextBlock>("LayoutDetails").Text=chosen==null ? UiText.Get("选择屏幕查看真实参数。") : chosen.Label+"\n"+chosen.Name+" · "+(chosen.Orientation==0 ? UiText.Get("横向") : chosen.Orientation==1 ? UiText.Get("纵向90°") : chosen.Orientation==2 ? UiText.Get("横向180°") : UiText.Get("纵向270°"))+UiText.Get(" · 坐标 (")+chosen.X+", "+chosen.Y+")";
            C<TextBlock>("LayoutState").Text=stale ? UiText.Get("Windows布局有变更；草稿已保留，应用前必须重新同步。") : LayoutPolicy.Same(before,draft,true) ? UiText.Get("与读取时的Windows布局一致 · 主屏锚定，其他屏可拖动") : UiText.Get("有未应用的位置草稿 · 确认应用后才同步Windows");
            UpdateButtons();
        }
        private void Drag(object sender,MouseEventArgs e)
        {
            if(!dragging || e.LeftButton!=MouseButtonState.Pressed)return;
            var pt=e.GetPosition(canvas);dragDisplay.X=dragX+(int)Math.Round((pt.X-dragOrigin.X)/scale);dragDisplay.Y=dragY+(int)Math.Round((pt.Y-dragOrigin.Y)/scale);
            var card=canvas.Children.OfType<Border>().FirstOrDefault(b=>b.Tag==dragDisplay);if(card!=null){Canvas.SetLeft(card,ox+dragDisplay.X*scale);Canvas.SetTop(card,oy+dragDisplay.Y*scale);}
        }
        private void EndDrag(object sender,MouseButtonEventArgs e)
        {
            if(!dragging)return;dragging=false;canvas.ReleaseMouseCapture();LayoutPolicy.Snap(dragDisplay,draft,(int)Math.Max(10,14/scale));Draw();
        }
        private void Move(string side)
        {
            if(applying || busy() || selected==null)return;
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
            if(applying || busy())return;
            LayoutPolicy.Validate(before,draft);
            if(!LayoutPolicy.Same(WindowsDisplays.Capture(),before,true))throw new InvalidOperationException(UiText.Get("Windows布局已变化，请重新读取后再拖动。"));
            if(MessageBox.Show(owner,UiText.Get("仅修改屏幕位置，保持主屏、方向、分辨率和刷新率不变。\n可能短暂闪屏或重排窗口。应用后15秒内确认保留，否则独立恢复进程还原。\n\n是否应用当前草稿？"),UiText.Get("应用到Windows"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            string dir=Path.Combine(settings.StateDirectory,"layout-transactions",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
            string request=Path.Combine(dir,"request.json");StudioServices.AtomicJson(request,new LayoutRequest{Before=before,After=draft,Language=UiText.Language,CreatedAt=DateTimeOffset.Now.ToString("o")});
            applying=true;if(ApplyingChanged!=null)ApplyingChanged();UpdateButtons();Process helper=null;
            try{
                var si=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--layout-watchdog "+ProcessRunner.QuoteArgument(request)){UseShellExecute=false,CreateNoWindow=true};
                helper=Process.Start(si);if(helper==null)throw new InvalidOperationException(UiText.Get("恢复进程未启动，未尝试修改Windows。"));
                Window confirmation=null;var began=DateTime.UtcNow;
                while(!helper.HasExited && (DateTime.UtcNow-began).TotalSeconds<40){
                    var status=ReadStatus(dir);
                    if(status!=null && status.Phase=="confirm" && confirmation==null){confirmation=ConfirmLayout(dir);confirmation.Show();}
                    if(status!=null && status.Phase!="confirm" && confirmation!=null){confirmation.Close();confirmation=null;}
                    await Task.Delay(150);
                }
                if(confirmation!=null)confirmation.Close();
                var final=ReadStatus(dir);if(!helper.HasExited)throw new InvalidOperationException(UiText.Get("恢复进程仍在运行，请勿重复应用。记录：")+dir);
                log(final==null ? UiText.Get("未读到布局结果，请核对Windows显示设置。") : final.Message);
                if(final!=null && final.Phase=="error")MessageBox.Show(owner,final.Message,UiText.Get("布局操作结果"),MessageBoxButton.OK,MessageBoxImage.Warning);
            }
            finally{applying=false;if(ApplyingChanged!=null)ApplyingChanged();if(helper!=null)helper.Dispose();Load(WindowsDisplays.Capture());}
        }
        private static LayoutStatus ReadStatus(string dir)
        {try{string p=Path.Combine(dir,"status.json");return File.Exists(p) ? new JavaScriptSerializer().Deserialize<LayoutStatus>(File.ReadAllText(p)) : null;}catch(IOException){return null;}}
        private Window ConfirmLayout(string dir)
        {
            var dialog=new Window{Title=UiText.Get("保留显示位置？"),Width=390,Height=190,Owner=owner,Topmost=true,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var panel=new StackPanel{Margin=new Thickness(20)};var title=new TextBlock{Text=UiText.Get("请在15秒内确认，未确认将自动还原。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};panel.Children.Add(title);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};var keep=new Button{Content=UiText.Get("保留位置"),Padding=new Thickness(18,8,18,8),Margin=new Thickness(0,0,12,0)};var undo=new Button{Content=UiText.Get("立即还原"),Padding=new Thickness(18,8,18,8)};
            bool decided=false;keep.Click+=delegate {File.WriteAllText(Path.Combine(dir,"keep"),"keep");decided=true;dialog.Close();};undo.Click+=delegate {File.WriteAllText(Path.Combine(dir,"cancel"),"cancel");decided=true;dialog.Close();};buttons.Children.Add(keep);buttons.Children.Add(undo);panel.Children.Add(buttons);dialog.Content=panel;
            dialog.Closed+=delegate {if(!decided)try{File.WriteAllText(Path.Combine(dir,"cancel"),"cancel");}catch(IOException){}};
            return dialog;
        }
    }
}
