@echo off
setlocal EnableExtensions DisableDelayedExpansion
call "%~dp0Recover-Japanese.bat" "%~1" --after-repair "%~2"
exit /b %ERRORLEVEL%
