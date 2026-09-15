"""Apply reviewed, SHA-256 guarded source edits on the dedicated feedback branch only."""
import hashlib
import json
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
paths = []
for manifest in sorted((root / '.workbench').glob('edits-*.json')):
    for item in json.loads(manifest.read_text(encoding='utf-8')):
        name = item['path']
        relative = Path(name)
        if relative.is_absolute() or '..' in relative.parts or not (
            name.startswith(('src/', 'tests/', 'scripts/', 'docs/')) or
            name in ('README.md', 'THIRD_PARTY_NOTICES.md')):
            raise SystemExit('Unsupported source path: ' + name)
        target = root / relative
        if name in paths:
            raise SystemExit('Duplicate path: ' + name)
        paths.append(name)
        if 'new' in item:
            if target.exists():
                raise SystemExit('New source already exists: ' + name)
            text = item['new']
        else:
            source = target.read_text(encoding='utf-8')
            if hashlib.sha256(source.encode()).hexdigest() != item['sha256']:
                raise SystemExit('Baseline mismatch: ' + name)
            lines = source.splitlines(keepends=True)
            for start, stop, replacement in reversed(item['edits']):
                lines[start:stop] = [replacement]
            text = ''.join(lines)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding='utf-8', newline='\n')
print('Applied reviewed edits to', len(paths), 'source files.')
if '--stage' in __import__('sys').argv:
    subprocess.run(['git', 'add', '--', *paths], cwd=root, check=True)
