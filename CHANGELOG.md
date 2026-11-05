# Changelog

## 0.10.2 — 2026-11-05 — documentation and release maintenance

- Improve release packaging and verify final artifacts before publication.
- Preserve source and package secret scans, dependency/license checks, SHA-256 checksums and verification before publication. No application behavior, local configuration or API Key storage changes.


## 0.10.1 — 2026-11-05 — original version naming

- Rebuild the verified screenshot performance improvements with `0.10.1` in the application version and package filenames, and use the original release title `截屏释义 v0.10.1`.
- Creating a tag for an already published release no longer starts a second build that could replace its verified assets.

## 0.10.1-unsigned.3 — 2026-11-05 — screenshot performance preview

- Windows capture now displays fresh opaque BGRA pixels without whole-desktop or per-monitor PNG round trips; only the selected region is encoded on a worker. Use a synchronized DIB capture, shared stride-aware monitor views, synthetic startup resource warmup and one overlay activation per session. Native HWND hide confirmation plus DWM replaces Windows fixed sleeps/dispatcher drains; Linux synchronization remains unchanged.
- Add operation-scoped metadata timing from actual hotkey/button entry through first rendered batch and input readiness, plus an opt-in native benchmark and lifecycle/DPI/freshness checks. The three-monitor workstation measured render P50/P95 193/233ms versus 1650/1801ms before; this is a software-triggered capture-path measurement, not verified physical hotkey latency. See `docs/CAPTURE_PERFORMANCE.md` for the full evidence and limits.
- Publish after verifying the final draft assets. No settings/key migration is introduced.

## 0.10.0 — 2026-11-05 — unified multimodal model refactor

### Changed

- Desktop capture and image-analysis dialogs now offer “Allow and remember”. Approval survives restart for the same service, endpoint, protocol and model; General settings can restore prompts without losing drafts. Existing one-time approval/cancellation and independent browser-extension consent remain available. Schema 16 gains only optional destination digests; key storage and capture-history behavior are unchanged.
- Home and runtime details now refresh on saved configuration changes and poll passive local state while visible. Runtime starts/stops invalidate cached model evidence, overlapping refresh requests are retained, and unchanged polls preserve operation messages and detail controls. Saved API capability evidence is shown as blue “Verified”, separately from local availability and live connectivity.
- The setup wizard now exposes the same one-click automatic local setup as Local models. Manual model/runtime choices remain expandable; progress, cancellation and failure retry stay in the wizard, which completes only after verified configuration is saved. Opening the wizard does not start downloads.
- Local models now has a one-click detect-and-configure action covering hardware, installed runtime checks, model reuse/download, vision/text verification and selection of the verified current model. Automatic GPU inference verification failures retry on CPU before saving. It is cancellable, retains unrelated setting/key drafts, and restores a previously running model if setup fails.
- Verified the official runtime download resolver against llama.cpp v0.9.0 / b12110 metadata for all Windows/Ubuntu CPU, Vulkan, CUDA 12/13 and ROCm packages. The bundled runtime and offline fallback remain b11146; online downloads resolve the latest stable release.
- Automatic runtime switching now probes installed candidates through CPU when GPU probes fail, and retains Auto during model loading so GPU startup failures can fall back to CPU. Resetting to Automatic also clears a saved GPU pin. Existing saved choices remain unchanged until explicitly applied.
- Automatic detection is now the default runtime backend and the first dropdown option. Saved backend/device choices are retained; missing or invalid backend settings use Auto. The reset action and Chinese/English guidance follow the new default.
- Local API endpoint presets (llama.cpp, Ollama, LM Studio and vLLM) now show a localized “Local” suffix to distinguish them from cloud presets. Endpoint URLs and model IDs are unchanged.
- Translation and screenshot recognition share a single current multimodal model. Image description and prompt reconstruction use the same selection.
- Current models must have verified image-input and text-output capabilities. Text-only models and catalog-only entries cannot be selected.
- Settings now has Current model, API models, Local models, Hotkeys and General sections. API models combines existing local and cloud endpoints in one editor; Local models is the separate built-in deployment/runtime/GPU feature.
- Preserve schema 16, service IDs, model capability evidence and scoped keys when combining the API editor. Existing local API services appear on the API status card rather than the built-in deployment card.
- Vision capability checks use random synthetic images without putting the answer in the text prompt.
- Schema 16 migrates services and preserves local keys, language and hotkeys. The first save creates a non-overwriting original-byte backup; ambiguous legacy task routes require selection. Old downloads and keys are not automatically deleted.
- Retain existing local key storage. Ignore private config and backups; audit staged content, committed trees and unpacked releases without printing secret values.

### Removed

- PaddleOCR / ONNX, Baidu Cloud OCR, Google Cloud Vision and traditional OCR configuration, code, installers, dependencies and tests specific to those retired engines.
- Lightweight mode, per-task model routing, OCR correction chains and translation fallback.
- Argos / CTranslate2 / Python inference host and offline baseline model packaging.
- Automatic text probing with traditional OCR. Automatic capture now tries local QR decoding, then uses the current model for text recognition and translation.

### Documentation

- Updated Chinese/English READMEs, settings/API/migration guides, architecture, contributor/security guidance and third-party notices.
- Earlier videos and release notes remain historical material and do not describe the new settings/model behavior.

Release packaging no longer passes retired offline-model arguments and audits source trees as well as unpacked assets. See the [v0.10.0 release notes](docs/releases/v0.10.0.md) for downloads, compatibility changes and upgrade guidance.

Earlier version details are retained in [release notes](docs/releases/).
