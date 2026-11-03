# 第三方模型 API

PingYi 支持 **OpenAI Chat Completions 兼容接口**，可将文字翻译、视觉 OCR、图片描述和提示词反推交给第三方模型服务。它不是原生 Anthropic Messages API 客户端；服务商必须提供兼容的 `/chat/completions` 接口。

## 配置

进入 **设置 → 自定义接口**：

1. 在“第三方 API 服务商”选择 OpenAI、DeepSeek、SiliconFlow、OpenRouter、阿里云百炼（DashScope）或智谱 BigModel，然后点击“填入服务商地址”。也可以手动填写其他兼容服务的完整 Chat Completions URL。
2. API 模式会自动禁用下方的本地“服务预设”选择和“应用服务预设”按钮，避免把远程 API 地址与本机 llama.cpp / Ollama / LM Studio 预设混用。切回本机回环地址后，本地预设会重新启用。
3. 点击 API Key 旁的“显示”输入密钥，然后保存设置。刷新模型列表前，软件会先将当前接口设置保存，并把密钥写入系统安全存储。
4. 点击“刷新模型列表”。软件使用当前服务地址和 Bearer API Key 请求 OpenAI 兼容的 `GET /models`，将服务商返回的模型 ID 填入可编辑下拉框。可以选择列表中的模型；也可以直接在下拉框中手动输入模型 ID。部分服务商或账号可能不开放模型列表接口，获取失败时手动输入仍然可用。
5. 点击“保存并应用”，再点击“测试翻译”。该测试会向服务商发送固定文本，可能计入服务商用量或费用。
6. 如需远程模型执行截图 OCR、图片描述或提示词反推，在“文字识别”中选择“兼容多模态大模型 OCR”，并使用支持图片输入（OpenAI image_url 格式）的模型；点击“测试图片”可发送固定合成图片验证能力。

## OpenAI Responses 与 Chat Completions 双协议

- OpenAI 预设默认选择 **Responses API**，请求地址由已保存的 Chat Completions 基础地址映射到 `/v1/responses`。Responses 请求使用 `instructions` / `input`，从 `output_text` 或 `output` 消息项读取结果。
- OpenAI 的 Responses API 同时支持文字翻译、视觉 OCR、图片描述和提示词反推。视觉请求使用 `input_image` 与 Base64 data URL。
- Responses 请求显式设置 `store: false`，避免将这些截图和翻译请求作为 Responses 历史存储。
- 在“API 协议”中可手动切换 **Responses API** 与 **Chat Completions**。若使用 OpenAI Chat Completions，选择 Chat Completions；切换到其他预设时会默认回到 Chat Completions。
- DeepSeek、SiliconFlow、OpenRouter、百炼和智谱仍默认使用 OpenAI Chat Completions 兼容协议。不要仅因服务商支持 OpenAI 兼容就假定它也支持 Responses API。
- 本机 llama.cpp、Ollama、LM Studio 回环服务保持 Chat Completions；切回本机地址时软件会自动切回该协议。

## 数据与安全

- 远程 API 必须使用 HTTPS；仅本机回环地址允许 HTTP。URL 中禁止嵌入用户名、密码或 API Key。
- API Key 使用 Bearer Authorization 发送。当前通用适配器面向 OpenAI Chat Completions 兼容协议；不兼容该协议或要求专有签名/请求头的服务不能直接使用。
- 选择远程文字翻译时，识别后的文本会发送给服务商；选择远程多模态 OCR／图片分析时，本次选中的截图会发送给服务商。服务商的数据保留、训练、地区、配额和计费政策由其自身决定。
- 本地处理仍可使用原来的 loopback 模型端点、Vulkan/CPU 后端及轻量模式。应用第三方预设只填入地址并选择兼容翻译提供商，不会自动发送请求；需用户保存并主动测试。
- 当前 API Key 输入框由兼容接口共用。切换服务商时请替换为对应服务商的密钥，避免将旧密钥发送到新端点。模型列表仅展示该 API Key/账号通过服务商模型列表接口返回的 ID；服务商可能对模型权限、地区、配额和列表接口进行限制。
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
