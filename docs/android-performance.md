# Android 0.7.0 本地验收 / Local acceptance

本轮只提供本地候选。完整解压本地 **acceptance-kit** 到新的 Windows 本地目录，在 PowerShell 运行 `./Build-Studio.ps1`，再打开该目录生成的 `Hu1.Display.exe`。这一步需要 Windows 自带的 .NET Framework 4.8 编译器，不需要 Android SDK，不会启动 ADB、安装手机应用、改显示布局或联网。遵守机器的脚本执行策略。**旧版 EXE 不识别新版手机端。**

1. 在工作台刷新、信任自己的手机，选择「安装手机端」。核对设备和手机提示。包名 `io.github.ya0hu1.display`，应用名 **晖映 · hu1 Display / hu1 Display**，保留原来的 AnyDisplay；两者同时存在时新版工作台优先打开晖映。安装包是本地调试签名，不是发行签名。
2. 手机端齿轮 → **显示 → 手机帧率浮层**：关闭（默认）、仅帧率、详细统计。连接后也可以点悬浮菜单里的 **FPS** 立即切换；不依赖 Windows 的副屏统计窗，镜像时也能用。关掉或转入后台不再每秒刷新手机 HUD，不会关闭主机协议所需的统计。
3. 手机端 **持续性能优先** 默认关闭。打开后重新连接，手机请求面板上报的最高帧率（上限 120），支持时只在前台视频窗口启用 Android 的持续性能提示。不支持该提示也能播放；关闭并重连回到保存的手动刷新率。电脑指定的档位仍优先。不要为此修改手机系统刷新策略、温控或锁频设置。
4. 建议在 Reno7 用同一段持续运动画面先比较 1080p60，再比较 1080p90。扩展模式在 PC 明确选 **90 fps** 才会尝试临时切换支持该档的 VDD；「跟随手机」不会把一个 60 Hz 的 Windows 源变成 90 Hz。记下协商目标、手机输出 FPS、丢帧、温度和运行时间，静止桌面不适合判断上限。结束后停止会话；回退只需移除独立预览客户端，原版数据和系统设置不需要还原。

## 为什么选这三个改动？

| 源码中的问题 | 这次的取舍 |
| --- | --- |
| 原手机 HUD 使用流控 ACK 计数。部分解码器启动停滞时进入提前 ACK 模式；排入输入队列不等于已经解码。 | 只统计 `MediaCodec.releaseOutputBuffer` 成功提交到 Surface 的视频输出，每秒按单调时钟取差值。首次采样显示 `—`，切换前后台、重建 Surface 时丢弃旧基线。**这不是屏幕实际扫描帧率**，合成器仍可丢帧。Android 官方说明 [OnFrameRenderedListener](https://developer.android.com/reference/android/media/MediaCodec.OnFrameRenderedListener) 的回调可延迟、合批，Android 14 以前也可能缺失；不把回调频率冒充面板读数。 |
| 解码器以前总是把工作速率设为 32767；视频 Surface 没有把协商节奏告诉系统。 | 仍保留低延迟和兼容性重试，但按**协商帧率**给解码器工作量提示。Android 11+ 前台 Surface 使用 [`setFrameRate`](https://developer.android.com/media/optimize/performance/frame-rate) 并在暂停时清除；Android 8–10 不调用。互动桌面使用默认兼容策略；这是给系统的提示，不强制模式切换。 |
| 手动选高 fps 与能长期维持的性能不是一回事。 | 仅在 [`PowerManager.isSustainedPerformanceModeSupported`](https://developer.android.com/reference/android/os/PowerManager#isSustainedPerformanceModeSupported()) 为真时请求 [`Window.setSustainedPerformanceMode`](https://developer.android.com/reference/android/view/Window#setSustainedPerformanceMode(boolean))，暂停即撤销。它追求长期稳定，可能低于短时峰值；不承诺最高功率，不覆盖省电/热保护。 |

**本轮证据**：Reno7 / PFJM10，Android 13，上报 1080×2400、60/90 Hz；USB 供电，基线电池温度 20°C，thermal status 0。设备身份和原始日志不提交。Android 调试 APK 已构建；原有 40 项 JVM 测试及另行运行的 4 项新策略/采样测试通过。**本候选 APK 未在手机上完成安装或传输验收**，没有本版 90 fps、能耗、持续性能模式生效或长时温升的实测结论。既有的 [0.5.1 性能表](testing.md) 是旧客户端数据。

## Rebuild

Use JDK 17, Gradle **8.13**, Android SDK 36 and Build Tools 35.0.0:

```sh
gradle -p android-client :app:testDebugUnitTest :app:assembleDebug
```

`android-client/app/build/outputs/apk/debug/app-debug.apk` is for local evaluation only. Build Studio with `windows-controller/build.ps1 -OutputDirectory <new-portable-folder> -AndroidPreviewApk <apk>` and place the identical APK at `packages/Hu1Display-Android.apk`. Its SHA-256 is embedded at compile time, not read from a mutable manifest. Without the optional build input, installation still requires the pinned upstream APK. USB launch/forward/cleanup uses the chosen package and its own port (47821 loopback for hu1, 47820 for upstream).

To assemble the offline Windows build kit without uploading anything: `python tools/make-android-acceptance-kit.py <reviewed-0.6.2-portable.zip> <apk> <new-kit.zip>`. No keys, APKs, SDKs or run logs belong in Git. For production use a separate release signing key and repeat device/thermal validation.
