from __future__ import annotations

import importlib.util
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock


SCRIPT = Path(__file__).with_name("audit-release-dependencies.py")
SPEC = importlib.util.spec_from_file_location("pingyi_release_audit_licenses", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)

COLLECTOR_SCRIPT = Path(__file__).with_name("collect-release-licenses.py")
COLLECTOR_SPEC = importlib.util.spec_from_file_location(
    "pingyi_collect_release_licenses", COLLECTOR_SCRIPT
)
assert COLLECTOR_SPEC is not None and COLLECTOR_SPEC.loader is not None
collector = importlib.util.module_from_spec(COLLECTOR_SPEC)
COLLECTOR_SPEC.loader.exec_module(collector)


class ReleaseLicenseAuditTests(unittest.TestCase):
    def test_pinned_onednn_license_files_remain_unchanged(self) -> None:
        expected = {
            "oneDNN-3.1.1-LICENSE.txt": "f961df79eec1c83e6c27483979216c6277bd2a5deed99646e915a92fde4b3d8e",
            "oneDNN-3.1.1-THIRD-PARTY-PROGRAMS.txt": "799d964b8be96c96405f56b419a9340362d486196a7be1d1110ca5bd9e37a8ef",
        }
        for name, digest in expected.items():
            content = (collector.CURATED_ROOT / name).read_bytes()
            self.assertEqual(digest, hashlib.sha256(content).hexdigest(), name)

    def test_collector_distinguishes_gnu_llvm_and_intel_openmp(self) -> None:
        class FakeIntelDistribution:
            version = "2025.3.0"
            files = (Path("LICENSE.txt"), Path("third-party-programs.txt"))

            def __init__(self, license_path: Path) -> None:
                self._license_path = license_path

            def locate_file(self, entry: Path) -> Path:
                return self._license_path.parent / entry.name

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            publish = root / "publish"
            output = publish / "licenses"
            publish.mkdir()
            output.mkdir()
            (publish / "libgomp-test.so.1").write_bytes(b"gnu")
            (publish / "libomp-test.so").write_bytes(b"llvm")
            (publish / "libiomp5md.dll").write_bytes(b"intel")
            (publish / "ctranslate2.dll").write_bytes(b"ctranslate2")
            intel_license = root / "LICENSE.txt"
            intel_license.write_text("Intel license terms\n" * 20, encoding="utf-8")
            (root / "third-party-programs.txt").write_text(
                "Intel OpenMP third-party notices\n" * 20,
                encoding="utf-8",
            )

            with mock.patch.object(collector.metadata, "distribution") as distribution:
                distribution.side_effect = lambda name: FakeIntelDistribution(intel_license)
                components = collector.collect_native_runtimes(publish, output)

            self.assertTrue(
                {
                    "GNU OpenMP runtime",
                    "LLVM OpenMP runtime",
                    "Intel OpenMP runtime",
                    "Intel oneMKL runtime",
                    "oneDNN",
                }.issubset({component["name"] for component in components})
            )
            self.assertTrue((output / "native" / "GNU-OpenMP-runtime" / "GPL-3.0.txt").is_file())
            self.assertTrue((output / "native" / "LLVM-OpenMP-runtime" / "LLVM-exception.txt").is_file())
            self.assertTrue((output / "native" / "Intel-OpenMP-runtime" / "LICENSE.txt").is_file())
            self.assertTrue(
                (output / "native" / "Intel-OpenMP-runtime" / "third-party-programs.txt").is_file()
            )

    def test_rejects_missing_license_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            self.assertEqual(
                ["licenses/manifest.json is missing"],
                audit.validate_licenses(Path(temporary)),
            )

    def test_accepts_complete_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            licenses = root / "licenses"
            licenses.mkdir()
            (licenses / "README.txt").write_text("release license summary\n" * 8, encoding="utf-8")
            components = []
            required = sorted(
                audit.REQUIRED_LICENSE_COMPONENTS | {"llama.cpp", "LLVM OpenMP runtime"}
            )
            for index, name in enumerate(required):
                relative = f"component-{index}.txt"
                (licenses / relative).write_text("complete license text\n" * 8, encoding="utf-8")
                components.append({"name": name, "licenseFiles": [relative]})
            (licenses / "manifest.json").write_text(
                json.dumps({"schemaVersion": 1, "components": components}),
                encoding="utf-8",
            )
            (root / "pingyi-complete.edition").write_text("PingYi Complete\n", encoding="utf-8")
            (root / "libomp140.x86_64.dll").write_bytes(b"test runtime")

            self.assertEqual([], audit.validate_licenses(root))

    def test_rejects_bundled_gnu_openmp_without_license_entry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            licenses = root / "licenses"
            licenses.mkdir()
            (licenses / "README.txt").write_text("release license summary\n" * 8, encoding="utf-8")
            components = []
            for index, name in enumerate(sorted(audit.REQUIRED_LICENSE_COMPONENTS)):
                relative = f"component-{index}.txt"
                (licenses / relative).write_text("complete license text\n" * 8, encoding="utf-8")
                components.append({"name": name, "licenseFiles": [relative]})
            (licenses / "manifest.json").write_text(
                json.dumps({"schemaVersion": 1, "components": components}),
                encoding="utf-8",
            )
            (root / "libgomp-test.so.1").write_bytes(b"test runtime")

            self.assertIn(
                "GNU OpenMP runtime is missing GPL-3.0 and GCC exception entries",
                audit.validate_licenses(root),
            )

    def test_rejects_bundled_intel_openmp_without_license_entry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            licenses = root / "licenses"
            licenses.mkdir()
            (licenses / "README.txt").write_text("release license summary\n" * 8, encoding="utf-8")
            components = []
            for index, name in enumerate(sorted(audit.REQUIRED_LICENSE_COMPONENTS)):
                relative = f"component-{index}.txt"
                (licenses / relative).write_text("complete license text\n" * 8, encoding="utf-8")
                components.append({"name": name, "licenseFiles": [relative]})
            (licenses / "manifest.json").write_text(
                json.dumps({"schemaVersion": 1, "components": components}),
                encoding="utf-8",
            )
            (root / "libiomp5md.dll").write_bytes(b"test runtime")

            self.assertIn(
                "Intel OpenMP runtime is missing the Intel EULA and third-party notices",
                audit.validate_licenses(root),
            )


if __name__ == "__main__":
    unittest.main()
