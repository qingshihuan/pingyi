"""Exercise the Windows signature release gate using existing trusted binaries; never import a test root."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SHELL = shutil.which("pwsh") or shutil.which("powershell")


@unittest.skipUnless(os.name == "nt" and SHELL, "Windows signature verification")
class WindowsSigningTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.signed_file = Path(shutil.which("dotnet"))
        # Read public certificate metadata only; this binary is never executed by these tests.
        env = os.environ.copy()
        env["PINGYI_TEST_SIGNED_BINARY"] = str(cls.signed_file)
        result = subprocess.run([SHELL, "-NoProfile", "-NonInteractive", "-Command",
            "$s = Get-AuthenticodeSignature -LiteralPath $env:PINGYI_TEST_SIGNED_BINARY; "
            "$s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false)"],
            env=env, capture_output=True, text=True, encoding="utf-8", errors="replace", check=True)
        cls.publisher = result.stdout.strip()
        if not cls.publisher:
            raise RuntimeError("The installed .NET host must have a trusted signing identity")

    def verify(self, path, publisher=None):
        return subprocess.run([SHELL, "-NoProfile", "-NonInteractive", "-File",
            str(ROOT / "scripts/verify-windows-signatures.ps1"), "-LiteralPath", str(path),
            "-ExpectedPublisher", self.publisher if publisher is None else publisher], capture_output=True, text=True,
            encoding="utf-8", errors="replace")

    def test_accepts_existing_trusted_timestamped_binary(self):
        result = self.verify(self.signed_file)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_rejects_an_unexpected_publisher(self):
        result = self.verify(self.signed_file, "Synthetic wrong publisher")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("publisher does not match", result.stderr)

    def test_rejects_unsigned_and_modified_files(self):
        with tempfile.TemporaryDirectory(prefix="pingyi-signing-") as directory:
            unsigned = Path(directory) / "unsigned.ps1"
            unsigned.write_text("# synthetic unsigned fixture\n", encoding="utf-8")
            self.assertNotEqual(self.verify(unsigned).returncode, 0)
            modified = Path(directory) / "modified.exe"
            contents = bytearray(self.signed_file.read_bytes())
            contents[4096] ^= 1
            modified.write_bytes(contents)
            self.assertNotEqual(self.verify(modified).returncode, 0)

    def test_required_signing_stops_before_creating_outputs(self):
        result = subprocess.run([SHELL, "-NoProfile", "-NonInteractive", "-File",
            str(ROOT / "scripts/publish.ps1"), "-RequireSigning", "-Version", "signing-gate-test"],
            capture_output=True, text=True, encoding="utf-8", errors="replace")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Trusted signing is not configured", result.stderr)
        self.assertFalse((ROOT / "artifacts/publish/win-x64-signing-gate-test-complete").exists())


if __name__ == "__main__":
    unittest.main()
