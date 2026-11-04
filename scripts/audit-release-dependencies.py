#!/usr/bin/env python3
"""Fail a release build if proprietary GPU runtimes were bundled accidentally."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


PROPRIETARY_GPU_TOKENS = (
    "cublas",
    "cudart",
    "cudnn",
    "cufft",
    "curand",
    "cusolver",
    "cusparse",
    "nvidia",
    "nvjitlink",
    "nvrtc",
    "tensorrt",
)
NATIVE_SUFFIXES = (".dll", ".dylib", ".pyd", ".so")
REQUIRED_LICENSE_COMPONENTS = {".net runtime"}


def is_native_binary(path: Path) -> bool:
    name = path.name.lower()
    return name.endswith(NATIVE_SUFFIXES) or ".so." in name


def find_forbidden_files(root: Path) -> list[Path]:
    forbidden: list[Path] = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue

        relative_parts = tuple(part.lower() for part in path.relative_to(root).parts[:-1])
        filename = path.name.lower()
        has_gpu_runtime_name = is_native_binary(path) and (
            any(token in filename for token in PROPRIETARY_GPU_TOKENS)
            or filename in {"libcuda.so", "libcuda.dylib"}
            or filename.startswith("onnxruntime_providers_cuda")
        )
        has_unexpected_vendor_package = any(
            part == "nvidia" or part.startswith("nvidia_") for part in relative_parts
        )
        has_unexpected_torch_runtime = "torch" in relative_parts
        has_retired_runtime = any(part in {"engine-host", "offline-models", "paddleocr", "argostranslate", "ctranslate2"} for part in relative_parts) or any(token in filename for token in ("onnxruntime", "ctranslate2", "pingyi-engine", "libiomp", "libdnnl"))
        if has_retired_runtime or has_gpu_runtime_name or has_unexpected_vendor_package or has_unexpected_torch_runtime:
            forbidden.append(path)

    return forbidden


def validate_licenses(root: Path) -> list[str]:
    errors: list[str] = []
    licenses = root / "licenses"
    manifest_path = licenses / "manifest.json"
    summary_path = licenses / "README.txt"
    if not manifest_path.is_file():
        return ["licenses/manifest.json is missing"]
    if not summary_path.is_file() or summary_path.stat().st_size < 100:
        errors.append("licenses/README.txt is missing or unexpectedly short")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return [f"licenses/manifest.json is invalid: {error}"]
    if manifest.get("schemaVersion") != 1:
        errors.append("licenses/manifest.json has an unsupported schemaVersion")
    components = manifest.get("components")
    if not isinstance(components, list):
        return errors + ["licenses/manifest.json components must be a list"]

    names: set[str] = set()
    components_by_name: dict[str, dict[str, object]] = {}
    licenses_root = licenses.resolve()
    for component in components:
        if not isinstance(component, dict):
            errors.append("licenses/manifest.json contains a non-object component")
            continue
        name = component.get("name")
        if isinstance(name, str):
            normalized_name = name.casefold()
            names.add(normalized_name)
            components_by_name.setdefault(normalized_name, component)
        files = component.get("licenseFiles")
        if not isinstance(files, list) or not files:
            errors.append(f"license entry has no files: {name or '<unnamed>'}")
            continue
        for relative in files:
            if not isinstance(relative, str) or not relative.strip():
                errors.append(f"license entry has an invalid path: {name or '<unnamed>'}")
                continue
            candidate = (licenses / relative).resolve()
            if licenses_root not in candidate.parents:
                errors.append(f"license path escapes licenses/: {relative}")
            elif not candidate.is_file() or candidate.stat().st_size < 100:
                errors.append(f"license text is missing or unexpectedly short: {relative}")

    missing = sorted(REQUIRED_LICENSE_COMPONENTS - names)
    if missing:
        errors.append("required license entries are missing: " + ", ".join(missing))
    all_files = [path for path in root.rglob("*") if path.is_file()]
    has_gnu_openmp = any(path.name.lower().startswith("libgomp") for path in all_files)
    has_llvm_openmp = any(path.name.lower().startswith("libomp") for path in all_files)
    if has_gnu_openmp and "gnu openmp runtime" not in names:
        errors.append("GNU OpenMP runtime is missing GPL-3.0 and GCC exception entries")
    if has_llvm_openmp and "llvm openmp runtime" not in names:
        errors.append("LLVM OpenMP runtime is missing Apache-2.0 and LLVM exception entries")
    if (root / "pingyi-complete.edition").is_file():
        if "llama.cpp" not in names:
            errors.append("Complete edition is missing the llama.cpp license entry")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--require-licenses",
        action="store_true",
        help="Require and validate the release licenses/ manifest",
    )
    parser.add_argument("target", type=Path)
    args = parser.parse_args()
    target = args.target.resolve()
    if not target.is_dir():
        parser.error(f"release dependency audit target is not a directory: {target}")

    forbidden = find_forbidden_files(target)
    if forbidden:
        print("Release dependency audit failed. Forbidden retired/GPU/runtime files:")
        for path in forbidden:
            print(f"- {path.relative_to(target)}")
        return 1

    if args.require_licenses:
        license_errors = validate_licenses(target)
        if license_errors:
            print("Release dependency audit failed. License bundle problems:")
            for error in license_errors:
                print(f"- {error}")
            return 1

    file_count = sum(1 for path in target.rglob("*") if path.is_file())
    license_status = " complete license bundle present;" if args.require_licenses else ""
    print(
        f"Release dependency audit passed: {file_count} files;{license_status} "
        "no NVIDIA/CUDA/cuDNN or Torch runtime bundled."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
