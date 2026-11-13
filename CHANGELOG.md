# Changelog

## 1.0.2-preview — local owner testing only

- One installer prepares the game overlay and sensor support automatically; portable first launch and in-app repair use the same verified provisioning flow.
- Explicit dark installer and uninstaller styling, including the title bar, with Dedsec artwork.
- Draggable application title bar with minimize/maximize/close and a window size that fits the work area.
- RTSS in-game OSD and OpenGL FPS fallback alongside PresentMon capture.
- Stable-release startup updates with verified downloads and rollback. Publication remains disabled pending owner feedback.

## 1.0.0 — 2026-11-13

First Windows x64 release of Dedsec Monitor.

- Native WPF dashboard with a dark green theme and live performance timeline.
- PresentMon ETW capture, automatic foreground process selection and manual process selection.
- FPS, rolling two-second 1% low and frame time.
- CPU/GPU load, available temperatures and clocks, RAM, VRAM and GPU power sensors.
- Click-through overlay with corners, display selection, metric toggles, scale and opacity.
- CSV session recording and persistent JSON summaries.
- Global overlay/recording hotkeys, system tray, optional Windows startup and RU/EN.
- Self-contained Portable ZIP and themed, per-user Inno Setup installer.
- Optional official signed PawnIO installer accessible from Settings.

Compatibility: windowed/borderless overlay; no universal game or anti-cheat guarantee. Exclusive fullscreen and mixed-DPI behavior need device-specific validation. Some sensors require PawnIO and elevated rights. Binaries are unsigned.
