# hu1 display

[简体中文](README.md) · **English**　|　Windows workspace · 0.4 source preview

Use an Android phone or tablet as a second screen for Windows over USB. Choose a device, inspect the desktop layout and control the session from one workspace. Video travels through local ADB forwarding, not a cloud service.

![English workspace with three synthetic displays and no connected device](docs/assets/workspace-en-US.png)

*Rendered by the app with a synthetic layout. No desktop or phone content was captured.*

## Workspace & language

Choose **English** or **简体中文** from **Interface language** in the upper right. On first launch, Chinese Windows defaults to 晖映; other locales default to hu1 display. Switching takes effect immediately, keeps your current session and layout draft, and is remembered on this computer. Windows system dialogs, the upstream Android client and existing log entries keep their original language.

- **Workspace:** Inspect primary, physical and virtual displays. Drag to edit a draft. Extend requires an existing non-primary virtual display; mirror is an explicit, separate choice.
- **Connection & setup:** Refresh USB devices and explicitly trust your device before installation or connection. Opening the workspace does not scan the phone or install a driver.
- **Preferences:** Choose one of four accent colors or your own; manage local trust and device nicknames. Language, theme and trust records stay on this computer.
- **Logs & help:** Inspect session status and diagnostics. FPS, bitrate and RTT use actual samples; unavailable metrics show a dash.

## Build and open

Check out the repository on a **local Windows 11 x64 drive**, with .NET Framework 4.8 and WPF installed. From the repository root:

```powershell
.\windows-controller\build.ps1
.\bin\Hu1.Display.exe
```

This builds only the controller and downloads nothing. If script execution is restricted, check your local policy; do not disable protections to run the build.

**Browsing the UI needs no phone or driver. Streaming additionally requires** Android 8.0+, a data cable, authorized USB debugging, the hash-pinned AnyDisplay v0.1.4 engine and APK, and Google ADB with its companion DLLs. Relative to the EXE, the default paths are `tools/engine/anydisplay-win.exe`, `tools/adb/` and `packages/AnyDisplay-v0.1.4.apk`. Extend also requires an existing non-primary VDD. Refresh and confirm your own device before connecting. Touch, audio and clipboard forwarding are filtered in this preview.

## Publication & validation

This is a **controller source preview**, not a ready-to-stream portable build. The public snapshot contains no prebuilt EXE, APK, ADB, drivers, upstream source mirrors or local runtime data. See [third-party attribution](THIRD_PARTY_NOTICES.md) for component origins and licenses.

The Windows build passed **93 policy checks, 21 synthetic layout checks and 10 WPF UI checks**. All four pages rendered in both languages with synthetic data. [Reproduce the checks](docs/testing.md). No phone, driver installation or real display-layout apply/restore transaction was tested in this round. **Keep your current Windows layout; we do not recommend using “Apply to Windows” yet.** No sustained FPS, end-to-end latency or device compatibility guarantee is implied.

The controller is [MIT licensed](LICENSE). Please do not attach device serials, trust files, pairing keys or unredacted logs to issues.
