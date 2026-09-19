# Linux 截图与快捷键修复

## 症状与已确认的代码缺陷

旧版在所有系统使用 `Ctrl+Alt+D`，X11 的 `XGrabKey` 注册没有收集异步 `BadAccess` 错误，冲突可能被误报为成功，甚至触发 Xlib 默认错误处理。旧截图后端明确拒绝 Wayland，而模型检查又可能覆盖截图失败提示。从桌面自定义快捷键启动 `--capture` 时，旧代码只对已有进程转发，首次启动没有执行该命令。

这些是源码中确认的缺陷，不是对某台机器会话类型或所有无响应原因的推断。

## 现在的行为

- **Linux 默认快捷键为 `Ctrl+Alt+Shift+D`**；Windows 仍是 `Ctrl+Alt+D`。Schema 9 只迁移旧 Linux 默认值，其余自定义组合不变。新版本中明确选择的旧组合也不再自动覆盖。系统可能仍占用其他组合，因此始终检查真实注册结果，不篡改 Ubuntu 的系统绑定。
- **X11** 继续使用屏译原生框选层。屏幕读取、PNG 编码不占用 UI 线程；快捷键在独立显示连接上注册，检查 `XSync` 后的错误，冲突时回滚部分注册，按当前键盘映射处理 Caps/Num/Scroll Lock。失败不禁用截图按钮。
- **Wayland** 通过 `org.freedesktop.portal.Screenshot` 的交互式系统界面选择截图，避免读到只有 XWayland 窗口的错误底图。不在系统选好的图片上再创建一次屏译框选层。支持拒绝、取消、请求超时、异常时的窗口恢复。
- Wayland 全局快捷键优先使用 `GlobalShortcuts` 门户，绑定由桌面授权；界面展示系统实际返回的按键而非假定首选值被接受。监听绑定变化与会话关闭。桌面不实现该接口或用户拒绝时，明确提示未注册，并提供复制实际可执行文件的 `--capture` 命令及打开系统键盘设置的入口。旧 GNOME 环境可以用自定义快捷键执行此命令，按钮仍可用。
- 首页、设置与托盘分别显示快捷键状态，模型“就绪”不再等于快捷键注册成功。截图故障还会打开独立的可见提示窗，避免托盘调用无反馈或状态被后台检查覆盖。
- 标准版与完全版 `.desktop` 都提供截图操作，首次启动和已有实例都执行 `--capture`。不需要为截图使用 sudo。

## Ubuntu 依赖与边界

`.deb` 依赖系统 GLib/GIO 库，并推荐 `xdg-desktop-portal` 和与桌面匹配的实现；GNOME 通常为 `xdg-desktop-portal-gnome`。便携包依赖同样的系统服务。缺少门户、未授权、没有图形会话时给出明确错误；没有自动下载依赖、修改系统设置或绕过 Wayland 的权限机制。

系统门户可能创建临时截图文件。屏译验证本地 URI、PNG 格式和大小后读取，不把它存为自身历史，也不删除桌面门户或用户拥有的文件。临时文件的路径和保留策略由桌面实现决定，不能将这条系统路径宣传成“全流程绝不落盘”。后续 OCR/翻译/图片分析的数据发送规则不变。

Wayland 的区域、窗口、屏幕选择方式由安装的桌面后端决定；不承诺每个桌面都有与 X11 完全相同的跨屏拖选 UI。非 GNOME 桌面缺少 `gnome-control-center` 时提供通用手动绑定说明。

## 自动验证

```sh
dotnet test PingYi.slnx -c Release
python -m unittest discover -s scripts -p 'test_*.py'
python -m unittest discover -s engine_host -p 'test_*.py'
bash scripts/test-linux-desktop.sh
```

最后一项在**独立 D-Bus 会话和 Xvfb 虚拟显示器**中执行。使用真实 libX11/XTest 验证快捷键冲突、释放、Num Lock、捕获桌面和原生框选；使用真实 GIO 客户端连接协议测试门户，验证提前响应信号、交互截图、取消与 Request.Close、授权失败、GlobalShortcuts 缺失、实际绑定、Activated/ShortcutsChanged/Session.Closed 和资源清理。测试门户仅处理自建的合成 PNG，不访问用户桌面、模型、网络或凭据。

原生 X11 冒烟与真实 D-Bus 客户端测试不等于物理 Ubuntu GNOME/Wayland 验收。实际桌面上仍应复核：按钮和热键在主/副屏的选择、拒绝授权、取消恢复、重启后首次 `--capture`、自定义键冲突，以及不同缩放、刷新率和门户版本。缺少真实桌面验证时必须明确这一范围。
