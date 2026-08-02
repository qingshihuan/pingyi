#!/usr/bin/env python3
"""Fail a release build if proprietary GPU runtimes were bundled accidentally."""

from __future__ import annotations

import argparse
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
        if has_gpu_runtime_name or has_unexpected_vendor_package or has_unexpected_torch_runtime:
            forbidden.append(path)

    return forbidden


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path)
    args = parser.parse_args()
    target = args.target.resolve()
    if not target.is_dir():
        parser.error(f"release dependency audit target is not a directory: {target}")

    forbidden = find_forbidden_files(target)
    if forbidden:
        print("Release dependency audit failed. Forbidden GPU/runtime files:")
        for path in forbidden:
            print(f"- {path.relative_to(target)}")
        return 1

    file_count = sum(1 for path in target.rglob("*") if path.is_file())
    print(f"Release dependency audit passed: {file_count} files; no NVIDIA/CUDA/cuDNN or Torch runtime bundled.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
