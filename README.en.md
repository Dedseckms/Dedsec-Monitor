![Dedsec Monitor](docs/banner.png)

# Every frame. Under control.

> **Dedsec Monitor 1.0.2** · [Download installer](https://github.com/Dedseckms/Dedsec-Monitor/releases/download/v1.0.2/Dedsec-Monitor-1.0.2-Setup-x64.exe) · [Portable ZIP](https://github.com/Dedseckms/Dedsec-Monitor/releases/download/v1.0.2/Dedsec-Monitor-1.0.2-Portable-x64.zip)

**Dedsec Monitor** is a native Windows performance monitor with a dark, acid-green interface, a click-through game overlay, live hardware readings and CSV session recording. Russian and English are included.

[Validation](docs/VALIDATION.md) · [Русский README](README.md) · [Report an issue](https://github.com/Dedseckms/Dedsec-Monitor/issues)

![Dedsec Monitor running on real hardware](docs/screenshots/dashboard.en.png)

## Get started

1. Install the **Setup x64 EXE**, or extract the entire **Portable x64 ZIP** and run `Dedsec.Monitor.exe`. The .NET runtime and PresentMon are included. Setup automatically prepares overlay and sensor support; internet access and Windows permission prompts are required. Portable first launch does the same when components are missing.
2. If frame capture or temperatures are unavailable, use **Settings → Restart with full access** and accept the Windows administrator prompt.
3. Launch a game after the monitor. RTSS supplies the in-game overlay; the desktop overlay uses windowed/borderless mode. Auto-select remembers the foreground graphics process; you can also choose it manually in Overview.
4. Enable the overlay. **Ctrl + Shift + O** toggles it; **Ctrl + Shift + R** starts/stops a recording.

CPU/GPU load, temperature and clocks, RAM, VRAM and available power sensors are collected through LibreHardwareMonitor. FPS comes from PresentMon's ETW capture or RTSS. The PresentMon path does not inject DLLs; RTSS uses an in-game hook. An unavailable reading is shown as `—`.

## Compatibility and measurements

- Windows 10 build 19041+ or Windows 11, x64. No Linux or ARM64 build in this release.
- Two overlay backends are available: an RTSS in-game OSD and a transparent desktop window for borderless/windowed games. Driver behavior and anti-cheat policies may restrict either path. No promise of support for every game.
- PresentMon FPS is `1000 / mean frame time` in a rolling two-second window; 1% low is the reciprocal of the average slowest 1% of frame times. The busiest swap chain is selected. These are application Present rates, not display refresh rates. RTSS uses its own measurement interval and does not supply per-frame 1% low.
- Sessions contain aggregated measurements at the configured 0.5–3 second polling interval, not every frame. Session average/minimum FPS summarize these measurements, not a per-frame benchmark.
- Hardware sensor availability depends on the device, drivers and permissions. Low-level CPU sensor support is prepared automatically using the bundled signed PawnIO installer, with Windows permission prompts. Restore components in Settings can repair missing support. See [technical documentation](docs/TECHNICAL.md).
- The initial binaries are unsigned. Verify downloads with the release's `SHA256SUMS.txt`.

Screenshots show the real application and real sensors. `Dedsec.OpenGlScene.exe` is a bounded rendering workload used to verify capture, not a game benchmark.

## Local first

No account, ads or cloud analytics. Optional startup updates contact GitHub for published stable releases and their assets; hardware readings are not sent. Settings, logs and recordings stay in `%LOCALAPPDATA%\Dedsec\Monitor`. Uninstall preserves this folder. Closing the window defaults to the system tray; use **Exit** in its menu to stop the app.

## Build

On Windows, install .NET 10 SDK, PowerShell 7 and Inno Setup 6.7.1+:

```powershell
./scripts/build.ps1 -Installer
dotnet format Dedsec.Monitor.slnx --verify-no-changes
```

The build script fetches verified dependencies, runs both test suites, then produces the self-contained app, updater, Portable ZIP and themed installer in `artifacts/`. GitHub Actions builds and tests packages. Releases are published explicitly after owner approval.

## Fullscreen / OpenGL and updates

Setup automatically downloads RTSS from the official Guru3D mirror, verifies pinned archive and executable hashes, and installs it when missing. No manual downloads or executable-path selection are required. Dedsec Monitor starts RTSS before the game. RTSS remains subject to its [author's terms](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/); no RTSS binaries are rehosted in our packages. The RTSS backend renders inside the game's API and is intended for fullscreen/OpenGL use; the desktop backend remains for windowed/borderless mode. Minecraft Java prefers RTSS readings when available. RTSS controls its panel placement. Anti-cheat restrictions still apply.

Real OpenGL capture and OSD rendering were verified on a separate graphics workload, not Minecraft itself. RTSS FPS uses its own measurement interval; per-frame 1% low is unavailable through this path and stays blank.

Startup updates ignore drafts/prereleases, verify SHA-256 and archive paths, preserve unrelated portable-folder files, and roll back if the new program cannot confirm startup. Recording defers application of an update. No published release or an offline check leaves the running app unaffected.

## License

GPL-3.0-or-later. [Third-party licenses and sources](THIRD-PARTY-NOTICES.md). This is an independent project, unaffiliated with Ubisoft, Intel, Microsoft or hardware manufacturers.
