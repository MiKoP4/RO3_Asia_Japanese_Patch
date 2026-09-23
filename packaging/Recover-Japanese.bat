@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "TARGET=%~1"
set "NO_PAUSE="
if /I "%~2"=="--no-pause" set "NO_PAUSE=1"
if /I "%~nx1"=="ro3.exe" set "TARGET=%~dp1"
if not defined TARGET if exist "%~dp0ro3.exe" set "TARGET=%~dp0"
if not defined TARGET (
    echo Close RO3 and RO3AsiaLauncher. Enter the Client folder containing ro3.exe:
    set /p "TARGET=> "
)
set "TARGET=%TARGET:"=%"
if "%TARGET:~-1%"=="\" set "TARGET=%TARGET:~0,-1%"
echo "%TARGET%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restore-Recovery.ps1" -GameClient "%TARGET%"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" echo [ERROR] Recovery stopped. Read the error above.
if "%RC%"=="0" echo [OK] Recovery complete. Use RO3AsiaLauncher to play.
if not defined NO_PAUSE pause
exit /b %RC%
