# PingYi launch kit

Public links:

- Repository: https://github.com/qingshihuan/pingyi
- Release: https://github.com/qingshihuan/pingyi/releases/tag/v0.3.0
- Demo: `docs/demo.gif`
- License: MIT

## One-line description

**English:** PingYi is an open-source, privacy-first desktop screenshot OCR and translation tool that works offline out of the box and can optionally use local LLMs or cloud providers.

**简体中文：** 屏译是一款开源、隐私优先的桌面截图 OCR 与翻译工具，安装后即可离线使用，也可按需接入本地大模型或云服务。

## Short launch post — English

I built PingYi, an MIT-licensed screenshot OCR and translation app for Windows 10/11 and Ubuntu X11.

Press `Ctrl+Alt+D`, select a screen region, and copy the recognized text or translation. The Standard edition bundles PaddleOCR and Argos Translate, so basic Chinese-English OCR and translation work without internet, Python, a separate .NET runtime, or a discrete GPU. The Complete edition can manage lightweight multimodal GGUF models through bundled llama.cpp CPU/Vulkan runtimes. Existing Ollama, LM Studio, vLLM, llama.cpp, Google Cloud, and Baidu setups are also supported.

PingYi stores no screenshot, recognized text, translation, or history by default. Local mode makes no network request.

Repository and downloads: https://github.com/qingshihuan/pingyi

Feedback on OCR failure cases, packaging, and Linux compatibility is especially welcome.

## 简短发布文案 — 中文

我做了一个开源桌面截图 OCR 与翻译工具「屏译」，支持 Windows 10/11 和 Ubuntu X11，采用 MIT 许可证。

按 `Ctrl+Alt+D` 框选屏幕区域，就能提取并复制原文或译文。标准版内置 PaddleOCR 与 Argos Translate，安装后无需网络、Python、单独的 .NET 环境或独立显卡，即可完成中英基础识别和翻译。完全版还可以通过内置 llama.cpp CPU/Vulkan 运行时一键管理轻量多模态 GGUF 模型，也支持已有的 Ollama、LM Studio、vLLM、llama.cpp、Google Cloud 和百度服务。

默认不保存截图、识别正文、译文或历史记录；本地模式不会发起网络请求。

仓库与下载：https://github.com/qingshihuan/pingyi

欢迎反馈 OCR 失败样本、安装体验和 Linux 兼容性问题。

## Show HN

**Title**

`Show HN: PingYi – Offline-first screenshot OCR and translation for Windows/Linux`

**Text**

I built PingYi because most screenshot translators either require uploading the image or assume several runtimes are already installed.

PingYi is an MIT-licensed Avalonia desktop app. The Standard release bundles PaddleOCR ONNX and Argos Translate, so a fresh Windows machine can perform basic Chinese-English OCR and translation entirely offline. The Complete edition adds llama.cpp CPU/Vulkan runtimes and one-click ModelScope downloads for lightweight multimodal GGUF models. It can also connect to Ollama, LM Studio, vLLM, Google Cloud, and Baidu.

The privacy boundary is intentionally explicit: local mode makes no network request, and screenshots, recognized text, translations, credentials, and history are not logged or persisted by default.

Current limitations: Ubuntu capture is X11-only, bundled offline translation is Chinese-English, and releases are not yet code-signed. I would value feedback on OCR failure cases and packaging on different hardware.

Repo and binaries: https://github.com/qingshihuan/pingyi

## Reddit / open-source communities

**Title**

`PingYi: open-source offline screenshot OCR + translation with optional local LLMs`

**Body**

PingYi is a privacy-first screenshot OCR and translation desktop app I have been building for Windows 10/11 and Ubuntu X11.

What works today:

- global region capture with `Ctrl+Alt+D`;
- bundled offline PaddleOCR + Argos Chinese-English fallback;
- local multimodal OCR and translation through llama.cpp, Ollama, LM Studio, or vLLM;
- optional Google Cloud and Baidu providers using your own credentials;
- English and Chinese UI;
- no screenshot/text/history persistence by default.

The project is MIT licensed, and self-contained installers/archives are available here: https://github.com/qingshihuan/pingyi

I am looking for practical feedback, especially difficult OCR samples, multi-monitor/DPI cases, and Ubuntu X11 packaging issues.

## Posting notes

- Keep the title factual and disclose that you are the maker.
- Do not ask for upvotes. A simple repository link and a request for technical feedback are enough.
- Use the short post for general communities and the detailed version where project posts are explicitly allowed.
- Check each community's current self-promotion and account-age rules immediately before posting.
- V2EX currently says not to submit AI-generated content. Do not paste this launch kit there; write a personal account in your own words if you choose to post.
