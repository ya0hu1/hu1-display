# 0.5 Preview · 验证记录 / Validation

## Windows 自测 / Offline checks

| Suite | Passed | What it covers |
|---|---:|---|
| `--self-test` | 100 | Session policy, input filtering, fragmented packets, profile field encoding and unchanged defaults |
| `--layout-self-test` | 21 | Synthetic displays, positions and validation; no Windows layout writes |
| `--relay-self-test` | 9 | Actual loopback sockets with synthetic peers, shutdown and filtering |
| `--ui-self-test` | 11 | Isolated WPF workspace: profile selectors, mirror isolation, locales, trust/selection state and page rendering |

**141 passed, 0 failed** on Windows. UI screenshots use synthetic displays only. To reproduce from a built checkout:

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

## Reno7 USB 实测 / On-device stream measurements

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
