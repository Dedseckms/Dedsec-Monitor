# Validation

## 1.0.2 — release validation

The owner approved publication on 2026-11-13 after reviewing the local 1.0.2-preview build. Stable 1.0.2 retains its implementation and removes preview labeling. CI builds and tests packages; GitHub release publication is performed explicitly.

- Stable packaging passed all 40 tests, format verification and self-contained publishing. [GitHub CI run 43406467095](https://github.com/Dedseckms/Dedsec-Monitor/actions/runs/43406467095) completed successfully, including both actual updater fixture paths.
- Stable installer smoke (`test-installer.ps1 -PrepareComponents`) passed without skipping component preparation: installed app launched, enumerated 106 sensors, recorded eight samples, controlled OSD visibility and uninstalled. RTSS/PawnIO were already present for this package check.
- Stable updater success and rollback fixture tests both returned `Verified=true`. Independent read-only QA approved the implementation and final documentation/screenshots before merge.

- 40 automated tests passed: 27 core tests and 13 Windows integration tests. Coverage includes actual shared-memory slot ownership and lock handling, mocked stable-release download/caching, checksum failures, ZIP traversal/reserved names, transactional rollback, automatic component provisioning and Intel hybrid-core clock selection.
- PresentMon alone produced no FPS for the separate OpenGL workload on the local Intel GPU. With RTSS, Dedsec Monitor read nonzero FPS from the same kind of workload, reported source `RTSS`, and rendered its OSD into the OpenGL framebuffer. The captured framebuffer is a real image, not a generated mockup.
- The custom frame's minimize/maximize/restore checks passed. The initial window remained inside the work area. Dragging is implemented on the visible caption; physical mouse dragging still needs owner feedback.
- The RTSS server was installed locally from its signed official distribution for the OpenGL test. It is not redistributed in our installation package. The new automatic production downloader was separately exercised against the actual official mirror: archive and executable hashes passed. Tests exercise automatic installer selection/arguments, registration checks, failures and reboot handling with fixtures; they do not replace a fresh-machine end-to-end installation test.
- The production `--prepare-components` flow installed the previously missing PawnIO 2.2.0 silently and returned success. A subsequent self-contained smoke run enumerated 106 sensors and read real CPU package temperature and hybrid-core clocks (about 67 °C and 1977 MHz at the final sample). RTSS was already installed during this run; a fresh-machine test of both installers together remains outstanding.
- The installer welcome, license and cancellation dialog were visually inspected in the compiled preview and displayed dark backgrounds. The final installer additionally forces styled title bars. Further UI automation was stopped when the owner pressed Escape.
- The owner has the earlier preview running concurrently. Its registered hotkeys prevent the isolated screenshot process from registering the same shortcuts; the screenshot process correctly reports the conflict. This is not a fresh single-instance hotkey validation.
- The disposable packaging smoke test explicitly skips shared component installation (`QA_SKIPCOMPONENTS=1`); it verifies app installation, self-contained launch, sampling/OSD and uninstall. Normal setup automatically prepares missing shared components. A fresh Windows VM with no RTSS/PawnIO is still needed to verify both prerequisite installers and their UAC prompts together.
- The final 1.0.2-preview setup was additionally tested with `scripts/test-installer.ps1 -PrepareComponents`, without the QA opt-out. Its component-preparation child returned success with RTSS/PawnIO already present; the installed app enumerated 106 sensors, recorded eight samples, showed/hid the OSD and uninstalled successfully.
- The actual bundled updater executed a windowless fixture successfully with a startup acknowledgment; a second run without acknowledgment restored old files and relaunched the previous fixture. No GitHub release was published for either test. `scripts/test-updater.ps1` reproduces both paths.
- Minecraft itself, protected games and a true exclusive fullscreen game still need the owner's manual test. No universal compatibility is claimed.

## 1.0.0 local baseline

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
