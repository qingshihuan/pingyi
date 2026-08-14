from __future__ import annotations

import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile


SCRIPT = Path(__file__).with_name("prepare-llama-runtime.py")
SPEC = importlib.util.spec_from_file_location("pingyi_prepare_llama_runtime", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
prepare = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare)


WINDOWS_REQUIRED = {
    "llama-server.exe",
    "llama-server-impl.dll",
    "llama-common.dll",
    "llama.dll",
    "mtmd.dll",
    "ggml.dll",
    "ggml-base.dll",
    "ggml-cpu-x64.dll",
    "libomp140.x86_64.dll",
}


def write_zip(path: Path, names: set[str]) -> None:
    with zipfile.ZipFile(path, "w") as bundle:
        for name in sorted(names):
            bundle.writestr(f"llama-bin/{name}", f"placeholder for {name}".encode())


def write_tar(path: Path, names: set[str]) -> None:
    with tarfile.open(path, "w:gz") as bundle:
        for name in sorted(names):
            content = f"placeholder for {name}".encode()
            member = tarfile.TarInfo(f"llama-bin/{name}")
            member.size = len(content)
            bundle.addfile(member, io.BytesIO(content))


class PrepareLlamaRuntimeTests(unittest.TestCase):
    def test_linux_package_declares_llama_runtime_libraries(self) -> None:
        control = (SCRIPT.parent.parent / "packaging" / "linux" / "control").read_text(
            encoding="utf-8"
        )
        for package in ("libgomp1", "libvulkan1", "libstdc++6", "libssl3"):
            self.assertIn(package, control)

    def test_keeps_only_server_runtime_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "runtime.zip"
            write_zip(
                archive,
                WINDOWS_REQUIRED
                | {"llama-cli.exe", "llama-bench.exe", "llama-quantize.exe", "README.md"},
            )
            destination = root / "runtime" / "cpu"

            executable = prepare.extract_runtime_payload(
                archive,
                destination,
                "llama-server.exe",
                "win-x64",
                "cpu",
            )

            self.assertEqual(destination / "llama-server.exe", executable)
            self.assertEqual(WINDOWS_REQUIRED, {path.name for path in destination.iterdir()})

    def test_vulkan_backend_requires_vulkan_library(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "runtime.zip"
            write_zip(archive, WINDOWS_REQUIRED)

            with self.assertRaisesRegex(RuntimeError, "ggml-vulkan.dll"):
                prepare.extract_runtime_payload(
                    archive,
                    root / "runtime" / "vulkan",
                    "llama-server.exe",
                    "win-x64",
                    "vulkan",
                )

    def test_linux_runtime_keeps_shared_libraries_and_server(self) -> None:
        required = {
            "llama-server",
            "libllama.so.0",
            "libmtmd.so.0",
            "libggml.so.0",
            "libggml-base.so.0",
            "libggml-cpu.so",
            "libggml-vulkan.so",
            "libomp.so.5",
        }
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "runtime.tar.gz"
            write_tar(
                archive,
                required
                | {"libgomp.so.1", "llama-cli", "llama-bench", "llama-quantize"},
            )
            destination = root / "runtime" / "vulkan"

            prepare.extract_runtime_payload(
                archive,
                destination,
                "llama-server",
                "linux-x64",
                "vulkan",
            )

            self.assertEqual(required, {path.name for path in destination.iterdir()})


if __name__ == "__main__":
    unittest.main()
