# 第三方模型 API

PingYi 支持 **OpenAI Chat Completions 兼容接口**，可将文字翻译、视觉 OCR、图片描述和提示词反推交给第三方模型服务。它不是原生 Anthropic Messages API 客户端；服务商必须提供兼容的 `/chat/completions` 接口。

## 配置

进入 **设置 → 自定义接口**：

1. 在“第三方 API 服务商”选择 OpenAI、DeepSeek、SiliconFlow、OpenRouter、阿里云百炼（DashScope）或智谱 BigModel，然后点击“填入服务商地址”。也可以手动填写其他兼容服务的完整 Chat Completions URL。
2. 填写服务商控制台提供的**准确模型 ID**。模型 ID 不由软件猜测或自动替换。
3. 点击 API Key 旁的“显示”输入密钥。密钥通过系统安全存储保存，不写入 settings.json、日志或错误提示。
4. 点击“保存并应用”，再点击“测试翻译”。该测试会向服务商发送固定文本，可能计入服务商用量或费用。
5. 如需远程模型执行截图 OCR、图片描述或提示词反推，在“文字识别”中选择“兼容多模态大模型 OCR”，并使用支持图片输入（OpenAI image_url 格式）的模型；点击“测试图片”可发送固定合成图片验证能力。

## 数据与安全

- 远程 API 必须使用 HTTPS；仅本机回环地址允许 HTTP。URL 中禁止嵌入用户名、密码或 API Key。
- API Key 使用 Bearer Authorization 发送。当前通用适配器面向 OpenAI Chat Completions 兼容协议；不兼容该协议或要求专有签名/请求头的服务不能直接使用。
- 选择远程文字翻译时，识别后的文本会发送给服务商；选择远程多模态 OCR／图片分析时，本次选中的截图会发送给服务商。服务商的数据保留、训练、地区、配额和计费政策由其自身决定。
- 本地处理仍可使用原来的 loopback 模型端点、Vulkan/CPU 后端及轻量模式。应用第三方预设只填入地址并选择兼容翻译提供商，不会自动发送请求；需用户保存并主动测试。
- 当前 API Key 输入框由兼容接口共用。切换服务商时请替换为对应服务商的密钥，避免将旧密钥发送到新端点。
- 不要把 API Key 放进 URL、截图、Issue 或日志。更换服务商后，检查模型 ID 是否属于该服务商。

## 当前预设地址

| 服务商 | Chat Completions URL |
| --- | --- |
| OpenAI | `https://api.openai.com/v1/chat/completions` |
| DeepSeek | `https://api.deepseek.com/chat/completions` |
| SiliconFlow | `https://api.siliconflow.cn/v1/chat/completions` |
| OpenRouter | `https://openrouter.ai/api/v1/chat/completions` |
| 阿里云百炼 / DashScope | `https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions` |
| 智谱 BigModel | `https://open.bigmodel.cn/api/paas/v4/chat/completions` |

预设只提供接口地址，不锁定模型 ID、价格或服务套餐；服务商可能调整产品和模型，使用前请核对其官方文档。
