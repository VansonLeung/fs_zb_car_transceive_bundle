# FS ZB Ground App (Avalonia)

Cross-platform Ground App for OpenIPC/FPV streaming, designed for:
- Windows desktop
- Android mobile

This app provides a responsive control layout that adapts between wide desktop windows and narrow mobile screens, with low-latency stream tuning options.

## Features

- Responsive UI (desktop + mobile form factors)
- OpenIPC stream URL input and quick presets
- Native libusb pipeline embedding via `fpv4win_bridge` with direct in-repo C++ build route
- VID:PID compatibility detection for fpv4win executable candidates: `0BDA:881A`, `2B89:0043`
- USB device address display for selected WFB adapter candidate
- Channel and channel-width configuration (fpv4win-style options)
- Single `START` / `STOP` action for WFB session control (fpv4win-style)
- RTSP/HTTP/UDP stream support via LibVLC
- Adjustable network caching and RTSP TCP mode

## Project layout

- `FsZbGroundApp/`: shared Avalonia UI + view models
- `FsZbGroundApp.Desktop/`: Windows desktop head
- `FsZbGroundApp.Android/`: Android head
- `references/sources.md`: study references used for this implementation

## Build prerequisites

- .NET SDK 9.x
- For Android builds: Android workload

Install Android workload:

```powershell
dotnet workload restore .\FsZbGroundApp.Android\FsZbGroundApp.Android.csproj
```

## Build

Windows desktop:

```powershell
dotnet build .\FsZbGroundApp.Desktop\FsZbGroundApp.Desktop.csproj -c Release
```

Android APK:

```powershell
dotnet build .\FsZbGroundApp.Android\FsZbGroundApp.Android.csproj -c Release
```

## Run

Windows desktop:

```powershell
dotnet run --project .\FsZbGroundApp.Desktop\FsZbGroundApp.Desktop.csproj
```

## Native libusb bridge

Build `fpv4win_bridge.dll` directly from this repository:

```powershell
.\native\fpv4win_bridge\scripts\build-windows.ps1 -Configuration Release
```

Build and copy to desktop output folders:

```powershell
.\native\fpv4win_bridge\scripts\build-windows.ps1 -Configuration Release -CopyToDesktopOutputs
```

After build, ensure:

- `fpv4win_bridge.dll`

in the desktop output directory (for example `FsZbGroundApp.Desktop\bin\Debug\net9.0\` or `FsZbGroundApp.Desktop\bin\Release\net9.0\`).

When the bridge is loaded, pressing `START` launches the native libusb receiver pipeline for the selected VID:PID adapter and forwards RTP to local UDP player port `52356`.

If the bridge is not present, app falls back to serial control transport when available.

Android deploy (example):

```powershell
dotnet build .\FsZbGroundApp.Android\FsZbGroundApp.Android.csproj -t:Install -c Debug
```

## OpenIPC tips

- Typical main stream URL: `rtsp://<camera-ip>:554/av0_0`
- Typical sub stream URL: `rtsp://<camera-ip>:554/av0_1`
- Replace `<camera-ip>` with your OpenIPC camera host/IP
- Keep controller and camera on the same low-latency network

## WFB workflow

1. Select WFB adapter candidate (VID:PID).
2. Verify VID:PID compatibility and address shown in the sidebar.
4. Select channel + channel width.
5. Set key path and codec.
6. Click `Apply Channel` (optional) then `START`.
7. Click `STOP` to end session.
8. Use `udp://@:52356` if your WFB process or native bridge forwards RTP to local port 52356.
