"""Installation checks must not import inference libraries or bypass integrity checks."""
import builtins
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import main


class LightweightHealthTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.context = patch.object(main, 'ACTIVE_MODEL_DIR', self.root)
        self.context.start()
        self.addCleanup(self.context.stop)
        self.manifest = {}
        for source, target in (('zh', 'en'), ('en', 'zh')):
            package = self.root / 'argos' / f'translate-{source}_{target}-1_9'
            (package / 'model').mkdir(parents=True)
            (package / 'metadata.json').write_text(json.dumps({'from_code': source, 'to_code': target}))
            for name in ('model/model.bin', 'model/config.json', 'model/shared_vocabulary.json', 'sentencepiece.model'):
                (package / name).write_bytes(b'fixed-test-fixture')
            self.manifest[f'argos-installed-{source}-{target}'] = hashlib.sha256(b'fixed-test-fixture').hexdigest()
        (self.root / 'translation-models.json').write_text(json.dumps({'sha256': self.manifest}))
        main._hash_cache.clear()

    def test_ready_health_does_not_import_argos_or_native_translation(self):
        original = builtins.__import__
        def guarded(name, *args, **kwargs):
            if name.split('.')[0] in {'argostranslate', 'ctranslate2', 'sentencepiece', 'numpy'}:
                raise AssertionError('Status check imported an inference dependency')
            return original(name, *args, **kwargs)
        with patch.object(main, 'module_available', return_value=True), patch('builtins.__import__', side_effect=guarded):
            self.assertTrue(main.health({})['translationModelsReady'])

    def test_missing_native_package_is_not_reported_ready(self):
        with patch.object(main, 'module_available', return_value=False):
            self.assertFalse(main.health({})['translationModelsReady'])

    def test_modified_model_still_fails_health(self):
        (self.root / 'argos/translate-zh_en-1_9/model/model.bin').write_bytes(b'bad')
        with patch.object(main, 'module_available', return_value=True):
            self.assertFalse(main.health({})['translationModelsReady'])

    def test_missing_tokenizer_does_not_count_as_installed_pair(self):
        (self.root / 'argos/translate-zh_en-1_9/sentencepiece.model').unlink()
        self.assertNotIn(('zh', 'en'), main.installed_argos_pairs())

    def test_invalid_or_oversized_metadata_is_ignored(self):
        metadata = self.root / 'argos/translate-zh_en-1_9/metadata.json'
        for contents in ('null', '[]', '{', 'x' * 65537, '{"from_code":[],"to_code":"en"}'):
            with self.subTest(contents=contents[:20]):
                metadata.write_text(contents)
                self.assertNotIn(('zh', 'en'), main.installed_argos_pairs())

    def test_translate_keeps_integrity_gate(self):
        with patch.object(main, 'module_available', return_value=True), patch.object(main, 'verify_translation_manifest', return_value=False):
            with self.assertRaises(main.EngineError):
                main.translate({'text': 'fixed sample', 'sourceLanguage': 'en', 'targetLanguage': 'zh'})


if __name__ == '__main__':
    unittest.main()
