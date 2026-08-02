<p align="center">
  <img src="src/PingYi.App/Assets/pingyi-v2-icon-512.png" width="112" alt="屏译 PingYi 图标">
</p>

<h1 align="center">屏译 PingYi</h1>

<p align="center"><strong>开源、隐私优先、开箱可离线使用的截图翻译与 OCR 文字提取工具</strong></p>

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

屏译（PingYi）是一款面向 Windows 10/11 和 Ubuntu X11 的桌面截图翻译器。按下 `Ctrl+Alt+D`，框选任意屏幕区域，即可完成截图、PaddleOCR 中英文识别、文字提取和翻译，并复制原文或译文。

标准安装包已包含本地 OCR 与中英基础翻译模型，新电脑在没有网络、没有独立显卡、没有 Python 或 .NET 环境的情况下也能使用。需要更好的翻译质量时，可连接本机的 llama.cpp、Ollama、LM Studio、vLLM 或其他兼容 Chat Completions 的大模型服务。

## 为什么选择屏译

- **截图翻译一次完成**：全局快捷键框选，结果卡直接显示原文和译文。
- **OCR 文字提取**：从图片、视频字幕、软件界面和不可复制网页中提取中英文文字。
- **真正的离线保底**：内置 PaddleOCR ONNX 与 Argos Translate，中英识别和翻译无需联网。
- **本地大模型增强**：支持 llama.cpp、Ollama、LM Studio、vLLM 和通用 OpenAI 兼容接口。
- **隐私优先**：本地模式不上传截图与文字，默认不保存截图、正文、译文或历史记录。
- **云端服务可选**：可自行配置百度 OCR、百度翻译或自定义 Chat Completions 接口。
- **跨平台桌面应用**：使用 Avalonia 与 C# 开发，支持 Windows x64 和 Ubuntu X11 x64。
- **开箱运行**：Windows 自包含安装包/便携 ZIP，Ubuntu 提供 `.deb`/`.tar.gz`，无需另装运行时。

## 下载与使用

从 [GitHub Releases](https://github.com/qingshihuan/pingyi/releases) 下载对应系统的最新版本：

- Windows：优先下载 `PingYi-*-win-x64-setup.exe`；免安装使用可下载 ZIP。
- Ubuntu X11：下载 `.deb` 安装包，或解压 `.tar.gz` 运行。

安装后启动屏译，按 `Ctrl+Alt+D`，拖动鼠标框选屏幕区域。识别完成后可以复制原文、复制译文、复制全部、重试或固定结果卡。快捷键、OCR/翻译提供商、本地模型和凭据均在“设置”中管理。

> Windows SmartScreen 可能会提示未识别的发布者，因为当前开源版本尚未购买商业代码签名证书。请只从本仓库 Releases 下载，并使用随 Release 提供的 SHA-256 校验文件核对成品。

## 本地、云端与显卡边界

| 能力 | 默认实现 | 是否联网 | 计算设备 |
| --- | --- | --- | --- |
| 截图与文字识别 | PaddleOCR + ONNX Runtime | 否 | CPU |
| 中英基础翻译 | Argos Translate | 否 | CPU |
| 本机大模型翻译 | llama.cpp / Ollama / LM Studio / vLLM | 否 | 由外部服务决定 |
| 百度 OCR | 百度含位置文字识别 | 是，上传所选图片 | 云端 |
| 百度/自定义翻译 | 百度翻译或 Chat Completions | 是，仅上传识别文字 | 云端 |

标准包不携带 NVIDIA CUDA/cuDNN、AMD 或 Intel 专有 GPU 运行库。外部本机大模型服务可以自行使用 NVIDIA、AMD 或 Intel 加速；屏译只访问用户配置的本机 HTTP 端点，因此没有显卡也不影响基础功能。

## 已实现功能

- Windows 虚拟桌面捕获、全局快捷键、多显示器和不同 DPI 框选。
- Ubuntu X11 的 Xlib 截图与全局快捷键实现。
- PaddleOCR PP-OCRv5 中英移动模型、ONNX Runtime CPU 推理和 SHA-256 完整性校验。
- Argos 中英双向基础翻译，以及本机大模型不可用时的自动离线回退。
- 百度含位置 OCR、百度通用翻译和自定义 Chat Completions 翻译接口。
- llama.cpp、Ollama、LM Studio、vLLM 与通用 OpenAI 兼容预设。
- 复制原文/译文/全部、重新处理、结果卡固定、托盘常驻和浅深色主题。
- 任务优先的主界面、独立设置窗口、故障修复卡和可选经典界面。
- Windows DPAPI 与 Linux Secret Service 密钥存储；凭据支持遮罩查看、明文切换、复制和粘贴。
- 默认零历史记录；日志禁止记录截图、识别正文、译文和密钥。

## 当前兼容范围

- Windows 10/11 x64。
- Ubuntu X11 x64；v1 暂不支持 Wayland 截图门户。
- OCR 与内置基础翻译支持简体中文和英文。
- v1 暂不包含实时覆盖翻译、PDF/图片批处理、表格/公式专项识别和历史记录。

## 从源码运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/)：

```powershell
dotnet build PingYi.slnx
dotnet run --project src/PingYi.App/PingYi.App.csproj
```

开发模式下，本地 OCR 不需要 Python。若要运行 Argos 独立翻译引擎：

```powershell
.\scripts\setup-engine.ps1
```

需要直接打开设置窗口排障时，可在可执行文件后添加 `--settings`。

## 测试与发布

```powershell
dotnet test PingYi.slnx
py -3 -m unittest discover -s engine_host -p "test_*.py"
.\scripts\run-quality-baseline.ps1 -ModelDirectory <已准备的离线模型目录>
.\scripts\publish.ps1 -Runtime win-x64
```

OCR 固定场景分数、翻译对比与成品依赖审计见 [质量基线](docs/QUALITY_BASELINE.md)。发布脚本会生成裁剪后的自包含 .NET 程序、精简独立引擎并打包离线模型；发现 NVIDIA/CUDA/cuDNN 或意外的 Torch 运行库时会直接中止发布。

Inno Setup 位于自定义目录时，可传入 `-InnoCompiler "D:\path\to\ISCC.exe"`。发布机尚无模型源时，先运行：

```powershell
python scripts/download-offline-models.py --destination artifacts/model-source
```

推送 `v*` 标签后，GitHub Actions 会分别生成 Windows 安装器/ZIP 与 Ubuntu `.deb`/`.tar.gz`，附带 SHA-256 校验文件并创建 GitHub Release。

## 参与贡献

欢迎提交 Bug、OCR 失败样本、翻译效果反馈、功能建议和 Pull Request。请先阅读 [贡献指南](CONTRIBUTING.md) 与 [安全策略](SECURITY.md)。反馈截图前请遮挡隐私信息，本项目不会要求上传密钥或私人截图。

## 许可证

屏译源码采用 [MIT License](LICENSE)。离线模型和第三方运行组件保留各自许可证，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。
