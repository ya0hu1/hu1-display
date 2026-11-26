using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Forms=System.Windows.Forms;

namespace DeskMateLearning
{
    public sealed class ThemePreference {public string Color="#70A5C2",Name=UiText.Get("雾霾蓝");}
    public sealed class StudioWindow
    {
        public readonly Window Window;
        private readonly Settings settings;
        private readonly Backend backend;
        private readonly DispatcherTimer timer=new DispatcherTimer();
        private ManualResetEvent stop;
        private ScanResult scan;
        private bool busy,closing,switchingLanguage;
        private DateTime began;
        private ThemePreference theme=new ThemePreference();
        private string selectedPage="Home";
        private readonly TopologyEditor topology;
        private Forms.NotifyIcon tray;
        public TopologyEditor Topology {get{return topology;}}
        private readonly string[] operations={"Refresh","SetupRefresh","Trust","HomeInstall","InstallApk","OpenPhone","Developer","DriverFolder","DriverWizard","ForgetDevice","RenameDevice"};
        private T C<T>(string name) where T:class {return Window.FindName(name) as T;}
        private void Text(string name,string text) {C<TextBlock>(name).Text=text;}
        private void Click(string name,Action action) {C<Button>(name).Click+=delegate {try{action();}catch(Exception e){Fail(e);}};}
        public StudioWindow(Settings settings,bool previewOnly=false)
        {
            this.settings=settings;UiText.SetLanguage(UiText.ReadLanguage(settings.StateDirectory));backend=new Backend(settings);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskLink.Studio.xaml"))
            {
                if(stream==null)throw new InvalidDataException(UiText.Get("工作台界面资源缺失。"));
                Window=(Window)XamlReader.Load(stream);
                UiText.Apply(Window);
                var area=SystemParameters.WorkArea;
                Window.Width=Math.Min(Window.Width,area.Width);Window.Height=Math.Min(Window.Height,area.Height);
            }
            try{using(var icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location))Window.Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());}catch(Exception){}
            topology=new TopologyEditor(Window,settings,Log,()=>busy);
            topology.ApplyingChanged+=delegate {SetBusy(busy);};
            Click("LayoutSystem",delegate {StudioServices.Open("ms-settings:display",false);});
            Click("RenameDevice",RenameDevice);
            EventHandler displayChanged=delegate {if(!Window.Dispatcher.HasShutdownStarted)Window.Dispatcher.BeginInvoke(new Action(delegate {topology.WindowsChanged();}));};
            if(!previewOnly){
                Window.Loaded+=delegate {try{topology.Refresh();}catch(Exception e){Log(UiText.Get("显示布局读取失败：")+e.Message);}};
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=displayChanged;
                Window.Closed+=delegate {Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=displayChanged;};
            }
            if(!previewOnly)SetupTray();
            C<ComboBox>("LanguagePicker").SelectedIndex=UiText.English ? 1 : 0;
            C<ComboBox>("LanguagePicker").SelectionChanged+=delegate {
                if(switchingLanguage)return;
                var item=C<ComboBox>("LanguagePicker").SelectedItem as ComboBoxItem;
                if(item==null)return;
                try{ApplyLanguage((string)item.Tag,true);}catch(Exception e){
                    switchingLanguage=true;C<ComboBox>("LanguagePicker").SelectedIndex=UiText.English ? 1 : 0;switchingLanguage=false;Fail(e);
                }
            };
            foreach(string page in new[]{"Home","Setup","Settings","Logs"}){
                string p=page;Click("Nav"+p,delegate {Page(p);});
            }
            Click("ThemeSky",delegate {ApplyTheme("#66BDE7",UiText.Get("天蓝"),true);});
            Click("ThemeMist",delegate {ApplyTheme("#70A5C2",UiText.Get("雾霾蓝"),true);});
            Click("ThemePink",delegate {ApplyTheme("#E497B4",UiText.Get("樱粉"),true);});
            Click("ThemeMint",delegate {ApplyTheme("#65B7A0",UiText.Get("薄荷"),true);});
            Click("CustomColor",ChooseColor);
            Click("Refresh",delegate {ScanDevices();});Click("SetupRefresh",delegate {Page("Home");ScanDevices();});
            Click("Trust",delegate {TrustDevice();});Click("ForgetDevice",ForgetDevice);
            Click("HomeInstall",delegate {Install();});Click("InstallApk",delegate {Install();});
            Click("OpenPhone",delegate {OpenPhone(false);});Click("Developer",delegate {OpenPhone(true);});
            Click("UsbHelp",delegate {MessageBox.Show(Window,UiText.Get("① 使用支持数据传输的USB线。\n② 手机启用开发者选项 → USB调试。\n③ 确认手机上的RSA授权弹窗。\n④ 刷新，选择设备并信任。\n⑤ 安装手机端；如品牌有USB安装开关，需手动允许。\n⑥ 选非主屏VDD，开始扩展。\n\n无需连接显卡接口，不启用无线ADB，不Root。"),UiText.Get("USB连接指南"));});
            Click("ShowApk",delegate {StudioServices.VerifyApk(settings);StudioServices.Open(System.IO.Path.GetDirectoryName(settings.ApkPath),false);});
            Click("DisplaySettings",delegate {StudioServices.Open("ms-settings:display",false);});
            Click("Protection",delegate {StudioServices.Open(System.IO.Path.Combine(Environment.SystemDirectory,"SystemPropertiesProtection.exe"),true);});
            Click("DriverFolder",delegate {PrepareDriver();});Click("DriverWizard",delegate {DriverWizard();});
            Click("OpenLogs",delegate {Directory.CreateDirectory(settings.LogDirectory);StudioServices.Open(settings.LogDirectory,false);});
            Click("ClearLogs",delegate {C<TextBox>("LogText").Clear();});
            Click("CopyDiagnostics",delegate {Clipboard.SetText(UiText.Brand+" Studio 0.4 Preview\nOS: "+Environment.OSVersion+"\n"+C<TextBlock>("StreamInfo").Text+UiText.Get("\n设备数: ")+(scan==null ? 0 : scan.Devices.Count)+UiText.Get("\n（未复制设备序列号或画面）"));Log(UiText.Get("诊断摘要已复制到Windows剪贴板，未转发到手机。"));});
            Click("About",delegate {MessageBox.Show(Window,UiText.Get("晖映 · 工作台预览版 0.4\n原创Windows界面与控制层（MIT）。\n视频引擎及Android客户端：AnyDisplay v0.1.4（MIT）。\n虚拟显示器：Virtual Display Driver / MikeTheTech。\nADB：Google Android Platform-Tools。\n\n学习DeskMate的工作台布局思路，不复制其代码、图标或品牌；非上述项目的官方发行版。\n仅USB，单设备；触控/音频/剪贴板回传暂未开放。"),UiText.Get("关于与开源说明"));});
            Click("Start",delegate {StartSession();});Click("Stop",RequestStop);
            C<ComboBox>("ModePicker").SelectionChanged+=delegate {BindDisplays();};
            C<ComboBox>("DevicePicker").SelectionChanged+=delegate {UpdateDeviceHint();UpdateButtons();};
            C<ComboBox>("DisplayPicker").SelectionChanged+=delegate {UpdateButtons();};
            Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(topology.IsApplying){e.Cancel=true;Log(UiText.Get("请先确认或还原显示布局，再关闭工作台。"));return;}if(busy){e.Cancel=true;closing=true;RequestStop();}};
            Window.Closed+=delegate {timer.Stop();if(tray!=null){tray.Visible=false;var icon=tray.Icon;tray.Dispose();if(icon!=null)icon.Dispose();}};
            timer.Interval=TimeSpan.FromMilliseconds(500);timer.Tick+=delegate {UpdateTelemetry();};timer.Start();
            try{string file=System.IO.Path.Combine(settings.StateDirectory,"studio-theme.json");if(File.Exists(file)){var pref=new JavaScriptSerializer().Deserialize<ThemePreference>(File.ReadAllText(file));if(pref!=null)theme=pref;}}
            catch(Exception){theme=new ThemePreference();}
            try{ApplyTheme(theme.Color,theme.Name,false);}catch(Exception){ApplyTheme("#70A5C2",UiText.Get("雾霾蓝"),false);}
            Page("Home");RefreshTrustSummary();UpdateButtons();Log(UiText.Get("工作台已就绪。点击刷新发现设备，启动窗口不会自动操作手机。"));
        }
        private DeviceInfo Device {get{return C<ComboBox>("DevicePicker").SelectedItem as DeviceInfo;}}
        private DisplayMode Mode {get{return C<ComboBox>("ModePicker").SelectedIndex==0 ? DisplayMode.Extend : DisplayMode.Mirror;}}
        private void RenameDevice()
        {
            var d=Device;if(busy || topology.IsApplying || d==null)return;
            string name=C<TextBox>("DeviceNickname").Text.Trim();
            if(name.Length<1 || name.Length>32 || name.Any(Char.IsControl))throw new InvalidOperationException(UiText.Get("昵称需为1–32个可显示字符。"));
            SafetyPolicy.ValidateDevice(settings,d);
            var next=settings.ApprovedDevices.Select(a=>new ApprovedDevice{Serial=a.Serial,Model=a.Model,Name=a.Serial==d.Serial ? name : a.Name}).ToArray();
            StudioServices.AtomicJson(System.IO.Path.Combine(settings.StateDirectory,"trusted-devices.json"),next);
            settings.ApprovedDevices=next;d.Name=name;C<ComboBox>("DevicePicker").Items.Refresh();RefreshTrustSummary();Log(UiText.Get("已保存设备昵称：")+name);
        }
        private void SetupTray()
        {
            tray=new Forms.NotifyIcon();
            try{tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);}catch(ArgumentException){}
            if(tray.Icon==null)tray.Icon=(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            tray.Text=UiText.Brand+" · "+UiText.Get("●  等待连接").Replace("●  ","");
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add(UiText.Get("打开晖映工作台"),null,delegate {Window.Dispatcher.BeginInvoke(new Action(ShowWindow));});
            menu.Items.Add(UiText.Get("停止当前副屏"),null,delegate {Window.Dispatcher.BeginInvoke(new Action(RequestStop));});
            menu.Items.Add(UiText.Get("退出并清理"),null,delegate {Window.Dispatcher.BeginInvoke(new Action(delegate {Window.Show();Window.Close();}));});
            tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate {Window.Dispatcher.BeginInvoke(new Action(ShowWindow));};tray.Visible=true;
            Window.StateChanged+=delegate {if(Window.WindowState==WindowState.Minimized && C<CheckBox>("MinimizeTray").IsChecked==true)Window.Hide();};
        }
        private void ShowWindow(){Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();}
        public void Page(string name)
        {
            selectedPage=name;
            foreach(string p in new[]{"Home","Setup","Settings","Logs"}){
                C<FrameworkElement>(p+"Page").Visibility=p==name ? Visibility.Visible : Visibility.Collapsed;
                var b=C<Button>("Nav"+p);b.Background=p==name ? (Brush)Window.Resources["Tint"] : Brushes.White;
                b.Foreground=p==name ? (Brush)Window.Resources["AccentStrong"] : new SolidColorBrush(Color.FromRgb(105,121,143));
                b.FontWeight=p==name ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        public void ApplyTheme(string hex,string name,bool save)
        {
            if(hex==null || !System.Text.RegularExpressions.Regex.IsMatch(hex,"^#[0-9a-fA-F]{6}$"))throw new InvalidDataException(UiText.Get("主题色无效。"));
            Color color=(Color)ColorConverter.ConvertFromString(hex);
            Window.Resources["Accent"]=new SolidColorBrush(color);
            Window.Resources["Tint"]=new SolidColorBrush(Color.FromRgb((byte)(color.R*0.12+255*0.88),(byte)(color.G*0.12+255*0.88),(byte)(color.B*0.12+255*0.88)));
            Window.Resources["AccentStrong"]=new SolidColorBrush(Color.FromRgb((byte)(color.R*0.56+22*0.44),(byte)(color.G*0.56+42*0.44),(byte)(color.B*0.56+61*0.44)));
            double luminance=(0.2126*color.R+0.7152*color.G+0.0722*color.B)/255;
            Window.Resources["AccentInk"]=luminance>0.57 ? new SolidColorBrush(Color.FromRgb(24,49,66)) : Brushes.White;
            theme=new ThemePreference{Color=hex,Name=name};Text("ThemeName",UiText.Format("当前：{0}  {1}",ThemeDisplayName(),hex));
            foreach(string swatch in new[]{"ThemeSky","ThemeMist","ThemePink","ThemeMint"}){
                var button=C<Button>(swatch);
                bool active=((SolidColorBrush)button.Background).Color==color;
                button.Tag=active ? "Selected" : null;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,active ? UiText.Get("当前主题") : "");
            }
            Page(selectedPage);
            if(topology!=null)topology.ThemeChanged();
            if(save){StudioServices.AtomicJson(System.IO.Path.Combine(settings.StateDirectory,"studio-theme.json"),theme);Log(UiText.Get("已保存主题：")+ThemeDisplayName());}
        }
        private string ThemeDisplayName()
        {
            string color=(theme.Color ?? "").ToUpperInvariant();
            return UiText.Get(color=="#66BDE7" ? "天蓝" : color=="#70A5C2" ? "雾霾蓝" : color=="#E497B4" ? "樱粉" : color=="#65B7A0" ? "薄荷" : "自定义");
        }
        public void ApplyLanguage(string language,bool save)
        {
            UiText.ValidateLanguage(language);
            // Persist first: on failure the previous language and selection remain in effect.
            if(save)UiText.SaveLanguage(settings.StateDirectory,language);
            string status=C<TextBlock>("StatusPill").Text;
            string nickname=C<TextBox>("DeviceNickname").Text;
            UiText.SetLanguage(language);UiText.Apply(Window);
            switchingLanguage=true;
            try{C<ComboBox>("LanguagePicker").SelectedIndex=UiText.English ? 1 : 0;}finally{switchingLanguage=false;}
            Text("StatusPill",UiText.Get(status));
            Text("ThemeName",UiText.Format("当前：{0}  {1}",ThemeDisplayName(),theme.Color));
            foreach(string id in new[]{"DevicePicker","DisplayPicker"})C<ComboBox>(id).Items.Refresh();
            RefreshTrustSummary();UpdateDeviceHint();C<TextBox>("DeviceNickname").Text=nickname;UpdateButtons();topology.LanguageChanged();UpdateTrayLanguage();
            foreach(string swatch in new[]{"ThemeSky","ThemeMist","ThemePink","ThemeMint"}){
                var button=C<Button>(swatch);if(button.Tag!=null)System.Windows.Automation.AutomationProperties.SetItemStatus(button,UiText.Get("当前主题"));
            }
            if(stop!=null)UpdateTelemetry();
            else {Text("StreamInfo",UiText.Get("连接后显示分辨率与编码信息"));Text("StatsHint",UiText.Get(C<TextBlock>("StatsHint").Text));}
            Log(UiText.Get("界面语言已切换为简体中文。"));
        }
        private void UpdateTrayLanguage()
        {
            if(tray==null)return;
            var items=tray.ContextMenuStrip.Items;
            items[0].Text=UiText.Get("打开晖映工作台");items[1].Text=UiText.Get("停止当前副屏");items[2].Text=UiText.Get("退出并清理");
            UpdateTelemetry();
        }
        private void ChooseColor()
        {
            using(var dialog=new Forms.ColorDialog()){
                dialog.FullOpen=true;dialog.Color=System.Drawing.ColorTranslator.FromHtml(theme.Color);
                if(dialog.ShowDialog()==Forms.DialogResult.OK)ApplyTheme("#"+dialog.Color.R.ToString("X2")+dialog.Color.G.ToString("X2")+dialog.Color.B.ToString("X2"),UiText.Get("自定义"),true);
            }
        }
        private void Log(string message)
        {
            var box=C<TextBox>("LogText");if(box.Text.Length>24000)box.Text=box.Text.Substring(box.Text.Length-12000);
            box.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);box.ScrollToEnd();
            Text("FooterStatus",message.Replace('\n',' '));
        }
        private void Fail(Exception e) {Log(UiText.Get("操作未完成：")+e.Message);MessageBox.Show(Window,e.Message,UiText.Get("操作未完成"),MessageBoxButton.OK,MessageBoxImage.Warning);}
        private bool Confirm(string text,string title) {return MessageBox.Show(Window,text,title,MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;}
        private void SetBusy(bool value)
        {
            busy=value;bool locked=value || (topology!=null && topology.IsApplying);
            foreach(string name in operations)C<Button>(name).IsEnabled=!locked;
            C<ComboBox>("DevicePicker").IsEnabled=!locked;C<ComboBox>("DisplayPicker").IsEnabled=!locked;
            C<ComboBox>("ModePicker").IsEnabled=!locked;C<CheckBox>("Continuous").IsEnabled=!locked;
            if(topology!=null)topology.SetBusy();
            C<ComboBox>("LanguagePicker").IsEnabled=topology==null || !topology.IsApplying;
            UpdateButtons();
        }
        private void UpdateButtons()
        {
            var d=Device;bool valid=d!=null && d.State=="device" && d.Trusted;
            bool idle=!busy && (topology==null || !topology.IsApplying);
            C<Button>("Start").IsEnabled=idle && valid && C<ComboBox>("DisplayPicker").SelectedItem!=null;
            C<Button>("Start").Content=Mode==DisplayMode.Extend ? UiText.Get("开始扩展副屏 →") : UiText.Get("开始主屏镜像 →");
            C<Button>("Stop").IsEnabled=stop!=null;
            C<Button>("Trust").IsEnabled=idle && d!=null && d.State=="device" && !d.Trusted;
            C<Button>("ForgetDevice").IsEnabled=idle && valid;
            C<Button>("RenameDevice").IsEnabled=idle && valid;C<TextBox>("DeviceNickname").IsEnabled=idle && valid;
            foreach(string name in new[]{"HomeInstall","InstallApk","OpenPhone","Developer"})C<Button>(name).IsEnabled=idle && valid;
        }
        private void UpdateDeviceHint()
        {
            var d=Device;C<TextBox>("DeviceNickname").Text=d==null ? "" : d.Name ?? d.Model;
            Text("DeviceHint",d==null && scan==null ? UiText.Get("点击刷新，发现USB设备后选择并信任。") : d==null ? UiText.Get("未发现设备？检查数据线、USB调试和手机授权。") : d.State!="device" ? UiText.Get("请在手机上确认RSA授权，然后刷新。") : d.Trusted ? UiText.Get("已信任此设备，可以安装客户端或开始连接。") : UiText.Get("新设备仅被发现，尚未授权操作。请先点信任。"));
        }
        private void RefreshTrustSummary(){Text("TrustSummary",settings.ApprovedDevices.Length==0 ? UiText.Get("尚未信任任何设备。") : UiText.Format("本机已信任 {0} 个设备：{1}",settings.ApprovedDevices.Length,String.Join(UiText.English ? ", " : "、",settings.ApprovedDevices.Select(d=>String.IsNullOrEmpty(d.Name) ? d.Model : d.Name).ToArray())));}
        private void BindDisplays()
        {
            if(scan==null){UpdateButtons();return;}
            C<ComboBox>("DisplayPicker").ItemsSource=scan.Displays.Where(d=>Mode==DisplayMode.Extend ? d.Virtual && !d.Primary : !d.Virtual && d.Primary).ToArray();
            if(C<ComboBox>("DisplayPicker").Items.Count>0)C<ComboBox>("DisplayPicker").SelectedIndex=0;
            else Log(UiText.Get("尚无可用")+(Mode==DisplayMode.Extend ? UiText.Get("非主屏VDD。请到连接与安装准备虚拟显示器。") : UiText.Get("物理主屏。")));
            UpdateButtons();
        }
        private async void ScanDevices()
        {
            if(busy)return;SetBusy(true);Text("StatusPill",UiText.Get("●  正在发现设备"));
            try{scan=await Task.Run(()=>backend.Scan());C<ComboBox>("DevicePicker").ItemsSource=scan.Devices;
                var trusted=scan.Devices.FirstOrDefault(d=>d.Trusted && d.State=="device");
                C<ComboBox>("DevicePicker").SelectedItem=trusted ?? scan.Devices.FirstOrDefault();BindDisplays();UpdateDeviceHint();
                Log(UiText.Format("扫描完成：发现 {0} 个 Android 设备，{1} 个桌面显示器。",scan.Devices.Count,scan.Displays.Count));Text("StatusPill",UiText.Get("●  等待开始"));}
            catch(Exception e){Fail(e);Text("StatusPill",UiText.Get("●  扫描未通过"));}
            finally{SetBusy(false);if(closing)Window.Close();}
        }
        private async void TrustDevice()
        {
            var d=Device;if(busy || d==null)return;
            if(!Confirm(UiText.Get("将允许此应用操作你选中的Android设备：\n型号：")+d.Model+UiText.Get("\n序列号：")+d.Serial+UiText.Get("\n\n仅在你点击时安装客户端、打开应用或建立副屏连接。不会自动信任其他设备。"),UiText.Get("信任此设备")))return;
            SetBusy(true);try{await Task.Run(()=>StudioServices.Trust(settings,d));RefreshTrustSummary();C<ComboBox>("DevicePicker").Items.Refresh();UpdateDeviceHint();Log(UiText.Get("已信任 ")+d.Model+UiText.Get("，授权记录只保存在本机。"));}
            catch(Exception e){Fail(e);}finally{SetBusy(false);if(closing)Window.Close();}
        }
        private void ForgetDevice()
        {
            var d=Device;if(busy || d==null)return;
            if(!Confirm(UiText.Get("移除 ")+d.Model+UiText.Get(" 的应用信任？\n不会卸载手机软件，也不会撤销手机系统中的RSA授权。"),UiText.Get("移除信任")))return;
            var next=settings.ApprovedDevices.Where(a=>a.Serial!=d.Serial).ToArray();
            StudioServices.AtomicJson(System.IO.Path.Combine(settings.StateDirectory,"trusted-devices.json"),next);
            settings.ApprovedDevices=next;d.Trusted=false;C<ComboBox>("DevicePicker").Items.Refresh();RefreshTrustSummary();UpdateDeviceHint();UpdateButtons();Log(UiText.Get("已移除所选设备的应用信任。"));
        }
        private async void Install()
        {
            var d=Device;if(busy || d==null)return;
            if(!Confirm(UiText.Get("向 ")+d.Model+UiText.Get(" 安装已固定校验的 AnyDisplay v0.1.4 客户端？\n已有同签名版本会执行保留数据的覆盖安装；不静默卸载其他应用。\n如果手机出现USB安装提示，请在手机上确认。"),UiText.Get("安装Android客户端")))return;
            SetBusy(true);try{Log(UiText.Get("正在安装手机客户端，请留意手机上的确认提示…"));string result=await Task.Run(()=>StudioServices.InstallClient(settings,d));Log(result);}
            catch(Exception e){Fail(e);}finally{SetBusy(false);if(closing)Window.Close();}
        }
        private async void OpenPhone(bool developer)
        {
            var d=Device;if(busy || d==null)return;SetBusy(true);
            try{await Task.Run(()=>StudioServices.OpenClient(settings,d,developer));Log(developer ? UiText.Get("已请求打开手机开发者设置，不自动修改开关。") : UiText.Get("已请求打开手机端应用。"));}
            catch(Exception e){Fail(e);}finally{SetBusy(false);if(closing)Window.Close();}
        }
        private async void PrepareDriver()
        {
            if(busy)return;
            if(!Confirm(UiText.Get("准备固定校验的驱动文件到 C:\\VirtualDisplayDriver？\n只复制文件，不安装驱动；已有不同配置则停止，不覆盖。\n若系统拒绝写入，不绕过权限，请手动以管理员准备。"),UiText.Get("准备VDD文件")))return;
            SetBusy(true);try{string dir=await Task.Run(()=>StudioServices.PrepareDriver(settings));StudioServices.Open(dir,false);Log(UiText.Get("驱动文件已准备；不代表已安装。请先检查系统保护，再打开安装向导。"));}
            catch(Exception e){Fail(e);}finally{SetBusy(false);if(closing)Window.Close();}
        }
        private async void DriverWizard()
        {
            if(busy)return;SetBusy(true);
            try{
                var current=await Task.Run(()=>backend.Scan());
                if(current.Displays.Any(d=>d.Virtual) || await Task.Run(()=>StudioServices.ExistingVddDevice())){MessageBox.Show(Window,UiText.Get("检测到已有VDD，请不要重复安装。可直接返回工作台选择扩展。"),UiText.Get("已有虚拟显示器"));return;}
                string target=System.IO.Path.Combine(System.IO.Path.GetPathRoot(Environment.SystemDirectory),"VirtualDisplayDriver","MttVDD.inf");
                if(!File.Exists(target)){MessageBox.Show(Window,UiText.Get("请先点击准备驱动文件。"),UiText.Get("缺少驱动文件"));return;}
                if(!Confirm(UiText.Get("即将打开Windows官方添加硬件向导，需要管理员确认。\n建议先创建系统恢复点。驱动可能造成闪屏或窗口重排。\n仅选择Virtual Display Driver；签名错误或要求关闭安全功能时停止。\n\n向导需你手动完成，不自动修改显卡驱动或重启。"),UiText.Get("安装前确认")))return;
                StudioServices.Open(System.IO.Path.Combine(Environment.SystemDirectory,"hdwwiz.exe"),true);Log(UiText.Get("已打开添加硬件向导，安装需手动确认。完成后刷新设备。"));
            }catch(Exception e){Fail(e);}finally{SetBusy(false);if(closing)Window.Close();}
        }
        private void RequestStop(){if(stop!=null){stop.Set();Text("StatusPill",UiText.Get("●  正在停止"));Text("FooterStatus",UiText.Get("正在结束本次连接并清理，请稍候…"));}}
        private async void StartSession()
        {
            if(busy || topology.IsApplying)return;var d=Device;var display=C<ComboBox>("DisplayPicker").SelectedItem as DisplayInfo;
            try{SafetyPolicy.ValidateDevice(settings,d);SafetyPolicy.ValidateDisplay(Mode,display);}catch(Exception e){Fail(e);return;}
            bool continuous=C<CheckBox>("Continuous").IsChecked==true;DisplayMode mode=Mode;
            if(!Confirm(UiText.Get("连接 ")+d.Model+" → "+(mode==DisplayMode.Extend ? UiText.Get("独立扩展桌面") : UiText.Get("主屏镜像"))+"\n"+(continuous ? UiText.Get("持续连接，点击停止或关闭窗口退出。") : UiText.Get("30秒限时测试。"))+UiText.Get("\n\n仅USB；触控、音频、剪贴板回传禁用。"),UiText.Get("开始副屏")))return;
            stop=new ManualResetEvent(false);began=DateTime.Now;SetBusy(true);Text("StatusPill",UiText.Get("●  连接中"));Log(UiText.Get("正在建立USB会话…"));
            try{
                SessionResult result=await Task.Run(()=>backend.RunSession(d,display,mode,stop,continuous));
                bool ok=result.ExitCode==0 && result.Error==null && !result.TimedOut && result.DisplayLayoutUnchanged && result.CleanupErrors.Count==0;
                Text("StatusPill",ok ? UiText.Get("●  已停止并清理") : UiText.Get("●  连接异常"));
                Log(UiText.Get("会话结束：")+result.StopReason+UiText.Get("；退出码=")+result.ExitCode+UiText.Get("；布局不变=")+result.DisplayLayoutUnchanged+UiText.Get("。日志：")+result.LogFile);
                if(result.Error!=null)Log(result.Error);foreach(string error in result.CleanupErrors)Log(UiText.Get("清理提示：")+error);
            }catch(Exception e){Text("StatusPill",UiText.Get("●  未能连接"));Fail(e);}
            finally{stop.Dispose();stop=null;SetBusy(false);ResetMetrics();if(closing)Window.Close();}
        }
        private void ResetMetrics()
        {
            foreach(string name in new[]{"FpsValue","RateValue","RttValue","DropValue"})Text(name,"—");
            Text("StreamInfo",UiText.Get("连接后显示分辨率与编码信息"));
            Text("StatsHint",UiText.Get("未在传输 · 不保留过期统计。静止桌面可能降低帧率，这是按需传输。"));C<Canvas>("Chart").Children.Clear();
        }
        private void UpdateTelemetry()
        {
            if(stop==null){if(tray!=null)tray.Text=UiText.Brand+" · "+UiText.Get("●  等待连接").Replace("●  ","");return;}
            var s=backend.CurrentTelemetry;
            if(tray!=null)tray.Text=UiText.Brand+" · "+(s.Fresh && s.Fps.HasValue ? s.Fps.Value.ToString("0.0")+" FPS" : UiText.Get("等待统计"))+" · "+(s.Fresh && s.Mbps.HasValue ? s.Mbps.Value.ToString("0.0")+" Mbps" : "USB");
            if(!stop.WaitOne(0))Text("StatusPill","●  "+(s.HasConfig ? UiText.Get("正在传输") : UiText.Get("连接中"))+"  "+(DateTime.Now-began).ToString(@"hh\:mm\:ss"));
            Text("FpsValue",s.Fresh && s.Fps.HasValue ? s.Fps.Value.ToString("0.0") : "—");
            Text("RateValue",s.Fresh && s.Mbps.HasValue ? s.Mbps.Value.ToString("0.0") : "—");
            Text("RttValue",s.Fresh && s.RttMs.HasValue ? s.RttMs.Value.ToString("0.0") : "—");
            Text("DropValue",s.HasStats ? s.TotalDropped.ToString() : "—");
            Text("StreamInfo",s.HasConfig ? s.Width+" × "+s.Height+"  ·  "+UiText.Get(s.Codec)+UiText.Get("  ·  目标 ")+s.TargetHz+"Hz" : UiText.Get("正在等待编码配置…"));
            Text("StatsHint",!s.HasStats ? UiText.Get("等待手机端真实统计，首个采样不计算FPS。") : !s.Fresh ? UiText.Get("统计已超过3秒未更新，帧率显示为 —。") : s.TotalDecoded==0 ? UiText.Get("尚无成功解码帧，请检查手机是否亮屏解锁。当前统计不代表画面已可用。") : UiText.Get("实际解码FPS，非目标刷新率。")+(s.DecodeMs.HasValue ? UiText.Get(" 平均解码 ")+s.DecodeMs.Value.ToString("0.0")+UiText.Get("ms（不是端到端延迟）。") : UiText.Get(" 静止画面按需传输。")));
            var chart=C<Canvas>("Chart");chart.Children.Clear();double w=chart.ActualWidth,h=chart.ActualHeight;if(w<1 || h<1)return;
            for(int i=1;i<4;i++)chart.Children.Add(new Line{X1=0,X2=w,Y1=h*i/4,Y2=h*i/4,Stroke=new SolidColorBrush(Color.FromRgb(229,237,245)),StrokeThickness=1});
            if(s.History.Length<2)return;var points=new PointCollection();double max=Math.Max(60,s.History.Max());
            for(int i=0;i<s.History.Length;i++)points.Add(new Point(5+i*(w-10)/59,h-5-Math.Min(1,s.History[i]/max)*(h-10)));
            chart.Children.Add(new Polyline{Points=points,Stroke=(Brush)Window.Resources["Accent"],StrokeThickness=2.4,StrokeLineJoin=PenLineJoin.Round});
        }
        // Explicit developer verification flag only. Never called on normal window startup.
        public async Task<bool> VerifyLive(string report)
        {
            var r=new System.Collections.Generic.Dictionary<string,object>();
            bool ok=false;
            r["StartedAt"]=DateTimeOffset.Now.ToString("o");
            r["Scope"]="Explicit 25-second USB/VDD UI statistics check; no APK install or driver changes.";
            try {
                scan=await Task.Run(()=>backend.Scan());
                var d=scan.Devices.Single(x=>x.State=="device" && settings.ApprovedDevices.Any(a=>a.Serial==x.Serial && a.Model==x.Model));
                var display=SafetyPolicy.SelectSmokeDisplay(DisplayMode.Extend,scan.Displays);
                C<ComboBox>("DevicePicker").ItemsSource=scan.Devices;C<ComboBox>("DevicePicker").SelectedItem=d;
                C<ComboBox>("ModePicker").SelectedIndex=0;BindDisplays();C<ComboBox>("DisplayPicker").SelectedItem=display;
                UpdateDeviceHint();stop=new ManualResetEvent(false);began=DateTime.Now;SetBusy(true);
                using(var deadline=new System.Threading.Timer(delegate {stop.Set();},null,25000,Timeout.Infinite)) {
                    Task<SessionResult> session=Task.Run(()=>backend.RunSession(d,display,DisplayMode.Extend,stop,true));
                    await Task.WhenAny(session,Task.Delay(12000));
                    UpdateTelemetry();var snapshot=backend.CurrentTelemetry;
                    r["LiveSnapshot"]=snapshot;
                    r["DecodedFramesObserved"]=snapshot.TotalDecoded>0;
                    r["VisualUserConfirmed"]=false;
                    r["DisplayedFps"]=C<TextBlock>("FpsValue").Text;
                    r["DisplayedRate"]=C<TextBlock>("RateValue").Text;
                    r["StreamInfo"]=C<TextBlock>("StreamInfo").Text;
                    RenderPreview(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report),"studio-preview","live.png"));
                    var result=await session;r["Session"]=result;
                    double parsed;
                    ok=snapshot.HasStats && snapshot.Fresh && snapshot.HasConfig && snapshot.Fps.HasValue &&
                        Double.TryParse((string)r["DisplayedFps"],out parsed) && result.ExitCode==0 &&
                        result.Error==null && !result.TimedOut && result.DisplayLayoutUnchanged && result.CleanupErrors.Count==0;
                }
            }catch(Exception e){r["Error"]=e.ToString();}
            finally{if(stop!=null){stop.Dispose();stop=null;}SetBusy(false);}
            r["Passed"]=ok;r["EndedAt"]=DateTimeOffset.Now.ToString("o");
            StudioServices.AtomicJson(report,r);return ok;
        }
        public void RenderPreview(string path)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));Window.UpdateLayout();
            var content=Window.Content as FrameworkElement;
            double w=content.ActualWidth,h=content.ActualHeight;var bmp=new RenderTargetBitmap((int)w,(int)h,96,96,PixelFormats.Pbgra32);bmp.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);
        }
    }
}
