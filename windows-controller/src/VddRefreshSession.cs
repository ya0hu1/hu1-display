using System;
using System.Linq;

namespace DeskMateLearning
{
    // A temporary rate change, owned by one explicitly requested high-refresh extend session.
    // Keep physical screens and the registry untouched. Never restore over a user's newer mode.
    internal sealed class VddRefreshSession
    {
        private readonly DesktopDisplay[] before, during;
        private readonly DesktopDisplay target;
        private readonly Func<DesktopDisplay[]> capture;
        private readonly Action<DesktopDisplay,int> setRate;
        private bool owned;

        internal VddRefreshSession(DesktopDisplay[] before,DisplayInfo choice,DisplayMode mode,int fps,
            Func<DesktopDisplay[]> capture,Action<DesktopDisplay,int> setRate)
        {
            this.before=LayoutPolicy.Clone(before); this.capture=capture; this.setRate=setRate;
            if(mode!=DisplayMode.Extend || (fps!=90 && fps!=120) || choice==null || !choice.Virtual || choice.Primary)return;
            var matches=this.before.Where(d=>d.Virtual && !d.Primary && choice.Description!=null &&
                choice.Description.StartsWith(d.Name+" ",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)return;
            var v=matches[0];
            if(v.RefreshHz==fps || v.AvailableRates==null || !v.AvailableRates.Contains(fps) || !v.AvailableRates.Contains(v.RefreshHz))return;
            target=v;during=LayoutPolicy.Clone(this.before);during.Single(d=>d.Key==v.Key).RefreshHz=fps;
        }
        internal void Apply()
        {
            if(target==null)return;
            if(!ScreenSessions.SameRouteLayout(before,capture(),target.Name))throw new InvalidOperationException(UiText.Get("显示布局已变化，请刷新后重试。"));
            owned=true; // The native call may partially succeed. Finally always verifies ownership.
            setRate(target,during.Single(d=>d.Key==target.Key).RefreshHz);
            if(!ScreenSessions.SameRouteLayout(during,capture(),target.Name))throw new InvalidOperationException(UiText.Get("虚拟屏未进入请求的刷新率。"));
        }
        internal void Restore()
        {
            if(!owned)return;
            var current=capture();
            if(!ScreenSessions.SameRouteLayout(before,current,target.Name,true)){
                if(!ScreenSessions.SameRouteLayout(during,current,target.Name,true))throw new InvalidOperationException(UiText.Get("显示布局已被更改，未覆盖新设置。请核对虚拟屏刷新率。"));
                setRate(current.Single(d=>d.Key==target.Key),target.RefreshHz);
                if(!ScreenSessions.SameRouteLayout(before,capture(),target.Name,true))throw new InvalidOperationException(UiText.Get("虚拟屏刷新率还原未通过验证。"));
            }
            owned=false;
        }
    }
}
