using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DeskMateLearning
{
    public enum DisplayMode { Mirror, Extend }
    public sealed class ApprovedDevice { public string Serial; public string Model; public string Name; }
    public sealed class Settings
    {
        public string EnginePath, AdbPath, EngineSha256, AdbSha256, LogDirectory, StateDirectory;
        public string ApkPath, DriverDirectory;
        public ApprovedDevice[] ApprovedDevices;
        public int StreamingSeconds = 30, WallClockSeconds = 75;
        public bool ReadOnly = true;
    }
    public sealed class DeviceInfo
    {
        public string Serial, Model, State, Name;
        public bool Trusted;
        public string Label { get { return (!String.IsNullOrEmpty(Name) ? Name : String.IsNullOrEmpty(Model) ? UiText.Get("Android 设备") : Model)+" · "+(State=="unauthorized" ? UiText.Get("请在手机确认授权") : State=="offline" ? UiText.Get("离线") : Trusted ? UiText.Get("已信任") : UiText.Get("待确认信任"))+" · …"+Serial.Substring(Math.Max(0,Serial.Length-4)); } }
    }
    public sealed class DisplayInfo
    {
        public int Index; public bool Primary, Virtual; public string Description;
        public string Label { get {
            Match m=Regex.Match(Description ?? "", "\"([^\"]+)\" (\\d+)x(\\d+)");
            return m.Success ? (Virtual ? UiText.Get("独立副屏") : Primary ? UiText.Get("主屏") : UiText.Get("显示器"))+" · "+m.Groups[2].Value+" × "+m.Groups[3].Value+" · "+m.Groups[1].Value : Description;
        } }
    }
    public sealed class ScanResult
    {
        public readonly List<DeviceInfo> Devices = new List<DeviceInfo>();
        public readonly List<DisplayInfo> Displays = new List<DisplayInfo>();
    }
    public sealed class SessionResult
    {
        public string StartedAt, EndedAt, Error, HostStdout, HostStderr;
        public string Mode, SelectedDisplay;
        public int SelectedDisplayIndex;
        public int? ExitCode;
        public bool TimedOut, UserStopped, DisplayLayoutUnchanged;
        public bool Continuous, LogTailTruncated;
        public string StopReason;
        public double ElapsedSeconds;
        public long BlockedInputFrames, BlockedClipboardFrames;
        public readonly List<string> CleanupErrors = new List<string>();
        public string LogFile;
        public TelemetrySnapshot Telemetry;
    }
    public static class SafetyPolicy
    {
        public static void ValidateSettings(Settings s)
        {
            if (s == null || s.ApprovedDevices == null)
                throw new InvalidOperationException(UiText.Get("设备信任配置无效。"));
            if (!s.ReadOnly) throw new InvalidOperationException(UiText.Get("此版本要求启用只读输入过滤。"));
            if (s.StreamingSeconds < 10 || s.StreamingSeconds > 60)
                throw new InvalidOperationException(UiText.Get("传输测试时长必须为 10–60 秒。"));
            if (s.WallClockSeconds < s.StreamingSeconds + 10 || s.WallClockSeconds > 90)
                throw new InvalidOperationException(UiText.Get("会话超时设置无效。"));
            foreach (ApprovedDevice d in s.ApprovedDevices) ValidateSerial(d.Serial);
        }
        public static void ValidateSerial(string serial)
        {
            if (String.IsNullOrEmpty(serial) || !Regex.IsMatch(serial,"^[A-Za-z0-9_.:-]+$"))
                throw new InvalidOperationException(UiText.Get("ADB 设备序列号为空或含有不受支持的字符。"));
        }
        public static void ValidateDevice(Settings s, DeviceInfo d)
        {
            if (d == null || d.State != "device") throw new InvalidOperationException(UiText.Get("请先选择一台已授权的设备。"));
            ValidateSerial(d.Serial);
            bool allowed = false;
            foreach (ApprovedDevice a in s.ApprovedDevices)
                if (a.Serial == d.Serial && a.Model == d.Model) allowed = true;
            if (!allowed) throw new InvalidOperationException(UiText.Get("设备与本机信任记录不符，已拒绝访问。"));
        }
        public static DisplayInfo SelectSmokeDisplay(DisplayMode mode, IEnumerable<DisplayInfo> displays)
        {
            if (mode != DisplayMode.Mirror && mode != DisplayMode.Extend)
                throw new InvalidOperationException(UiText.Get("不支持此显示模式。"));
            DisplayInfo selected = null;
            foreach (DisplayInfo d in displays)
            {
                if (d == null) continue;
                bool eligible = mode == DisplayMode.Mirror ? d.Primary && !d.Virtual : d.Virtual && !d.Primary;
                if (!eligible) continue;
                if (selected != null) throw new InvalidOperationException(UiText.Get("找到多个可用显示器，请在工作台中明确选择。"));
                selected = d;
            }
            ValidateDisplay(mode, selected);
            return selected;
        }
        public static void ValidateDisplay(DisplayMode mode, DisplayInfo d)
        {
            if (mode != DisplayMode.Mirror && mode != DisplayMode.Extend)
                throw new InvalidOperationException(UiText.Get("不支持此显示模式。"));
            if (d == null || d.Index < 0) throw new InvalidOperationException(UiText.Get("请选择可用的显示器。"));
            if (mode == DisplayMode.Mirror && (!d.Primary || d.Virtual))
                throw new InvalidOperationException(UiText.Get("镜像模式仅支持实体主屏。"));
            if (mode == DisplayMode.Extend && (!d.Virtual || d.Primary))
                throw new InvalidOperationException(UiText.Get("扩展模式需要已有的非主屏 VDD，不会改用镜像。"));
        }
        public static string[] RemoveOwnedForward(string serial,int port)
        {
            ValidateSerial(serial);
            if (port<1024 || port>65535) throw new InvalidOperationException(UiText.Get("本次会话的转发端口无效。"));
            return new string[] { "-s",serial,"forward","--remove","tcp:"+port };
        }
        public static List<DeviceInfo> ParseDevices(string text)
        {
            var result = new List<DeviceInfo>();
            foreach (string line in text.Split('\n'))
            {
                Match m = Regex.Match(line.Trim(),@"^(\S+)\s+(device|unauthorized|offline)(?:\s|$)");
                if (!m.Success) continue;
                Match model = Regex.Match(line,@"(?:^|\s)model:(\S+)");
                result.Add(new DeviceInfo { Serial=m.Groups[1].Value, State=m.Groups[2].Value,
                    Model=model.Success ? model.Groups[1].Value : "" });
            }
            return result;
        }
        public static List<DisplayInfo> ParseDisplays(string text)
        {
            var result = new List<DisplayInfo>();
            foreach (string line in text.Split('\n'))
            {
                Match m=Regex.Match(line.Trim(),@"^(\d+): (.+)$");
                if (!m.Success) continue;
                result.Add(new DisplayInfo { Index=Int32.Parse(m.Groups[1].Value), Description=m.Groups[2].Value.Trim(),
                    Primary=line.Contains("[primary]"), Virtual=line.Contains("[Virtual Display Driver]") });
            }
            return result;
        }
    }
}
