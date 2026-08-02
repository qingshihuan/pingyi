from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path


AUDIT_SCRIPT = Path(__file__).parents[1] / "scripts" / "audit-release-dependencies.py"
SPEC = importlib.util.spec_from_file_location("pingyi_release_audit", AUDIT_SCRIPT)
assert SPEC is not None and SPEC.loader is not None
release_audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release_audit)


class ReleaseDependencyAuditTests(unittest.TestCase):
    def test_engine_requirements_keep_optional_gpu_stack_out(self) -> None:
        requirement_lines = (Path(__file__).parent / "requirements.txt").read_text(encoding="utf-8").splitlines()
        package_names = {
            line.split("==", maxsplit=1)[0].strip().lower()
            for line in requirement_lines
            if line.strip() and not line.lstrip().startswith("#")
        }

        for forbidden in ("torch", "stanza", "spacy", "cuda", "cudnn", "nvidia"):
            self.assertNotIn(forbidden, package_names)

    def test_accepts_cpu_only_runtime(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            runtime = root / "_internal" / "ctranslate2" / "ctranslate2.dll"
            runtime.parent.mkdir(parents=True)
            runtime.write_bytes(b"cpu runtime placeholder")

            self.assertEqual([], release_audit.find_forbidden_files(root))

    def test_rejects_proprietary_gpu_and_torch_runtimes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            cudnn = root / "_internal" / "ctranslate2" / "cudnn64_9.dll"
            vendor_data = root / "_internal" / "nvidia" / "metadata.json"
            torch = root / "_internal" / "torch" / "torch_cpu.dll"
            cudnn.parent.mkdir(parents=True)
            vendor_data.parent.mkdir(parents=True)
            torch.parent.mkdir(parents=True)
            cudnn.write_bytes(b"gpu runtime placeholder")
            vendor_data.write_text("{}", encoding="utf-8")
            torch.write_bytes(b"unexpected runtime placeholder")

            self.assertEqual(
                {cudnn, vendor_data, torch},
                set(release_audit.find_forbidden_files(root)),
            )


if __name__ == "__main__":
    unittest.main()
