#!/usr/bin/env python3
"""Compare isolated engine-health startup with real dependencies but no models.

No screenshots, input text, keys, network probes, or real user configuration.
This is NOT a benchmark of the desktop UI or actual OCR/translation inference.
"""
import argparse
import json
import os
from pathlib import Path
import statistics
import subprocess
import sys
import tempfile
import time

CHILD = r'''
import contextlib, json, os, resource, runpy, sys, time
class Sink:
    def write(self, value): return len(value)
    def flush(self): pass
start = time.perf_counter()
start_cpu = time.process_time()
sys.path.insert(0, sys.argv[1])
with contextlib.redirect_stdout(Sink()), contextlib.redirect_stderr(Sink()):
    engine = runpy.run_path(os.path.join(sys.argv[1], 'main.py'))
    with engine['local_only_network_guard']():
        result = engine['health']({})
metrics = {
    'engine_health_ms': (time.perf_counter() - start) * 1000,
    'cpu_ms': (time.process_time() - start_cpu) * 1000,
    'peak_rss_mib': resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024,
    'native_translation_imported': 'ctranslate2' in sys.modules,
    'argos_translate_imported': 'argostranslate.translate' in sys.modules,
    'argos_available': result['argos'],
    'models_ready': result['translationModelsReady'],
}
print(json.dumps(metrics))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True, help='Baseline engine_host directory')
    parser.add_argument('--current', type=Path, default=Path(__file__).resolve().parents[1] / 'engine_host')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=5)
    args = parser.parse_args()
    if not 1 <= args.samples <= 20:
        parser.error('--samples must be between 1 and 20')
    if not sys.platform.startswith('linux'):
        parser.error('Linux is required for ru_maxrss KiB measurements')
    rows = {'baseline': [], 'current': []}
    for _ in range(args.samples):
        for name, directory in (('baseline', args.baseline), ('current', args.current)):
            with tempfile.TemporaryDirectory(prefix='pingyi-perf-') as root:
                env = dict(os.environ)
                for key in ('ARGOS_PACKAGES_DIR', 'PINGYI_ENGINE_HOST'):
                    env.pop(key, None)
                env.update(PINGYI_MODEL_DIR=root + '/user-models',
                           PINGYI_BUNDLED_MODEL_DIR=root + '/bundled-models',
                           XDG_DATA_HOME=root + '/data', XDG_CONFIG_HOME=root + '/config',
                           PYTHONDONTWRITEBYTECODE='1', HOME=root)
                start = time.perf_counter()
                completed = subprocess.run([sys.executable, '-c', CHILD, str(directory.resolve())],
                                           env=env, capture_output=True, text=True, timeout=60, check=True)
                row = json.loads(completed.stdout)
                row['process_roundtrip_ms'] = (time.perf_counter() - start) * 1000
                if not row['argos_available'] or row['models_ready']:
                    raise RuntimeError('Benchmark requires installed pinned Argos dependencies and empty model directories')
                rows[name].append(row)
    summary = {name: {key: statistics.median(row[key] for row in data)
                      for key in ('engine_health_ms', 'cpu_ms', 'peak_rss_mib', 'process_roundtrip_ms')}
               for name, data in rows.items()}
    result = {'scope': 'Fresh Python process, empty model directories, real pinned dependencies; NOT desktop startup/inference.',
              'python': sys.version.split()[0], 'platform': sys.platform,
              'filesystem_cache': 'not flushed; results are not cold-disk measurements',
              'samples_per_revision': args.samples, 'median': summary, 'samples': rows}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))
    if any(row['argos_translate_imported'] or row['native_translation_imported'] for row in rows['current']):
        raise RuntimeError('Health must not import the translation runtime')


if __name__ == '__main__':
    main()
