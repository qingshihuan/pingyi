# 浏览器插件开发版 · 实现与验证

**开发状态：2026-09-28 用户决定暂时冻结。**保留插件 0.1.1、原生主机及桌面连接协议和命名管道；不删除连接入口，不增加 TCP 监听端口，不继续扩展插件功能或上架。后续开发重点为桌面“截屏释义”。

日期：2026-09-28。基于 `8aedab2`，插件版本 `0.1.1`，不改变正式版发布版本号。

## 本机使用

1. 退出托盘中的旧版截屏释义，再双击 `scripts/start-browser-desktop.cmd`。该入口启动 `artifacts/browser-desktop/win-x64/PingYi.App.exe`；完全版运行 `scripts/start-browser-desktop.cmd complete`。
2. 当前机器已通过 `scripts/register-browser-host.py` 注册 `artifacts/browser-host/win-x64/PingYi.BrowserHost.exe`，供 Chrome / Edge / Chromium 当前用户使用。移动主机文件后需重新注册。
3. 在浏览器扩展管理页打开开发者模式，“加载已解压的扩展程序”，选择项目中的 `browser-extension` 目录。也可解压 `artifacts/ScreenInsight-Browser-0.1.1.zip` 后加载内层目录。
4. 打开插件，选择完全版（连接 v0.6.0 及更新桌面端），点击重新连接。提供商跟随桌面设置，模型和 API 凭据仍在截屏释义中管理。

构建与其他机器安装见 [插件 README](../browser-extension/README.md)。这里的连接组件已注册状态仅描述本次开发机器，不代表仓库其他用户已安装。未修改用户平常使用的浏览器配置，实际加载在隔离测试配置中完成。

## 2026-09-28 品牌定稿

软件名称为“截屏释义”（英文 Screen Insight）。用户确认沿用原图形，将原绿色改为蓝色，四角取景边框与右侧深色图形改为黑色；保留橙色和白色。桌面图标、窗口、托盘、安装展示名称及插件资源已统一。插件面板、帮助页、划词与翻译浮窗使用桌面浅色／深色蓝色主题，随系统外观切换。配置、模型目录、Native Messaging 名称和扩展 ID 沿用原值。详见 [品牌资源说明](../design-assets/BRANDING.md)。

本次名称与图标更新后重新通过 295 项 .NET 测试、31 项 Python 打包脚本测试、Edge DOM 回归及 4 项 Avalonia 合成预览测试；已检查主界面和插件面板的浅深色渲染，重新发布 Windows x64 桌面程序并生成插件 0.1.1 压缩包。安装程序与 Ubuntu 桌面展示名称尚未实机验收。

## 结构

- `browser-extension/`：Manifest V3、服务工作线程、弹出面板与网页脚本，无第三方前端依赖。
- `PingYi.BrowserHost`：标准输入/输出原生消息协议，转发至当前用户、指定标准版或完全版的命名管道。无网络监听。
- `BrowserBridgeServer`：受限帧长度、固定数量的连接工作线程、超时和固定错误码。不记录消息正文或异常详情。
- `BrowserTranslationService`：复用 `IOcrProvider` / `ITranslationProvider`，从 AppServices 读取当前设置。文字和图片授权分开，授权绑定服务、端点与模型；配置变化要求重新确认。
- `AppServices`：桌面启动时运行桥接，退出时先取消并回收桥接工作，再释放模型。设置保存与翻译请求互斥，避免请求期间改变服务；状态查询不会等待慢推理完成。

全页翻译序列处理当前加载的最多 200 段；取消后不再提交后续段落。结果渲染使用文本节点，保留已有 DOM 节点身份；恢复时不会覆盖网页自己更新的文本。输入框、密码、可编辑内容、代码、`translate=no` 不参与正文翻译。截图先在扩展工作线程内按截图像素/视口比例裁剪，只有选中部分交给桌面。

## 本次已验证

| 验证 | 结果 | 说明 |
| --- | --- | --- |
| `dotnet test PingYi.slnx` | 295 通过 | Core 232，App 63；包括 11 项新增桥接/路由用例 |
| Python 引擎测试 | 29 通过 | `unittest discover -s engine_host -p test_*.py` |
| Python 打包脚本测试 | 31 通过 | `unittest discover -s scripts -p test_*.py` |
| 插件基础测试 | 3 通过 | 裁剪缩放与边界、偏好字段白名单、语言代码归一化 |
| Edge 真实 DOM 回归 | 通过 | 合成提供商；双语、仅译文、恢复保留节点、可编辑/代码排除、取消后无迟到写入、Alt 悬停、划词浮标、框选几何、面板控件与无横向溢出 |
| Edge 实际原生消息连接 | 通过 | 正式插件 manifest、注册的原生可执行文件、实际 AppServices 已保存设置；无凭据输出 |
| 实际离线模型 | 通过 | 合成文字经插件 → 原生主机 → Argos 真实推理，并显示双语网页 |
| 实际截图 OCR | 通过 | 合成网页画布 → captureVisibleTab → 内存裁剪 → PaddleOCR → Argos → 结果浮窗 |
| Windows 发布构建 | 通过 | self-contained win-x64 桌面端及原生主机；输出位于忽略的 artifacts 下 |

实际截图测试在隔离测试副本中增加 `<all_urls>` 截图权限，以便无浏览器工具栏点击的自动化环境调用截图；交付插件没有该额外权限，使用工具栏或命令触发的 `activeTab` 授权。普通用户环境的工具栏/快捷键授权交互仍需要手动验收。OCR 与翻译结果可能不准确：合成图片中 `Hello world` 在本次识别为 `Helo world`，链路成功不等于模型质量验收。

合成测试截图：`artifacts/browser-extension/popup.png`、`popup-dark.png`、`live-page.png`、`live-popup.png`、`live-ocr.png`。所有截图均为专门生成的测试页面，无用户网页或历史内容。临时测试进程已退出。

## 未验证和当前边界

- 本次未在真实 Ubuntu X11 / Wayland 桌面或用户日常 Chrome 配置中执行插件验收；未实际调用付费云 API 或用户大模型。它们复用既有提供商代码，已有提供商单测和新增路由/远程授权单测通过，不代表各服务实测。
- 仅普通 HTTP / HTTPS 顶层网页；内置 PDF、受限浏览器页面、跨域 iframe 正文和 Shadow DOM 正文不改写。截图可以包含当前可见的图片、画布、iframe。
- 不提供文档批量翻译、Pro 订阅或登录；参考图只用于面板层级和交互参考。
- 动态新增正文需重新触发，复杂网页的布局或可交互内联文本需按网站验收；可恢复原文。
- 当前请求取消后可能继续完成推理，但结果不会写回已取消的网页操作。授权与文本缓存仅在内存中；页面内嵌译文对网站自身可见。
- 旧版 v0.5.2 不提供该桥接；桌面 v0.6.0 起保留连接能力。发布流程没有将冻结的插件和原生主机注册纳入 GitHub Release 桌面安装包，它们仍需单独构建、加载和注册。
