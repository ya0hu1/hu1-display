using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DeskMateLearning
{
    internal static class StudioUiTests
    {
        public static int LastExitCode=1;
        public static void Run(StudioWindow studio,string report,string stateDirectory)
        {
            var lines=new List<string>();int failed=0;
            Action<string,Action> test=delegate(string name,Action body){try{body();lines.Add("PASS "+name);}catch(Exception e){failed++;lines.Add("FAIL "+name+": "+e);}};
            test("all actionable setup buttons exist",delegate {
                foreach(string name in new[]{"Trust","HomeInstall","InstallApk","OpenPhone","Developer","DriverFolder","DriverWizard","DisplaySettings","Protection","Refresh","Stop","CustomColor","ForgetDevice","RenameDevice","ReleasePortable","CapacityGuide","LayoutApply","LayoutReset","LayoutRefresh","LayoutIdentify"})
                    if(!(studio.Window.FindName(name) is Button))throw new Exception("Missing button "+name);
            });
            test("default device not selected and no automatic connection",delegate {
                if(((ComboBox)studio.Window.FindName("DevicePicker")).Items.Count!=0 || ((Button)studio.Window.FindName("Start")).IsEnabled)throw new Exception("Unexpected auto scan or start eligibility.");
            });
            test("default extend and continuous",delegate {
                if(((ComboBox)studio.Window.FindName("ModePicker")).SelectedIndex!=0 || ((CheckBox)studio.Window.FindName("Continuous")).IsChecked!=true)throw new Exception("Wrong defaults.");
            });
            test("extend profile controls bind and mirror never inherits them",delegate {
                var size=(ComboBox)studio.Window.FindName("StreamSize");var fps=(ComboBox)studio.Window.FindName("StreamFps");var mode=(ComboBox)studio.Window.FindName("ModePicker");
                if(!studio.SelectedProfile().IsDefault)throw new Exception("Unexpected override");size.SelectedIndex=1;fps.SelectedIndex=3;
                if(!studio.SelectedProfile().Matches(1280,720,90))throw new Exception("Wrong profile");fps.SelectedIndex=4;if(!studio.SelectedProfile().Matches(1280,720,120))throw new Exception("120 fps not wired");mode.SelectedIndex=1;
                if(size.IsEnabled || fps.IsEnabled || !studio.SelectedProfile().IsDefault)throw new Exception("Mirror override");mode.SelectedIndex=0;size.SelectedIndex=0;fps.SelectedIndex=0;
            });
            test("idle shelf has no HUD or stop actions; window handoff and scoped retirement default on",delegate {
                foreach(string id in new[]{"HudEnabled","HudCompact","HudCorner","HudOpacity","Stop"})if(((FrameworkElement)studio.Window.FindName(id)).IsEnabled)throw new Exception(id);
                foreach(string id in new[]{"RetireDesktop","ReturnWindows"})if(((CheckBox)studio.Window.FindName(id)).IsChecked!=true)throw new Exception(id);
            });
            test("metrics blank until actual samples",delegate {
                if(((TextBlock)studio.Window.FindName("FpsValue")).Text!="—")throw new Exception("Fabricated FPS.");
            });
            test("theme updates resources",delegate {studio.ApplyTheme("#66BDE7","天蓝",false);if(((TextBlock)studio.Window.FindName("ThemeName")).Text.IndexOf(UiText.Get("天蓝"))<0)throw new Exception("Theme text missing.");studio.ApplyTheme("#70A5C2","雾霾蓝",false);});
            test("populated device picker opens safely",delegate {
                var picker=(ComboBox)studio.Window.FindName("DevicePicker");
                picker.ItemsSource=new[]{new DeviceInfo {Serial="SYNTHETIC",Model="Synthetic Android",State="device"}};
                picker.SelectedIndex=0;picker.IsDropDownOpen=true;studio.Window.UpdateLayout();
                studio.Window.Dispatcher.Invoke(new Action(delegate {}),DispatcherPriority.ContextIdle);
                if(picker.ItemContainerGenerator.ContainerFromIndex(0)==null)throw new Exception("Dropdown item missing.");
                picker.IsDropDownOpen=false;picker.ItemsSource=null;
            });
            test("topology includes primary physical secondary and virtual monitor",delegate {
                studio.Topology.Load(LayoutTests.Sample());
                if(studio.Topology.CurrentDraft.Length!=3 || studio.Topology.CurrentDraft.Count(d=>!d.Virtual)!=2)throw new Exception("Missing original screens.");
                var picker=(ComboBox)studio.Window.FindName("TopologyPicker");picker.SelectedIndex=2;
                ((Button)studio.Window.FindName("Layoutbottom")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(studio.Topology.CurrentDraft[2].Y!=1440 || !((Button)studio.Window.FindName("LayoutApply")).IsEnabled)throw new Exception("Draft button not wired.");
                ((Button)studio.Window.FindName("LayoutReset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(studio.Topology.CurrentDraft[2].X!=2560 || ((Button)studio.Window.FindName("LayoutApply")).IsEnabled)throw new Exception("Draft reset failed.");
            });
            test("language selector persists both locales without resetting work",delegate {
                var language=(ComboBox)studio.Window.FindName("LanguagePicker");
                var picker=(ComboBox)studio.Window.FindName("DevicePicker");
                var name=(TextBox)studio.Window.FindName("DeviceNickname");
                var device=new DeviceInfo{Serial="SYNTHETIC",Model="Test Android",Name="My tablet",State="device",Trusted=true};
                picker.ItemsSource=new[]{device};picker.SelectedIndex=0;name.Text="Draft nickname";
                ((Button)studio.Window.FindName("Layoutbottom")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var draft=studio.Topology.CurrentDraft;
                studio.Page("Settings");
                foreach(int index in new[]{1-language.SelectedIndex,language.SelectedIndex}){
                    language.SelectedIndex=index;studio.Window.UpdateLayout();
                    string code=index==1 ? "en-US" : "zh-CN",brand=index==1 ? "hu1 display" : "晖映";
                    if(UiText.Language!=code || UiText.ReadLanguage(stateDirectory)!=code ||
                       ((TextBlock)studio.Window.FindName("BrandName")).Text!=brand || !studio.Window.Title.StartsWith(brand))throw new Exception("Brand, selector or persisted language disagrees");
                    if(picker.SelectedItem!=device || name.Text!="Draft nickname" || !((FrameworkElement)studio.Window.FindName("SettingsPage")).IsVisible || !LayoutPolicy.Same(draft,studio.Topology.CurrentDraft,true))throw new Exception("Switch reset pending work");
                    if(((Button)studio.Window.FindName("Refresh")).Content.ToString()!=(index==1 ? "Refresh" : "刷新"))throw new Exception("Action not translated");
                }
                picker.ItemsSource=null;((Button)studio.Window.FindName("LayoutReset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            test("unrecognized preference falls back to system language",delegate {
                File.WriteAllText(Path.Combine(stateDirectory,"studio-language.json"),"{\"Language\":\"unsupported\"}");
                if(UiText.ReadLanguage(stateDirectory)!=UiText.SystemLanguage)throw new Exception("Invalid locale accepted");
            });
            test("compact and detailed HUD keep every digit at four DPIs, including narrow work areas",delegate {
                using(var hud=new StudioHud())foreach(int dpi in new[]{96,120,144,192})foreach(bool compact in new[]{false,true})foreach(int available in new[]{280,1000}){
                    var stats=new TelemetrySnapshot{HasConfig=true,HasStats=true,Fresh=true,Width=1920,Height=1080,TargetHz=60,Fps=59.9,Mbps=12.4,RttMs=4,TotalDropped=0};
                    using(var bmp=new System.Drawing.Bitmap(1000,300)){
                        bmp.SetResolution(dpi,dpi);using(var g=System.Drawing.Graphics.FromImage(bmp)){
                            var strip=hud.Measure(g,stats,compact,available);
                            if(strip.Size.Width>available || !strip.Runs.Any(r=>r.Text=="1920 × 1080") || strip.Runs.Count!=(compact?4:6))throw new Exception("Lost a HUD field");
                            foreach(var r in strip.Runs){var size=g.MeasureString(r.Text,r.Font);if(r.Position.X+size.Width>strip.Size.Width || r.Position.Y+size.Height>strip.Size.Height)throw new Exception("Clipped: "+r.Text);}
                            StudioHud.PaintStrip(g,strip,strip.Size);
                            if(dpi==144 && available==1000){var dest=Path.Combine(Path.GetDirectoryName(report),"studio-preview");Directory.CreateDirectory(dest);
                                using(var image=bmp.Clone(new System.Drawing.Rectangle(System.Drawing.Point.Empty,strip.Size),bmp.PixelFormat))image.Save(Path.Combine(dest,compact?"hud-compact.png":"hud-detail.png"),System.Drawing.Imaging.ImageFormat.Png);}
                        }
                    }
                }
            });
            test("a managed active target reports real dimensions; a standby target makes no claim",delegate {
                var d=new DisplayInfo{Virtual=true,MonitorPath="test",Slot=256,Description=@"\\.\DISPLAY5 ""VDD by MTT"" 1920x1080 at (0,0)"};
                if(!d.Label.StartsWith("1920 × 1080") || !d.Label.Contains("DISPLAY5"))throw new Exception("Incomplete active label");
                d.Dormant=true;if(d.Label.Contains("1080"))throw new Exception("Stale standby resolution");
            });
            test("narrow workspace stacks pickers and keeps the full monitor size in the inspector",delegate {
                double old=studio.Window.Width;studio.Page("Home");try{
                    studio.Window.Width=850;studio.Window.UpdateLayout();studio.Topology.Load(LayoutTests.Sample());
                    var picker=(ComboBox)studio.Window.FindName("TopologyPicker");picker.SelectedIndex=2;studio.Window.UpdateLayout();
                    var pair=(Grid)studio.Window.FindName("ConnectionPair");var label=(TextBlock)studio.Window.FindName("SelectedResolution");
                    if(Grid.GetRow(pair.Children[1])!=1 || !label.Text.StartsWith("1920 × 1080"))throw new Exception("Missing full-size inspection");
                }finally{studio.Window.Width=old;studio.Window.UpdateLayout();}
            });
            test("a streaming VDD can be positioned while physical, pending and stopping routes stay locked",delegate {
                var baseline=LayoutTests.Sample();var v=baseline[2];studio.Topology.Load(baseline);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var routes=(System.Collections.IList)typeof(StudioWindow).GetField("routes",flags).GetValue(studio);
                var type=typeof(StudioWindow).GetNestedType("Route",System.Reflection.BindingFlags.NonPublic);var route=Activator.CreateInstance(type,true);
                var worker=new Backend(new Settings{ApprovedDevices=new ApprovedDevice[0]});worker.SessionTarget=v;
                type.GetField("Device").SetValue(route,new DeviceInfo{Serial="TEST",Model="TEST"});type.GetField("Worker").SetValue(route,worker);
                type.GetField("Mode").SetValue(route,DisplayMode.Extend);type.GetField("Display").SetValue(route,new DisplayInfo{Virtual=true,MonitorPath=v.MonitorId});
                var picker=(ComboBox)studio.Window.FindName("TopologyPicker");var move=(Button)studio.Window.FindName("Layoutbottom");var apply=(Button)studio.Window.FindName("LayoutApply");
                routes.Add(route);try{studio.Topology.SetBusy();if(move.IsEnabled)throw new Exception("Move allowed during startup");
                    worker.LayoutReady=true;studio.Topology.PollAccess();picker.SelectedIndex=1;if(move.IsEnabled)throw new Exception("Physical screen movable");
                    picker.SelectedIndex=2;if(!move.IsEnabled)throw new Exception("VDD move locked");move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(!apply.IsEnabled || studio.Topology.CurrentDraft[2].Y!=baseline[0].Height)throw new Exception("Live draft cannot apply");
                    worker.LayoutReady=false;studio.Topology.PollAccess();if(apply.IsEnabled)throw new Exception("Move allowed during teardown");
                }finally{routes.Remove(route);((IDisposable)route).Dispose();studio.Topology.Load(baseline);}
            });
            test("theme surfaces follow seed hue; selected labels and buttons have AA contrast",delegate {
                foreach(var seed in new[]{"#66BDE7","#70A5C2","#E497B4","#65B7A0","#000000","#FFFFFF","#FFFF00"}){
                    studio.ApplyTheme(seed,seed,false);var color=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(seed);
                    var palette=new ThemePalette(color);
                    if(ThemePalette.Contrast(palette.Strong,palette.Selection)<4.5 || ThemePalette.Contrast(palette.Strong,System.Windows.Media.Colors.White)<4.5 || ThemePalette.Contrast(palette.ButtonInk,color)<4.5)throw new Exception(seed);
                    if(((System.Windows.Media.SolidColorBrush)studio.Window.Resources["ThemeSidebar"]).Color!=palette.Sidebar)throw new Exception("Stale sidebar");
                }
                studio.ApplyTheme("#70A5C2",UiText.Get("雾霾蓝"),false);
            });
            test("render four pages in both languages with synthetic data only",delegate {
                string dir=Path.Combine(Path.GetDirectoryName(report),"studio-preview");
                foreach(string language in new[]{"zh-CN","en-US"}){
                    studio.ApplyLanguage(language,false);
                    ((TextBlock)studio.Window.FindName("LayoutState")).Text=UiText.Get("合成布局 · 仅作界面预览");
                    foreach(string page in new[]{"Home","Setup","Settings","Logs"}){
                        studio.Page(page);studio.Window.UpdateLayout();
                        studio.RenderPreview(Path.Combine(dir,language,page.ToLowerInvariant()+".png"));
                    }
                }
                studio.Page("Home");
                foreach(string seed in new[]{"#E497B4","#65B7A0"}){studio.ApplyTheme(seed,seed,false);studio.Window.UpdateLayout();studio.RenderPreview(Path.Combine(dir,"theme-"+seed.Substring(1)+".png"));}
                studio.ApplyTheme("#70A5C2",UiText.Get("雾霾蓝"),false);
                studio.Page("Home");
            });
            lines.Add("RESULT passed="+(lines.Count-failed)+" failed="+failed);
            lines.Add("Scope: own WPF window rendered to PNG; no desktop screenshot, ADB, phone or driver actions.");
            File.WriteAllLines(report,lines);LastExitCode=failed==0 ? 0 : 1;
        }
    }
}
