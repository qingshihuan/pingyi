# 截屏释义浏览器插件（Chrome / Edge）

**状态：2026-09-28 起暂时冻结开发。**保留当前 0.1.1 功能和桌面 Native Messaging／命名管道连接接口，不新增功能、不推进商店上架。桌面端新增二维码解析，不扩展浏览器协议。下面的构建和加载方式仍适用于已有版本。

本目录可直接“加载已解压的扩展程序”。需运行截屏释义 v0.6.0 或之后版本，并单独注册原生连接组件；v0.5.2 不包含桥接服务。正式桌面安装包不会自动安装插件或注册原生主机。

## 构建与连接

在项目根目录执行（.NET 10 SDK，注册脚本需要 Python 3）：

```powershell
dotnet build src/PingYi.App/PingYi.App.csproj -c Release
dotnet publish src/PingYi.App/PingYi.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/browser-desktop/win-x64
dotnet publish src/PingYi.BrowserHost/PingYi.BrowserHost.csproj -c Release -r win-x64 --self-contained true -o artifacts/browser-host/win-x64
py -3 scripts/register-browser-host.py --host artifacts/browser-host/win-x64/PingYi.BrowserHost.exe
dotnet run --project src/PingYi.App/PingYi.App.csproj -c Release --no-build
```

Windows 构建完成后，也可双击 `scripts/start-browser-desktop.cmd` 启动，传入 `complete` 可启动完全版数据配置。此开发入口会复用本仓库 `artifacts` 中已存在的离线模型、引擎和 llama 运行时，不下载或复制模型。若旧版 PingYi 已在托盘运行，请先手动退出旧版，再启动新版；本项目的单实例机制不会自动替换正在运行的程序。原有数据和设置保持不变。

插件压缩包：`py -3 scripts/package-browser-extension.py`，生成 `artifacts/ScreenInsight-Browser-0.1.1.zip`。连接组件仍需单独注册。

Ubuntu X11：将 `win-x64` 改为 `linux-x64`，`py -3` 改为 `python3`，可执行文件名去掉 `.exe`。Linux 主机文件需有执行权限。注册支持普通 Chrome、Chromium 与 Edge 用户配置目录；Snap / Flatpak 沙箱浏览器的外部原生主机发现不在本版支持范围内。

打开 `chrome://extensions` 或 `edge://extensions`，启用开发者模式，加载本目录。manifest 内的公开公钥固定开发版插件 ID；它不包含私钥或服务凭据。注册程序自动读取对应 ID，商店版 ID 不同时可传 `--extension-id <id>`。

连接桌面 v0.6.0 及更新版本时，插件面板选择 **完全版**。标准版选项仅保留给旧版桌面连接。完全版的数据目录和模型仍由截屏释义管理。在桌面设置保存后，下次插件请求读取新设置，无需重载插件。服务或模型变化会使旧远程授权失效。

取消注册：`py -3 scripts/register-browser-host.py --unregister`。从浏览器移除扩展即可移除插件偏好。移动主机文件后请重新注册。发布插件包时一并保留 LICENSE 与本 README；构建输出不提交到 Git。

## 功能

- 自动检测语言；支持与截屏释义相同的目标语言列表，具体能力取决于所选服务。
- 网页双语对照 / 仅译文 / 恢复原文；Alt+A 切换翻译与恢复。
- 开启悬停后按住 Alt 停留 650ms 翻译段落；划词浮标与右键翻译；面板文本翻译。
- Alt+S 或面板入口框选可见网页，按实际截图比例裁剪后复用 截屏释义 OCR 与翻译。
- 远程文字按页面及当前服务配置确认；远程 OCR 每次圈选单独确认。
- 只存偏好，不保存内容、图片、密钥、历史。模型调用遵循桌面设置，不悄悄切换到其他云端服务。错误仅返回固定代码，避免传播提供商可能含正文的异常信息。

## 边界

普通 HTTP / HTTPS 顶层网页支持。受限页面、内置 PDF 查看器、跨域 iframe 和 Shadow DOM 不进行正文改写；图像/画布可圈选。跳过密码、输入框、可编辑区域、代码和 `translate=no`。一次处理当前已加载正文的前 200 段，单段 12,000 字；超长或请求期间变化的段落会跳过并显示数量。动态新增内容须再次触发。恢复时保留网页自己在翻译后更新的文本，避免覆盖站点的新状态。

取消操作阻止后续段落请求、忽略迟到结果；已经提交给推理服务的单个请求可能继续到完成或超时。正文翻译可能影响复杂网站排版，请用“恢复原文”撤销。内嵌译文是页面 DOM 的一部分，可被网站读取。

Chrome 权限：`nativeMessaging` 连接桌面，`storage` 保存偏好，`activeTab` 临时截图权限，`scripting` 为安装前打开的页面补充内容脚本，`contextMenus` 提供划词菜单；内容脚本匹配普通网页以提供悬停/划词。没有远程 fetch、追踪器、远程字体或模型密钥。连接组件使用当前用户限定的命名管道，不监听网络端口。

原生消息协议遵循 [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)；截图使用 [captureVisibleTab](https://developer.chrome.com/docs/extensions/reference/api/tabs#method-captureVisibleTab)。

## 验证

```sh
dotnet test PingYi.slnx
python -m unittest discover -s engine_host -p "test_*.py"
node --test browser-extension/tests/shared.test.mjs
```

测试浏览器交互：`node scripts/test-browser-extension.cjs`（设置 `PINGYI_PLAYWRIGHT_MODULE` 指向已有 Playwright 包；如使用非默认 Chromium，可设置 `PINGYI_TEST_BROWSER`）。测试内容为合成文字，不使用用户网页或凭据。
