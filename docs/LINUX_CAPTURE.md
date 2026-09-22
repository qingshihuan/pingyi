# Linux 截图与快捷键

## 默认键与自定义（v0.5.2）

Linux X11 默认使用 `Ctrl+Shift+D`，Windows 保持 `Ctrl+Alt+D`。Ubuntu/GNOME 官方常用系统快捷键表未列出该 Linux 组合，但用户绑定和其他应用仍可能使用它；例如 Firefox 使用该键收藏所有标签页。不能承诺所有 Linux 环境都无冲突。

在“设置 → 外观与启动”可录制或输入组合、取消录制、恢复默认和单独应用。应用前注册检查占用，注册或保存失败恢复旧绑定，恢复失败也明确报告。录制期间只暂停屏译自己持有的全局键，结束或取消后恢复。完整操作、支持范围、迁移与来源见 [快捷键说明](HOTKEYS.md)。

Schema 10 迁移未标记自定义的旧 Linux 默认：schema < 9 的 `Ctrl+Alt+D`，以及 schema < 10 的 `Ctrl+Alt+Shift+D`。其他自定义组合保留；schema 9 的 `Ctrl+Alt+D` 保留。旧配置无法区分显式选择了同一旧默认组合的用户，详见迁移说明。

## 截图与原生处理

- XGrabKey 是异步请求，必须在 XSync 后检查 BadAccess，不能把调用返回或线程启动当作注册成功。只截获本连接的 GrabKey 错误、转发其他连接错误，恢复原全局错误处理器；失败释放部分抢占和显示连接，不退出程序。
- Caps Lock 与实际 Num Lock 修饰映射均处理；重复启动不创建多余线程，冲突不阻断按钮截图。
- Wayland 使用公开 XDG Screenshot 门户：同一连接先订阅自己的 Request::Response，再请求 interactive 截图；不绕过授权，也不读取 XWayland 根窗口。用户操作最长五分钟，独立于 X11 15 秒像素抓取期限。取消发送 Request.Close；用户取消正常退出，缺失/失败明确显示。
- 门户已选择的像素直接进入处理流程，不再套 X11 框选层或屏幕坐标。结果定位使用安全的 (0,0) 起点，因为门户未返回全局屏幕坐标。
- Wayland 不调用 XGrabKey，也不假报“已启用”。保存的是偏好组合，实际快捷键需在系统中绑定设置页提供的命令；没有实现 GlobalShortcuts 门户。
- 首次进程与后续进程均支持 `--capture`，所有入口共享截图事务。截图取消或错误恢复原可见窗口；模型健康刷新不能掩盖快捷键注册失败。

## 依赖和数据处理

Ubuntu GNOME 需要系统 `xdg-desktop-portal`、`xdg-desktop-portal-gnome`、GLib/GIO；其他桌面需匹配后端。不调用私有 GNOME Screenshot 接口，不执行 shell/Python 帮助脚本，不修改桌面快捷键。

门户文件仅接受本地 URI，拒绝 HTTP 等 URI；限制文件大小和像素数。读取后只清理临时目录中的门户副本，不删除其他任意用户图片。门户自行保存到其他目录的文件由系统管理，本程序不保证这些副本零留存。截图不会增加应用历史，也不会绕过图片上传前的确认。

## 自动验证

CI 在 Windows/Ubuntu 执行 Core/UI/Python 测试。隔离的 dbus-run-session + Xvfb/Openbox 原生回归执行真实 X11 注册、冲突、清理、像素截图、按键事件、重绑定回滚；操作真实 Avalonia 进程验证部分旧配置迁移、冷启动/二次 `--capture`、鼠标截图按钮、录制/应用快捷键、旧抓键释放、进程重启后的自定义组合、取消录制和恢复默认。

真实 GIO/D-Bus 往返对接测试私有会话中的模拟门户，验证提前 Response、成功返回图像、用户取消、服务失败、Request.Close 和取消后恢复。普通无显示测试不冒充原生桌面验证。

## 物理桌面验收边界

真实 Ubuntu GNOME X11/Wayland、不同输入法与键盘布局、多屏混合缩放、门户取消、后端缺失和权限拒绝仍需按实际桌面验收。系统后端交互界面可能不同，不能把模拟 D-Bus 服务当作真实 GNOME 门户 UI 已验证。Wayland 快捷键仍需系统显式绑定，窗口显示也由 Avalonia 与 XWayland/桌面环境负责。
