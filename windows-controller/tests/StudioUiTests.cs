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
                foreach(string name in new[]{"Trust","HomeInstall","InstallApk","OpenPhone","Developer","DriverFolder","DriverWizard","DisplaySettings","Protection","Refresh","Stop","CustomColor","ForgetDevice","RenameDevice","LayoutApply","LayoutReset","LayoutRefresh","LayoutIdentify"})
                    if(!(studio.Window.FindName(name) is Button))throw new Exception("Missing button "+name);
            });
            test("default device not selected and no automatic connection",delegate {
                if(((ComboBox)studio.Window.FindName("DevicePicker")).Items.Count!=0 || ((Button)studio.Window.FindName("Start")).IsEnabled)throw new Exception("Unexpected auto scan or start eligibility.");
            });
            test("default extend and continuous",delegate {
                if(((ComboBox)studio.Window.FindName("ModePicker")).SelectedIndex!=0 || ((CheckBox)studio.Window.FindName("Continuous")).IsChecked!=true)throw new Exception("Wrong defaults.");
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
            });
            lines.Add("RESULT passed="+(lines.Count-failed)+" failed="+failed);
            lines.Add("Scope: own WPF window rendered to PNG; no desktop screenshot, ADB, phone or driver actions.");
            File.WriteAllLines(report,lines);LastExitCode=failed==0 ? 0 : 1;
        }
    }
}
