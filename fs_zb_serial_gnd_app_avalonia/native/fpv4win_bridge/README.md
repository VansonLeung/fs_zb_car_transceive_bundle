# fpv4win_bridge (Direct Native Build)

This folder builds `fpv4win_bridge.dll` directly from in-repo native sources.

## What it builds

- `fpv4win_bridge.dll`
- C ABI exports expected by the Avalonia app:
  - `fpv4win_bridge_probe`
  - `fpv4win_bridge_start`
  - `fpv4win_bridge_stop`
  - `fpv4win_bridge_get_last_error`

The build links fpv4win WFB processor sources and rtl8812au monitor-mode receiver sources.

## Prerequisites

- Windows with Visual Studio 2022 Build Tools (C++ workload)
- Git
- CMake (the script supports PATH lookup and `C:\Program Files\CMake\bin\cmake.exe` fallback)

## Build (PowerShell)

From `fs_zb_serial_gnd_app_avalonia`:

```powershell
.\native\fpv4win_bridge\scripts\build-windows.ps1 -Configuration Release
```

Build and copy DLL into desktop output folders:

```powershell
.\native\fpv4win_bridge\scripts\build-windows.ps1 -Configuration Release -CopyToDesktopOutputs
```

## Notes

- The script uses short default roots to avoid Windows path-length errors:
  - `C:\vcpkg-fsbridge`
  - `C:\b\fpv4win_bridge`
- Override roots with environment variables:
  - `FPV4WIN_BRIDGE_VCPKG_ROOT`
  - `FPV4WIN_BRIDGE_BUILD_ROOT`
- If `__references\fpv4win-main\fpv4win-main\3rd\rtl8812au-monitor-pcap` or `...\3rd\devourer` are missing, the script clones them automatically.
