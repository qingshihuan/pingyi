# 默认 Vulkan 与手动后端管理（v0.8.1）

## 默认行为

新安装默认选择 **通用显卡 · Vulkan（默认）**。首次配置优先复用已经安装的 Vulkan／CPU 文件，不因为配置模型而强制访问 GitHub 更新后端。首次模型权重下载仍需用户确认。

Schema 13 将旧配置中“自动后端＋自动设备”的旧默认组合迁移为 Vulkan。明确指定的 CUDA、ROCm、CPU、Vulkan 以及具体设备选择保留；升级后仍可以主动选择“自动检测（可选）”。旧配置没有记录自动选项是否曾被主动选择，因此这类用户可在升级后重新选 Auto。现有模型、端点、凭据和语言不重置。

## 从 Vulkan 切换到 ROCm

进入 **设置 → 本地模型 → 显卡与运行后端**，选择 **AMD · ROCm / HIP**，然后点击 **安装并切换到所选后端**。没有安装的后端会按现有可信目录下载，已安装的直接复用。GitHub 不可达时可主动启用备用源；仍必须校验文件大小和 SHA-256。

操作顺序是：**准备新后端 → 检查启动和实际设备 → 停用旧进程并启动新后端 → 用已配置的模型验证固定 OCR／翻译样例 → 保存新配置**。设备或依赖检查失败时不停止旧后端。激活或保存失败会尝试恢复原进程；恢复失败明确报告，不假报成功。下载的原后端文件不会在切换时删除。

只选择下拉选项不会切换。整页“保存并应用”不能绕过后端切换校验。切换成功后可继续保存其他表单内容；未保存的凭据和提供商输入不会被后端切换提交。未配置托管模型时，仅保存已检查的后端偏好，并提示继续模型配置，不宣称推理测试已通过。连接已有外部服务的用户仍需在该服务中更换后端。

后端安装成功后可刷新其实际执行显卡列表并指定设备，再点击同一切换按钮应用。不用 Vulkan 编号代替 ROCm 编号，不把操作系统识别到 AMD 当作 ROCm 可运行证明。不自动安装驱动、SDK 或强制使用不支持的架构。

## 卸载旧后端

确认已切换成功后，在下拉框选择旧后端，点击 **卸载所选已下载后端**，阅读提示并在十秒内再次点击确认。只删除本应用 runtime-packages 中该后端的下载版本、元数据和能确认不被其他后端引用的归档缓存。模型权重、普通配置、密钥、系统驱动和外部服务均保留。

**当前保存或正在运行的后端不能卸载，必须先切换。**不会按进程名称杀死其他程序，也不会删除正在使用的 DLL。安装／卸载共用锁；取消发生在文件移除前时不改动目录。遇到符号链接或目录联接拒绝递归清理；文件被其他进程占用时会说明磁盘清理未完成。

**内置 Vulkan／CPU 属于安装包的恢复组件，不在这里卸载。**从内置 Vulkan 切换到 ROCm 后，Vulkan 进程停止，不再持有其显存；留下的是恢复用磁盘文件。通过下载器更新的 Vulkan 可卸载，之后重新显露内置版本。无需先破坏旧的可用环境再尝试新后端。

## 验证边界

自动化覆盖默认与迁移、明确切换、准备失败／激活失败／保存失败／取消的恢复顺序、卸载确认、忙碌交互、在用进程保护、锁与路径安全、缓存共享和内置文件保留。界面测试使用明确标注的合成设备与回调。

没有在物理 NVIDIA／AMD 显卡上完成 ROCm/CUDA 推理或吞吐对比；不保证 ROCm 比 Vulkan 更快，也不保证某一 ROCm 包支持所有 AMD 显卡。升级不会凭空补齐缺失驱动或依赖；运行检查失败时仍应使用已工作的 Vulkan。原有退出释放资源行为保持，主窗口 X 仍隐藏到托盘。

## English

Vulkan is the default for new installs. Schema 13 migrates the former Auto-backend/Auto-device default while retaining explicit CPU/CUDA/ROCm/Vulkan and device choices. Installed runtime files are reused for model setup; downloading an update is a separate explicit action.

Select a backend/device and choose **Install and switch**. Preparation and device checks precede activation; configured managed models must pass fixed OCR and translation requests before saving. Failure retains the previous settings and files and attempts bounded restoration. No model weights are redownloaded just to change backend. Without a configured managed model, only the backend preference is saved; finish model setup before inference.

After switching away, select the old backend and confirm **Uninstall selected downloaded backend** with a second click. Only owned downloaded versions and demonstrably unshared caches are removed. Active/saved backends are protected. Bundled Vulkan/CPU are recovery files and remain on disk, without VRAM use when not running. Models, settings, credentials, system drivers and external services remain untouched. GPU hardware compatibility and speed are not certified by synthetic CI tests.
