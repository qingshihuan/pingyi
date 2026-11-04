# Changelog

## Unreleased — unified multimodal model refactor

### Changed

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

No version number, release marker or tag is changed. This entry does not declare a release.

Earlier version details are retained in [release notes](docs/releases/).
