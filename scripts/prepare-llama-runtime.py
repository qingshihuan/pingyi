#!/usr/bin/env python3
"""Download and verify the pinned llama.cpp runtimes used by PingYi Complete."""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
import urllib.request
import zipfile


LLAMA_VERSION = "b10227"
ASSETS = {
    "win-x64": (
        {
            "backend": "vulkan",
            "name": "llama-b10227-bin-win-vulkan-x64.zip",
            "sha256": "f6aeab1674c445d54b59d8ae5f7be581ebdae2aac74e523234d057386ae54184",
        },
        {
            "backend": "cpu",
            "name": "llama-b10227-bin-win-cpu-x64.zip",
            "sha256": "fa78c20c800d32df50067afda92cc3380c0e62e783ce5c6bb9bbf864aa87c31a",
        },
    ),
    "linux-x64": (
        {
            "backend": "vulkan",
            "name": "llama-b10227-bin-ubuntu-vulkan-x64.tar.gz",
            "sha256": "ec481577abfa57aa7b7cec7b3e4b2ec1a61dd9146f5a1c229d4ea78a038fbdbc",
        },
        {
            "backend": "cpu",
            "name": "llama-b10227-bin-ubuntu-x64.tar.gz",
            "sha256": "80c4c32a459d12a18db5043ac485ba91abe32a0b1c40231d942d1b36169412c4",
        },
    ),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def download(asset: dict[str, str], destination: Path) -> None:
    url = (
        f"https://github.com/ggml-org/llama.cpp/releases/download/"
        f"{LLAMA_VERSION}/{asset['name']}"
    )
    request = urllib.request.Request(url, headers={"User-Agent": "PingYi release builder"})
    digest = hashlib.sha256()
    with urllib.request.urlopen(request, timeout=120) as response, destination.open("wb") as output:
        while chunk := response.read(1024 * 1024):
            output.write(chunk)
            digest.update(chunk)
    actual = digest.hexdigest()
    if actual != asset["sha256"]:
        destination.unlink(missing_ok=True)
        raise RuntimeError(f"SHA-256 mismatch for {asset['name']}: {actual}")


def safe_member_path(root: Path, member_name: str) -> Path:
    target = (root / member_name).resolve()
    if root.resolve() not in (target, *target.parents):
        raise RuntimeError(f"Archive member escapes destination: {member_name}")
    return target


def extract(archive: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    if archive.suffix == ".zip":
        with zipfile.ZipFile(archive) as bundle:
            for member in bundle.infolist():
                safe_member_path(destination, member.filename)
            bundle.extractall(destination)
    else:
        with tarfile.open(archive, "r:gz") as bundle:
            for member in bundle.getmembers():
                safe_member_path(destination, member.name)
            bundle.extractall(destination, filter="data")


def extract_runtime_payload(
    archive: Path,
    destination: Path,
    executable_name: str,
    runtime: str,
    backend: str,
) -> Path:
    extraction_root = destination.parent / f".{destination.name}-extract"
    if extraction_root.exists():
        shutil.rmtree(extraction_root)
    try:
        extract(archive, extraction_root)
        executables = list(extraction_root.rglob(executable_name))
        if len(executables) != 1:
            raise RuntimeError(
                f"Expected one {executable_name} in {archive.name}, found {len(executables)}"
            )

        payload_root = executables[0].parent
        if runtime == "win-x64":
            required_patterns = [
                "llama-server.exe",
                "llama-server-impl.dll",
                "llama-common.dll",
                "llama.dll",
                "mtmd.dll",
                "ggml.dll",
                "ggml-base.dll",
                "ggml-cpu-*.dll",
                "libomp140.x86_64.dll",
            ]
            optional_patterns: list[str] = []
            if backend == "vulkan":
                required_patterns.append("ggml-vulkan.dll")
        else:
            required_patterns = [
                "llama-server",
                "libllama-server-impl.so*",
                "libllama-common.so*",
                "libllama.so*",
                "libmtmd.so*",
                "libggml.so*",
                "libggml-base.so*",
                "libggml-cpu*.so*",
            ]
            # LLVM OpenMP may be bundled by upstream and is covered by the
            # Complete-edition license manifest. GNU libgomp is intentionally
            # not redistributed; Ubuntu supplies it as the libgomp1 package.
            optional_patterns = ["libomp.so*"]
            if backend == "vulkan":
                required_patterns.append("libggml-vulkan.so*")

        sources = [source for source in payload_root.iterdir() if source.is_file() or source.is_symlink()]
        missing = [
            pattern
            for pattern in required_patterns
            if not any(fnmatch.fnmatch(source.name, pattern) for source in sources)
        ]
        if missing:
            raise RuntimeError(
                f"{archive.name} is missing required llama-server files: {', '.join(missing)}"
            )
        keep_patterns = required_patterns + optional_patterns
        destination.mkdir(parents=True, exist_ok=True)
        for source in sources:
            if not any(fnmatch.fnmatch(source.name, pattern) for pattern in keep_patterns):
                continue
            target = destination / source.name
            shutil.copy2(source, target, follow_symlinks=True)
    finally:
        if extraction_root.exists():
            shutil.rmtree(extraction_root)

    return destination / executable_name


def reset_destination(destination: Path) -> None:
    resolved = destination.resolve()
    if resolved == resolved.parent or len(resolved.parts) < 3:
        raise RuntimeError(f"Unsafe runtime destination: {resolved}")
    if resolved.exists():
        shutil.rmtree(resolved)
    resolved.mkdir(parents=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", required=True, choices=sorted(ASSETS))
    parser.add_argument("--destination", required=True, type=Path)
    args = parser.parse_args()

    destination = args.destination.resolve()
    reset_destination(destination)
    manifest_assets: list[dict[str, object]] = []
    with tempfile.TemporaryDirectory(prefix="pingyi-llama-runtime-") as temporary:
        temporary_path = Path(temporary)
        for asset in ASSETS[args.runtime]:
            archive = temporary_path / asset["name"]
            print(f"Downloading {asset['name']}", flush=True)
            download(asset, archive)
            backend_directory = destination / asset["backend"]
            executable_name = (
                "llama-server.exe" if args.runtime == "win-x64" else "llama-server"
            )
            executable = extract_runtime_payload(
                archive,
                backend_directory,
                executable_name,
                args.runtime,
                asset["backend"],
            )
            if not executable.is_file():
                raise RuntimeError(f"llama-server is missing after extracting {asset['name']}")
            if args.runtime == "linux-x64":
                executable.chmod(executable.stat().st_mode | 0o111)
            manifest_assets.append(
                {
                    "backend": asset["backend"],
                    "archive": asset["name"],
                    "sha256": asset["sha256"],
                    "files": [
                        {
                            "name": path.name,
                            "size": path.stat().st_size,
                        }
                        for path in sorted(backend_directory.iterdir())
                        if path.is_file()
                    ],
                }
            )

    manifest = {
        "component": "llama.cpp",
        "version": LLAMA_VERSION,
        "license": "MIT",
        "source": f"https://github.com/ggml-org/llama.cpp/releases/tag/{LLAMA_VERSION}",
        "runtime": args.runtime,
        "assets": manifest_assets,
    }
    (destination / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Prepared llama.cpp {LLAMA_VERSION} at {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
