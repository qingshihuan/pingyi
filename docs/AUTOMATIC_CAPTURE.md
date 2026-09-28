# 一键识别、基础／轻量模式与首次配置

本说明对应当前源码实现；发行版以各自 Release 为准。本次不提升版本标记，不修改浏览器协议或 Complete 安装／数据目录。

## 一次截图，任务可纠正

主按钮和默认截图快捷键选择 `CapturePurpose.Auto`；关闭“自动判断任务”后恢复文字翻译。手动翻译、图片描述、二维码和提示词反推直接执行所选任务，不被自动规则覆盖。

本地探测顺序是二维码解码、PaddleOCR 可用性／识别，再由 Core 的纯策略分流：

| 本地证据 | 行为 |
| --- | --- |
| 成功解码二维码，且没有明显正文 | 展示二维码内容，不自动打开链接 |
| 可靠文字量或紧密文字区域达到启发式条件 | 执行当前 OCR／翻译组合，保留源语言和目标语言设置 |
| 二维码与明显正文同时存在 | 要求选择，不丢弃其中一种意图 |
| 正常 OCR 探测返回空结果 | 尝试图片描述，并明确说明“未探测到文字不等于没有文字” |
| 少量片段、数字、低置信度、探测错误或超时 | 提示手动选择，不为判断任务自动调用云端 |

阈值不是校准的概率：当前只将有限置信度且分数至少 0.70 的本地文字块视为可读；字母／汉字数量至少 12，或至少 2 且文字框面积占比至少 0.12 时视为明显文字。面积限制在截图边界内。短字、特殊字体、其他语言、照片上的文字等仍可误判，不承诺意图准确率。

二维码探测、OCR 可用性探测与 OCR 的取消期限分别为 8、8、20 秒。原生调用只能协作取消，无法保证到期瞬间停止；旧操作完成清理前不会并发释放所需资源。这不是“零额外延迟”承诺。

结果窗口区分 `RequestedPurpose`（自动或用户明确选择）与 `Purpose`（实际任务）。同图改选先使旧操作失效、发出取消，再执行新任务；晚返回结果通过操作编号和令牌检查被拒绝。自动结果重试仍保留自动来源和上传确认，不悄悄降级为免确认的手动请求。

探测缓存以截图对象为弱键。仅当实际 OCR 也是 PaddleOCR 且源语言设置一致时复用文字；直接视觉 OCR 不接收这些探测文字。二维码解码结果可复用。关闭结果释放对应截图会话；不增加磁盘历史或内容日志。

## 基础与轻量

`basic`：`local-vlm-ocr` + `custom-chat`，仅本机回环、无内嵌用户名／密码的端点才能匹配此名称。不会为了选择基础模式覆盖远程端点或模型。

`lite`：`local-paddle` + `local-argos`，延续原离线中英路径。旧 `offline` 模式调用映射到 `lite`；删除 `vision` 方案，不再有纠错提供商注册、初稿生成或纠错提示词。保留 PaddleOCR → 大模型**翻译**组合，它不是 OCR 纠错。

直接视觉 OCR 的上游没有提供逐字置信度或真实文字框，因而返回的兼容块不再伪造 0.85／0.90 分数；未知置信度使用 0，不能当作识别准确率。它也不参与本地分流的置信度判定。

## 首次引导

新安装默认基础组合，`InitialSetupCompleted=false`。首次显示窗口时引导用户选择：

- **一键下载并配置：**展示当前目录模型、后端、下载量、许可与设备提示；用户点击后从现有固定 ModelScope 目录下载、校验并启动。用项目合成的“PINGYI OCR 2026”测试图检查视觉转录，再用固定短句检查翻译响应，成功后原子保存模型与基础组合。
- **已有本机服务：**记录已选择自行配置并打开设置，不下载模型，也不宣称服务已经就绪。
- **使用轻量模式：**保存 PaddleOCR／Argos，不下载大模型。随后需要图片描述时仍须配置视觉模型。

下载取消／失败不标记完成；恢复下载沿用已有校验／续传机制。未提交的运行模型在可安全清理时停止，旧配置保留。下载过程中其他设置变动会阻止覆盖，需要重新应用。没有运行时的源码构建不会提供无法完成的下载按钮，明确引导安装完整程序包或连接已有服务。

连接检查只证明固定请求能完成，不证明整个模型的 OCR／翻译质量、真实设备速度或所有模型硬件组合可用。主界面保留“模型配置引导”供重新进入。

## 升级

Schema 11 保留旧 OCR／翻译组合、端点、模型、凭据、语言及快捷键；`local-vlm-corrected` 迁移到 `local-vlm-ocr`，不强制改写翻译提供商。旧 JSON 缺少 provider 字段时沿用旧 PaddleOCR／Argos 默认，不被新安装默认覆盖。

旧 schema 记为已完成首次选择，避免升级后强制弹窗；需要改成基础模式时使用模型配置引导。完全不存在设置文件才按新安装处理。自动任务开关可保存，改变它不启动本机大模型。

## 隐私与范围

探测仅调用本地二维码与 PaddleOCR，既不下载模型，也不访问用户所选远程 OCR。二维码只解码，不自动打开链接、执行载荷或配置网络。

自动选择远程 OCR／文字翻译时，先显示对应接收提供商及自定义端点／模型，明确同意后才执行。自定义提供商使用请求配置快照，确认后配置变化不会把内容改发另一端点。图片分析继续沿用每次上传确认；拒绝后可继续手动选择同一张截图。

网络模型与 OS 门户的行为仍遵循各自的留存设置。默认本机模式和轻量路径不上传到云端，但不能替用户部署的独立服务保证“不记录请求”。浏览器扩展保持冻结，其现有协议与独立授权规则不变。

## 验证

Core 测试覆盖明确文字、紧密短字、混合二维码正文、空白与失败区别、低质量／非语言证据、旧配置迁移、直接视觉请求无初稿及未知置信度。界面测试覆盖自动／手动状态、处理中允许改选、语言切换、首次引导不静默下载、跳过、取消和失败重试。Windows／Ubuntu 既有回归与原生 Linux 截图检查继续保留。

首次引导自动测试使用可替换回调，不访问 ModelScope 或下载权重。CI 的本机大模型 opt-in 测试只有在提供运行服务并显式启用时才执行；不得把普通 CI 的通过称为真实模型验收。隔离 Xvfb／模拟 D-Bus 也不替代物理桌面、多屏或显卡验收。

## English summary

Smart capture runs local QR / PaddleOCR probes, recommends translation, image description or QR decoding, and asks for a choice when evidence is mixed or uncertain. Manual tasks always override; the same image is reused and obsolete operations cannot overwrite the new result. Empty OCR is a tentative description route, never proof that text is absent.

Basic is direct local vision OCR plus local model translation. Lightweight retains PaddleOCR / Argos. The correction provider and visual-correction mode are removed. First-run download is explicit, verified and committed only after synthetic OCR / translation checks; users may instead connect an existing local server or skip to Lightweight. Existing preferences migrate without overwriting endpoints or credentials. Automatic remote processing requires per-request confirmation; QR URLs never open automatically. No new dependency, package variant, browser protocol, retained history or release version is introduced.
