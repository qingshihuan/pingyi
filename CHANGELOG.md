# Changelog

## Unreleased — unified multimodal model refactor

### Changed

- Translation and screenshot recognition share a single current multimodal model. Image description and prompt reconstruction use the same selection.
- Current models must have verified image-input and text-output capabilities. Text-only models and catalog-only entries cannot be selected.
- Settings now has Current model, Cloud APIs, Local models, Hotkeys and General sections. Local deployment/runtime management is independent of cloud API credentials.
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
