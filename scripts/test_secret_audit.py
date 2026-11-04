import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("audit", Path(__file__).with_name("audit-secrets.py"))
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

class SecretAuditTests(unittest.TestCase):
    def test_detects_tokens_without_printing_values(self):
        secret = b"sk-" + b"X" * 40
        findings = audit.inspect("example.md", b"example\n" + secret)
        self.assertEqual(1, len(findings))
        self.assertIn(":2:", findings[0])
        self.assertNotIn(secret.decode(), findings[0])

    def test_private_files_are_blocked_even_when_binary(self):
        for name in ("settings.json", "nested/settings.json.pre-v16.bak", "secrets/local.dpapi", ".env.local",
                     "signing.pfx", "signing.p12", "nested/publisher.crt", "publisher.cer", "private.key"):
            self.assertTrue(audit.inspect(name, b"\0data"), name)
        self.assertFalse(audit.inspect("settings.example.json", b'{"apiKey":"YOUR_API_KEY"}'))
        self.assertFalse(audit.inspect(".env.example", b"API_KEY=YOUR_API_KEY"))

if __name__ == "__main__": unittest.main()
