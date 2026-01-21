@echo off
setlocal
title RC Bundle Launcher (win-x64)

:: Resolve ROOT to the folder where this script lives, then append win-x64
for %%i in ("%~dp0.") do set "ROOT=%%~fi\win-x64"

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
  start "GND" /d "%ROOT%\rc_gnd" "%GND%"
) else (
  echo Ground app not found at %GND%
)

timeout /t 5 /nobreak >nul

@REM if exist "%GUI%" (
@REM   echo Starting Web GUI (Kestrel)...
@REM   set "ASPNETCORE_CONTENTROOT=%GUI_DIR%"
@REM   start "GUI" /d "%GUI_DIR%" "%GUI%"
@REM   set "ASPNETCORE_CONTENTROOT="
@REM ) else (
@REM   echo Web GUI not found at %GUI%
@REM )

@REM timeout /t 5 /nobreak >nul

@REM if exist "%GUI_LAUNCHER%" (
@REM   echo Starting GUI Launcher (WebView2)...
@REM   start "GUI_LAUNCHER" "%GUI_LAUNCHER%"
@REM ) else (
@REM   echo GUI Launcher not found at %GUI_LAUNCHER%
@REM )

echo Done.
endlocal
