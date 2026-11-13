# Third-party software

Dedsec Monitor is licensed under **GPL-3.0-or-later**. The following independent components retain their own licenses. They are distributed unmodified.

| Component | Version | License | Source |
| --- | --- | --- | --- |
| PresentMon console | 2.3.1 (x64) | MIT | [GameTechDev/PresentMon](https://github.com/GameTechDev/PresentMon/tree/v2.3.1) |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0, with third-party components | [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6) |
| PawnIO optional official installer | 2.2.0 | GPL-2.0-or-later with linking exception; upstream distribution | [PawnIO source at 2.2.0](https://github.com/namazso/PawnIO/tree/2.2.0), [official release](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0) |
| .NET Desktop Runtime | 10.0.x, exact version in build metadata | MIT, with third-party components | [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/wpf](https://github.com/dotnet/wpf) |

PresentMon copyright © 2017–2025 Intel Corporation. LibreHardwareMonitor copyright © LibreHardwareMonitor contributors; portions © Open Hardware Monitor contributors. .NET copyright © .NET Foundation and contributors.

License texts for PresentMon, LibreHardwareMonitor and its dependencies are in `licenses/`, copied alongside the installed program. The self-contained .NET distribution also includes its own notices. NuGet dependency licenses and exact versions can be inspected with `dotnet list src/Dedsec.Monitor package --include-transitive`; upstream source is linked above. No changes were made to MPL-covered source files.

PawnIO copyright © namazso. Its source corresponding to the bundled 2.2.0 installer is available from [this exact upstream tag](https://github.com/namazso/PawnIO/tree/2.2.0). Obtain the complete source including submodules with `git clone --recurse-submodules --branch 2.2.0 https://github.com/namazso/PawnIO.git`. The license, including its linking exception, is included in `licenses/PawnIO.txt`. The signed official installer is distributed unmodified and only runs after a user action.

The Dedsec name is a project name chosen by its author. This project is not affiliated with Ubisoft, Intel, Microsoft or hardware manufacturers.
