@echo off
setlocal
chcp 65001 > nul
pushd "%~dp0"

py "_TranslationWorkspace\import_translations.py"
if errorlevel 1 (
    set "RC=%ERRORLEVEL%"
    echo.
    echo [ERROR] Build failed. Deployment was not attempted.
    popd
    exit /b %RC%
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Deploy-To-Game.ps1"
set "RC=%ERRORLEVEL%"
popd
exit /b %RC%
