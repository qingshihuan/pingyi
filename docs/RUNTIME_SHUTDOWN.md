# 退出软件与后端生命周期

主窗口底部和托盘菜单提供 **退出并释放资源 / Quit and release resources**。
真正退出会取消截图、推理及进行中的模型配置，停止由本次截屏释义进程启动的
llama.cpp 模型后端与 Python 离线引擎，并等待已拥有的进程退出。
这些后端由统一的进程所有权层管理，不依赖 Vulkan、CPU 或后续扩展后端的名称。
不会通过进程名、显卡编号或端口扫描并终止其他进程。

**主窗口右上角 X 仍为隐藏到托盘，不是真正退出。**这保留后台快捷键的现有行为。
需要结束后台运行时，请使用明确的退出入口。操作过程中重复点击退出共用一次清理任务。
系统注销或关机也会进入退出流程，不把系统退出转换成隐藏窗口。

## 释放范围

- 只停止应用自己创建的模型与引擎进程及能够归属的子进程。
- 用户自行启动的 Ollama、LM Studio、llama.cpp 或远程服务不属于本应用，不会被关闭。
  这些服务仍可能占用显存，需要在其所属程序中停止模型。
- 不删除模型权重、后端文件、下载缓存、配置或凭据；再次使用时仍可按需加载。
- 进程结束后，其 GPU 分配由操作系统和驱动回收。显存监测工具可能延后刷新，
  也可能继续显示桌面、驱动或其他程序的用量；本程序不声称显卡总占用必须归零。
  不使用 GPU 重置、清理其他程序缓存或强杀所有同名进程等操作。

## 实现

`OwnedProcessScope` 只登记自己 `Start` 创建的进程，并保留对应进程句柄。
正常停止会请求终止进程树、等待退出，再释放句柄。Windows 尝试为每个拥有的后端建立
私有 Job Object（`KILL_ON_JOB_CLOSE`），并在正常停止时核对 Job 中的活跃进程数。
这也能处理启动器先退出、后端子进程仍存活的情况。限制 Job 嵌套的宿主环境中，仍保留
普通的进程树清理；不会声称所有宿主都建立了 Job 保护。

退出先封锁新任务并取消现有操作。后端停止不依赖托盘销毁、快捷键解除、浏览器连接或设置
锁先成功，任一清理回调失败不会跳过其他回调。原生 ONNX 推理仍由其自己的推理锁保护，
不会在 `Run()` 正在执行时释放会话。

退出等待有上限；出现故障时执行不依赖 UI dispatcher 的最终停止，并以非零退出码反映
清理异常，而不是静默报告成功。程序入口的 `finally`、桌面生命周期 Exit 和托管
ProcessExit 均有所有权范围内的收尾。Linux 的 SIGKILL、机器断电、驱动故障等无法靠
托管 ProcessExit 保证运行；这里不将正常退出回归表述为所有异常终止都已保障。

后续新增 CUDA / ROCm 或显卡选择路径，必须继续从 `OwnedProcessScope.Start` 启动应用管理的
后端；不可另建一个不受退出流程管理的 `Process.Start`。本次不会改变后端选择或下载规则。

## 验证

自动测试包括：普通退出等待、重复退出、同步或异步清理失败、忙碌的连接不阻止后端停止、
拥有与非拥有进程隔离、进程树终止、Windows 启动器退出后的 Job 子进程回收、模型管理服务
清理及释放后拒绝重新启动。进程测试使用固定的合成 Python 子进程，不下载模型或接触
用户内容。Linux Xvfb/Openbox 测试会实际点击主窗口退出按钮，检查应用及其子进程退出。

这些检查验证生命周期与进程结束，不等于已用物理 NVIDIA/AMD 显卡实测显存回收数值。

参考：
- https://learn.microsoft.com/dotnet/api/system.diagnostics.process.kill
- https://learn.microsoft.com/windows/win32/procthread/job-objects
- https://docs.avaloniaui.net/api/avalonia/controls/applicationlifetimes/iclassicdesktopstyleapplicationlifetime

## English

Use **Quit and release resources** in the window footer or tray menu to stop Screen Insight and the
model/engine processes it started. Closing the main window still hides it to the tray. Repeated quit
requests share one cleanup. A UI, hotkey or bridge failure must not skip owned backend termination.
External Ollama, LM Studio, llama.cpp and remote services are not killed, and downloaded files and
settings are retained. GPU allocations are reclaimed by the OS/driver when the owning backend exits;
this is not a promise that total GPU usage becomes zero. Tests use synthetic processes and a virtual
desktop, not physical GPU memory measurements.
