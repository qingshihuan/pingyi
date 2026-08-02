#!/usr/bin/env python3
"""Remove optional vendor GPU binaries from the CPU-only engine environment."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path


GPU_TOKENS = (
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


def main() -> int:
    environment_root = Path(sys.prefix).resolve()
    specification = importlib.util.find_spec("ctranslate2")
    if specification is None or not specification.submodule_search_locations:
        raise RuntimeError("CTranslate2 is not installed in the engine environment.")

    package_root = Path(next(iter(specification.submodule_search_locations))).resolve()
    if environment_root not in package_root.parents:
        raise RuntimeError(f"Refusing to modify a package outside {environment_root}: {package_root}")

    removed: list[Path] = []
    for path in package_root.rglob("*"):
        if path.is_file() and any(token in path.name.lower() for token in GPU_TOKENS):
            path.unlink()
            removed.append(path)

    if removed:
        print("Removed optional GPU runtime binaries from the CPU-only engine environment:")
        for path in removed:
            print(f"- {path.relative_to(environment_root)}")
    else:
        print("No optional vendor GPU runtime binaries were present in CTranslate2.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
