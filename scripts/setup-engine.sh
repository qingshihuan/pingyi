#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
engine_venv="$project_root/.venv-engine"

python3 -m venv --clear "$engine_venv"
"$engine_venv/bin/python" -m pip install --upgrade pip
"$engine_venv/bin/python" -m pip install --no-deps -r "$project_root/engine_host/requirements.txt"
"$engine_venv/bin/python" "$project_root/scripts/prune-engine-gpu-runtimes.py"
"$engine_venv/bin/python" "$project_root/scripts/audit-release-dependencies.py" "$engine_venv"

echo "本地引擎依赖安装完成：$engine_venv"
