from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import socket
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import main


class ModelHashTests(unittest.TestCase):
    def setUp(self) -> None:
        main._hash_cache.clear()

    def test_hash_streams_without_reading_the_entire_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.bin"
            payload = b"model-block" * 200_000
            path.write_bytes(payload)
            with patch.object(Path, "read_bytes", side_effect=AssertionError("whole-file read")):
                self.assertEqual(hashlib.sha256(payload).hexdigest(), main.sha256_file(path))

    def test_hash_of_empty_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.bin"
            path.touch()
            self.assertEqual(hashlib.sha256(b"").hexdigest(), main.sha256_file(path))

    def test_unchanged_model_uses_cached_digest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.bin"
            path.write_bytes(b"model")
            expected = main.sha256_file(path)
            with patch.object(Path, "open", side_effect=AssertionError("cache miss")):
                self.assertEqual(expected, main.sha256_file(path))

    def test_replacement_with_same_size_and_mtime_invalidates_cache(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.bin"
            path.write_bytes(b"before")
            previous = path.stat()
            main.sha256_file(path)
            replacement = Path(directory) / "replacement.bin"
            replacement.write_bytes(b"after!")
            os.utime(replacement, ns=(previous.st_atime_ns, previous.st_mtime_ns))
            os.replace(replacement, path)
            self.assertEqual(hashlib.sha256(b"after!").hexdigest(), main.sha256_file(path))

    def test_model_changed_during_hash_is_not_cached(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.bin"
            path.write_bytes(b"before")
            digest = hashlib.sha256()

            class MutatingDigest:
                def update(self, data) -> None:
                    digest.update(data)
                    with path.open("ab") as stream:
                        stream.write(b"changed")
                    # Restore update so this is a single mutation, not an endless append.
                    self.update = digest.update

                def hexdigest(self) -> str:
                    return digest.hexdigest()

            with patch.object(main.hashlib, "sha256", return_value=MutatingDigest()):
                with self.assertRaises(OSError):
                    main.sha256_file(path)
            self.assertNotIn(path, main._hash_cache)


class SentenceRegressionTests(unittest.TestCase):
    def test_preserves_decimals_versions_and_domains(self) -> None:
        text = "Use v0.3.0 at example.com. Value 3.14 stays intact."
        self.assertEqual(
            ["Use v0.3.0 at example.com.", "Value 3.14 stays intact."],
            main.SimpleSentencizer().split_sentences(text),
        )

    def test_enforces_exact_chunk_limit_without_losing_characters(self) -> None:
        for size in (220, 221, 440, 441, 1000):
            with self.subTest(size=size):
                text = "长" * size
                chunks = main.SimpleSentencizer().split_sentences(text)
                self.assertTrue(all(len(chunk) <= 220 for chunk in chunks))
                self.assertEqual(text, "".join(chunks))

    def test_comma_at_chunk_boundary_stays_within_limit(self) -> None:
        text = "长" * 220 + "，" + "文" * 240
        chunks = main.SimpleSentencizer().split_sentences(text)
        self.assertTrue(all(len(chunk) <= 220 for chunk in chunks))
        self.assertEqual(text, "".join(chunks))

    def test_cjk_and_english_punctuation(self) -> None:
        self.assertEqual(
            ["你好。", "世界！", "Hello!", "Next?"],
            main.SimpleSentencizer().split_sentences("你好。世界！ Hello! Next?"),
        )


class ProtocolRegressionTests(unittest.TestCase):
    def run_protocol(self, request: str, method=None) -> tuple[list[dict], str]:
        output, errors = io.StringIO(), io.StringIO()
        source = request + '\n{"id":99,"method":"shutdown","params":{}}\n'
        overrides = {"health": method} if method is not None else {}
        with patch.object(main.sys, "stdin", io.StringIO(source)), patch.dict(main.METHODS, overrides):
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
                self.assertEqual(0, main.main())
        return [json.loads(line) for line in output.getvalue().splitlines()], errors.getvalue()

    def test_dependency_exception_never_echoes_private_content(self) -> None:
        sentinel = "PRIVATE_OCR_TEXT_AND_API_KEY_123"

        def fail(_):
            raise RuntimeError(sentinel)

        responses, errors = self.run_protocol('{"id":1,"method":"health"}', fail)
        self.assertNotIn(sentinel, json.dumps(responses) + errors)
        self.assertEqual("", errors)
        self.assertIsNotNone(responses[0]["error"])
        self.assertEqual(99, responses[1]["id"])

    def test_dependency_output_does_not_pollute_protocol(self) -> None:
        sentinel = "PRIVATE_DEPENDENCY_DIAGNOSTIC"

        def noisy(_):
            print(sentinel)
            print(sentinel, file=main.sys.stderr)
            return {"ok": True}

        responses, errors = self.run_protocol('{"id":1,"method":"health"}', noisy)
        self.assertEqual({"ok": True}, responses[0]["result"])
        self.assertEqual("", errors)
        self.assertNotIn(sentinel, json.dumps(responses))

    def test_unknown_method_is_not_echoed(self) -> None:
        responses, errors = self.run_protocol('{"id":1,"method":"PRIVATE_METHOD"}')
        self.assertNotIn("PRIVATE_METHOD", json.dumps(responses) + errors)
        self.assertEqual("unknown_method", responses[0]["error"]["code"])

    def test_invalid_requests_recover_for_the_next_request(self) -> None:
        for request in (
            'not-json', '[]', 'null',
            '{"id":true,"method":"health"}',
            '{"id":2147483648,"method":"health"}',
            '{"id":1,"method":[]}',
            '{"id":1,"method":"health","params":[]}',
            '{"id":1,"method":"health","params":false}',
        ):
            with self.subTest(request=request):
                responses, errors = self.run_protocol(request)
                self.assertEqual("invalid_request", responses[0]["error"]["code"])
                self.assertEqual(99, responses[1]["id"])
                self.assertEqual("", errors)

    def test_known_safe_error_remains_actionable(self) -> None:
        def fail(_):
            raise main.EngineError("请重新安装离线模型。", "model_missing")

        responses, _ = self.run_protocol('{"id":1,"method":"health"}', fail)
        self.assertEqual(
            {"code": "model_missing", "message": "请重新安装离线模型。"},
            responses[0]["error"],
        )

    def test_health_is_guarded_before_dependency_work(self) -> None:
        def connect(_):
            socket.create_connection(("example.invalid", 443))
            return {"ok": True}

        with patch.object(socket, "create_connection", return_value=None) as connection:
            responses, _ = self.run_protocol('{"id":1,"method":"health"}', connect)
            connection.assert_not_called()
        self.assertIn("本地模式", responses[0]["error"]["message"])

    def test_explicit_install_is_not_wrapped_in_offline_guard(self) -> None:
        def install(_):
            socket.create_connection(("example.invalid", 443))
            return {"installed": []}

        with patch.object(socket, "create_connection", return_value=None) as connection:
            with patch.dict(main.METHODS, {"install_translation_models": install}):
                responses, _ = self.run_protocol('{"id":1,"method":"install_translation_models"}')
            connection.assert_called_once()
        self.assertIsNone(responses[0]["error"])


if __name__ == "__main__":
    unittest.main()
