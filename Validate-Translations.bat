@echo off
setlocal
chcp 65001 > nul
pushd "%~dp0"
py -3 "_TranslationWorkspace\import_translations.py" --check
set "RC=%ERRORLEVEL%"
popd
exit /b %RC%
