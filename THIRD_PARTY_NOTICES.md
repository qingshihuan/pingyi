# Third-party notices

Screen Insight (截屏释义, formerly PingYi) is licensed under MIT. Complete release packages contain the following offline components:

- PaddlePaddle PP-OCRv5 mobile detection and recognition ONNX models, licensed under Apache-2.0. Source: https://huggingface.co/PaddlePaddle
- Argos Translate runtime components, licensed under MIT. Source: https://github.com/argosopentech/argos-translate
- Chinese-English and English-Chinese Argos model packages derived from OPUS-MT. The model package README identifies the original model license as CC BY 4.0 and credits Jörg Tiedemann and Santhosh Thottingal, “OPUS-MT — Building open translation services for the World,” EAMT 2020.
- CTranslate2, licensed under MIT. Source: https://github.com/OpenNMT/CTranslate2
- CTranslate2's x64 wheels statically link Intel oneMKL and oneDNN 3.1.1. Intel oneMKL is redistributed under the Intel Simplified Software License with its complete third-party notices; oneDNN is Apache-2.0 with its upstream third-party-programs file.
- SentencePiece and PaddleOCR, licensed under Apache-2.0; ONNX Runtime, Avalonia, SkiaSharp and their transitive runtime components under their respective licenses.

Depending on the target platform, the CTranslate2 engine bundles an OpenMP
runtime. GNU libgomp is GPLv3 with the GCC Runtime Library Exception 3.1;
LLVM OpenMP is Apache-2.0 with the LLVM exception; and the Intel OpenMP binary
in the official Windows wheel is redistributed under the Intel Developer Tools
End User License Agreement. Release license manifests distinguish these
runtimes and include the applicable full terms only when each binary is present.

PingYi release builds intentionally exclude NVIDIA CUDA, cuDNN and related
proprietary GPU runtime binaries. The release dependency audit fails the build
if these files, an NVIDIA Python package, or a Torch runtime are detected.

From v0.6.0, Complete is the only published edition. Its packages also contain pinned official llama.cpp
CPU and Vulkan runtime binaries, licensed under MIT. Source and exact release:
https://github.com/ggml-org/llama.cpp/releases/tag/b10227. These binaries do not
include CUDA, cuDNN, ROCm, or model weights.

The pinned llama.cpp packages may also include the LLVM OpenMP runtime required
by the server binaries. LLVM OpenMP is distributed under Apache-2.0 with the LLVM
exception. Complete release artifacts include both full texts in `licenses/`.
The llama.cpp payload does not duplicate GNU libgomp; Ubuntu packages declare
the system `libgomp1` runtime dependency instead.

At the user's request, PingYi Complete can download the following model files
from pinned ModelScope revisions. They are not bundled in PingYi releases:

- Qwen3.5-2B GGUF conversions by Unsloth, based on Qwen3.5-2B, Apache-2.0.
  Source: https://modelscope.cn/models/unsloth/Qwen3.5-2B-GGUF
- Gemma 4 E2B GGUF conversion published by ggml-org, Apache-2.0 as reported by
  the model repository. Source: https://modelscope.cn/models/ggml-org/gemma-4-E2B-it-GGUF

The download catalog pins each repository revision, file size, and SHA-256 hash.
Users remain responsible for reviewing model terms and acceptable-use
requirements applicable to their jurisdiction and use case.

PingYi can optionally call Google Cloud Vision API and Google Cloud Translation
Basic v2 with an API key supplied from the user's own Google Cloud project.
Google services and credentials are not bundled with PingYi. Their use remains
subject to Google's terms, project configuration, quotas, and billing. Source:
https://cloud.google.com/vision and https://cloud.google.com/translate

Every release archive and installer includes a `licenses/` directory. It contains
the complete license texts and upstream third-party notices collected from the
exact NuGet and Python packages used by that build, the self-contained .NET and
Python runtimes, bundled fonts and offline models, and llama.cpp in the Complete
edition, including its LLVM OpenMP dependency. `licenses/manifest.json` maps
each bundled component to its files, and the release audit fails if a required
text is absent or truncated.

The upstream projects and authors retain all rights granted by their respective
licenses. This summary does not replace the complete texts distributed in
`licenses/`.

## PingYi runtime integration

PingYi's own Python adapter uses a lightweight offline sentence splitter and
streaming SHA-256 verification. It discards Python-level dependency diagnostics
during engine requests and exposes only intentionally authored error messages;
unknown dependency exceptions are represented by a type and a generic message.
The existing Python socket guard also covers dependency initialization during
health and translation requests. Explicit model installation remains a separate
network-capable operation. These adapter changes do not add, replace, or upgrade
third-party packages, models, native runtimes, or their license terms. They are
not an operating-system network sandbox or a guarantee about native-library I/O.

## Workspace UI development tests

The workspace redesign reuses the existing Avalonia/Fluent/Skia runtime and
repository branding assets. It adds no production runtime, font, icon-pack,
model or service dependency.

The separate `PingYi.App.Tests` project uses Avalonia.Headless.XUnit 12.1.1
(from Avalonia, MIT; https://github.com/AvaloniaUI/Avalonia), xUnit v3 3.2.2
(Apache-2.0; https://github.com/xunit/xunit), and the existing versions of
Microsoft.NET.Test.Sdk and xunit.runner.visualstudio. These packages are test
infrastructure only and are not part of PingYi application publishing.
CI may install Noto CJK fonts on the test runner to render Chinese snapshots;
those runner fonts and generated test snapshots are not bundled in releases.

## Startup and resource policy

The startup/resource changes use the existing pinned Avalonia, ONNX Runtime,
Argos and CTranslate2 dependencies. No production dependency or model version
is added or upgraded. ONNX session threading is configured according to its
upstream API, and dense output buffers are borrowed only while their output
owners remain alive. The optional CI performance workflow installs the existing
CPU-only requirements without transitive optional NLP stacks; its measurements
contain no user screenshots, recognized text, translations or credentials.

## macOS-inspired visual refinement

The refined interface uses original XAML geometry, a project-authored semantic
palette, and the existing PingYi branding. No Apple artwork, SF Symbols,
San Francisco font files, or new third-party icon/font packages are bundled.
Font-family names refer only to fonts already installed on the host system,
with the existing Inter package retained as a fallback. This visual refinement
does not add macOS platform support, a web runtime, or additional production
dependencies. Synthetic text in UI test previews is authored for this project;
the previews do not contain real captures, credentials, or connected services.

The grouped-preferences finishing pass adds only original static document-card
geometry and project-authored text. Microsoft YaHei and Noto CJK family names
select installed host fonts; these fonts are not distributed by this change.
The additional layout tests reuse the existing test packages. Native window
controls, model providers and runtime resource policies are unchanged.

## Usability regression fixes

The unified workspace, live language resources, descriptive mode picker, and Help & About page add no production dependencies, fonts, artwork, telemetry, or web runtime. Language dictionaries are compiled into the existing Avalonia application. Windows capture preparation uses documented DWM and User32 APIs for the application's own windows; temporary display-affinity and transition settings are restored afterwards. The Windows 10 2004 capture-exclusion value is never used on older Windows builds. Synthetic UI/capture tests do not access user credentials or persist real screenshots.

## Image analysis and model readiness

Image description and prompt reconstruction reuse the already-declared SkiaSharp,
.NET HTTP/JSON, Avalonia, and user-configured compatible model services. No new
production package, font, model weight or provider credential is bundled. Native
model diagnostics retain only authored categories; image-analysis output remains
in memory and is not added to logs or persistent history. Model licenses and
third-party service terms remain applicable; no original-prompt recovery or
model-accuracy guarantee is implied.

## Linux screenshot portal integration

The Wayland capture adapter calls the public XDG Screenshot D-Bus interface via
the system-installed GLib / GIO / GObject libraries (LGPL-2.1-or-later). These
libraries and the desktop portal/backends are not bundled or copied into PingYi;
Debian packages declare system dependencies and recommend a matching backend.
No NuGet, Python, model, or third-party source is bundled by this adapter.
X11 continues to use the system libX11 (MIT / X11).

Sources: https://docs.gtk.org/gio/ and
https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Screenshot.html

Development-only native tests use Xvfb, xdotool, dbus-run-session and the system
PyGObject bindings on a private test session bus. They are not release assets.

## Shortcut customization (0.5.2)

The shortcut recorder and reset controls reuse the existing Avalonia controls,
headless test packages, Windows User32 and system X11 integrations. They add no
keyboard hook package, production dependency, font, model, telemetry or retained
key-event history. Wayland bindings remain explicitly managed by the desktop;
no private compositor API or automatic system-configuration edit is introduced.

## Browser extension and native messaging bridge

The Screen Insight browser extension, interface icons/product assets, native messaging host and current-user registration script are project code under the repository MIT license. The extension bundles no third-party JavaScript, fonts, analytics, credentials or model weights. It uses Chromium extension APIs (Native Messaging, scripting, storage, context menus, language detection and visible-tab capture), which are provided by the user's browser and are not redistributed. The desktop bridge reuses the existing .NET, SkiaSharp, OCR and translation dependencies listed above; no new inference libraries are bundled.

## Screen Insight branding (2026-09-28)

The approved blue, orange, white and black icon is derived from the project's existing icon, revised with OpenAI image generation at the maintainer's request. The source image and exported PNG/ICO files are project assets; no third-party icon pack, font or artwork was added. `scripts/build-brand-icons.cjs` uses sharp (Apache-2.0) only as an optional development-time image resizer/encoder; sharp is not bundled with the desktop application or browser extension. Desktop and browser colors come from the project's existing ApplePalette resources. See `design-assets/BRANDING.md` for the approved artwork and reproduction instructions.

## Offline QR decoding (2026-09-28)

Desktop QR decoding adds [ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11), licensed under Apache-2.0 ([upstream project and license](https://github.com/micjahn/ZXing.Net)). Its managed decoder consumes pixels from the existing SkiaSharp image pipeline; no System.Drawing binding, camera framework, model, external service or new native runtime is added. The release license collector discovers the NuGet package and includes its license text. QR screenshots and decoded payloads stay in memory and are not logged or saved. Only an explicit click sends an HTTP/HTTPS URL to the operating system's default browser. Synthetic test QR codes contain example text or local loopback URLs. The frozen browser extension and its protocol are unchanged.

## Automatic capture, Basic / Lightweight modes and first-run setup

Automatic task routing reuses the bundled PaddleOCR and ZXing.Net components for local content probes. This is not the removed PaddleOCR-draft correction provider: direct vision OCR never receives probe text as a correction draft. Basic uses a user-configured local vision / translation model; Lightweight retains the existing PaddleOCR / Argos path. These are application modes, not new package variants. Existing offline models, dependencies, license texts and data directories remain in place.

First-run setup downloads model weights only after an explicit user action, using the existing pinned ModelScope catalog and its verification / resumable-download implementation. Existing model terms above apply. Connection checks use project-authored synthetic text and images, not user screenshots, clipboard content or credentials in logs. No new production package, font, image asset, runtime or telemetry service is introduced.

Automatic remote processing adds per-request confirmation and immutable provider configuration snapshots. Decoded QR links never open automatically. Screenshot and probe reuse is confined to in-memory sessions; no persistent content history or keyboard recording is added. The frozen browser extension and its wire protocol are not modified by this work. Synthetic UI and routing tests do not establish real-model accuracy or physical-desktop compatibility.

## Remotion documentation videos

The two v0.7.0 documentation videos use Remotion 4.0.506 with React, TypeScript and FFmpeg as isolated development tools in `media/remotion/`. Remotion retains its own license (https://www.remotion.dev/license); this does not relicense Remotion under the repository MIT license. No Remotion dependency is included in the desktop app or its installer.

The video layouts, synthesized instrumental score and illustrative text are project-authored. UI frames are rendered from actual Avalonia controls with synthetic inputs, never private screenshots, model outputs or credentials. Existing approved Screen Insight artwork is reused. Noto CJK font family names select runner-installed fonts; no font files are committed or distributed. Videos have burned-in Chinese subtitles plus SRT files, and no spoken voiceover. See `docs/videos/SOURCES.md` for provenance and limitations.

## Owned backend shutdown

Backend lifecycle management reuses .NET process APIs and Windows Job Objects. It adds no runtime
package, device driver, executable, font or telemetry dependency. It stops only processes started
by this application, never scans other applications by process name or GPU. Lifecycle tests use
synthetic child processes and the existing CI Python interpreter. No GPU reset or external-model
service shutdown API is called; user data, model files and credentials remain untouched.
