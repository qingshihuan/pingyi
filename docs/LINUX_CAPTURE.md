# Linux 截图与快捷键修复

## 发现的问题与代码处理

- 原默认 `Ctrl+Alt+D` 在部分 Ubuntu/桌面配置中被占用。XGrabKey 是异步请求，必须在 XSync 后检查 BadAccess，不能把调用返回或线程已启动视为注册成功。现在只截获本连接的 GrabKey 错误、转发其他连接错误，恢复原全局错误处理器；失败释放部分抢占和显示连接，不退出程序。
- 0.5.2 的 Linux X11 默认是 `Ctrl+Shift+D`。Schema 10 迁移 schema < 9 的旧默认 `Ctrl+Alt+D` 和 schema < 10 的旧默认 `Ctrl+Alt+Shift+D`，保留其他自定义组合与 Windows 配置。升级后可主动重新选择旧组合。Caps Lock 与实际 Num Lock 修饰映射均处理，重复启动不创建多余线程，冲突和停止不阻塞 UI 线程。
- 旧截图实现主动拒绝 Wayland。新增公开 XDG Screenshot 门户：同一连接先订阅自己的 Request::Response，再请求 interactive 截图；无需绕过授权或读取 XWayland 假桌面。用户操作最长五分钟，独立于 X11 15 秒像素抓取期限。取消发送 Request.Close；用户取消正常退出；缺失/失败明确显示。
- 门户已选择的像素直接交给处理流程，不再套 X11 原生框选层或屏幕坐标。结果定位使用安全的 (0,0) 起点，因为门户未返回全局屏幕坐标。
- Wayland 不调用 XGrabKey，也不假报“已启用”。在设置中提供可复制的当前版本截图命令，由用户在系统中绑定。首次进程也处理 `--capture`；之前仅后续进程转发命令会生效。
- 所有入口共享截图事务，错误重新显示主窗口；模型健康刷新不能掩盖快捷键注册失败。

## 0.5.2 自定义快捷键

打开“设置 → 外观与启动”，点击“录入快捷键…”，按下 Ctrl、Alt、Shift 中至少一个修饰键与一个字母 A–Z 或数字 0–9。录入结果需要确认，Esc 取消；返回设置后点击“保存并应用”。“恢复默认”只修改表单，同样需要保存。手动输入仍可用，非法组合会在保存时拒绝。暂不支持 Super、功能键和小键盘键。

录入时暂停屏译自己的 Windows/X11 全局键，防止录入原组合时触发截图。确认、取消或异常退出后恢复已经保存的组合，不应用尚未保存的输入。暂停、录入和恢复期间禁止同时保存。保存新组合遇到注册冲突时沿用原来的回滚机制；无法恢复旧绑定时保留错误状态，截图按钮仍可用。

Wayland 的快捷键输入是可编辑的偏好，不代表已经注册。保存后仍需复制截图命令，在桌面键盘设置中显式绑定所选组合；屏译不会覆盖系统配置。系统已占用的组合可能无法被软件内的录入窗口捕获，此时请手动输入或在系统中选择其他组合。

GNOME 官方常用快捷键表未列出 Ctrl+Shift+D，但不能据此保证所有发行版、桌面插件或个人配置均无冲突。Chrome 将它用于收藏全部标签页，Konsole 将它用于键盘选择模式；这些是应用内快捷键，注册为全局键会影响其原用途。参考：[GNOME](https://help.gnome.org/gnome-help/shell-keyboard-shortcuts.html)、[Chrome](https://support.google.com/chrome/answer/157179)、[Konsole](https://docs.kde.org/trunk_kf6/en/konsole/konsole/selection.html)。

## 依赖和数据处理

Ubuntu GNOME 需要系统 `xdg-desktop-portal`、`xdg-desktop-portal-gnome`、GLib/GIO；其他桌面需匹配后端。不调用私有 GNOME Screenshot 接口，不执行 shell/Python 帮助脚本、不修改桌面快捷键。

门户文件仅接受本地 URI，拒绝 HTTP 等 URI；限制文件大小和像素数。读取后只清理临时目录中的门户副本，不删除其他任意用户图片。门户自行保存到其他目录的文件由系统管理，本程序不保证这些副本零留存。截图不会增加应用历史，也不会绕过图片上传前的确认。

## 自动验证

CI 在 Windows/Ubuntu 跑原有 Core/UI/Python 测试。额外在隔离的 `dbus-run-session` + Xvfb 中执行 Linux 原生回归：真实 X11 注册冲突、部分/全部清理、冲突后的截图、按键事件、停止和重启；真实 GIO/D-Bus 往返对接模拟门户，验证提前 Response、成功返回图像、用户取消、服务失败、Request.Close 和取消后恢复。模拟门户只运行在测试私有会话，不影响真实桌面。

界面测试覆盖门户不重复裁剪、X11 保留框选、系统命令复制与语言切换，以及快捷键录入确认/取消、组合格式、恢复默认和配置迁移。原生测试通过环境变量显式启用，普通无显示测试不冒充桌面验证。

## 尚须物理桌面验收

在实际 Ubuntu GNOME X11/Wayland 分别从按钮、托盘和快捷键触发；含首次 `--capture`、多屏混合缩放、门户取消、后端缺失、截图权限拒绝。各后端的交互式系统界面可能不同；不能把模拟 D-Bus 服务当作真实 GNOME 门户 UI 已验证。Wayland 的快捷键仍需要在系统中显式绑定，本轮没有实现 GlobalShortcuts 门户。原生无依赖窗口层的显示还由 Avalonia 与 XWayland/桌面环境负责。
