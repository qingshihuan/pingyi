<p align="center">
  <img src="src/PingYi.App/Assets/pingyi-v2-icon-512.png" width="96" height="96" alt="屏译 PingYi 图标">
</p>

<h1 align="center">屏译 PingYi</h1>

<p align="center"><strong>框选屏幕，提取文字，读懂内容。</strong></p>
<p align="center">离线优先的截图翻译与图片理解工具 · Windows / Ubuntu · MIT 开源</p>

<p align="center">
  <a href="README.md">简体中文</a> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/qingshihuan/pingyi/releases/latest"><img src="https://img.shields.io/github/v/release/qingshihuan/pingyi?display_name=tag" alt="最新正式版"></a>
  <a href="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml"><img src="https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml/badge.svg?branch=main" alt="主线 CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-0f766e" alt="MIT License"></a>
</p>

<p align="center">
  <a href="https://github.com/qingshihuan/pingyi/releases/latest"><strong>下载正式版</strong></a> ·
  <a href="#quick-start">快速开始</a> ·
  <a href="#docs">使用与开发文档</a> ·
  <a href="https://github.com/qingshihuan/pingyi/issues">反馈问题</a>
</p>

屏译是一款桌面截图 OCR 与翻译工具。无需切换到浏览器或手动上传文件，框选屏幕上的文字即可识别、翻译和复制；连接兼容的视觉模型后，还能描述图片、生成相似画面的参考提示词。

**中英基础 OCR 与翻译可离线运行，无需独立显卡。**正式安装包包含基础模型及应用运行时，不需要另装 Python 或 .NET。图片理解和大模型增强是可选能力，需要另行配置或下载兼容模型。

<a id="download"></a>
## 下载与安装

前往 **[GitHub Releases 最新正式版](https://github.com/qingshihuan/pingyi/releases/latest)**，展开 **Assets**，选择系统与版本。普通用户下载下面的安装包，不要把 GitHub 自动生成的 `Source code` 源码压缩包当作安装包。

| 版本 | 包含内容 | 适合的使用方式 |
| --- | --- | --- |
| **标准版** · `PingYi-` | 离线 OCR、中英基础翻译；可连接外部模型与云服务 | 先使用离线基础功能，或已有 Ollama、LM Studio 等服务 |
| **完全版** · `PingYi-Complete-` | 标准版能力，加内置 llama.cpp CPU / Vulkan 运行时与模型下载管理 | 希望在屏译内下载、配置和运行本机多模态模型 |

**Windows 10/11 x64：**选择 `*-win-x64-setup.exe` 安装程序，或 `*-win-x64.zip` 便携包。安装程序会创建开始菜单和桌面快捷方式。

**Ubuntu 22.04+ x64：**选择 `*-linux-x64.deb`，或解压 `*-linux-x64.tar.gz`。Linux 仍需要系统图形库等依赖；DEB 会声明系统依赖，安装缺失依赖时可能需要联网。X11 与 Wayland 的截图方式不同，见下方说明。

完全版**不附带大模型权重**，首次使用增强能力需要下载模型；离线基础功能不受影响。两个版本使用独立的安装与数据目录，可以共存。每次正式发布提供两版共 8 个程序包及 `SHA256SUMS.txt`。

> 请仅从本仓库 Releases 下载，并核对 SHA-256。Windows 的未签名构建可能触发 SmartScreen 提示；校验和用于核对下载文件，不替代代码签名。

<a id="quick-start"></a>
## 三步开始使用

1. **启动屏译。**首次使用保留本地 OCR 与中英离线翻译方案，无需配置 API Key。
2. **选择屏幕区域。**点击“开始截图”，或使用下表中的快捷键；Wayland 会显示系统截图／授权界面。
3. **读取并复制结果。**在结果卡中复制原文、译文或全部内容，也可重试、固定结果卡；按 Esc 可取消框选。

| 桌面环境 | 默认截图入口 |
| --- | --- |
| Windows | `Ctrl+Alt+D`，或主界面截图按钮 |
| Linux X11 | `Ctrl+Shift+D`，或主界面截图按钮 |
| Linux Wayland | 主界面截图按钮；全局快捷键需在桌面系统中绑定截图命令 |

### 自定义快捷键

打开 **设置 → 外观与启动**，手动输入组合，或点击 **“录入快捷键…”** 后按下组合，使用 **Enter 确认、Esc 取消**。点击 **“保存并应用”** 后生效；**“恢复默认”** 同样需要保存。

支持 `Ctrl`、`Alt`、`Shift` 中至少一个修饰键，加一个 `A–Z` 字母或主键盘 `0–9` 数字；暂不支持 Super、功能键和小键盘键。录入期间暂停屏译自己的原生全局绑定，结束后恢复已保存的组合。新键注册失败时会尝试恢复原绑定，并显示错误；仍可使用截图按钮。

全局快捷键可能与系统、其他应用或个人设置重叠，不能保证任意组合都无冲突。已有自定义组合与旧默认值的升级处理，见 [Linux 截图与快捷键说明](docs/LINUX_CAPTURE.md)。

### Ubuntu Wayland

Wayland 使用 **XDG Screenshot portal**，不读取 XWayland 根窗口。Ubuntu GNOME 需要 `xdg-desktop-portal` 及匹配的桌面后端（通常为 `xdg-desktop-portal-gnome`）。

**软件内保存快捷键只是保存偏好，不代表系统已完成绑定。**请复制设置页提供的截图命令，到系统 **键盘 → 自定义快捷键** 中绑定同一组合。屏译不会自动改写桌面快捷键；系统占用的组合可能无法被录入窗口捕获，此时可手动输入并在系统中调整。详见 [依赖、排障与验证边界](docs/LINUX_CAPTURE.md)。

<a id="features"></a>
## 能做什么

- **从屏幕提取与翻译文字。**识别图片、视频字幕、软件界面和不可复制网页中的文字；内置 PaddleOCR 与 Argos Translate 提供中英离线基础能力。
- **按需要增强识别与翻译。**连接 llama.cpp、Ollama、LM Studio、vLLM 或其他兼容 Chat Completions 的服务；选择直接视觉 OCR、PaddleOCR 加视觉纠错或大模型翻译，效果与语言范围取决于模型。
- **理解没有文字的图片。**“描述图片”生成内容说明，“反推提示词”生成相似画面的参考描述；需要支持 `image_url` 的视觉模型，不会恢复原始提示词或生成参数。
- **保持桌面操作简洁。**统一主界面、独立分类设置、中英文切换、浅深色主题、托盘常驻和可复用结果卡；支持多显示器框选，具体桌面与混合 DPI 组合仍需实机验证。

<details>
<summary>查看早期工作流演示</summary>

<p align="center">
  <img src="docs/demo.gif" width="800" alt="屏译早期版本的截图、OCR 与翻译工作流演示">
</p>

这段演示用于说明操作流程，界面与快捷键来自早期版本，不代表当前布局或 Linux 默认键。当前用法以上文为准；界面回归截图可在 [CI 工件](https://github.com/qingshihuan/pingyi/actions/workflows/ci.yml) 中查看。

</details>

<a id="models"></a>
## 本机模型与云服务

**完全版管理本机模型：**进入 **设置 → 本地模型**，选择目录中的模型和后端，再点击“一键下载并配置”。下载支持断点续传与文件完整性校验。自动后端先尝试 Vulkan，失败时回退 CPU；显式选择 Vulkan 则报告错误而不自动切换。实际内存、显存需求与速度取决于模型和设备，以软件内目录及实测为准。

**已有本机服务：**标准版和完全版都可在 **设置 → 自定义接口** 中连接兼容端点，填写模型名称及服务要求的凭据。图片分析必须使用视觉模型并加载其视觉组件，仅支持文字的模型不能替代。完全版已应用的本机模型按需启动，不在打开主界面时预加载大模型。

**可选云端服务：**在 **设置 → 云端服务** 配置自己的 Google Cloud 或百度凭据，再在 **识别与翻译** 中选择提供商。OCR 与翻译提供商可分别选择，服务开通、配额和费用由用户自己的账户承担。自定义接口只允许本机回环地址使用 HTTP，非回环地址必须使用 HTTPS。

图片任务的模型要求、上传确认和输出限制见 [图片分析说明](docs/IMAGE_ANALYSIS.md)；资源回收及性能测量口径见 [性能说明](docs/PERFORMANCE.md)。

<a id="privacy"></a>
## 数据去向与隐私

| 处理方式 | 数据发送到哪里 |
| --- | --- |
| 默认本地 OCR 与 Argos 翻译 | 在本机处理，推理过程不需要联网 |
| 本机模型增强 | 将对应文字或所选图片交给配置的本机服务；外部服务的联网与留存行为由其自身配置决定 |
| 远程 OCR／远程图片分析 | 将所选截图发送给选定的服务 |
| 远程文字翻译 | 将识别后的文字发送给选定的服务，不等于上传整张截图 |

屏译默认不建立截图、识别正文、译文或图片分析的持久历史；日志不记录这些内容或密钥。凭据使用 Windows DPAPI 或 Linux Secret Service 处理，不写入普通 `settings.json`。

每次远程图片分析（含重试）发送前会单独确认接收端点和模型，文字翻译的上传许可不自动授权图片上传。模型下载、主动检查更新／开启自动更新检查、使用远程服务时会联网；自动更新检查默认关闭。

**系统与外部服务仍有各自的数据边界。**Wayland 门户可能创建截图文件，屏译只清理临时目录中的门户副本；系统保存到其他目录的文件不保证由屏译删除。远程服务和自行部署的模型服务有各自的日志与留存设置。

<a id="limits"></a>
## 兼容范围与限制

正式分发面向 **Windows 10/11 x64** 与 **Ubuntu 22.04+ x64**。X11 使用应用框选层，Wayland 使用系统截图门户并由桌面管理全局快捷键。macOS、ARM、其他 Linux 发行版不在当前声明的正式支持范围内。

内置基础模型面向简体中文与英文；更多语言、视觉识别及图片理解依赖所选服务或模型。OCR 与模型输出可能出错，需要核对。当前不提供实时覆盖翻译、PDF／图片批处理、表格／公式专项识别或持久历史记录。

CI 覆盖 Windows／Ubuntu 构建、单元与界面测试，并在隔离的 Xvfb／D-Bus 环境执行 Linux 原生检查。**自动化通过不代表所有真实 GNOME／Wayland、多屏、输入法或显卡配置均已验收。**

<a id="docs"></a>
## 使用与开发文档

[Linux 截图与快捷键](docs/LINUX_CAPTURE.md) · [图片分析](docs/IMAGE_ANALYSIS.md) · [质量基线](docs/QUALITY_BASELINE.md) · [性能与资源](docs/PERFORMANCE.md)

[界面设计与验收](docs/UI_WORKSPACE.md) · [可靠性说明](docs/RELIABILITY_OPTIMIZATION.md) · [版本记录](docs/releases) · [贡献指南](CONTRIBUTING.md) · [安全策略](SECURITY.md)

<a id="development"></a>
### 从源码运行

开发需要 **.NET 10 SDK**；修改或运行 Argos 独立翻译引擎时需要 **Python 3.13**。下面的命令在仓库根目录执行：

```sh
dotnet restore PingYi.slnx
dotnet build PingYi.slnx
dotnet run --project src/PingYi.App/PingYi.App.csproj
```

源码构建与正式安装包不同：基础模型和独立翻译引擎需要另外准备。`--settings` 可直接打开设置；`--capture` 支持首次启动截图或向已有实例转发命令。

<details>
<summary>展开引擎准备、测试与打包命令</summary>

Windows 使用 `scripts/setup-engine.ps1`，Linux 使用 `scripts/setup-engine.sh` 准备翻译引擎环境。本地 OCR 本身不依赖 Python，但需要可用的 OCR 模型。

```sh
dotnet test PingYi.slnx
python -m unittest discover -s engine_host -p "test_*.py"
python -m unittest discover -s scripts -p "test_*.py"
python scripts/download-offline-models.py --destination artifacts/model-source
```

Windows 中没有 `python` 命令时可使用已配置的 `py -3`。模型准备会联网；不要提交模型文件或本地配置。

Windows 打包示例（PowerShell，读取仓库版本标记）：

```powershell
$version = (Get-Content .github/release-version.txt -Raw).Trim()
.\scripts\setup-engine.ps1
.\scripts\publish.ps1 -Runtime win-x64 -Version $version `
  -OfflineModelSource artifacts/model-source -BuildInstaller
```

完全版还需准备固定版本的 llama.cpp CPU／Vulkan 运行时：

```powershell
python scripts/prepare-llama-runtime.py --runtime win-x64 --destination artifacts/llama-runtime/win-x64
.\scripts\publish.ps1 -Runtime win-x64 -Version $version -Edition Complete `
  -OfflineModelSource artifacts/model-source `
  -LlamaRuntimeSource artifacts/llama-runtime/win-x64 -BuildInstaller
```

Inno Setup 不在默认路径时传入 `-InnoCompiler`。离线质量检查使用 `scripts/run-quality-baseline.ps1 -ModelDirectory <已准备的离线模型目录>`；分发前须执行质量基线与许可证审计，完整跨平台流程以 [Release 工作流](.github/workflows/release.yml) 为准。

Windows 发布支持可选 `-SigningCertificateThumbprint` 和 `-TimestampUrl`。CI 可使用仓库机密 `PINGYI_SIGNING_CERTIFICATE_BASE64` 与 `PINGYI_SIGNING_CERTIFICATE_PASSWORD`；不要把证书或密码提交到仓库。

发布由 `v*` 标签，或 `main` 上 `.github/release-version.txt` 的版本变更触发，并读取对应的 `docs/releases/v<版本>.md`。工作流通过双平台测试、构建、全部 8 个附件核验后生成 Release 和校验和；已有标签指向其他提交时拒绝覆盖。普通 README 修改不更新版本或触发此发布入口。

</details>

## 参与贡献与许可

欢迎提交 Bug、OCR 失败场景、翻译反馈和 Pull Request。提交前请阅读 [贡献指南](CONTRIBUTING.md)；不要上传私人截图、正文或凭据，安全问题按 [安全策略](SECURITY.md) 报告。

屏译源码采用 **[MIT License](LICENSE)**。模型及第三方组件保留各自许可，详见 [第三方声明](THIRD_PARTY_NOTICES.md)；正式程序包的 `licenses/` 目录包含实际分发组件的许可证与清单。
