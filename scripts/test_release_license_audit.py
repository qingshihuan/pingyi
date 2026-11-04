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


def sha256_text_file(path: Path) -> str:
    # Git may check text files out with CRLF on Windows. Compare canonical LF
    # bytes so pinned upstream text has the same digest on every platform.
    content = path.read_bytes().replace(b"\r\n", b"\n")
    return hashlib.sha256(content).hexdigest()


class ReleaseLicenseAuditTests(unittest.TestCase):
    def test_text_hash_is_independent_of_checkout_line_endings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            lf_file = root / "lf.txt"
            crlf_file = root / "crlf.txt"
            lf_file.write_bytes(b"first line\nsecond line\n")
            crlf_file.write_bytes(b"first line\r\nsecond line\r\n")

            self.assertEqual(sha256_text_file(lf_file), sha256_text_file(crlf_file))



    def test_retired_engines_are_rejected_from_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for relative in ("engine-host/runtime.exe", "Microsoft.ML.OnnxRuntime.dll", "offline-models/model.bin"):
                p = root / relative; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(b"test")
            self.assertEqual(3, len(audit.find_forbidden_files(root)))

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



if __name__ == "__main__":
    unittest.main()
