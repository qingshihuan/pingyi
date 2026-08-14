#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
engine_venv="$project_root/.venv-engine"
engine_python="$engine_venv/bin/python"
output_dir="$project_root/artifacts/engine-host/linux-x64"

if [[ ! -x "$engine_python" ]]; then
  "$project_root/scripts/setup-engine.sh"
fi

"$engine_python" -m pip install "pyinstaller==6.21.0"
"$engine_python" -m PyInstaller --noconfirm --clean --onedir \
  --name pingyi-engine \
  --runtime-hook "$project_root/engine_host/runtime_minimal.py" \
  --additional-hooks-dir "$project_root/packaging/pyinstaller" \
  --collect-data argostranslate \
  --copy-metadata argostranslate \
  --copy-metadata ctranslate2 \
  --exclude-module torch \
  --exclude-module ctranslate2.converters \
  --exclude-module ctranslate2.models \
  --exclude-module ctranslate2.specs \
  --exclude-module numpy \
  --exclude-module psutil \
  --exclude-module huggingface_hub \
  --exclude-module hf_xet \
  --exclude-module aiohttp \
  --exclude-module anyio \
  --exclude-module httpx \
  --exclude-module PIL \
  --exclude-module pydantic \
  --exclude-module sacremoses \
  --exclude-module regex \
  --exclude-module yaml \
  --exclude-module tqdm \
  --exclude-module stanza \
  --exclude-module spacy \
  --exclude-module thinc \
  --exclude-module blis \
  --exclude-module minisbd \
  --exclude-module onnxruntime \
  --exclude-module cv2 \
  --exclude-module paddle \
  --exclude-module paddleocr \
  --exclude-module paddlex \
  --exclude-module pandas \
  --exclude-module pypdfium2 \
  --exclude-module transformers \
  --exclude-module matplotlib \
  --exclude-module tkinter \
  --distpath "$output_dir" \
  --workpath "$project_root/artifacts/pyinstaller-work-linux" \
  --specpath "$project_root/artifacts" \
  "$project_root/engine_host/main.py"

"$engine_python" "$project_root/scripts/audit-release-dependencies.py" \
  "$output_dir/pingyi-engine"

echo "Local engine: $output_dir/pingyi-engine"
