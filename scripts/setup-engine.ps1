param(
    [string]$Python = "py"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$engineVenv = Join-Path $projectRoot ".venv-engine"

if ($Python -eq "py") {
    & $Python -3 -m venv --clear $engineVenv
} else {
    & $Python -m venv --clear $engineVenv
}
if ($LASTEXITCODE -ne 0) { throw "Failed to create the Python virtual environment." }

$enginePython = Join-Path $engineVenv "Scripts\python.exe"
& $enginePython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "Failed to upgrade pip." }
& $enginePython -m pip install --no-deps -r (Join-Path $projectRoot "engine_host\requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Failed to install local engine dependencies." }
& $enginePython (Join-Path $PSScriptRoot "prune-engine-gpu-runtimes.py")
if ($LASTEXITCODE -ne 0) { throw "Failed to prune optional GPU runtimes." }
& $enginePython (Join-Path $PSScriptRoot "audit-release-dependencies.py") $engineVenv
if ($LASTEXITCODE -ne 0) { throw "The engine environment dependency audit failed." }

Write-Host "Local engine dependencies installed: $engineVenv"
Write-Host "Optional environment override: PINGYI_ENGINE_PYTHON=$enginePython"
