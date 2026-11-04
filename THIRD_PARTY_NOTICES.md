# Third-party notices

Screen Insight (截屏释义, formerly PingYi) is MIT licensed. This notice describes the unreleased unified-model branch; historical release notes describe older packages.

## Shipped components

- Avalonia 12.1.1, Avalonia.Desktop, Fluent and Inter font integration: MIT; Inter font: SIL Open Font License 1.1.
- SkiaSharp 3.119.4 and transitive native graphics components: the licenses and upstream notices supplied by their NuGet packages.
- ZXing.Net 0.16.11 for local QR decoding: Apache-2.0.
- .NET 10 and System.Security.Cryptography.ProtectedData: the exact runtime/package license and third-party notices included with the build.
- Pinned llama.cpp CPU and Vulkan runtimes: MIT. The bundled revision is recorded by `scripts/prepare-llama-runtime.py`; no model weights are bundled.
- LLVM OpenMP, when present: Apache-2.0 with LLVM exception. GNU libgomp, when present: GPL-3.0 with GCC Runtime Library Exception 3.1. The collector includes these only when the corresponding native binaries are present; Linux packages may rely on the system libgomp1 dependency.

PaddleOCR, ONNX Runtime, Argos Translate, CTranslate2, SentencePiece and the embedded Python/PyInstaller inference runtime have been removed, along with traditional OCR/translation model bundles. Google Cloud Vision and Baidu OCR/translation adapters are removed. No corresponding runtime, model or license bundle is copied into new packages.

Release builds reject retired engines and NVIDIA CUDA/cuDNN/Torch binaries. Optional runtimes downloaded later at the user's explicit request are separate from bundled release contents and retain their upstream licenses.

Automatic backend detection is the default selection and uses CPU as the fallback when GPU backends are unusable. This preference does not add bundled GPU dependencies; the package still includes CPU/Vulkan only, and optional backends retain their existing installation and license requirements.

One-click local setup reuses installed runtimes and configured models, downloading missing components only after the user starts setup. It uses the same pinned model hashes and official runtime digest checks. The source/version audit is documented in [runtime downloads](docs/RUNTIME_DOWNLOADS.md); this UI addition does not replace the bundled runtime.

## User-downloaded models and services

Model downloads use pinned ModelScope revisions, expected lengths and SHA-256 values from `ManagedMultimodalModels`. The UI presents each model's source, license and device guidance before downloading. Model weights and remote service credentials are not included in releases. Users should review the applicable model/service terms. Capability verification is not a quality, performance or legal-use guarantee.

Built-in local deployment remains separate from API model management. The unified local/cloud API editor adds no bundled third-party runtime or new dependency; external services retain their own licenses and process ownership.

## Desktop integration

Windows capture/hotkeys and DPAPI use operating-system APIs. Linux X11 uses the system libX11 (MIT/X11); Wayland capture uses system GLib/GIO/GObject (LGPL-2.1-or-later) and the public XDG screenshot portal. These system libraries and portal backends are not copied into the app package.

The browser extension and native messaging bridge are repository MIT code. They bundle no analytics, credentials, third-party JavaScript or model weights. Existing repository branding and XAML artwork are retained; no Apple artwork, SF Symbols or San Francisco fonts are distributed.

## Development and historical media

Avalonia.Headless.XUnit 12.1.1, xUnit v3 3.2.2, xUnit v2, Microsoft.NET.Test.Sdk and the Visual Studio test runner are test-only dependencies. Python is used for repository/packaging checks only. CI fonts, PyGObject, Xvfb and other native test tools are not release assets.

Historical videos and their Remotion source remain under `docs/videos` and `media/remotion`; their source/asset attribution remains in those directories. They describe older UI and model behavior.

## Release license manifest

Every release archive and installer contains `licenses/manifest.json`, a summary and full license/notice texts collected from exact NuGet packages, the self-contained .NET runtime, fonts and bundled native runtimes. Packaging fails if required text is missing, truncated or outside the license directory. This summary does not replace those full terms; upstream authors retain their rights.
