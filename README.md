# 晖映 · hu1 display

**简体中文** · [English](README.en.md)　|　Windows x64 → Android · USB 副屏工作台 · 0.5.1 Preview

用一根 USB 数据线，把 Android 手机变成 Windows 的独立副屏。可以把窗口拖到手机上，也可以明确选择镜像主屏。右上角可切换中英文；视频在本机传输，不经过云端。

[快速上手](#快速上手) · [选择分辨率和帧率](#选择分辨率和帧率) · [支持范围](#支持范围) · [源码构建](#方式二从源码构建) · [测试记录](docs/testing.md)

![工作台：合成虚拟屏、Windows 刷新率和会话传输设置](docs/assets/workspace-zh-CN.png)

## 快速上手

### 方式一：使用预编译 Windows 便携包（推荐）

[下载 0.5.1 Preview · Windows x64 便携 ZIP](https://github.com/ya0hu1/hu1-display/releases/download/v0.5.1-preview/hu1-display-0.5.1-preview-windows-x64.zip) · [SHA-256 校验文件](https://github.com/ya0hu1/hu1-display/releases/download/v0.5.1-preview/hu1-display-0.5.1-preview-windows-x64.zip.sha256) · [其他版本与发布说明](https://github.com/ya0hu1/hu1-display/releases)。**不需要编译、安装 Android Studio 或覆盖旧版**。请选择带 `windows-x64` 的附件，而不是 GitHub 自动生成的 **Source code (zip)**。

1. **校验并解压。** 可用 `Get-FileHash .\hu1-display-0.5.1-preview-windows-x64.zip -Algorithm SHA256` 对照随包 `.sha256`。完整解压到 Windows 本地独立目录；不要只取 EXE，也不要直接从压缩包或网络路径运行。
2. **打开工作台。** 双击 `Hu1.Display.exe`；只看界面不需要手机或虚拟显示驱动。便携包未正式签名，请核对来源，不要关闭系统防护。
3. **连接并信任手机。** 在 Android 开发者选项开启 **USB 调试**，使用数据线连接并解锁手机，核对 RSA 提示后允许。工作台点「刷新」，选择自己的设备并点「信任此设备」。手机端的 USB 授权和晖映的本机信任是两步。
4. **准备客户端。** 已有 AnyDisplay v0.1.4 则无须重复安装。否则在「连接与安装」点「安装手机端」，在手机上确认；如系统要求允许 USB 安装，按本机策略决定。客户端沿用上游名称，不叫“晖映”。
5. **开始使用。** 有独立副屏需求，先完成下面的 Windows 首次配置，选「扩展」和对应 VDD。只想看主屏则选「镜像」，无须 VDD。点击开始；结束时点「停止」或退出工作台。默认不会自动连接新插入的设备。

### Windows 首次配置：区分 USB 与虚拟屏

| 用途 | 需要什么 | 不需要什么 |
|---|---|---|
| 识别手机、传输视频 | 支持数据传输的 USB 线、授权的 ADB、匹配的 Android 客户端 | 不使用 AOA，不要用 Zadig 替换手机 MTP 接口；不需要 Root |
| 独立扩展桌面 | Windows 已启用的**非主屏** Virtual Display Driver / MttVDD | 已有可用 VDD 不要重装；不自动创建或删除虚拟屏 |
| 镜像实体主屏 | Windows 主屏及以上 USB 条件 | 无须安装虚拟显示驱动 |

第一次使用扩展模式：先在 **Windows 设置 → 系统 → 显示** 确认是否已有虚拟屏，并设为扩展而非复制。没有时，可在「连接与安装」查看系统保护、准备已校验的驱动文件、打开手动安装向导；**准备文件不等于安装**。跟随 [VDD 官方项目](https://github.com/VirtualDrivers/Virtual-Display-Driver) 对应版本的安装说明；不要替换 GPU 驱动或关闭驱动签名检查。准备好后回工作台刷新；缺少 VDD 时不会偷偷镜像主屏。

## 选择分辨率和帧率

在工作台的**手机虚拟屏 →「传输设置 ↓」**，或直接到「选择独立桌面」卡片，在开始连接前选择。更换档位请先停止会话；它不是修改主屏的按钮。

| 设置 | 可选值 | 怎么选 |
|---|---|---|
| 副屏传输分辨率 | 跟随手机端、1280 × 720、1920 × 1080 | 文字清晰优先选 1080p；减轻编码、USB 和解码负载先试 720p |
| 目标帧率 | 跟随手机端、30、60、90、120 fps | 办公建议从 60 开始；功耗优先试 30；90/120 需要源和接收端都支持 |

两项可独立选择；默认保留 Android 请求，镜像模式不使用这些覆盖值。引擎根据手机能力和 VDD 模式协商：可用时可能临时切换虚拟屏模式，否则可能按比例缩放视频。工作台区分 **Windows 源刷新率**（画布旁）、**协商后的传输规格**和 **Android 实际解码 FPS**。输入 90 并不会让 60 Hz 源产生 90 个不同画面；静止桌面可接近 0 fps。

**怎样让源屏也进入高刷？** 在扩展模式明确选 90/120 后，如果所选非主屏 VDD 在当前桌面分辨率下列出了该 Hz，0.5.1 会临时切换其刷新率，正常停止后还原，不写入注册表。跟随手机端、30/60、镜像、未列出的刷新率不会触发此切换。如果用户在会话中改动了桌面，程序不会为了还原而覆盖新布局；异常强退后可在 Windows「高级显示」中核对虚拟屏。手机仍可协商到较低帧率。

**虚拟屏仍只列出 60 Hz？** 本机的 MttVDD 25.7.23 在 `vdd_settings.xml` 的 `<global>` 下使用多个 `<g_refresh_rate>` 声明可用刷新率。[单个 1080p 虚拟屏的参考配置](docs/vdd-high-refresh.xml) 保留 60，并增加 90 和 120。驱动准备包默认仍是保守配置。仅在使用对应驱动时，停止会话并备份原 XML，按需合并这些条目，按驱动文档仅重新启动对应虚拟适配器，再在工作台同步 Windows。不要覆盖其他驱动的配置、重启实体显卡或修改签名策略。回退方法是恢复原 XML 并重载同一虚拟设备。

## 支持范围

| 平台 / 设备 | 本版证据与边界 |
|---|---|
| Windows 11 x64 + .NET Framework 4.8 | 实机运行 WPF 控制端；USB / H.264，单设备、单会话 |
| OPPO Reno7 / Android 13 | 0.5.1 动态实测：1080p/60 中位数 59 fps；1080p/90 与 720p/90 均为 87 fps。源屏 120 Hz 时 Reno7 协商到 90，不是手机跑到了 120 fps |
| OPPO Find X8 / ColorOS 16.0.10 / Android 16 | 用户反馈 0.4 可正常使用；未单独验证 0.5 的 90 fps 档位 |
| 其他 Android 8.0+ | 客户端最低要求，不是对所有机型、GPU 或 OEM 系统的兼容保证 |
| macOS、HarmonyOS NEXT、无线、多设备 | 不支持。手机触控、键鼠、音频及剪贴板回传均被过滤 |

## 方式二：从源码构建

在 Windows 11 x64 的**本地磁盘**上，使用现有 .NET Framework 4.8、WPF 及对应编译器。无须为控制层安装 Rust、CMake 或 Android SDK。

```powershell
git clone https://github.com/ya0hu1/hu1-display.git
cd hu1-display
.\windows-controller\build.ps1
.\bin\Hu1.Display.exe
```

也可把本仓库地址交给编程助手，按 `windows-controller/build.ps1` 构建控制层。脚本只编译，不下载软件、不安装驱动。若本机脚本策略受限，应先核对组织策略，不要为此绕过安全限制。

**编译成功 ≠ 已具备传输依赖。** 若已有完整便携包，最简单的是在其独立副本中替换控制端 EXE。干净检出的目录需自行取得 [AnyDisplay v0.1.4](https://github.com/efekurucay/anydisplay/releases/tag/v0.1.4) 的 Windows CLI 与 APK、[Google Platform-Tools](https://developer.android.com/tools/releases/platform-tools) 的匹配 Windows 组件。本地验证使用 Platform-Tools 37.0.3；工作台拒绝不匹配的哈希，不要用随机版本替代。路径均相对 EXE：

```text
Hu1.Display.exe
tools/engine/anydisplay-win.exe
tools/adb/{adb.exe, AdbWinApi.dll, AdbWinUsbApi.dll}
packages/AnyDisplay-v0.1.4.apk
packages/VirtualDisplayDriver/    # 可选：用于手动准备已校验的 VDD 驱动文件
```

源码位于 `windows-controller/src/`，测试在 `windows-controller/tests/`。`bin/` 是本地控制层编译输出，`dist/windows/` 是本地便携包及 SHA-256 交付目录；不要提交二进制产物、真实信任文件、设备序列号或原始会话日志。

### 排查与退出

- **看不到手机：** 换数据线，亮屏解锁并确认 RSA 授权，再点「刷新」。不要批量结束 ADB 进程或替换 USB 接口驱动。
- **90 档只有约 60 fps：** 先核对虚拟屏旁的源 Hz 和当前分辨率可用 Hz，再核对已协商规格和实际 FPS。若 Windows 不提供 90 Hz，降低传输分辨率可减轻负载，但不会解除源刷新率上限。
- **卸载或迁移：** 退出程序后可移除便携目录。设备信任和偏好另存于 `%LOCALAPPDATA%\DeskLinkStudio`，删除会重置；移除工作台不等于卸载 Windows 显示驱动。

[测试方法与结果](docs/testing.md) · [第三方来源](THIRD_PARTY_NOTICES.md) · [MIT 许可](LICENSE)。本轮未验收真实拓扑拖动后的「应用 / 保留 / 取消 / 超时还原」，使用副屏无须执行这些操作。
