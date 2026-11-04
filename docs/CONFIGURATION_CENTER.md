# 配置中心 / Configuration center

此功能位于 `refactor/configuration-center` 分支（PR #33），尚未随 v0.8.4 发布。
This feature is on the configuration-center branch (PR #33), not in the v0.8.4 release.

## 使用流程

进入 **设置 → 配置中心**。先在“服务与模型”添加服务，填写名称、API 或本机地址、协议和该服务独立的 API Key。选择地址预设只填表，不发请求。OpenAI 预设默认 Responses，其他预设保持 Chat Completions；本机回环地址仍使用 Chat Completions。添加、切换或编辑服务不会删除已安装模型或改变 GPU 后端。

“获取模型列表”使用该服务当前草稿密钥，不会把整页设置自动保存。列表来自服务商，不表示每个模型都支持文字/图片推理或拥有足够额度。可手动输入模型 ID。文字和图片测试分别发送固定文本、合成图片，可能计费；不会发送用户截图。修改服务地址、协议或密钥会使对应测试证据失效。取消、超时或失败的测试不标为通过。

在“任务配置”分别设置文字 OCR、翻译、图片描述和提示词反推的服务与模型。可选择本机托管模型、独立服务；OCR 和翻译还可选择轻量离线组件。二维码始终本地解析，无模型路由。允许离线回退的选项只作用于 OCR／翻译，不会把失败请求悄悄改发另一家云服务。

“配置概览”显示当前表单的任务目的地、模型、兼容继承关系和回退策略。它只读取配置与历史固定样例测试记录，不联网、不读取密钥、不启动模型；“样例已测试”不是当前可达性、余额或任务质量保证。文件、进程、显卡状态仍到“运行状态”检查。底部 **保存并应用** 才提交服务、独立密钥和任务绑定。

## 升级、密钥和运行环境

Schema 15 迁移保留原快捷键、模式、服务地址、模型和显卡后端。未单独绑定的任务继续“沿用原配置”；不会自行改用云端。首次覆盖旧配置前保留原始 `.pre-v15.bak`，后续保存不覆盖该备份；配置中不包含密钥。

每个服务的密钥以服务 ID 和规范化地址隔离，存入操作系统安全存储。改变地址不会带入旧地址的密钥；同一家服务的两个账号也可使用两个档案。删除仍被任务引用的服务会被阻止；解除绑定、确认删除并保存后才清理该档案密钥。保存失败会尝试恢复受影响密钥；失败状态会明确提示。旧兼容区只服务“沿用原配置”任务。

保存配置不会提前加载本机大模型。浏览器只请求文字翻译时，不会为了 OCR 路由启动 GPU 模型；本机任务按需启动。本机模型文件、默认 Vulkan、CUDA/ROCm/CPU 和手动显卡选择沿用现有运行环境管理。退出仍关闭本软件拥有的进程，不关闭用户独立运行的模型服务。

## 验证与边界

自动化测试使用合成服务、图片和密钥，验证迁移、隔离、回滚、独立路由、协议序列化和界面行为，不代表真实付费账号或 GPU 兼容性认证。当前不支持自动跨云备用模型；模型能力是固定样例证据而非模型名称推断。高级网络／下载／快捷键／运行环境继续复用已有页面；旧界面保留为折叠兼容区，不强制重新配置所有用户。

## English workflow

Open **Settings → Configuration center**. Add independent service profiles under **Services**, then select a model for each task under **Tasks**. Profiles have separate OS-vault credentials bound to the service ID and normalized destination. Discovery and fixed sample tests use the current draft key without saving the whole form. Manual model IDs remain supported. Models returned by a provider are not automatically certified for inference access or vision capability.

**Overview** reads the current draft only. It shows each task's destination, model, compatibility inheritance and offline-fallback policy, without network requests, key reads or model startup. Recorded sample tests are not live availability, quota or quality guarantees. Use **Save and apply** to commit the changes. Cancellation, timeouts and failed retests do not produce successful capability evidence.

Schema 15 retains existing settings, installed models and GPU preferences. Existing tasks continue their previous routes until explicitly reassigned. The first migration save keeps the original `.pre-v15.bak` bytes. QR decoding stays local. Saving does not preload a model, and remote browser text translation does not start a GPU backend merely because another task uses one. No automatic cross-cloud fallback is performed. Existing runtime, privacy, download and shortcut controls remain accessible, including a collapsed compatibility editor.
