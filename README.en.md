<p align="center"><img src="src/PingYi.App/Assets/screen-insight-icon-512.png" width="96" height="96" alt="Screen Insight icon"></p>
<h1 align="center">Screen Insight</h1>
<p align="center"><strong>One capture. Understand the content.</strong></p>
<p align="center">Automatic translation, image description or QR decoding · Local models first · Windows / Ubuntu</p>
<p align="center"><a href="README.md">简体中文</a> · <a href="README.en.md">English</a></p>
<p align="center">
  <a href="https://github.com/qingshihuan/pingyi/releases/latest"><img src="https://img.shields.io/github/v/release/qingshihuan/pingyi?display_name=tag" alt="Latest stable release"></a>
  <a href="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml"><img src="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml/badge.svg?branch=main" alt="Main branch CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-0f766e" alt="MIT License"></a>
</p>
<p align="center"><a href="https://github.com/qingshihuan/pingyi/releases/latest"><strong>Download</strong></a> · <a href="#quick-start">Quick start</a> · <a href="#docs">Documentation</a> · <a href="https://github.com/qingshihuan/pingyi/issues">Report an issue</a></p>

Screen Insight (截屏释义, formerly PingYi) is a desktop screenshot recognition tool. Select **Smart capture** and a screen region. Local text and QR probes recommend translation, image description or QR decoding. Uncertain or mixed content asks for your choice; manual actions remain available on the same image without another capture.

**Version 0.7.0 includes automatic tasks, Basic / Lightweight modes and first-run model setup. Installed-package features depend on the corresponding Release notes.** Routing is a content-based heuristic, not a guarantee of user intent or recognition accuracy. [Rules, migration and verification limits](docs/AUTOMATIC_CAPTURE.md)

<!-- screen-insight-videos:start -->
## Download the product and setup videos

**One-click local model setup turns screenshot translation, image description and reference prompts into desktop actions. QR codes use a separate local decoder, not the model.**

| Product introduction · 60 seconds | First-run guide · 1 minute 50 seconds |
| --- | --- |
| [![Product introduction](docs/videos/intro-poster.jpg)](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4) | [![First-run guide](docs/videos/setup-poster.jpg)](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4) |
| [Download introduction MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4) | [Download setup MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4) |

**Click a cover or download link to save the MP4, then open it in a local video player. The GitHub file preview is not an online player for these videos; its file-size warning does not mean the video is damaged.**

Made with Remotion: 1080p/30fps, Chinese captions and an original instrumental score, no voiceover. Native v0.7.0 UI with authored synthetic examples; process animations are not performance measurements. [Downloads, playback instructions and source](docs/videos/README.md) · [Windows upgrade and restart guidance](docs/WINDOWS_UPGRADE.md)

<!-- screen-insight-videos:end -->

<a id="download"></a>
## Download and install

Open **[the latest stable GitHub Release](https://github.com/qingshihuan/pingyi/releases/latest)** and expand **Assets**. Download an application package, not GitHub's generated `Source code` archive.

Since v0.6.0, Complete is the only distributed edition, using the `PingYi-Complete-` prefix. Windows 10/11 x64 has an installer and portable ZIP; Ubuntu 22.04+ x64 has DEB and tar.gz. Four packages are accompanied by `SHA256SUMS.txt`. Installing missing Linux system libraries may require internet access.

Packages include the application runtimes, lightweight Chinese/English OCR and translation models, and llama.cpp CPU / Vulkan runtimes; separate Python or .NET installation is unnecessary. **LLM weights are not bundled.** Basic and Lightweight are in-app processing modes, not separate installers, and do not change Complete's data directories. Legacy Standard data is neither migrated nor deleted automatically.

> Download only from this repository's Releases and verify SHA-256 checksums. Unsigned Windows builds may trigger SmartScreen; checksums verify file integrity, not code signing.

<a id="quick-start"></a>
## First run

1. **Choose a mode.** The first-run guide offers an explicit one-click Basic model download, an existing local service, or **Skip download · use Lightweight**. Opening the app alone does not download a large model.
2. **Capture a region.** Select Smart capture or use the capture shortcut. Text is routed to translation, decoded QR content can be shown locally, and mixed or uncertain content asks you to choose.
3. **Read or override.** Switch to translation, description, QR decoding or manual prompt reconstruction using the same image. Switching cancels the previous operation and prevents its late result from replacing your choice. Closing the result ends that screenshot session.

| Mode | OCR and translation | Requirements |
| --- | --- | --- |
| **Basic — new-install default** | Local custom vision OCR → local custom model translation | A local image-capable model with vision components; one-click setup is available |
| **Lightweight** | PaddleOCR → Argos Translate | Bundled Chinese/English baseline models; no LLM download or discrete GPU needed |
| **Other combinations** | PaddleOCR with LLM translation, Baidu, or custom providers | Configure the chosen model or credentials; remote services are not labelled local Basic |

The **PaddleOCR + model correction** provider and **Visual correction** mode are removed. **Basic transcribes the screenshot directly with a vision model; it does not send a PaddleOCR draft for correction.** Automatic task detection can still use local PaddleOCR to inspect text; that is routing, not a correction pass. Image description still requires a vision model when Lightweight is selected.

The setup guide shows download size, license, source and hardware guidance. Only clicking Download fetches and verifies weights from ModelScope, starts the local runtime, and checks OCR / translation using a fixed synthetic image and short phrase. Settings are committed after success. Failed or cancelled setup is not marked complete; retry or choose Lightweight. Partial downloads are retained for resuming. These connection checks are not a real-world OCR accuracy or hardware benchmark.

Upgrades preserve the selected providers, endpoint, model, language and shortcut. The removed correction OCR ID migrates to direct vision OCR; unrelated preferences are not overwritten. **Model setup** on the home screen reopens the guide.

### Capture shortcuts

| Desktop | Default entry |
| --- | --- |
| Windows | `Ctrl+Alt+D` or the capture button |
| Linux X11 | `Ctrl+Shift+D` or the capture button |
| Linux Wayland | Capture button; bind the provided command in desktop keyboard settings |

Turn **Auto task** off to make the main button / default shortcut perform text translation. Manual Translate, Describe, Prompt and QR actions remain available.

Under **Settings → Appearance & startup**, type a combination or choose **Record shortcut…**. Enter confirms, Escape cancels, and **Save and apply** activates the choice. Restore default also requires saving. Supported combinations contain Ctrl / Alt / Shift and one A–Z or top-row 0–9 key, not Super, function or keypad keys. Recording suspends the app's native binding and restores the saved combination afterwards. Registration conflicts are reported and attempt to restore the previous binding.

### Ubuntu Wayland

Wayland uses the public XDG Screenshot portal, not the XWayland root window. It requires `xdg-desktop-portal` and a matching backend, typically `xdg-desktop-portal-gnome` on Ubuntu GNOME. Saving a preferred key inside the app is not system registration. Copy the capture command into desktop **Keyboard → Custom Shortcuts**; Screen Insight does not overwrite desktop bindings. Other applications and personal settings can use the same combination. [Linux dependencies and troubleshooting](docs/LINUX_CAPTURE.md)

<a id="features"></a>
## Desktop capabilities

**Automatic tasks, manual correction.** One region can be translated, described or decoded. Multiple QR results are selectable. QR decoding needs neither OCR nor a model / API; URLs are opened only after an explicit click. [QR details](docs/DESKTOP_QR.md)

**Direct vision OCR and translation.** Connect llama.cpp, Ollama, LM Studio, vLLM or compatible Chat Completions services. Basic requires a loopback endpoint; remote endpoints remain custom combinations. Language coverage and recognition quality depend on the model. Lightweight retains the offline Chinese/English fallback.

**Image description and prompts.** An `image_url`-capable model is required; a text-only model cannot substitute. Prompt reconstruction is manual, creating a reference for a similar scene rather than recovering the original prompt, seed or generation settings. [Image analysis](docs/IMAGE_ANALYSIS.md)

**Desktop controls.** English / Chinese UI, light / dark themes, separate categorized settings, tray controls and reusable result cards. Multi-monitor selection is supported, but specific mixed-DPI, input-method and desktop combinations require real-device validation.

<details>
<summary>Early workflow demo</summary>

<p align="center"><img src="docs/demo.gif" width="800" alt="An earlier screenshot OCR and translation workflow"></p>

This older demo does not represent the current layout, processing modes or Linux default shortcut. Follow the instructions above.

</details>

<a id="models"></a>
## Models and cloud services

**Managed local models:** use the first-run guide or **Settings → Local models**. Auto tries Vulkan and falls back to CPU; explicit Vulkan does not automatically switch. Consult the catalog's size and hardware guidance and test your own device; model speed and memory needs are not guaranteed.

**Existing services:** configure your compatible endpoint, model and credentials under **Settings → Custom endpoint**, and ensure the model can accept images. Configured managed models load on demand. Toggling automatic task selection does not itself start an LLM.

**Optional cloud providers:** configure your Google Cloud or Baidu credentials under **Cloud services** and select OCR and translation providers independently. Fees, quotas and terms belong to your account. Non-loopback custom endpoints require HTTPS.

<a id="privacy"></a>
## Data boundaries

| Operation | Where data goes |
| --- | --- |
| Automatic probes, QR, Lightweight OCR / translation | On-device processing; probing neither contacts cloud providers nor downloads models |
| Basic / local model | Relevant text or image goes to the configured local service; an independent server controls its own logging and network behavior |
| Automatically selected remote OCR or translation | Explicit confirmation of this request's data destination before execution; refusal sends nothing |
| Remote description / prompt reconstruction | Each request and retry confirms endpoint and model, independently of text-translation permission |

Screenshots, OCR text, translations, analysis and QR payloads are not written to history or logs. Probe reuse is confined to the in-memory screenshot session. Credentials use Windows DPAPI or Linux Secret Service, not ordinary settings JSON. Automatic update checks default off; explicit model downloads, update checks and remote services use the network.

The operating system and external services have their own retention boundaries. Wayland portals may create screenshot files; the app cleans temporary-directory copies but does not guarantee deletion of files saved elsewhere by the desktop. Independently configured local and remote servers may retain requests.

## Browser extension: development frozen

Existing Chrome / Edge functionality and the Native Messaging / current-user named-pipe bridge remain available, without new extension features, store submission work or a TCP listener. The extension and native host still need separate building / registration; the desktop installer does not install them. API keys are not copied to the browser and remote OCR requires per-image approval. Existing page, selection and hover translation features and webpage restrictions are documented in [the extension guide](browser-extension/README.md). This change does not alter its protocol.

<a id="docs"></a>
## Documentation and development

[Automatic capture and onboarding](docs/AUTOMATIC_CAPTURE.md) · [Linux capture](docs/LINUX_CAPTURE.md) · [QR decoding](docs/DESKTOP_QR.md) · [Image analysis](docs/IMAGE_ANALYSIS.md) · [Performance](docs/PERFORMANCE.md) · [Quality baseline](docs/QUALITY_BASELINE.md) · [Release notes](docs/releases)

Official platform scope is Windows 10/11 x64 and Ubuntu 22.04+ x64, not macOS, ARM or every Linux distribution. Live translation overlays, batch PDF / image processing, specialized table / formula recognition and persistent history are not included. Routing, OCR and model output can be wrong; use manual selection and check results.

Development requires .NET 10 SDK; the standalone Argos engine needs Python 3.13. Run from the repository root:

```sh
dotnet restore PingYi.slnx
dotnet build PingYi.slnx
dotnet run --project src/PingYi.App/PingYi.App.csproj
```

Source builds need models, the translation engine and llama.cpp prepared separately. Without bundled llama.cpp, the guide disables managed downloads while keeping existing-service and Lightweight choices. `--settings` opens Settings; `--capture` starts capture on first launch or forwards it to the running instance.

<details>
<summary>Tests and packaging</summary>

Prepare the translation environment with `scripts/setup-engine.ps1` on Windows or `scripts/setup-engine.sh` on Linux. PaddleOCR does not need Python but does need models.

```sh
dotnet test PingYi.slnx
python -m unittest discover -s engine_host -p "test_*.py"
python -m unittest discover -s scripts -p "test_*.py"
python scripts/download-offline-models.py --destination artifacts/model-source
```

Model preparation uses the network. A configured Windows `py -3` launcher can replace `python`. Complete packaging example in PowerShell:

```powershell
$version = (Get-Content .github/release-version.txt -Raw).Trim()
.\scripts\setup-engine.ps1
python scripts/prepare-llama-runtime.py --runtime win-x64 --destination artifacts/llama-runtime/win-x64
.\scripts\publish.ps1 -Runtime win-x64 -Version $version -Edition Complete `
  -OfflineModelSource artifacts/model-source `
  -LlamaRuntimeSource artifacts/llama-runtime/win-x64 -BuildInstaller
```

Use `-InnoCompiler` for a non-default Inno Setup path. Quality checks use `scripts/run-quality-baseline.ps1 -ModelDirectory <model-directory>`. Optional signing parameters are `-SigningCertificateThumbprint` / `-TimestampUrl`; CI secret names are `PINGYI_SIGNING_CERTIFICATE_BASE64` / `PINGYI_SIGNING_CERTIFICATE_PASSWORD`. Never commit models, screenshots, credentials or certificates.

Publishing is triggered by a version tag or a main-branch change to `.github/release-version.txt`, after cross-platform builds, tests, four Complete package checks and license auditing. Existing tags pointing at another commit are not replaced. This feature work does not itself change the release version. [Release workflow](.github/workflows/release.yml)

</details>

Automation includes pure routing, migrations, synthetic model responses, UI and isolated Xvfb / D-Bus checks; **it is not real-model accuracy, physical GNOME / Wayland, multi-monitor or GPU certification**.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md) before reporting issues or submitting changes. Do not upload private screenshots, text or credentials. Source is [MIT licensed](LICENSE); models and dependencies retain their own terms, documented in [third-party notices](THIRD_PARTY_NOTICES.md) and each package's `licenses/` manifest.

## Quit and release resources

Use **Quit and release resources** in the main-window footer or tray to stop the app and its owned
model backends and wait for their exit. Closing the window still hides it to the tray. External
Ollama / LM Studio services are not killed; models and settings are retained. Backend allocations
are reclaimed by the OS/driver, not by resetting the entire GPU. See [shutdown and ownership](docs/RUNTIME_SHUTDOWN.md).
These are source changes; installed behavior depends on the corresponding Release.
