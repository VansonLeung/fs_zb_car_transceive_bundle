@echo off
setlocal
set URL=http://localhost:5080
set FLAGS=--app=%URL% --use-fake-ui-for-media-stream

set EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe
if exist "%EDGE%" goto launch_edge
set EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe
if exist "%EDGE%" goto launch_edge
set EDGE=%LocalAppData%\Microsoft\Edge\Application\msedge.exe
if exist "%EDGE%" goto launch_edge

goto check_chrome

:launch_edge
"%EDGE%" %FLAGS%
goto end

:check_chrome
set CHROME=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe
if exist "%CHROME%" goto launch_chrome
set CHROME=%ProgramFiles%\Google\Chrome\Application\chrome.exe
if exist "%CHROME%" goto launch_chrome
set CHROME=%LocalAppData%\Google\Chrome\Application\chrome.exe
if exist "%CHROME%" goto launch_chrome

goto fallback

:launch_chrome
"%CHROME%" %FLAGS%
goto end

:fallback
start "" %URL%

echo Could not find Edge/Chrome; opened default browser without auto camera permission flags.

:end
endlocal
