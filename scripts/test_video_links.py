"""Offline regression checks for the documentation video download entry points."""
import ast
import hashlib
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
VIDEO_DIR = ROOT / 'docs/videos'
RAW = 'https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/'
MOVIES = {'screen-insight-intro-zh-CN.mp4', 'screen-insight-setup-zh-CN.mp4'}


class VideoDownloadLinksTests(unittest.TestCase):
    def test_readme_links_bypass_github_file_preview(self):
        for name in ('README.md', 'README.en.md', 'docs/videos/README.md'):
            with self.subTest(file=name):
                text = (ROOT / name).read_text(encoding='utf-8')
                links = re.findall(r'\]\(([^\s()]+\.mp4)\)', text)
                self.assertGreaterEqual(len(links), 2)
                self.assertEqual(set(links), {RAW + movie for movie in MOVIES})

    def test_renderer_templates_match_committed_video_sections(self):
        # Inspect constants without importing the renderer or generating any media.
        source = (ROOT / 'media/remotion/tools/assets.py').read_text(encoding='utf-8')
        assignments = [node for node in ast.walk(ast.parse(source))
                       if isinstance(node, ast.Assign)
                       and any(isinstance(target, ast.Name) and target.id == 'blocks'
                               for target in node.targets)]
        self.assertEqual(len(assignments), 1)
        templates = ast.literal_eval(assignments[0].value)
        self.assertEqual(set(templates), {'README.md', 'README.en.md'})
        start = '<!-- screen-insight-videos:start -->'
        end = '<!-- screen-insight-videos:end -->'
        for name, block in templates.items():
            with self.subTest(file=name):
                text = (ROOT / name).read_text(encoding='utf-8')
                self.assertEqual(text.count(start), 1)
                self.assertEqual(text.count(end), 1)
                section = text.split(start, 1)[1].split(end, 1)[0]
                self.assertEqual(section, '\n' + block)

    def test_download_targets_match_render_manifest(self):
        manifest = json.loads((VIDEO_DIR / 'render-manifest.json').read_text(encoding='utf-8'))
        self.assertEqual({entry['file'] for entry in manifest['videos']}, MOVIES)
        for entry in manifest['videos']:
            with self.subTest(file=entry['file']):
                data = (VIDEO_DIR / entry['file']).read_bytes()
                self.assertEqual(len(data), entry['bytes'])
                self.assertEqual(hashlib.sha256(data).hexdigest(), entry['sha256'])


if __name__ == '__main__':
    unittest.main()
