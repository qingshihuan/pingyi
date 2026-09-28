@echo off
setlocal
if not exist "%~dp0..\artifacts\browser-desktop\win-x64\PingYi.App.exe" (
  echo Build first using the instructions in browser-extension\README.md.
  pause
  exit /b 1
)
set "PINGYI_EDITION=standard"
if /I "%~1"=="complete" set "PINGYI_EDITION=complete"
if exist "%~dp0..\artifacts\offline-models\ocr-models.json" set "PINGYI_BUNDLED_MODEL_DIR=%~dp0..\artifacts\offline-models"
if exist "%~dp0..\artifacts\engine-host\win-x64\pingyi-engine\pingyi-engine.exe" set "PINGYI_ENGINE_HOST=%~dp0..\artifacts\engine-host\win-x64\pingyi-engine\pingyi-engine.exe"
if exist "%~dp0..\artifacts\llama-runtime\win-x64" set "PINGYI_LLAMA_RUNTIME_DIR=%~dp0..\artifacts\llama-runtime\win-x64"
start "" "%~dp0..\artifacts\browser-desktop\win-x64\PingYi.App.exe"
