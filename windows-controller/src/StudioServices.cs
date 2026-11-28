using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Threading;
using System.Management;

namespace DeskMateLearning
{
    public static class StudioServices
    {
        private static IDisposable PhoneLease(DeviceInfo d){
            if(d==null)throw new InvalidOperationException(UiText.Get("请先选择一台已授权的设备。"));
            return ScreenSessions.Reserve(d.Serial,new DisplayInfo{Description="setup:"+d.Serial},DisplayMode.Extend);
        }
        public const string ApkHash="732ae1ebaabe0c0b2b40e8e88349b698be930443919fa4f3c0059bd79dd50803";
        public static string Hash(string path) {using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        public static Settings Load(string config)
        {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            Settings s;
            if(File.Exists(config))s=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(config));
            else s=new Settings {EnginePath=Path.Combine(root,"tools","engine","anydisplay-win.exe"),
                AdbPath=Path.Combine(root,"tools","adb","adb.exe"),
                EngineSha256="de3f996c840fa3e08470181d04759e0afb6145c1ba9382166fc4c6fb4dff71cd",
                AdbSha256="bae5ce98846b19caa74a34900850caa8b21d01976932b2ad83baeb5de5651aee",
                StateDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskLinkStudio"),
                ApprovedDevices=new ApprovedDevice[0]};
            if(String.IsNullOrEmpty(s.LogDirectory))s.LogDirectory=Path.Combine(s.StateDirectory,"logs");
            if(String.IsNullOrEmpty(s.ApkPath))s.ApkPath=Path.Combine(root,"packages",AndroidClient.PreviewHash==null ? "AnyDisplay-v0.1.4.apk" : "Hu1Display-Android.apk");
            if(String.IsNullOrEmpty(s.DriverDirectory))s.DriverDirectory=Path.Combine(root,"packages","VirtualDisplayDriver");
            SafetyPolicy.ValidateSettings(s);
            string approvals=Path.Combine(s.StateDirectory,"trusted-devices.json");
            if(File.Exists(approvals)){
                var list=new JavaScriptSerializer().Deserialize<ApprovedDevice[]>(File.ReadAllText(approvals));
                if(list==null)throw new InvalidDataException(UiText.Get("设备信任文件无效。"));
                foreach(var d in list){if(d==null || String.IsNullOrWhiteSpace(d.Model))throw new InvalidDataException(UiText.Get("设备信任记录无效。"));SafetyPolicy.ValidateSerial(d.Serial);}
                s.ApprovedDevices=list.GroupBy(d=>d.Serial+"/"+d.Model).Select(g=>g.First()).ToArray();
            }
            return s;
        }
        public static void AtomicJson(string path,object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(value));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
            finally{if(File.Exists(tmp))File.Delete(tmp);}
        }
        public static string Adb(Settings s,params string[] args)
        {
            var r=ProcessRunner.Run(s.AdbPath,args,90000,null);
            if(r.ExitCode!=0)throw new InvalidOperationException(r.Error+r.Output);
            return r.Output.Trim();
        }
        public static void Trust(Settings s,DeviceInfo d)
        {
            using(var lease=PhoneLease(d)){TrustOwned(s,d);}
        }
        private static void TrustOwned(Settings s,DeviceInfo d)
        {
            if(d==null || d.State!="device" || String.IsNullOrWhiteSpace(d.Model))throw new InvalidOperationException(UiText.Get("请先在手机上允许USB调试，再刷新设备。"));
            SafetyPolicy.ValidateSerial(d.Serial);new Backend(s).VerifyTools();
            if(Adb(s,"-s",d.Serial,"get-state")!="device" || Adb(s,"-s",d.Serial,"shell","getprop","ro.product.model")!=d.Model)
                throw new InvalidOperationException(UiText.Get("设备身份有变化，请重新扫描。"));
            var approved=s.ApprovedDevices.Where(a=>a.Serial!=d.Serial).Concat(new[]{new ApprovedDevice{Serial=d.Serial,Model=d.Model,Name=d.Model}}).ToArray();
            AtomicJson(Path.Combine(s.StateDirectory,"trusted-devices.json"),approved);
            s.ApprovedDevices=approved;d.Trusted=true;
        }
        public static void VerifyApk(Settings s) {if(!File.Exists(s.ApkPath) || Hash(s.ApkPath)!=(AndroidClient.PreviewHash ?? ApkHash))throw new InvalidDataException(UiText.Get("Android安装包缺失或校验失败。请使用完整便携包，不执行未知APK。"));}
        public static string InstallClient(Settings s,DeviceInfo d)
        {
            using(var lease=PhoneLease(d)){return InstallClientOwned(s,d);}
        }
        private static string InstallClientOwned(Settings s,DeviceInfo d)
        {
            new Backend(s).VerifyTools();SafetyPolicy.ValidateDevice(s,d);VerifyApk(s);
            if(Adb(s,"-s",d.Serial,"shell","getprop","ro.product.model")!=d.Model)throw new InvalidOperationException(UiText.Get("设备身份变化。"));
            int api;if(!Int32.TryParse(Adb(s,"-s",d.Serial,"shell","getprop","ro.build.version.sdk"),out api)||api<26)throw new InvalidOperationException(UiText.Get("客户端需要Android 8.0/API 26及以上。"));
            string result=Adb(s,"-s",d.Serial,"install","-r",s.ApkPath);
            if(!result.Split('\n').Any(x=>x.Trim()=="Success"))throw new InvalidOperationException(UiText.Get("安装未确认成功：")+result);
            if(AndroidClient.PreviewHash!=null)return UiText.Get("晖映 Android 预览客户端已安装。旧客户端未被替换。");
            return UiText.Get("安装成功。手机端当前使用 AnyDisplay v0.1.4（上游客户端），可点击打开。");
        }
        public static string OpenClient(Settings s,DeviceInfo d,bool developerSettings)
        {
            using(var lease=PhoneLease(d)){return OpenClientOwned(s,d,developerSettings);}
        }
        private static string OpenClientOwned(Settings s,DeviceInfo d,bool developerSettings)
        {
            new Backend(s).VerifyTools();SafetyPolicy.ValidateDevice(s,d);
            if(Adb(s,"-s",d.Serial,"shell","getprop","ro.product.model")!=d.Model)throw new InvalidOperationException(UiText.Get("设备身份变化。"));
            if(developerSettings)return Adb(s,"-s",d.Serial,"shell","am","start","-a","android.settings.APPLICATION_DEVELOPMENT_SETTINGS");
            var client=AndroidClient.Installed(pkg=>Adb(s,"-s",d.Serial,"shell","pm","list","packages",pkg));
            return Adb(s,"-s",d.Serial,"shell","am","start","-W","-n",client.Activity);
        }
        public static bool ExistingVddDevice()
        {
            using(var query=new ManagementObjectSearcher("SELECT Name,HardwareID FROM Win32_PnPEntity WHERE ClassGuid='{4D36E968-E325-11CE-BFC1-08002BE10318}'"))
            using(var result=query.Get())foreach(ManagementObject device in result){
                using(device){
                    string name=device["Name"] as string;var ids=device["HardwareID"] as string[];
                    if(String.Equals(name,"Virtual Display Driver",StringComparison.OrdinalIgnoreCase) ||
                        (ids!=null && ids.Any(id=>String.Equals(id,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase) || String.Equals(id,"MttVDD",StringComparison.OrdinalIgnoreCase))))return true;
                }
            }
            return false;
        }
        public static string PrepareDriver(Settings s)
        {
            string target=Path.Combine(Path.GetPathRoot(Environment.SystemDirectory),"VirtualDisplayDriver");
            string[] names={"MttVDD.inf","MttVDD.dll","mttvdd.cat","vdd_settings.xml"};
            string[] hashes={"550d211fe481e74dfe3f9d724ed78be48b3a9113405965d683d9373e8d672f5d","c9ca837f57a98fbd43bc416a7f535a95843626e7759eaf85cf0cd7ce334dbb05","08a0093fc9b2e32b287a6f8a77ca4de0a31830d29fc33d2b13a918dc859468f6","ffccc8c9e104f96914284302cddd8d54a3e41d73f099d86e9b8c9db5c42fb05d"};
            for(int i=0;i<names.Length;i++)if(!File.Exists(Path.Combine(s.DriverDirectory,names[i]))||Hash(Path.Combine(s.DriverDirectory,names[i]))!=hashes[i])throw new InvalidDataException(UiText.Get("驱动包缺失或哈希不匹配：")+names[i]);
            if(Directory.Exists(target)){
                for(int i=0;i<names.Length;i++)if(!File.Exists(Path.Combine(target,names[i]))||Hash(Path.Combine(target,names[i]))!=hashes[i])throw new InvalidOperationException(UiText.Get("系统目录已有其他驱动/配置，不覆盖。请手动核对：")+target);
                return target;
            }
            Directory.CreateDirectory(target);
            for(int i=0;i<names.Length;i++)File.Copy(Path.Combine(s.DriverDirectory,names[i]),Path.Combine(target,names[i]),false);
            return target;
        }
        public static void Open(string path,bool admin)
        {
            var si=new ProcessStartInfo(path){UseShellExecute=true};if(admin)si.Verb="runas";Process.Start(si);
        }
    }
}
