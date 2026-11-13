# Release validation — 1.0.0

Local validation on Windows x64, 2026-11-13. These results describe the tested machine and workload; they are not claims about all games or hardware.

| Check | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings / 0 errors |
| Core tests | 14 passed, 0 failed |
| `dotnet format --verify-no-changes` | Passed |
| NuGet transitive vulnerability query | No known vulnerable packages reported by the configured NuGet source at validation time |
| PresentMon 2.3.1 binary | Pinned SHA-256 verified |
| Optional PawnIO 2.2.0 installer | Pinned SHA-256 and official Authenticode signature verified; not installed during QA |
| Live hardware | CPU/GPU utilization and RAM available; 76 sensors enumerated |
| Real graphics capture | Separate `Dedsec.TestScene.exe` process captured through ETW with nonzero FPS, frame time and rolling 1% low |
| Overlay | Actual overlay window shown and hidden by app logic during the smoke run |
| Hotkeys | Both global key registrations succeeded on this machine |
| Session recording | Nine actual polling samples written; CSV and JSON summary saved |
| Cleanup | No Dedsec PresentMon child or named ETW capture session left after normal exit |
| Installer | Installed into a disposable directory, installed self-contained app launched, smoke run passed, generated uninstaller removed the executable |
| Screenshots | Five RU pages and an EN overview exported from the real WPF visual tree and visually inspected |

CPU temperature and CPU clocks were unavailable on the test machine because PawnIO was not installed. The app displayed `—`. The optional driver's installation and those readings were not tested. GPU readings were tested on Intel integrated graphics; AMD and NVIDIA were not available for local testing.

Protected games, exclusive fullscreen, mixed-DPI/multi-monitor configurations, Windows startup across a real sign-in cycle and the administrator consent dialog require separate manual validation. Successful registration is not an end-to-end keyboard test. No game-specific anti-cheat compatibility has been established by the rendering workload.

The reproducible installer smoke procedure is `./scripts/test-installer.ps1`. Detailed local logs and diagnostics are in the ignored `artifacts/qa/` folder; machine-specific process lists and temporary paths are not published in this report.
