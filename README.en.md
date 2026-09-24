<p align="center">
  <img src="src/PingYi.App/Assets/pingyi-v2-icon-512.png" width="96" height="96" alt="PingYi icon">
</p>

<h1 align="center">PingYi</h1>

<p align="center"><strong>Select a screen region. Extract text. Understand what you see.</strong></p>
<p align="center">Offline-first screenshot translation and image understanding · Windows / Ubuntu · MIT licensed</p>

<p align="center">
  <a href="README.md">简体中文</a> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/qingshihuan/pingyi/releases/latest"><img src="https://img.shields.io/github/v/release/qingshihuan/pingyi?display_name=tag" alt="Latest stable release"></a>
  <a href="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml"><img src="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml/badge.svg?branch=main" alt="Main branch CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-0f766e" alt="MIT License"></a>
</p>

<p align="center">
  <a href="https://github.com/qingshihuan/pingyi/releases/latest"><strong>Download</strong></a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#docs">Documentation</a> ·
  <a href="https://github.com/qingshihuan/pingyi/issues">Report an issue</a>
</p>

PingYi is a desktop screenshot OCR and translation app. Select text on your screen to recognize, translate and copy it, without switching to a browser or manually uploading a file. Connect a compatible vision model to describe images or create reference prompts for visually similar scenes.

**Basic Chinese-English OCR and translation work offline, without a discrete GPU.** Release packages include the baseline models and application runtimes; a separate Python or .NET installation is not required. Image understanding and LLM enhancement are optional and require a separately configured or downloaded compatible model.

<a id="download"></a>
## Download and install

Open **[the latest stable GitHub Release](https://github.com/qingshihuan/pingyi/releases/latest)**, expand **Assets**, and choose your platform and edition. Use the application packages, not GitHub's automatically generated `Source code` archives.

| Edition | Included | Choose it for |
| --- | --- | --- |
| **Standard** · `PingYi-` | Offline OCR and basic Chinese-English translation; connections to external models and cloud providers | Offline essentials, or an existing Ollama / LM Studio service |
| **Complete** · `PingYi-Complete-` | Standard features plus llama.cpp CPU / Vulkan runtimes and model download management | Downloading, configuring and running local multimodal models from PingYi |

**Windows 10/11 x64:** choose the `*-win-x64-setup.exe` installer or a portable `*-win-x64.zip`. The installer creates Start Menu and desktop shortcuts.

**Ubuntu 22.04+ x64:** choose a `*-linux-x64.deb` or extract a `*-linux-x64.tar.gz`. Linux still needs system graphics libraries and other dependencies; DEB packages declare them, and installing missing dependencies may require internet access. X11 and Wayland use different capture paths, explained below.

Complete **does not bundle LLM weights**: download a model before using its enhancement features. Offline essentials remain available. Both editions have separate installation and data directories and can coexist. Each stable release provides eight application packages across the two editions, plus `SHA256SUMS.txt`.

> Download only from this repository's Releases and verify SHA-256 checksums. Unsigned Windows builds may trigger SmartScreen. A checksum verifies file integrity; it is not a substitute for code signing.

<a id="quick-start"></a>
## Start in three steps

1. **Open PingYi.** Keep local OCR and Chinese-English offline translation selected for your first run. No API key is needed.
2. **Select a screen region.** Choose **Start capture**, or use the shortcut below. Wayland opens the system screenshot / permission dialog.
3. **Read and copy.** Copy the source, translation or both from the result card. Retry or pin the card when needed; Escape cancels region selection.

| Desktop | Default capture entry |
| --- | --- |
| Windows | `Ctrl+Alt+D`, or the capture button |
| Linux X11 | `Ctrl+Shift+D`, or the capture button |
| Linux Wayland | The capture button; bind a global shortcut to the capture command in desktop settings |

### Customize your shortcut

Open **Settings → Appearance & startup**. Type a combination, or choose **Record shortcut…**, press the combination, then use **Enter to confirm or Escape to cancel**. Select **Save and apply** to activate the change. **Restore default** also requires saving.

Supported combinations contain at least one of `Ctrl`, `Alt` or `Shift`, plus one `A–Z` letter or top-row `0–9` digit. Super, function and keypad keys are not supported. Recording temporarily suspends PingYi's own native global binding, then restores the saved combination. Failed registration attempts to restore the previous binding and reports the error; button capture remains available.

Global shortcuts can overlap with system, application or personal bindings. No combination is guaranteed conflict-free. See [Linux capture and shortcuts](docs/LINUX_CAPTURE.md) for conflicts and migration of custom combinations versus previous defaults.

### Ubuntu Wayland

Wayland capture uses the **XDG Screenshot portal**, not the XWayland root window. Ubuntu GNOME requires `xdg-desktop-portal` and a matching backend, typically `xdg-desktop-portal-gnome`.

**Saving a shortcut in PingYi stores a preference, not a system binding.** Copy the capture command from Settings and bind the same combination under your desktop's **Keyboard → Custom Shortcuts**. PingYi does not rewrite system shortcuts. An existing system binding can intercept recording; enter the combination manually and adjust desktop settings instead. See [dependencies, troubleshooting and validation limits](docs/LINUX_CAPTURE.md).

<a id="features"></a>
## What PingYi does

- **Extract and translate screen text.** Read text from images, video subtitles, application windows and non-selectable pages. Bundled PaddleOCR and Argos Translate provide offline Chinese-English essentials.
- **Add models when you need them.** Connect llama.cpp, Ollama, LM Studio, vLLM or another compatible Chat Completions service. Choose direct vision OCR, PaddleOCR with visual correction, or LLM translation; quality and language coverage depend on the model.
- **Understand images without text.** **Describe image** explains the content; **Reconstruct prompt** creates a reference description for a similar scene. These require an `image_url`-capable vision model and do not recover the original prompt or generation settings.
- **Keep the desktop workflow simple.** One home screen, separate categorized settings, English / Chinese UI, light / dark themes, a tray icon and a reusable result card. Multi-monitor selection is supported; particular desktop and mixed-DPI configurations still need real-device validation.

<details>
<summary>Watch the early workflow demo</summary>

<p align="center">
  <img src="docs/demo.gif" width="800" alt="Early PingYi workflow demo showing capture, OCR and translation">
</p>

This recording illustrates the workflow using an earlier interface and shortcut configuration. It is not a preview of the current layout or Linux default key; follow the instructions above. UI regression snapshots are available in [CI artifacts](https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml).

</details>

<a id="models"></a>
## Local models and cloud services

**Manage models in Complete:** open **Settings → Local models**, select a catalog model and backend, then **Download and configure**. Downloads support resuming and integrity checks. Auto tries Vulkan first and falls back to CPU; explicitly selecting Vulkan reports failure rather than switching automatically. Memory requirements and speed depend on the model and device; consult the in-app catalog and test your configuration.

**Connect an existing local server:** both editions support compatible endpoints under **Settings → Custom endpoint**. Enter the model name and any credentials required by the server. Image analysis needs a vision model with its vision components loaded; a text-only model cannot replace it. An applied managed model starts on demand rather than preloading when the home screen opens.

**Use optional cloud providers:** add your own Google Cloud or Baidu credentials under **Settings → Cloud services**, then select providers under **Recognition & translation**. OCR and translation can use different providers. Service enablement, quotas and billing belong to your account. Custom endpoints may use HTTP only on loopback addresses; non-loopback endpoints must use HTTPS.

See [image analysis](docs/IMAGE_ANALYSIS.md) for model requirements, upload confirmation and output limits, and [performance](docs/PERFORMANCE.md) for resource policies and measurement boundaries.

<a id="privacy"></a>
## Where your data goes

| Processing mode | Destination |
| --- | --- |
| Default local OCR and Argos translation | Processed on the device; inference does not need the network |
| Local model enhancement | Relevant text or the selected image goes to the configured local service; that service controls its own network and retention behavior |
| Remote OCR / remote image analysis | The selected screenshot goes to the chosen service |
| Remote text translation | Recognized text goes to the chosen service; this is not the same as uploading the screenshot |

PingYi does not create persistent screenshot, recognized-text, translation or image-analysis history by default. Logs exclude this content and secrets. Credentials use Windows DPAPI or Linux Secret Service rather than ordinary `settings.json` storage.

Every remote image-analysis request, including retries, separately confirms the destination and model before sending. Text-translation permission does not authorize image uploads. Model downloads, manually checking for updates / enabling automatic update checks, and remote services use the network. Automatic update checks are off by default.

**The operating system and external services have their own data boundaries.** Wayland portals may create screenshot files; PingYi cleans up portal copies in temporary directories, but does not guarantee deletion of files the desktop saves elsewhere. Remote providers and independently configured model servers have their own logging and retention settings.

<a id="limits"></a>
## Compatibility and limits

Official packages target **Windows 10/11 x64** and **Ubuntu 22.04+ x64**. X11 uses application selection overlays; Wayland uses the system screenshot portal and desktop-managed global shortcuts. macOS, ARM and other Linux distributions are outside the currently declared official support scope.

Bundled baseline models target Simplified Chinese and English. Additional languages, visual OCR and image understanding depend on the selected service or model. OCR and model output can be wrong and should be checked. Live translation overlays, PDF / image batch processing, specialized table / formula recognition and persistent history are not currently included.

CI covers Windows / Ubuntu builds, unit and UI tests, plus native Linux checks on isolated Xvfb / D-Bus sessions. **Passing automation does not certify every real GNOME / Wayland, multi-monitor, input-method or graphics configuration.**

<a id="docs"></a>
## Documentation

[Linux capture and shortcuts](docs/LINUX_CAPTURE.md) · [Image analysis](docs/IMAGE_ANALYSIS.md) · [Quality baseline](docs/QUALITY_BASELINE.md) · [Performance and resources](docs/PERFORMANCE.md)

[UI design and validation](docs/UI_WORKSPACE.md) · [Reliability](docs/RELIABILITY_OPTIMIZATION.md) · [Release notes](docs/releases) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md)

<a id="development"></a>
### Run from source

Development requires the **.NET 10 SDK**; working on or running the standalone Argos engine also requires **Python 3.13**. Run these commands from the repository root:

```sh
dotnet restore PingYi.slnx
dotnet build PingYi.slnx
dotnet run --project src/PingYi.App/PingYi.App.csproj
```

Unlike release packages, a source build needs baseline models and the standalone translation engine prepared separately. `--settings` opens Settings; `--capture` starts capture on first launch or forwards the command to an existing instance.

<details>
<summary>Engine setup, testing and packaging commands</summary>

Use `scripts/setup-engine.ps1` on Windows or `scripts/setup-engine.sh` on Linux to prepare the translation environment. Local OCR itself does not depend on Python, but needs usable OCR models.

```sh
dotnet test PingYi.slnx
python -m unittest discover -s engine_host -p "test_*.py"
python -m unittest discover -s scripts -p "test_*.py"
python scripts/download-offline-models.py --destination artifacts/model-source
```

On Windows, use your configured `py -3` launcher when `python` is unavailable. Model preparation uses the network. Do not commit model weights or local settings.

Windows packaging example in PowerShell, reading the repository's version marker:

```powershell
$version = (Get-Content .github/release-version.txt -Raw).Trim()
.\scripts\setup-engine.ps1
.\scripts\publish.ps1 -Runtime win-x64 -Version $version `
  -OfflineModelSource artifacts/model-source -BuildInstaller
```

Complete also requires the pinned llama.cpp CPU / Vulkan runtimes:

```powershell
python scripts/prepare-llama-runtime.py --runtime win-x64 --destination artifacts/llama-runtime/win-x64
.\scripts\publish.ps1 -Runtime win-x64 -Version $version -Edition Complete `
  -OfflineModelSource artifacts/model-source `
  -LlamaRuntimeSource artifacts/llama-runtime/win-x64 -BuildInstaller
```

Pass `-InnoCompiler` when Inno Setup is outside the default path. Run the offline quality check with `scripts/run-quality-baseline.ps1 -ModelDirectory <prepared-offline-model-directory>`. Distribution requires the quality baseline and license audit; see the [Release workflow](.github/workflows/release.yml) for the complete cross-platform process.

Windows publishing accepts optional `-SigningCertificateThumbprint` and `-TimestampUrl`. CI can use repository secrets `PINGYI_SIGNING_CERTIFICATE_BASE64` and `PINGYI_SIGNING_CERTIFICATE_PASSWORD`. Never commit certificates or passwords.

A `v*` tag, or a version change to `.github/release-version.txt` on `main`, triggers publishing with the corresponding `docs/releases/v<version>.md`. The workflow generates the Release and checksums after both platform builds, tests and all eight package validations succeed. It refuses to replace an existing tag pointing at another commit. An ordinary README edit does not change the version or trigger this release entry point.

</details>

## Contributing and license

Bug reports, OCR failure scenarios, translation feedback and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first. Do not upload private screenshots, text or credentials; report security issues according to [SECURITY.md](SECURITY.md).

PingYi source is licensed under **[MIT](LICENSE)**. Models and third-party components retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). Each application package includes a `licenses/` directory with the applicable license texts and manifest.
