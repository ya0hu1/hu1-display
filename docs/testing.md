# 0.6.2 Preview · 在线位置与主题 / Live placement and themes

Windows 离线验证：策略 102、布局 35、回环 9、WPF/UI 17，共 **163** 项。用例覆盖已连接虚拟屏可编辑、连接中/退出中和实体屏不可在线移动、已占用方向避让、会话专属位置许可、高刷恢复不覆盖新坐标，以及浅深色和标签对比度。租约自测使用独立互斥锁，不会争用真实会话。

在线验收方法：确认手机正在扩展传输，拖动其编号，先检查草稿未改变原生坐标；应用后核对只有该虚拟屏的 X/Y 变化，实体屏、源模式、视频进程不变；选择还原或等待 15 秒，再核对原布局和连接。保留在线位置仅适用于当前桌面，不代表跨断开保存。

**单机 USB 实测：** 在 1920 × 1080 / 60 Hz 扩展会话中拖动虚拟屏编号。应用前 Windows 坐标未变；应用后只有该虚拟屏移动，两块实体屏和源模式均不变。手动还原恢复了原布局，视频进程没有重启，会话持续传输。本次没有实测「保留」、超时还原或双机并发。

**USB hardware check:** On one connected Android, dragging changed only the draft until Apply. Windows then moved only the selected VDD; both physical displays stayed put. Revert restored the original layout without restarting the stream. Keep, timeout and simultaneous phones were not exercised in this run.

## 0.6.1 Preview · 排版验证 / UI verification

Windows 自测：策略 102、布局 32、回环 9、WPF/UI 15，共 **158 项**。HUD 使用同一套度量与绘制逻辑验证 `1920 × 1080`：紧凑、详细，96/120/144/192 DPI，280/1000 像素可用宽度；窄窗口另外检查连接区换行和选屏参数。README 的图来自双语工作台自测与 144 DPI 合成 HUD，不是手机截图。

参考了 [PowerToys 的 Power Display](https://learn.microsoft.com/en-us/windows/powertoys/power-display)、[Apple HIG 的侧栏层级](https://developer.apple.com/design/human-interface-guidelines/sidebars)，以及 [Impeccable 的 distill / polish 指引](https://github.com/pbakaus/impeccable/tree/2fc5e1459efbbf06d92c7306517c286bf12490cf/.agents/skills/impeccable/reference)。保留 Windows 原生标题栏、表单、键盘焦点与系统显示器编号；没有打包第三方图像、字体或插件。

实机证据需分开看：最近一次 0.6 会话的 Windows 源和传输 CONFIG 均为 1920 × 1080、目标 60 fps。这不能证明手机最后 80 行可见。本轮已发现受信任的 Reno7；设备处于锁屏时，边缘验证入口会跳过，不启动会话、不修改手机设置。完整像素边缘检查尚未验收。可选的本地开发探针在解锁且空闲时，使用非主屏 MttVDD 的四边及第 1005 行色带检查显示范围；只在副屏客户端位于前台时内存取样，不存储手机截图。

## 0.6 Preview · 验证记录 / Validation

Windows x64/.NET Framework：`--self-test` **102**、`--layout-self-test` **32**、`--relay-self-test` **9**、`--ui-self-test` **12**，共 **155** 项通过。布局用例模拟活动/待机槽位、独占源、复制屏、主屏保护、显示器序号复用与容量副本；回环用例不使用手机。界面截图仅含合成设备。

此外，`--vdd-path-check <report>` 在现有 MttVDD 上通过了停用单一路径的 Windows **SDC_VALIDATE**，未执行 SDC_APPLY。单窗口探针只移动自己的 HWND：普通、最大化、最小化都保留状态，且还原到主屏。它不代表每个提权或自绘应用都能迁移。

**仍需实机验收 / hardware checklist**（用自己的普通测试窗口和设备）：

1. 预留两块 MttVDD，连接 A、B 到不同槽位。更换一个会话的 HUD 位置/透明度，只应改变对应手机画面。在镜像会话中 HUD 应不可用。
2. 把普通窗口和最大化窗口拖到 B。分别测试点停止、拔 B 的 USB 线、关闭工作台。B 的桌面应消失；窗口应出现在实体主屏，还原后仍可见。单独停 B 时 A 应继续传输；重新连接 B 应能再次使用待机槽位。
3. 取消「停用所选 MttVDD」后连接一块已活动的 VDD：窗口迁回按独立选项执行，已有桌面应保留。连接失败不应停用一块原本活动、尚未传输的桌面。
4. 保持另一个工具的 ADB 工作，选「退出并释放便携目录」，再尝试重命名新版本解压目录。既有工具的服务及信任、主题和语言文件不应被删除。

未以两台真机并发执行上述清单，未修改已安装驱动的容量，未实际切断当前 Windows 桌面；不要将上一版本的 Reno7 单机测量当作 0.6 多机性能数据。

## 0.5.1 Preview · 验证记录 / Validation

## Windows 自测 / Offline checks

| Suite | Passed | What it covers |
|---|---:|---|
| `--self-test` | 100 | Session policy, input filtering, fragmented packets, profile field encoding and unchanged defaults |
| `--layout-self-test` | 23 | Guarded VDD refresh switching/restore, synthetic displays, positions and validation; no Windows layout writes |
| `--relay-self-test` | 9 | Actual loopback sockets with synthetic peers, shutdown and filtering |
| `--ui-self-test` | 11 | Isolated WPF workspace: profile selectors, mirror isolation, locales, trust/selection state and page rendering |

**143 passed, 0 failed** on Windows. UI screenshots use synthetic displays only. To reproduce from a built checkout:

```powershell
$exe = (Resolve-Path '.\bin\Hu1.Display.exe').Path
$output = Join-Path ([IO.Path]::GetTempPath()) ('hu1-display-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($suite in 'self-test','layout-self-test','relay-self-test','ui-self-test') {
    $report = Join-Path $output ($suite + '.txt')
    $p = Start-Process $exe -ArgumentList ('--' + $suite), ('"' + $report + '"') -Wait -PassThru
    Get-Content -LiteralPath $report
    if ($p.ExitCode -ne 0) { throw "$suite failed" }
}
```

The four suites above do not access phones or change the real layout. `--ui-self-test` briefly opens an isolated window and writes to its own temporary state directory. The hardware measurements below were run separately with explicit consent; do not substitute a hardware smoke test or layout watchdog for an offline suite.

## 0.5.1 高刷复测 / High-refresh hardware rerun

Windows 11 + MttVDD at 1920 × 1080: the original single 60 Hz configuration was backed up; adding 90 and 120 to the same driver's global rate list and reloading **only that virtual device** made Windows enumerate **60, 90, 120 Hz**. Its idle mode remained 60 Hz. The physical panels, primary assignment, positions and orientation remained unchanged. Android device: OPPO Reno7, Android 13, maximum reported refresh 90 Hz.

每档 18 秒，17 个手机解码间隔样本，虚拟屏只显示移动条纹。下列 Windows 源 Hz 是会话中读取的实际值，不是请求值。The animation harness used a scoped 1 ms timer resolution; these are end-to-end session observations, not an isolated A/B measurement of any single optimization.

| Requested size / fps | Measured Windows source Hz | Negotiated fps | Median decoded fps | Decoded / reported dropped |
|---|---:|---:|---:|---:|
| 1920 × 1080 / 60 | 60 | 60 | 59.0 | 1044 / 0 |
| 1920 × 1080 / 90 | 90 | 90 | 87.0 | 1544 / 0 |
| 1280 × 720 / 90 | 90 | 90 | 87.0 | 1547 / 0 |
| 1920 × 1080 / 120 | 120 | **90** | 87.0 | 1540 / 0 |

All four sessions exited normally, reported no cleanup errors and restored the original native layout / 60 Hz idle mode. The last row verifies **120 Hz on Windows and a correctly clamped 90 fps request on the receiver**. It is not an end-to-end 120 fps measurement. Long-duration stability and performance on an actual 120 Hz receiver were not verified in this run.

## 0.5 历史基线：Reno7 USB 实测 / On-device stream measurements

2026-11-26 · Final 0.5 candidate · Windows 11 x64 · OPPO Reno7 (PFJM10), Android 13 · USB / H.264 · existing MttVDD. Android exposed 60 and 90 Hz modes. At its current 1920 × 1080 resolution, **Windows enumerated only 60 Hz** on the virtual display. No driver configuration, phone refresh policy or physical display was changed.

三组使用同一个移动条纹窗口，只画在非主屏 VDD 上；每组请求 18 秒。下表取最终候选每组 17 个实际间隔的 Android 解码 FPS 样本中位数，不是用屏幕刷新率推算的值。

| Requested | Negotiated CONFIG | Actual Windows VDD during capture | Median decoded fps | Decoded / reported dropped |
|---|---|---|---:|---:|
| 1920 × 1080 / 60 | 1920 × 1080 / 60 | 1920 × 1080 / 60 Hz | 59.0 | 1044 / 0 |
| 1280 × 720 / 90 | 1280 × 720 / 90 | 1920 × 1080 / 60 Hz | 60.0 | 1080 / 0 |
| 1920 × 1080 / 90 | 1920 × 1080 / 90 | 1920 × 1080 / 60 Hz | 60.1 | 1075 / 0 |

All three exited normally, reported no cleanup errors, and had matching native display identities, positions, modes and rates before/after. In particular the 720p test scaled the stream, **not** the Windows desktop. A decoded-frame counter is not a count of unique source frames or a motion-to-photon latency measurement. These short tests do not prove sustained 90 fps, long-session stability or zero network loss. Find X8 (ColorOS 16.0.10 / Android 16) is a **user-reported 0.4 compatibility observation**, not part of this benchmark.

## Relay hot path / 转发内存开销

0.5 allocates a bounded 64 KiB payload buffer once per direction instead of allocating a buffer and a tail array for every video packet. A Windows .NET Framework microbenchmark compared the 0.4 source (`a9bf129`) to the new hot path: after 100 warm-up packets, forward 12,000 identical 8 KiB synthetic video packets from a reusable memory stream to `Stream.Null`. Current-thread managed allocations: **99,360,000 bytes → 0 bytes** in the measured loop; generation-0 collections: **15 → 0** in this run. This measures only in-memory packet forwarding after initialization, not total application memory, CPU usage, USB latency or whole-session throughput.
