# Startup and resource improvements

This change builds on the workspace redesign. It optimizes existing functions; it does not add history, telemetry, cloud uploads or a second UI framework.

## Changes

- **Modern workspace startup:** constructing `AppServices` no longer preloads the managed multimodal model. Opening/focusing the modern workspace verifies baseline installation without starting the large model. Capture starts/checks it on demand; explicit model start/apply actions retain their behavior. The classic/settings window's explicit model-management flow is unchanged.
- **Configuration I/O:** startup loads/normalizes settings without rewriting them on every launch. A user save still uses the existing atomic settings writer.
- **Passive refresh:** unchanged settings within 30 seconds reuse the displayed dashboard status. Changing settings invalidates this; F5 and the refresh button bypass the throttle. Actual OCR/translation availability checks and integrity gates are not skipped.
- **Translation health:** inspect Argos metadata and required files without importing `argostranslate.translate`, CTranslate2, SentencePiece or NumPy. Verify the existing SHA-256 manifest. The status means installation readiness, not proof of native runtime compatibility; real translation still imports the runtime, checks integrity and resolves the translator.
- **OCR responsiveness:** model-file status work and native OCR run off the UI dispatcher. The inference gate remains held until native work really finishes; shutdown never disposes a running native session.
- **OCR CPU use:** sequential execution, 1–4 intra-op threads (half of logical processors, clamped), inter-op count 1, both thread-pool spinning switches disabled. This favors desktop responsiveness/power over unrestricted batch throughput; some machines may take longer per inference. No recognition model, threshold, image scale or dictionary change.
- **OCR allocations:** borrow row-major dense output buffers during output-owner lifetime instead of copying detection maps and recognition logits using `ToArray`. Other layouts retain the prior copy path. A 4,194,304-float output avoids a 16 MiB duplicate payload, not 16 MiB of total application memory. Initialization now disposes SessionOptions and cleans partial native sessions on failure.
- **Python idle release:** a one-shot timer releases the engine after five idle minutes. The next request starts it automatically. The request gate prevents termination during translation/download; no recurring polling, forced GC or working-set trimming. First translation after idle costs a fresh engine/model load. Constructor callers may use `Timeout.InfiniteTimeSpan` to disable the idle timeout; no new end-user settings switch is added.
- **Model changes:** after a successful install/delete RPC, recycle the Python process so the next request sees the new package directory or bundled fallback instead of Argos' imported directory snapshot. Unsupported delete scopes remain no-ops on files.
- **Managed model reuse:** an already-validated, owned live model with matching backend and a successful endpoint/model probe is reused before re-reading its weight files. Cold starts/restarts and model/backend changes still perform full file validation. A running process continues to use its loaded weights; this fast path is not a revalidation of externally edited disk files.
- **Cleanup/cancellation:** bound stderr draining to 1,024 characters instead of accumulating an arbitrarily long line; concurrent disposal callers share completion; propagate availability cancellation rather than turning it into a misleading unavailable status.

## Verification

```sh
dotnet test PingYi.slnx -c Release
python -m unittest discover -s engine_host -p 'test_*.py'
python -m unittest discover -s scripts -p 'test_*.py'
```

`RuntimePerformanceTests` covers refresh invalidation, managed-runtime selection, bounded ONNX threads, output-memory reuse, idle engine release/restart, gate ownership, safe no-op model mutation recycling, and concurrent disposal. Python regression tests cover metadata-only readiness, malformed/missing metadata, missing tokenizer, and retained integrity validation. Existing screenshot, UI and provider tests remain in place.

## Measurement scope

`Performance validation` installs the exact CPU dependencies in `engine_host/requirements.txt` using `--no-deps`, then compares five new Python processes per revision against pre-optimization commit `48b2138c7cf64ca530299f04aea099358823060d`. It uses isolated empty model directories and never processes user content. The report records engine-health wall time, CPU time, peak RSS, parent-observed process round-trip and native import flags. Filesystem caches are not flushed. No performance percentage threshold is imposed on shared CI runners.

Results measure **the engine health/startup path with real dependencies but without real models**, not desktop launch, first capture, OCR accuracy, GPU usage or full installed-application memory. Do not present its speedup as an overall application speedup. Raw metrics and tracked-source snapshot are retained in the workflow artifact for three days.

## Required desktop acceptance before release

Use the same installed models, language, backend, display scale and power mode for before/after measurements. Record process-start-to-visible-window, first and second capture latency, idle CPU/private bytes/working set, and child-process/GPU memory. Verify local-only capture, canceled captures, model-install/delete fallback, idle restart, changing away from the managed model, and Windows/Ubuntu X11 multi-monitor behavior. Measure cold disk and warm disk separately. Native model accuracy and speed require a real-model baseline; CI's no-model tests do not establish them.

No model binaries, dependency version upgrades or release-version changes are included.
