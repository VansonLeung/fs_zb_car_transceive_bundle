@echo off
setlocal
title RC Bundle Launcher (win-x64)

set ROOT=%~dp0win-x64

set HID=%ROOT%\rc_hid_monitor\HIDDeviceMonitor.exe
set GND=%ROOT%\rc_gnd\RCCarController.exe
set GUI_DIR=%ROOT%\rc_gui
set GUI=%GUI_DIR%\fs_zb_serial_gnd_app_win10_net_webapp_gui.exe
set GUI_LAUNCHER=%ROOT%\rc_gui_launcher\fs_zb_serial_gnd_app_win10_net_webapp_gui_launcher.exe

rem Launch sequence with 5s gaps
if exist "%HID%" (
  echo Starting HID monitor...
  start "HID" "%HID%"
) else (
  echo HID monitor not found at %HID%
)

timeout /t 5 /nobreak >nul

if exist "%GND%" (
  echo Starting Ground app...
  start "GND" "%GND%"
) else (
  echo Ground app not found at %GND%
)

timeout /t 5 /nobreak >nul

if exist "%GUI%" (
  echo Starting Web GUI (Kestrel)...
  set "ASPNETCORE_CONTENTROOT=%GUI_DIR%"
  start "GUI" /d "%GUI_DIR%" "%GUI%"
  set "ASPNETCORE_CONTENTROOT="
) else (
  echo Web GUI not found at %GUI%
)

timeout /t 5 /nobreak >nul

if exist "%GUI_LAUNCHER%" (
  echo Starting GUI Launcher (WebView2)...
  start "GUI_LAUNCHER" "%GUI_LAUNCHER%"
) else (
  echo GUI Launcher not found at %GUI_LAUNCHER%
)

echo Done.
endlocal
