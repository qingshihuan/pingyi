using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using PingYi.Core;

namespace PingYi.App;

public static partial class UiText
{
    public const string Auto = "auto";
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly ConditionalWeakTable<AvaloniaObject, object> AttachedObjects = new();
    private static readonly object AttachedMarker = new();
    private static bool _isApplying;

    private static readonly IReadOnlyDictionary<string, string> EnglishResources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["String.AppName"] = "PingYi",
            ["String.AppTagline"] = "Capture, recognize, and translate in one step",
            ["String.StartCapture"] = "Start capture",
            ["String.ResultTitle"] = "Recognition results",
            ["String.CopySource"] = "Copy source",
            ["String.CopyTranslation"] = "Copy translation",
            ["String.CopyAll"] = "Copy all",
            ["String.Retry"] = "Process again",
            ["String.OpenSettings"] = "Open settings",
            ["String.Close"] = "Close"
        };

    private static readonly IReadOnlyDictionary<string, string> EnglishText =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["屏译"] = "PingYi",
            ["屏译设置"] = "PingYi Settings",
            ["屏译 完全版"] = "PingYi Complete",
            ["屏译 完全版设置"] = "PingYi Complete Settings",
            ["屏译结果"] = "PingYi Results",
            ["设置"] = "Settings",
            ["打开屏译设置"] = "Open PingYi settings",
            ["处理、模型、服务、快捷键与外观"] = "Processing, models, services, shortcuts, and appearance",
            ["截图、识别、翻译，一次完成"] = "Capture, recognize, and translate in one step",
            ["开始截图"] = "Start capture",
            ["开始截图翻译"] = "Capture and translate",
            ["开始截图识别和翻译"] = "Capture, recognize, and translate",
            ["框选一次，读懂屏幕"] = "Select once. Understand your screen.",
            ["框选一下，立即读懂屏幕文字"] = "Select an area and read screen text instantly",
            ["从屏幕画面到可复制原文，再到自然译文。默认本地处理，不保存历史。"] = "Turn screen content into copyable text and natural translations. Local by default, with no history.",
            ["截图仅在内存中处理。识别完成后可复制原文、译文，或固定结果卡继续对照。"] = "Screenshots are processed in memory only. Copy the source or translation, or pin the result for reference.",
            ["本地优先 · 默认不留历史"] = "Local first · No history by default",
            ["本地优先时，截图与文字不会离开设备"] = "In local-first mode, screenshots and text never leave this device",
            ["全局快捷键"] = "Global shortcut",
            ["全局截图快捷键"] = "Global capture shortcut",
            ["处理引擎"] = "Processing engines",
            ["本地与云端服务可以分别组合"] = "Mix local and cloud OCR and translation providers",
            ["检查状态"] = "Check status",
            ["检查处理引擎状态"] = "Check processing engine status",
            ["文字识别"] = "Text recognition",
            ["文字识别提供商"] = "OCR provider",
            ["处理所选区域图片"] = "Process the selected image area",
            ["翻译方式"] = "Translation",
            ["翻译提供商"] = "Translation provider",
            ["处理识别后的文字"] = "Process recognized text",
            ["目标语言"] = "Target language",
            ["翻译目标语言"] = "Translation target language",
            ["选择云端翻译时，只发送识别后的纯文字。"] = "Cloud translation sends recognized text only.",
            ["多模态 OCR 会发送本次框选图片；127.0.0.1 服务不会离开设备。"] = "Multimodal OCR sends the selected image; loopback services at 127.0.0.1 stay on this device.",
            ["本地模型"] = "Local models",
            ["标准版随安装包提供离线基础模型；每次加载都会校验完整性。"] = "The Standard edition includes baseline offline models and verifies them whenever they load.",
            ["下载中英 OCR 模型"] = "Download Chinese/English OCR model",
            ["下载中英文字识别模型"] = "Download Chinese/English OCR model",
            ["下载中英翻译模型"] = "Download Chinese/English translation model",
            ["下载中英离线翻译模型"] = "Download offline Chinese/English translation model",
            ["清理下载模型"] = "Remove downloaded models",
            ["清理用户下载的翻译模型"] = "Remove user-downloaded translation models",
            ["截图、正文与密钥不会写入日志"] = "Screenshots, text, and credentials are never written to logs",
            ["云端与自定义服务"] = "Cloud and custom services",
            ["密钥交由系统安全存储，不写入 settings.json。"] = "Credentials use secure system storage and are never written to settings.json.",
            ["完全版 · 一键本机多模态模型"] = "Complete edition · One-click local multimodal model",
            ["魔搭模型库"] = "ModelScope model library",
            ["可选 CPU / 通用显卡"] = "Choose CPU or a general-purpose GPU",
            ["运行后端"] = "Runtime backend",
            ["选择本机模型使用 CPU 或通用显卡"] = "Choose CPU or GPU for the local model",
            ["选择一键安装的本机多模态模型"] = "Choose a local multimodal model to install",
            ["正在检查完整模型包…"] = "Checking the complete model package…",
            ["查看魔搭来源"] = "View on ModelScope",
            ["打开模型目录"] = "Open model folder",
            ["打开本机模型目录"] = "Open local model folder",
            ["打开所选模型的魔搭页面"] = "Open the selected model on ModelScope",
            ["取消"] = "Cancel",
            ["取消模型下载或启动"] = "Cancel model download or startup",
            ["启动并应用"] = "Start and apply",
            ["启动并应用已下载的本机多模态模型"] = "Start and apply the downloaded local multimodal model",
            ["一键下载并配置"] = "Download and configure",
            ["从魔搭下载并自动配置本机多模态模型"] = "Download and configure a local multimodal model from ModelScope",
            ["只下载当前选择的 GGUF 与视觉投影文件；下载支持断点续传并强制校验 SHA-256。模型在本机端口 18080 运行，截图和文字不会离开设备。"] = "Downloads only the selected GGUF and vision projector files, with resume support and mandatory SHA-256 verification. The model runs locally on port 18080; screenshots and text stay on this device.",
            ["百度 OCR 与翻译凭据"] = "Baidu OCR and Translation credentials",
            ["百度 OCR API Key"] = "Baidu OCR API Key",
            ["百度 OCR Secret Key"] = "Baidu OCR Secret Key",
            ["翻译 APP ID"] = "Translation APP ID",
            ["百度翻译 APP ID"] = "Baidu Translation APP ID",
            ["翻译密钥"] = "Translation secret",
            ["百度翻译密钥"] = "Baidu Translation secret",
            ["尚未保存"] = "Not saved",
            ["显示"] = "Show",
            ["隐藏"] = "Hide",
            ["显示或隐藏"] = "Show or hide",
            ["显示 OCR API Key"] = "Show OCR API Key",
            ["显示 OCR Secret Key"] = "Show OCR Secret Key",
            ["显示翻译 APP ID"] = "Show translation APP ID",
            ["显示翻译密钥"] = "Show translation secret",
            ["点击“显示”后可直接编辑，并可在输入框中使用 Ctrl+C / Ctrl+V。"] = "Select Show to edit the value. Ctrl+C and Ctrl+V work inside the field.",
            ["保存并验证凭据"] = "Save and verify credentials",
            ["保存并验证百度凭据"] = "Save and verify Baidu credentials",
            ["Google Cloud OCR 与翻译凭据"] = "Google Cloud OCR and Translation credentials",
            ["显示 Google Cloud API Key"] = "Show Google Cloud API Key",
            ["保存并验证 Google 凭据"] = "Save and verify Google credentials",
            ["保存并验证 Google Cloud 凭据"] = "Save and verify Google Cloud credentials",
            ["该密钥由你自己的 Google Cloud 项目提供；建议只允许调用 Cloud Vision API 与 Cloud Translation API。截图仅在选择 Google OCR 时上传，翻译只上传识别后的文字。"] = "Use an API key from your own Google Cloud project and restrict it to Cloud Vision and Cloud Translation. Images are uploaded only when Google OCR is selected; translation uploads recognized text only.",
            ["本机 / 自定义大模型接口"] = "Local / custom LLM endpoint",
            ["服务预设"] = "Service preset",
            ["本机大模型服务预设"] = "Local LLM service preset",
            ["服务地址"] = "Endpoint",
            ["兼容大模型服务地址"] = "Compatible LLM endpoint",
            ["模型名"] = "Model name",
            ["例如 gemma-4-e4b-it"] = "e.g. gemma-4-e4b-it",
            ["兼容大模型模型名"] = "Compatible LLM model name",
            ["本地无鉴权服务可留空"] = "Leave blank for unauthenticated local services",
            ["兼容翻译接口 API Key"] = "Compatible endpoint API Key",
            ["显示兼容接口 API Key"] = "Show compatible endpoint API Key",
            ["显示后可直接编辑，并可在输入框中使用 Ctrl+C / Ctrl+V。"] = "After selecting Show, edit the value directly or use Ctrl+C / Ctrl+V in the field.",
            ["支持文本翻译与 image_url 多模态识别；图片测试只发送固定合成样本。"] = "Supports text translation and image_url multimodal OCR. Image tests send a fixed synthetic sample only.",
            ["应用服务预设"] = "Apply service preset",
            ["应用本机大模型服务预设"] = "Apply local LLM service preset",
            ["测试翻译"] = "Test translation",
            ["测试本地或自定义翻译接口"] = "Test local or custom translation endpoint",
            ["测试图片"] = "Test image",
            ["测试本地或自定义多模态图片接口"] = "Test local or custom multimodal image endpoint",
            ["外观、快捷键与启动"] = "Appearance, shortcut, and startup",
            ["主界面样式"] = "Main interface style",
            ["切换后重新启动屏译生效；经典完整界面会继续保留。"] = "Restart PingYi after changing this option. The full classic interface remains available.",
            ["保存后立即重新注册；冲突时会在当前窗口提示。"] = "The shortcut is re-registered when saved. Conflicts appear in this window.",
            ["界面语言"] = "Interface language",
            ["选择界面语言"] = "Choose interface language",
            ["跟随系统会在中文系统使用简体中文，其他系统使用 English；重新启动后生效。"] = "Follow system uses Simplified Chinese on Chinese systems and English elsewhere. Restart to apply.",
            ["启动后最小化到托盘"] = "Start minimized to tray",
            ["检查并提示可用的新版本"] = "Check for updates",
            ["启动时检查更新（会访问 GitHub）"] = "Check for updates at startup (contacts GitHub)",
            ["立即检查更新"] = "Check now",
            ["立即检查 GitHub 更新"] = "Check GitHub for updates now",
            ["打开下载页"] = "Open download page",
            ["在 GitHub 打开最新版本"] = "Open the latest release on GitHub",
            ["OpenAI 兼容接口地址无效。"] = "The OpenAI-compatible endpoint is invalid.",
            ["远程自定义服务必须使用 HTTPS；只有本机回环地址可以使用 HTTP。"] = "Remote custom services must use HTTPS; only loopback endpoints may use HTTP.",
            ["检查应用更新"] = "Check for application updates",
            ["经典完整界面"] = "Full classic interface",
            ["保留旧版完整页面，便于对照或恢复熟悉的工作方式。"] = "Keep the original full settings page for comparison and familiar workflows.",
            ["打开经典界面"] = "Open classic interface",
            ["打开经典完整界面"] = "Open full classic interface",
            ["保存并应用"] = "Save and apply",
            ["保存所有设置"] = "Save all settings",
            ["正在初始化…"] = "Initializing…",
            ["当前方案"] = "Current mode",
            ["常用模式可快速切换"] = "Switch quickly between common modes",
            ["快速切换"] = "Quick switch",
            ["快速切换处理方案"] = "Quickly switch processing mode",
            ["本地优先 · PaddleOCR + Argos"] = "Local first · PaddleOCR + Argos",
            ["本机大模型翻译 · PaddleOCR + 兼容接口"] = "Local LLM translation · PaddleOCR + compatible endpoint",
            ["本机多模态 · PaddleOCR + 大模型纠错"] = "Local multimodal · PaddleOCR + LLM correction",
            ["云端增强 · 百度 OCR + 百度翻译"] = "Cloud enhanced · Baidu OCR + Baidu Translation",
            ["自定义组合…"] = "Custom combination…",
            ["本地优先"] = "Local first",
            ["本地 PaddleOCR"] = "Local PaddleOCR",
            ["本地 Argos"] = "Local Argos",
            ["启动时校验离线模型"] = "Verify offline models at startup",
            ["隐私边界"] = "Privacy boundary",
            ["正在读取当前方案的数据范围"] = "Reading the current mode's data boundary",
            ["当前方案需要处理"] = "This mode needs attention",
            ["请检查模型或服务配置。"] = "Check the model or service configuration.",
            ["立即修复"] = "Fix now",
            ["打开设置修复问题"] = "Open settings to fix the issue",
            ["准备就绪"] = "Ready",
            ["按 Ctrl+Alt+D 或点击“开始截图”。"] = "Press Ctrl+Alt+D or select Start capture.",
            ["正在检查"] = "Checking",
            ["运行正常"] = "Running normally",
            ["需要处理"] = "Needs attention",
            ["操作未完成"] = "Operation incomplete",
            ["拖动框选文字区域"] = "Drag to select a text area",
            ["Esc 取消"] = "Esc to cancel",
            ["正在识别…"] = "Recognizing…",
            ["固定"] = "Pin",
            ["已固定"] = "Pinned",
            ["固定结果卡"] = "Pin result card",
            ["关闭"] = "Close",
            ["关闭结果卡"] = "Close result card",
            ["正在处理截图内容"] = "Processing captured content",
            ["原文"] = "Source",
            ["识别到的原文"] = "Recognized source text",
            ["译文"] = "Translation",
            ["翻译结果"] = "Translation result",
            ["复制"] = "Copy",
            ["复制原文"] = "Copy source",
            ["复制译文"] = "Copy translation",
            ["复制原文和译文"] = "Copy source and translation",
            ["内容仅在内存中处理"] = "Content is processed in memory only",
            ["重新识别和翻译"] = "Recognize and translate again",
            ["OCR 模型：正在检查…"] = "OCR model: checking…",
            ["翻译模型：正在检查…"] = "Translation model: checking…",
            ["OCR 凭据：正在检查安全存储…"] = "OCR credentials: checking secure storage…",
            ["翻译凭据：正在检查安全存储…"] = "Translation credentials: checking secure storage…",
            ["Google OCR 凭据：正在检查安全存储…"] = "Google OCR credential: checking secure storage…",
            ["Google 翻译凭据：正在检查安全存储…"] = "Google Translation credential: checking secure storage…",
            ["自动（推荐）"] = "Automatic (recommended)",
            ["自动翻译"] = "Automatic translation",
            ["新版精简主界面"] = "Modern compact interface",
            ["云端增强"] = "Cloud enhanced",
            ["本机大模型"] = "Local LLM",
            ["本机多模态"] = "Local multimodal",
            ["自定义服务"] = "Custom service",
            ["自定义组合"] = "Custom combination",
            ["可用"] = "Available",
            ["不可用"] = "Unavailable",
            ["正在检查…"] = "Checking…",
            ["正在测试…"] = "Testing…",
            ["正在验证…"] = "Verifying…",
            ["正在保存…"] = "Saving…",
            ["正在下载…"] = "Downloading…",
            ["正在启动…"] = "Starting…",
            ["验证通过"] = "Verified",
            ["验证未通过"] = "Verification failed",
            ["验证失败"] = "Verification failed",
            ["连接成功"] = "Connected",
            ["连接失败"] = "Connection failed",
            ["图片可用"] = "Image test passed",
            ["图片不可用"] = "Image test failed",
            ["下载完成"] = "Download complete",
            ["下载失败"] = "Download failed",
            ["启动失败"] = "Startup failed",
            ["启动已取消。"] = "Startup canceled.",
            ["已启动并应用"] = "Started and applied",
            ["已安装并应用"] = "Installed and applied",
            ["已取消"] = "Canceled",
            ["已取消，可继续"] = "Canceled; can resume",
            ["保存失败"] = "Save failed",
            ["已保存并应用"] = "Saved and applied",
            ["检查失败"] = "Check failed",
            ["检查未通过"] = "Check failed",
            ["状态正常"] = "All systems ready",
            ["预设已应用"] = "Preset applied",
            ["应用失败"] = "Apply failed",
            ["已应用，连接失败"] = "Applied; connection failed",
            ["截图翻译"] = "Capture and translate",
            ["退出"] = "Exit",
            ["快捷键已启用"] = "Global shortcut enabled",
            ["离线基础功能就绪"] = "Offline baseline ready",
            ["基础模型需要处理"] = "Baseline models need attention",
            ["请先填写百度 OCR API Key 和 Secret Key。"] = "Configure the Baidu OCR API Key and Secret Key first.",
            ["请先填写百度翻译 APP ID 和密钥。"] = "Configure the Baidu Translation APP ID and secret first.",
            ["请填写 OpenAI 兼容接口地址和模型名。"] = "Enter an OpenAI-compatible endpoint and model name.",
            ["连接本地大模型超时；请确认所选服务已启动。"] = "The local model connection timed out. Confirm that the selected service is running.",
            ["无法连接本地大模型；请确认 llama.cpp、Ollama、LM Studio 或兼容服务已启动，并检查端口。"] = "Could not connect to the local model. Start llama.cpp, Ollama, LM Studio, or the compatible service and check its port.",
            ["本地模型列表响应格式不正确；请确认服务兼容 OpenAI API。"] = "The local model list has an unsupported format. Confirm that the service implements the OpenAI API.",
            ["尚未安装 Argos Translate 引擎。"] = "The Argos Translate engine is not installed.",
            ["尚未安装中英离线翻译模型，请在设置中下载。"] = "The offline Chinese-English translation model is missing. Download it in Settings.",
            ["中英 OCR 离线模型完整性校验失败，请重新安装标准离线版。"] = "The offline OCR model failed integrity verification. Reinstall the Standard offline edition.",
            ["安装包缺少中英 OCR 离线模型，请重新安装标准离线版。"] = "The installer is missing the offline OCR model. Reinstall the Standard offline edition.",
            ["离线 OCR 模型未包含在安装包中，请重新安装标准离线版或导入离线模型包。"] = "The offline OCR model is not bundled. Reinstall the Standard offline edition or import an offline model package.",
            ["正在校验基础模型"] = "Verifying baseline models",
            ["确认无网络环境下仍可执行 OCR 与翻译"] = "Confirming OCR and translation work without a network connection",
            ["PaddleOCR 与中英离线翻译均已校验"] = "PaddleOCR and offline Chinese/English translation are verified",
            ["离线基础模型不可用。"] = "Offline baseline models are unavailable.",
            ["截图服务尚未初始化。"] = "Screen capture is not initialized.",
            ["设置服务尚未初始化。"] = "Settings are not initialized.",
            ["已有截图任务正在进行。"] = "A capture task is already in progress.",
            ["所选区域中没有识别到文字。"] = "No text was found in the selected area.",
            ["未找到可用于框选的显示器。"] = "No display is available for area selection.",
            ["全程本地处理 · 内容不离开设备"] = "Fully local · Content stays on this device",
            ["图片与文字发送到本机大模型服务 · 内容不离开设备"] = "Image and text are sent to a local LLM service · Content stays on this device",
            ["全程在本机处理；截图、原文和译文不会离开设备。"] = "Everything is processed locally; screenshots, source text, and translations stay on this device.",
            ["图片与文字只发送到本机大模型服务，不会离开设备。"] = "Images and text are sent only to the local LLM service and never leave this device.",
            ["翻译暂不可用，可复制上方原文或重新处理。"] = "Translation is temporarily unavailable. Copy the source text above or try again.",
            ["原文已复制"] = "Source copied",
            ["译文已复制"] = "Translation copied",
            ["原文与译文已复制"] = "Source and translation copied",
            ["确认清理下载模型"] = "Confirm model cleanup",
            ["删除操作已取消。"] = "Deletion canceled.",
            ["用户下载模型已清理，安装包内离线基础模型仍可使用。"] = "Downloaded models were removed. Bundled offline baseline models remain available.",
            ["再次点击可清理用户下载的翻译模型；安装包内离线基础模型、凭据和设置都会保留。"] = "Select again to remove downloaded translation models. Bundled models, credentials, and settings will be kept.",
            ["敏感凭据已写入系统安全存储。"] = "Credentials were saved to secure system storage.",
            ["Linux 密钥服务不可用，凭据仅保存到本次运行结束。"] = "Linux Secret Service is unavailable. Credentials will be kept only until the app exits.",
            ["Google Cloud Vision 与 Translation 均验证通过。"] = "Google Cloud Vision and Translation were verified.",
            ["部分 Google Cloud API 验证失败，请确认项目已启用对应 API 并检查密钥限制。"] = "Some Google Cloud API checks failed. Enable the required APIs and review the API key restrictions.",
            ["尚未填写 Google Cloud API Key。"] = "Google Cloud API Key has not been entered.",
            ["Google OCR 凭据：尚未配置"] = "Google OCR credential: not configured",
            ["Google 翻译凭据：尚未配置"] = "Google Translation credential: not configured",
            ["Google OCR 凭据：未配置"] = "Google OCR credential: not configured",
            ["Google 翻译凭据：未配置"] = "Google Translation credential: not configured",
            ["Google OCR 凭据：验证通过"] = "Google OCR credential: verified",
            ["Google 翻译凭据：验证通过"] = "Google Translation credential: verified",
            ["Google OCR 凭据：已安全保存"] = "Google OCR credential: securely saved",
            ["Google 翻译凭据：已安全保存"] = "Google Translation credential: securely saved",
            ["Google OCR 凭据状态未知"] = "Google OCR credential status is unknown",
            ["Google 翻译凭据状态未知"] = "Google Translation credential status is unknown",
            ["正在安全保存并验证 Google Cloud 凭据…"] = "Securely saving and verifying Google Cloud credentials…",
            ["正在安全保存并验证百度凭据…"] = "Securely saving and verifying Baidu credentials…",
            ["正在保存设置与安全凭据…"] = "Saving settings and secure credentials…",
            ["自动识别原文；外语译成简体中文，中文译成英文。列表仅显示该引擎已适配的目标语言。"] = "Source language is detected automatically. Non-Chinese text is translated to Simplified Chinese; Chinese is translated to English. The list shows targets supported by this provider.",
            ["自动识别原文；外语译成简体中文，中文译成英文。也可手动指定目标语言；实际效果取决于所选模型。"] = "Source language is detected automatically. Non-Chinese text is translated to Simplified Chinese; Chinese is translated to English. You can also choose a target manually; quality depends on the selected model.",
            ["正在检查引擎…"] = "Checking engines…",
            ["正在下载并配置…"] = "Downloading and configuring…",
            ["正在校验已下载模型…"] = "Verifying downloaded model…",
            ["正在应用并测试…"] = "Applying and testing…",
            ["正在连接并发送固定测试文本…"] = "Connecting and sending fixed test text…",
            ["正在发送固定合成图片测试多模态能力…"] = "Sending a fixed synthetic image to test multimodal support…",
            ["OCR 模型：已安装，可离线使用"] = "OCR model: installed and available offline",
            ["翻译模型：已安装，可离线使用"] = "Translation model: installed and available offline",
            ["OCR 模型：正在下载并校验…"] = "OCR model: downloading and verifying…",
            ["翻译模型：正在下载并校验…"] = "Translation model: downloading and verifying…",
            ["中英 OCR 模型已安装"] = "Chinese/English OCR model installed",
            ["中英翻译模型已安装"] = "Chinese/English translation model installed",
            ["中英离线 OCR 模型安装完成并已校验。"] = "Offline Chinese/English OCR model installed and verified.",
            ["中英离线翻译模型安装完成并已校验。"] = "Offline Chinese/English translation model installed and verified.",
            ["正在下载中英离线 OCR 模型，请勿退出…"] = "Downloading the offline Chinese/English OCR model. Keep PingYi open…",
            ["正在下载中英离线翻译模型，请勿退出…"] = "Downloading the offline Chinese/English translation model. Keep PingYi open…",
            ["操作已取消；已下载部分会保留，下次可断点续传。"] = "Canceled. Downloaded data was kept and can be resumed later.",
            ["模型操作已取消，可稍后继续。"] = "Model operation canceled; you can continue later.",
            ["下载或配置失败"] = "Download or configuration failed",
            ["完全版 llama.cpp 运行时缺失，请重新安装完全版。"] = "The Complete edition llama.cpp runtime is missing. Reinstall PingYi Complete.",
            ["本地 / 自定义大模型翻译已可用。"] = "Local / custom LLM translation is available.",
            ["本机 / 自定义大模型图片识别已可用。"] = "Local / custom multimodal image recognition is available.",
            ["多模态模型服务不可用。"] = "The multimodal model service is unavailable.",
            ["大模型服务不可用。"] = "The LLM service is unavailable.",
            ["服务已连接，但没有返回测试译文。"] = "The service connected but returned no test translation.",
            ["服务可以接收图片，但未正确读出固定测试文字；请确认模型支持视觉并已加载 mmproj。"] = "The service accepted the image but did not read the fixed test text. Confirm that the model supports vision and its mmproj is loaded.",
            ["连接、模型名与翻译请求均验证通过"] = "Connection, model name, and translation request verified",
            ["连接、模型名与多模态图片识别均验证通过"] = "Connection, model name, and multimodal image recognition verified",
            ["部分百度凭据验证失败，请查看字段下方提示。"] = "Some Baidu credential checks failed. Review the inline messages.",
            ["已配置的百度凭据均验证通过。"] = "All configured Baidu credentials were verified.",
            ["尚未填写完整的百度 OCR 或翻译凭据。"] = "Baidu OCR or translation credentials are incomplete.",
            ["已启用自动翻译：外语译成简体中文，中文译成英文。"] = "Automatic translation enabled: non-Chinese text to Simplified Chinese, and Chinese to English.",
            ["当前为经典界面"] = "Currently using the classic interface",
            ["模型与视觉组件均已通过 SHA-256 校验"] = "The model and vision projector passed SHA-256 verification",
            ["发现未完成下载，可继续断点续传"] = "An incomplete download was found and can be resumed",
            ["尚未下载"] = "Not downloaded",
            ["模型下载完成并通过 SHA-256 校验"] = "The model download completed and passed SHA-256 verification",
            ["本机模型服务已经运行"] = "The local model service is already running",
            ["已连接正在运行的本机模型服务；其计算后端由该服务决定"] = "Connected to an existing local model service; that service controls its compute backend",
            ["模型已通过 Vulkan 显卡后端启动"] = "The model started with the Vulkan GPU backend",
            ["模型已通过 CPU 后端启动"] = "The model started with the CPU backend",
            ["显卡后端不可用，已自动回退 CPU 并启动"] = "The GPU backend was unavailable, so the model started with the CPU fallback",
            ["Vulkan 启动失败，正在自动回退 CPU…"] = "Vulkan startup failed; falling back to CPU…",
            ["Vulkan 通用显卡"] = "Vulkan GPU",
            ["正在验证图片识别与翻译…"] = "Validating image recognition and translation…"
        };

    public static string CurrentLanguage { get; private set; } = Resolve(Auto);
    public static bool IsEnglish => CurrentLanguage == English;

    public static void Configure(string? language)
    {
        CurrentLanguage = Resolve(language);
        var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (IsEnglish && Application.Current?.Resources is { } resources)
        {
            foreach (var (key, value) in EnglishResources)
            {
                resources[key] = value;
            }
        }
    }

    public static string Resolve(string? language)
    {
        if (language is Chinese or English)
        {
            return language;
        }

        return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "zh" ? Chinese : English;
    }

    public static string T(string? text)
    {
        if (!IsEnglish || string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        if (EnglishText.TryGetValue(text, out var translated))
        {
            return translated;
        }

        return TranslateDynamic(text);
    }

    public static string Error(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!IsEnglish)
        {
            return exception.Message;
        }

        if (exception is ProviderException providerException)
        {
            if (providerException.Code.EndsWith("_timeout", StringComparison.OrdinalIgnoreCase))
            {
                return "The operation timed out. Try a smaller capture area, then retry.";
            }

            if (providerException.Code.Equals("RuntimeError", StringComparison.OrdinalIgnoreCase))
            {
                if (providerException.Message.Contains("不支持", StringComparison.Ordinal))
                {
                    return "The offline translator does not support the selected language pair. Choose a multilingual local model or cloud provider.";
                }

                if (providerException.Message.Contains("尚未安装", StringComparison.Ordinal) ||
                    providerException.Message.Contains("找不到", StringComparison.Ordinal))
                {
                    return "The offline translation model is missing. Open Settings → Local models to repair it.";
                }
            }

            return providerException.Code switch
            {
                "no_text" => "No text was found in the selected area. Select a clearer or larger area and retry.",
                "ocr_models_missing" => "The offline OCR model is unavailable. Open Settings → Local models to repair it.",
                "credentials_missing" => "Credentials are missing. Open Settings and add credentials for the selected provider.",
                "credentials_invalid" => "The saved credentials were rejected. Check them in Settings and retry.",
                "custom_endpoint_invalid" or "vlm_ocr_endpoint_invalid" =>
                    "The OpenAI-compatible endpoint or model name is invalid.",
                "custom_endpoint_insecure_transport" or "vlm_ocr_insecure_transport" =>
                    "Remote custom services must use HTTPS; only loopback endpoints may use HTTP.",
                "managed_runtime_unavailable" =>
                    "The local model could not start. Open Settings → Local multimodal model to repair it.",
                "ocr_unavailable" => "The selected OCR provider is unavailable. Open Settings to repair or switch it.",
                "translation_unsupported" or "translation_fallback_language_unsupported" =>
                    "The offline translator does not support this language pair. Choose a multilingual local model or a cloud provider.",
                "translation_unavailable" or "translation_fallback_unavailable" =>
                    "The selected translator is unavailable. Open Settings to repair or switch it.",
                "translation_primary_and_fallback_failed" =>
                    "Both the selected translator and offline fallback failed. Check the provider and local models in Settings.",
                "custom_translate_http" =>
                    "The compatible translation service rejected the request. Check its endpoint, model, and API key.",
                "custom_translate_schema" =>
                    "The compatible service returned an unsupported response format.",
                "vlm_ocr_image_invalid" => "The captured image is empty. Capture the area again.",
                "vlm_ocr_http" =>
                    "The multimodal service rejected the image request. Confirm that the vision projector is loaded.",
                "vlm_ocr_schema" or "vlm_ocr_empty" =>
                    "The multimodal service did not return usable OCR text. Check model vision support and retry.",
                "engine_timeout" => "The offline translation engine timed out. Retry or repair the local model in Settings.",
                "baidu_ocr_http" or "baidu_ocr_api" or "baidu_translate_api" =>
                    "Baidu Cloud rejected the request. Check the credentials, quota, and network connection.",
                "google_vision_http" or "google_vision_api" or "google_translate_http" or "google_translate_api" =>
                    "Google Cloud rejected the request. Check the API key restrictions, enabled APIs, quota, and network.",
                _ => T(providerException.Message)
            };
        }

        if (exception is HttpRequestException)
        {
            return "The service could not be reached. Check the network or local model endpoint, then retry.";
        }

        if (exception is OperationCanceledException)
        {
            return "The operation was canceled.";
        }

        if (exception.Message.Contains("模型文件尚未完整下载", StringComparison.Ordinal) ||
            exception.Message.Contains("llama.cpp", StringComparison.OrdinalIgnoreCase))
        {
            return "The local model is incomplete or could not start. Open Settings → Local multimodal model to repair it.";
        }

        return T(exception.Message);
    }

    public static string ProviderName(string id, string fallback) => id switch
    {
        "local-paddle" => IsEnglish ? "Local PaddleOCR" : "本地 PaddleOCR",
        "local-vlm-corrected" => IsEnglish ? "PaddleOCR + local LLM correction" : "PaddleOCR + 本机大模型纠错",
        "local-vlm-ocr" => IsEnglish ? "Local multimodal LLM OCR" : "本机多模态大模型 OCR",
        "local-argos" => IsEnglish ? "Local Argos" : "本地 Argos",
        "baidu-ocr" => IsEnglish ? "Baidu Cloud OCR" : "百度云 OCR",
        "baidu-translate" => IsEnglish ? "Baidu Translation" : "百度翻译",
        "google-vision-ocr" => "Google Cloud Vision OCR",
        "google-translate" => "Google Cloud Translation",
        "custom-chat" => IsEnglish ? "Local / custom LLM" : "本地 / 自定义大模型",
        _ => T(fallback)
    };

    public static string LanguageName(string code) => code == LanguageCatalog.AutoOpposite
        ? T("自动翻译")
        : LanguageCatalog.All.FirstOrDefault(language =>
            string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)) is { } language
            ? IsEnglish ? language.EnglishDisplayName : language.DisplayName
            : code;

    public static void Attach(Control root)
    {
        ApplyAndAttach(root);
        foreach (var child in root.GetLogicalDescendants().OfType<Control>())
        {
            ApplyAndAttach(child);
        }
    }

    private static void ApplyAndAttach(Control control)
    {
        LocalizeControl(control);
        if (AttachedObjects.TryGetValue(control, out _))
        {
            return;
        }

        AttachedObjects.Add(control, AttachedMarker);
        control.PropertyChanged += (_, eventArgs) =>
        {
            if (!IsEnglish || _isApplying)
            {
                return;
            }

            if (eventArgs.Property == TextBlock.TextProperty ||
                eventArgs.Property == ContentControl.ContentProperty ||
                eventArgs.Property == Expander.HeaderProperty ||
                eventArgs.Property == MenuItem.HeaderProperty ||
                eventArgs.Property == TextBox.PlaceholderTextProperty ||
                eventArgs.Property == Window.TitleProperty ||
                eventArgs.Property == AutomationProperties.NameProperty ||
                eventArgs.Property == ToolTip.TipProperty)
            {
                LocalizeControl(control);
            }
        };
    }

    private static void LocalizeControl(Control control)
    {
        if (!IsEnglish)
        {
            return;
        }

        _isApplying = true;
        try
        {
            if (control is Window window) window.Title = T(window.Title);
            if (control is TextBlock textBlock) textBlock.Text = T(textBlock.Text);
            if (control is ContentControl contentControl && contentControl.Content is string content)
            {
                contentControl.Content = T(content);
            }
            if (control is Expander expander && expander.Header is string header)
            {
                expander.Header = T(header);
            }
            if (control is MenuItem menuItem && menuItem.Header is string menuHeader)
            {
                menuItem.Header = T(menuHeader);
            }
            if (control is TextBox textBox) textBox.PlaceholderText = T(textBox.PlaceholderText);

            var automationName = AutomationProperties.GetName(control);
            if (!string.IsNullOrWhiteSpace(automationName))
            {
                AutomationProperties.SetName(control, T(automationName));
            }

            if (ToolTip.GetTip(control) is string tip)
            {
                ToolTip.SetTip(control, T(tip));
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    private static string TranslateDynamic(string text)
    {
        var value = text;
        value = RecognizingPattern().Replace(value, match =>
            $"Recognizing with {TranslateToken(match.Groups[1].Value)}…");
        value = RecognizedPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} recognized · Translating…");
        value = CompletedPattern().Replace(value, match =>
            $"Complete · {TranslateToken(match.Groups[1].Value)}");
        value = ReadyPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} · {TranslateToken(match.Groups[2].Value)} is ready. Press {match.Groups[3].Value} to capture.");
        value = ReadyCapturePattern().Replace(value, match =>
            $"Ready. Press {match.Groups[1].Value} or select Start capture.");
        value = AppliedPattern().Replace(value, match =>
            $"Applied: {TranslateToken(match.Groups[1].Value)} + {TranslateToken(match.Groups[2].Value)}.");
        value = ModePattern().Replace(value, match =>
            $"Switched to ‘{TranslateToken(match.Groups[1].Value)}’. Checking availability.");
        value = TargetLanguagePattern().Replace(value, match =>
            $"Target language: {TranslateToken(match.Groups[1].Value)}.");
        value = UploadBothPattern().Replace(value, match =>
            $"The selected image is sent to {TranslateToken(match.Groups[1].Value)}; recognized text is sent to {TranslateToken(match.Groups[2].Value)}.");
        value = UploadTextPattern().Replace(value, match =>
            $"The image is recognized locally; only recognized text is sent to {TranslateToken(match.Groups[1].Value)}.");
        value = LoadingModelPattern().Replace(value, match =>
            $"Loading {TranslateToken(match.Groups[1].Value)}…");
        value = LoadingBackendPattern().Replace(value, match =>
            $"Loading the model with the {TranslateToken(match.Groups[1].Value)} backend…");
        value = DownloadingModelPattern().Replace(value, match =>
            $"Downloading {TranslateToken(match.Groups[1].Value)} from ModelScope…");
        value = DownloadingFilePattern().Replace(value, match =>
            $"Downloading {match.Groups[1].Value}");
        value = VerifyingFilePattern().Replace(value, match =>
            $"Verifying {match.Groups[1].Value}…");
        value = ExistingFilePattern().Replace(value, match =>
            $"{match.Groups[1].Value} already exists and passed verification");
        value = RunningBackendPattern().Replace(value, match =>
            $"The local model service is running with {TranslateToken(match.Groups[1].Value)}");
        value = CredentialVisiblePattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} is visible and can be edited or pasted.");
        value = CredentialHiddenPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} is hidden.");
        value = SettingsSavedPattern().Replace(value, match =>
            $"Settings saved. {TranslateToken(match.Groups[1].Value)}");
        value = InitializationPattern().Replace(value, match =>
            $"Initialization failed: {TranslateToken(match.Groups[1].Value)}");
        value = ErrorPrefixPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)}: {TranslateToken(match.Groups[2].Value)}");
        value = MissingLocalModelPattern().Replace(
            value,
            "The service is connected, but model ‘$1’ was not found. Available: $2.");
        return value;
    }

    private static string TranslateToken(string value)
    {
        if (EnglishText.TryGetValue(value, out var translated))
        {
            return translated;
        }

        var knownValue = value switch
        {
            "OCR 模型" => "OCR model",
            "翻译模型" => "Translation model",
            "OCR 凭据" => "OCR credential",
            "翻译凭据" => "Translation credential",
            "Google OCR 凭据" => "Google OCR credential",
            "Google 翻译凭据" => "Google Translation credential",
            "本地 PaddleOCR ONNX" => "Local PaddleOCR ONNX",
            "本机多模态大模型 OCR" => "Local multimodal LLM OCR",
            "PaddleOCR + 本机大模型纠错" => "PaddleOCR + local LLM correction",
            "本地 / 自定义大模型" => "Local / custom LLM",
            "百度云 OCR" => "Baidu Cloud OCR",
            "百度翻译" => "Baidu Translation",
            _ => null
        };
        if (knownValue is not null)
        {
            return knownValue;
        }

        return ManagedMultimodalModels.All.FirstOrDefault(model =>
                   string.Equals(model.DisplayName, value, StringComparison.Ordinal))?.LocalizedDisplayName
               ?? value;
    }

    [GeneratedRegex("^正在使用 (.+) 识别…$")]
    private static partial Regex RecognizingPattern();
    [GeneratedRegex("^(.+) 已识别 · 正在翻译…$")]
    private static partial Regex RecognizedPattern();
    [GeneratedRegex("^处理完成 · (.+)$")]
    private static partial Regex CompletedPattern();
    [GeneratedRegex("^(.+) · (.+) 已就绪，按 (.+) 开始截图。$")]
    private static partial Regex ReadyPattern();
    [GeneratedRegex("^就绪。按 (.+) 或点击“开始截图”。$")]
    private static partial Regex ReadyCapturePattern();
    [GeneratedRegex(@"^已应用：(.+) \+ (.+)。$")]
    private static partial Regex AppliedPattern();
    [GeneratedRegex("^已切换到“(.+)”，正在检查可用性。$")]
    private static partial Regex ModePattern();
    [GeneratedRegex("^目标语言已切换为：(.+)。$")]
    private static partial Regex TargetLanguagePattern();
    [GeneratedRegex("^所选图片(?:将)?发送给 (.+)；(?:识别)?文字(?:将)?发送给 (.+)[。]?$")]
    private static partial Regex UploadBothPattern();
    [GeneratedRegex("^图片在本地识别；(?:只有)?识别文字(?:会|将)发送给 (.+)[。]?$")]
    private static partial Regex UploadTextPattern();
    [GeneratedRegex("^正在加载 (.+)…$")]
    private static partial Regex LoadingModelPattern();
    [GeneratedRegex("^正在使用 (.+) 后端加载模型…$")]
    private static partial Regex LoadingBackendPattern();
    [GeneratedRegex("^正在从魔搭下载 (.+)…$")]
    private static partial Regex DownloadingModelPattern();
    [GeneratedRegex("^正在下载 (.+)$")]
    private static partial Regex DownloadingFilePattern();
    [GeneratedRegex("^正在校验 (.+)…$")]
    private static partial Regex VerifyingFilePattern();
    [GeneratedRegex("^(.+) 已存在并通过校验$")]
    private static partial Regex ExistingFilePattern();
    [GeneratedRegex("^本机模型服务已通过 (.+) 运行$")]
    private static partial Regex RunningBackendPattern();
    [GeneratedRegex("^(.+) 已显示，可直接编辑或粘贴。$")]
    private static partial Regex CredentialVisiblePattern();
    [GeneratedRegex("^(.+) 已隐藏。$")]
    private static partial Regex CredentialHiddenPattern();
    [GeneratedRegex("^设置已保存。(.*)$")]
    private static partial Regex SettingsSavedPattern();
    [GeneratedRegex("^初始化失败：(.+)$")]
    private static partial Regex InitializationPattern();
    [GeneratedRegex("^(OCR 模型|翻译模型|OCR 凭据|翻译凭据|Google OCR 凭据|Google 翻译凭据)：(.+)$")]
    private static partial Regex ErrorPrefixPattern();
    [GeneratedRegex("^服务已连接，但模型名“(.+)”不存在；当前可用：(.+)。$")]
    private static partial Regex MissingLocalModelPattern();
}
