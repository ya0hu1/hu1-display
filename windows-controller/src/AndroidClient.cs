using System;
using System.Linq;

namespace DeskMateLearning
{
    // Keep launch, forward and cleanup paired for the whole session. A preview install never
    // needs to uninstall, stop or take over the listener of the original AnyDisplay client.
    internal sealed partial class AndroidClient
    {
        internal static readonly AndroidClient Preview=new AndroidClient("io.github.ya0hu1.display",47821);
        internal static readonly AndroidClient Upstream=new AndroidClient("dev.anydisplay.client",47820);
        internal readonly string Package, Activity, Endpoint;
        private AndroidClient(string package,int port) {
            Package=package;Activity=package+"/dev.anydisplay.client.ui.MainActivity";Endpoint="tcp:"+port;
        }
        internal static AndroidClient Installed(Func<string,string> listPackages) {
            foreach(var client in new[]{Preview,Upstream})
                if(listPackages(client.Package).Split('\n').Any(s=>s.Trim()=="package:"+client.Package))return client;
            throw new InvalidOperationException(UiText.Get("未检测到手机客户端。工作台不会自动安装 APK。"));
        }
        // Only an explicitly supplied build input can authorize the packaged preview APK.
        // No user-editable runtime manifest or checksum sidecar is trusted for installation.
        static partial void SupplyPreviewHash(ref string hash);
        internal static string PreviewHash { get { string hash=null;SupplyPreviewHash(ref hash);return hash; } }
    }
}
