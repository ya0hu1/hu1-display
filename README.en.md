# hu1 display

[简体中文](README.md) · **English**　|　Windows x64 → Android · USB second-screen workspace · 0.5.1 Preview

Use your phone as an independent Windows desktop, or explicitly mirror the primary monitor. The Windows workspace is bilingual and sends video over local USB forwarding, not a cloud service.

[Quick start](#quick-start) · [Stream settings](#stream-settings) · [Compatibility](#compatibility) · [Build from source](#build-from-source) · [Test results](docs/testing.md)

![Workspace with a synthetic VDD, source refresh information and the stream selectors](docs/assets/workspace-en-US.png)

## Quick start

**Option A — prebuilt Windows portable ZIP (recommended).** If you have `hu1-display-0.5.1-preview-windows-x64.zip`, you do not need a compiler or Android Studio. [GitHub Releases](https://github.com/ya0hu1/hu1-display/releases) is the public distribution location. A local 0.5.1 candidate has been built and checked; only treat a matching, named release asset as a binary download. GitHub's *Source code (zip)* is not an installer.

1. Compare `Get-FileHash .\hu1-display-0.5.1-preview-windows-x64.zip -Algorithm SHA256` with the `.sha256` sidecar. Extract the **entire ZIP to a local Windows drive** in a separate folder. Do not run from the archive or a network share.
2. Open `Hu1.Display.exe`. Browsing the workspace needs no phone or driver. The binary is not formally signed; verify its source instead of disabling security protections.
3. Enable **USB debugging** on Android, connect a data-capable cable, unlock the device and verify the RSA prompt. In the workspace click **Refresh**, select your device and **Trust this device**. Android authorization and application trust are separate.
4. If needed, use **Connection & setup → Install Android client** and confirm the installation on your device. The client is the original AnyDisplay v0.1.4, not a separate hu1 display APK.
5. Choose **Extend** and an existing non-primary VDD, or explicitly choose **Mirror** and the physical primary screen. Start the session; stop or exit when finished. The workspace does not automatically connect devices.

### First-time Windows setup

USB transport uses **ADB**, not USB AOA or WinUSB. Do not use Zadig, replace an MTP/GPU driver, root the phone or disable signature checks. Mirror needs no virtual display. For Extend, check **Windows Settings → System → Display** for an enabled, extended Virtual Display Driver / MttVDD monitor. Do not reinstall a working driver. If missing, Connection & setup can open System Protection, prepare the verified driver files and open the manual wizard. Preparing files does **not** install a driver. Follow the applicable [VDD upstream instructions](https://github.com/VirtualDrivers/Virtual-Display-Driver), then refresh. Missing VDD never silently falls back to mirroring.

## Stream settings

Select the **virtual display → Stream settings ↓**, or use **Choose your desktop**. Set the options before starting; stop the session before changing them. These are independent Extend-only requests, not changes to the primary monitor.

| Setting | Choices | Starting point |
|---|---|---|
| Stream resolution | Client setting, 1280 × 720, 1920 × 1080 | 1080p for text; 720p to reduce pixel load |
| Target frame rate | Client setting, 30, 60, 90, 120 fps | 60 for normal use; 30 for lower load; 90/120 when both source and receiver support it |

Both default to preserving Android's request. The engine negotiates with the device, may temporarily switch a supported VDD mode, or scale video to fit. **Windows source Hz**, **negotiated stream target** and **measured Android decoder FPS** are different metrics. A 60 Hz source does not produce 90 distinct desktop frames by requesting 90 fps. A static desktop can legitimately report near-zero FPS.

**High-refresh source:** An explicit 90/120 Extend request temporarily switches only the selected non-primary VDD if Windows exposes that rate at its current desktop size. Normal session cleanup restores the prior rate without writing registry settings. Client default, 30/60, mirror and unsupported rates do not trigger this switch. If the layout changes externally the controller will not overwrite the new settings. After a forced termination, check the VDD under Windows Advanced display. The client may negotiate a lower target.

**Only 60 Hz listed?** For the tested MttVDD 25.7.23 configuration, multiple `<g_refresh_rate>` elements under `<global>` advertise additional rates. The [one-screen 1080p example](docs/vdd-high-refresh.xml) preserves 60 and adds 90/120. Packaged driver preparation still uses conservative defaults. Stop sessions, back up your XML, merge only applicable entries and reload only the matching virtual adapter using its driver instructions. Do not restart physical display adapters. To revert, restore the original XML and reload that VDD.

## Compatibility

| Platform / device | Evidence and scope |
|---|---|
| Windows 11 x64 + .NET Framework 4.8 | WPF controller; USB/H.264, one device and one session |
| OPPO Reno7 / Android 13 | 0.5.1: median 59 fps at 1080p/60, 87 fps at 1080p/90 and 720p/90. With a 120 Hz source Reno7 negotiated 90, not 120 fps |
| OPPO Find X8 / ColorOS 16.0.10 / Android 16 | User reports 0.4 works. No separate 0.5 high-FPS benchmark on this model |
| Other Android 8.0+ | Client minimum, not a compatibility certification |
| macOS, HarmonyOS NEXT, wireless, multiple devices | Not supported. Reverse touch/keyboard, audio and clipboard are filtered |

## Build from source

**Option B — customize the controller.** On a local Windows drive with .NET Framework 4.8, WPF and the framework compiler:

```powershell
git clone https://github.com/ya0hu1/hu1-display.git
cd hu1-display
.\windows-controller\build.ps1
.\bin\Hu1.Display.exe
```

You can give the repository URL to a coding assistant and ask it to follow the build script. It builds only the controller, downloads nothing and installs no driver. Do not bypass local execution policy. Rust, CMake and Android Studio are not required for this build.

For streaming, replace the EXE in a separate copy of a complete portable package, or obtain the pinned Windows CLI and APK from [AnyDisplay v0.1.4](https://github.com/efekurucay/anydisplay/releases/tag/v0.1.4) and the matching Windows [Google Platform-Tools](https://developer.android.com/tools/releases/platform-tools) (tested: 37.0.3). Put `anydisplay-win.exe` in `tools/engine/`; put `adb.exe`, `AdbWinApi.dll` and `AdbWinUsbApi.dll` in `tools/adb/`; put `AnyDisplay-v0.1.4.apk` in `packages/`, all relative to the EXE. `packages/VirtualDisplayDriver/` is optional for manual driver preparation. Do not substitute unverified binaries: hashes are checked before use.

Sources: `windows-controller/src/`. Tests: `windows-controller/tests/`. The local controller build goes to `bin/`; local Windows ZIPs and their checksums go to `dist/windows/`, not Git.

**Troubleshooting:** If the phone is missing, check the data cable, lock screen and RSA prompt, then refresh. If 90 fps looks like 60, compare the VDD's available rates with the negotiated target and decoder stats before changing settings. Exit the app before removing its portable folder; `%LOCALAPPDATA%\DeskLinkStudio` holds separate local trust and preferences. Do not attach device identifiers, credentials or raw session logs to issues.

[Tests & measurements](docs/testing.md) · [Third-party notices](THIRD_PARTY_NOTICES.md) · [MIT license](LICENSE). Real layout apply/keep/cancel/timeout was not tested in this round and is not required for streaming.
