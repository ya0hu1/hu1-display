# Repeat the preview checks / 复现预览版自测

Build the controller on a local Windows drive as described in the [English](../README.en.md) or [Chinese](../README.md) README. From the repository root, run:

```powershell
$exe = (Resolve-Path '.\bin\Hu1.Display.exe').Path
$output = Join-Path ([IO.Path]::GetTempPath()) ('hu1-display-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($suite in 'self-test','layout-self-test','ui-self-test') {
    $report = Join-Path $output ($suite + '.txt')
    $p = Start-Process $exe -ArgumentList ('--' + $suite), ('"' + $report + '"') -Wait -PassThru
    Get-Content -LiteralPath $report
    if ($p.ExitCode -ne 0) { throw "$suite failed" }
}
```

| Suite | Checks | Boundary |
| --- | ---: | --- |
| Policy & protocol / 策略及协议 | 93 | In-memory fixtures, not a streaming session. |
| Layout / 布局 | 21 | Synthetic monitors; no Windows layout changes. |
| WPF UI / 工作台 | 10 | Isolated language preferences, synthetic devices, layout draft, theme and page rendering. |

The UI suite briefly opens its own window. It uses a fresh `ui-state-*` directory by the report, not the user's device trust store or `appsettings.local.json`. The eight PNGs in `studio-preview/{zh-CN,en-US}/` contain only this application's content. Preserve these two folder levels when comparing language variants.

本轮只运行上表三个入口。WPF 自测使用合成设备，在临时目录验证语言保存、回退以及切换时的设备选择、昵称草稿和布局草稿。不会读取真实信任记录、使用 ADB、访问手机或写入 Windows 布局。不要用 `--studio-live-check`、`--smoke-test` 或 `--layout-watchdog` 代替这些测试，它们并非普通界面自测。
