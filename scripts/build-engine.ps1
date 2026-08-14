param(
    [string]$Python = "py"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$engineVenv = Join-Path $projectRoot ".venv-engine"
$enginePython = Join-Path $engineVenv "Scripts\python.exe"

if (-not (Test-Path $enginePython)) {
    & (Join-Path $PSScriptRoot "setup-engine.ps1") -Python $Python
}

& $enginePython -m pip install "pyinstaller==6.21.0"
if ($LASTEXITCODE -ne 0) { throw "Failed to install PyInstaller." }
& $enginePython -m PyInstaller --noconfirm --clean --onedir `
    --name pingyi-engine `
    --runtime-hook (Join-Path $projectRoot "engine_host\runtime_minimal.py") `
    --additional-hooks-dir (Join-Path $projectRoot "packaging\pyinstaller") `
    --collect-data argostranslate `
    --copy-metadata argostranslate `
    --copy-metadata ctranslate2 `
    --exclude-module torch `
    --exclude-module ctranslate2.converters `
    --exclude-module ctranslate2.models `
    --exclude-module ctranslate2.specs `
    --exclude-module numpy `
    --exclude-module psutil `
    --exclude-module huggingface_hub `
    --exclude-module hf_xet `
    --exclude-module aiohttp `
    --exclude-module anyio `
    --exclude-module httpx `
    --exclude-module PIL `
    --exclude-module pydantic `
    --exclude-module sacremoses `
    --exclude-module regex `
    --exclude-module yaml `
    --exclude-module tqdm `
    --exclude-module stanza `
    --exclude-module spacy `
    --exclude-module thinc `
    --exclude-module blis `
    --exclude-module minisbd `
    --exclude-module onnxruntime `
    --exclude-module cv2 `
    --exclude-module paddle `
    --exclude-module paddleocr `
    --exclude-module paddlex `
    --exclude-module pandas `
    --exclude-module pypdfium2 `
    --exclude-module transformers `
    --exclude-module matplotlib `
    --exclude-module tkinter `
    --distpath (Join-Path $projectRoot "artifacts\engine-host\win-x64") `
    --workpath (Join-Path $projectRoot "artifacts\pyinstaller-work") `
    --specpath (Join-Path $projectRoot "artifacts") `
    (Join-Path $projectRoot "engine_host\main.py")
if ($LASTEXITCODE -ne 0) { throw "Failed to build the local engine." }

& $enginePython (Join-Path $PSScriptRoot "audit-release-dependencies.py") `
    (Join-Path $projectRoot "artifacts\engine-host\win-x64\pingyi-engine")
if ($LASTEXITCODE -ne 0) { throw "The local engine dependency audit failed." }
