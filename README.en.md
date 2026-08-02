<p align="center">
  <img src="src/PingYi.App/Assets/pingyi-v2-icon-512.png" width="112" alt="PingYi icon">
</p>

<h1 align="center">PingYi Screen Translator</h1>

<p align="center"><strong>An open-source, privacy-first screenshot translator and OCR text extractor that works offline out of the box</strong></p>

<p align="center">
  <a href="README.md">简体中文</a> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/qingshihuan/pingyi/releases"><img alt="GitHub Release" src="https://img.shields.io/github/v/release/qingshihuan/pingyi?display_name=tag&include_prereleases"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-0f766e"></a>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4">
  <img alt="Ubuntu X11" src="https://img.shields.io/badge/Ubuntu-X11-e95420">
</p>

PingYi is a desktop screen translator for Windows 10/11 and Ubuntu X11. Press `Ctrl+Alt+D`, select any screen region, and get a screenshot, PaddleOCR Chinese/English recognition, extracted text, and translation in one compact result card.

The standard package bundles local OCR and basic Chinese-English translation models. It works on a new computer without internet access, a discrete GPU, Python, or a preinstalled .NET runtime. For higher translation quality, connect PingYi to a local llama.cpp, Ollama, LM Studio, vLLM, or another Chat Completions-compatible model server.

## Why PingYi

- **One-step screenshot translation:** select a region with a global hotkey and see the source text and translation immediately.
- **OCR text extraction:** copy text from images, video subtitles, desktop applications, and non-selectable web pages.
- **Local multimodal OCR:** use a vision model through llama.cpp, Ollama, LM Studio, or vLLM directly, or let it correct a PaddleOCR draft.
- **Real offline fallback:** bundled PaddleOCR ONNX and Argos Translate provide Chinese-English OCR and translation without network access.
- **Local LLM enhancement:** supports llama.cpp, Ollama, LM Studio, vLLM, and generic OpenAI-compatible endpoints, with automatic source-language detection and 34 common target languages.
- **Privacy first:** local mode uploads nothing and stores no screenshots, recognized text, translations, or history by default.
- **Optional cloud providers:** configure Baidu OCR, Baidu Translate, or a custom Chat Completions service when desired.
- **Cross-platform desktop app:** built with Avalonia and C# for Windows x64 and Ubuntu X11 x64.
- **Self-contained releases:** Windows installer/portable ZIP and Ubuntu `.deb`/`.tar.gz`, with no separate runtime installation.

## Download and use

Download the latest build for your system from [GitHub Releases](https://github.com/qingshihuan/pingyi/releases):

- Windows: use `PingYi-*-win-x64-setup.exe`, or download the ZIP for a portable installation.
- Ubuntu X11: install the `.deb`, or extract and run the `.tar.gz` package.

Start PingYi, press `Ctrl+Alt+D`, and drag to select a screen region. The result card lets you copy the source text, translation, or both; retry processing; or pin the card. Manage the hotkey, OCR/translation providers, local models, and credentials in Settings.

> Windows SmartScreen may show an unknown-publisher warning because the current open-source release does not yet use a paid commercial code-signing certificate. Download only from this repository's Releases page and verify the files with the supplied SHA-256 checksums.

## Local, cloud, and GPU boundaries

| Capability | Default implementation | Network | Compute |
| --- | --- | --- | --- |
| Screenshot OCR | PaddleOCR + ONNX Runtime | No | CPU |
| Local multimodal OCR/correction | Vision model with compatible `image_url` input | No | Chosen by the external server; the model's vision components must be loaded |
| Basic zh-en/en-zh translation | Argos Translate | No | CPU |
| Local LLM translation | llama.cpp / Ollama / LM Studio / vLLM | No | Chosen by the external server; multilingual quality depends on the model |
| Baidu OCR | Baidu position-aware OCR | Yes; selected image only | Cloud |
| Baidu/custom translation | Baidu Translate or Chat Completions | Yes; recognized text only | Cloud |

The standard package includes no proprietary NVIDIA CUDA/cuDNN, AMD, or Intel GPU runtime. An external local model server may use NVIDIA, AMD, or Intel acceleration independently. PingYi only calls the local HTTP endpoint, so the core features still work without a GPU.

## Implemented features

- Windows virtual-desktop capture, global hotkey, multi-monitor selection, and mixed-DPI handling.
- Ubuntu X11 screenshot and global-hotkey implementation through Xlib.
- PP-OCRv5 mobile Chinese/English models, ONNX Runtime CPU inference, and SHA-256 model integrity verification.
- Bundled Argos Chinese-English fallback with automatic recovery when a local LLM is unavailable.
- Baidu position-aware OCR, Baidu general translation, and custom Chat Completions translation.
- Presets for llama.cpp, Ollama, LM Studio, vLLM, and generic OpenAI-compatible services.
- Local multimodal OCR plus a PaddleOCR + vision-model correction mode for small text, terminals, and unusual fonts.
- Copy source/translation/all, retry, pin, tray mode, and light/dark themes.
- Task-focused home screen, separate Settings window, contextual repair cards, and an optional classic interface.
- Windows DPAPI and Linux Secret Service credential storage, with masked display, reveal, copy, and paste controls.
- Zero history by default; logs exclude screenshots, recognized text, translations, and secrets.

## Current compatibility

- Windows 10/11 x64.
- Ubuntu X11 x64; Wayland screenshot portals are outside the v1 scope.
- PaddleOCR and bundled Argos translation support Simplified Chinese and English; local/custom LLM translation offers 34 common target languages in Settings.
- v1 does not include live overlay translation, PDF/image batch processing, specialized table/formula OCR, or history.

## Run from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/):

```powershell
dotnet build PingYi.slnx
dotnet run --project src/PingYi.App/PingYi.App.csproj
```

Local OCR does not need Python during development. To run the standalone Argos translation engine:

```powershell
.\scripts\setup-engine.ps1
```

Append `--settings` to the executable when you need to open Settings directly for troubleshooting.

## Test and release

```powershell
dotnet test PingYi.slnx
py -3 -m unittest discover -s engine_host -p "test_*.py"
.\scripts\run-quality-baseline.ps1 -ModelDirectory <prepared-offline-model-directory>
.\scripts\publish.ps1 -Runtime win-x64
```

See the [quality baseline](docs/QUALITY_BASELINE.md) for deterministic OCR scores, the translation comparison, and release dependency auditing. Release builds contain a trimmed self-contained .NET app, a minimized standalone translation engine, and the offline models. The build fails if it detects NVIDIA/CUDA/cuDNN or an unexpected Torch runtime.

If Inno Setup is installed in a custom directory, pass `-InnoCompiler "D:\path\to\ISCC.exe"`. To prepare model sources on a release machine:

```powershell
python scripts/download-offline-models.py --destination artifacts/model-source
```

Pushing a `v*` tag builds the Windows installer/ZIP and Ubuntu `.deb`/`.tar.gz`, generates SHA-256 checksums, and creates a GitHub Release.

## Contributing

Bug reports, OCR failure samples, translation feedback, feature requests, and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md) first. Redact private information before sharing screenshots; the project will never ask you to upload secrets or private captures.

## License

PingYi source code is available under the [MIT License](LICENSE). Offline models and third-party runtime components retain their respective licenses; see [third-party notices](THIRD_PARTY_NOTICES.md).
