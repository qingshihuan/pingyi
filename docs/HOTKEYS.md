# 截图快捷键 / Capture shortcuts (v0.5.2)

Linux 默认：`Ctrl+Shift+D`。Windows 默认保持 `Ctrl+Alt+D`。

## 自定义

打开 **设置 → 外观与启动**。可在截图快捷键输入框手动输入，或点击 **录入快捷键**，按住 Ctrl、Alt、Shift 中的一种或多种，再按一个 A–Z 字母或主键盘 0–9 数字。松开组合后填入输入框；按 Esc 或点击取消不更改原快捷键。暂不支持 Super/Meta、功能键、数字小键盘或多段快捷键。

点击 **保存并应用** 才会保存并切换绑定；**恢复默认** 同样需要保存。录入期间暂时释放屏译自己的全局绑定，结束或取消后恢复。格式错误不会释放现有绑定；新组合被占用、或设置文件保存失败时尝试恢复原绑定。恢复失败会保留错误提示；截图按钮仍可用。主窗口和托盘显示在保存后更新，重启保留自定义组合。

旧版 Linux 的已知默认组合（schema 8 及以前的 `Ctrl+Alt+D`、schema 9 及以前的 `Ctrl+Alt+Shift+D`）会迁移为新默认。其他组合和有显式自定义标记的设置保留。旧版没有自定义标记，因此无法区分“曾手动选成与旧默认完全相同的值”和未更改的默认值；前者需要重新设置一次。Windows 的旧默认不变。

## 与系统或其他软件冲突

2026-09-22 查阅的 Ubuntu/GNOME 官方常用系统快捷键表没有列出 `Ctrl+Shift+D`，因此本版本采用该组合，但这不是对所有 Linux 发行版、桌面扩展和用户自定义配置的无冲突保证。X11 通过实际注册检测其他全局占用；Wayland 由桌面系统处理绑定和冲突。

Chrome 在 Windows/Linux 上用 `Ctrl+Shift+D` 收藏所有标签页。它是应用快捷键，而不是系统保留键；屏译全局注册后可能优先收到该组合，从而覆盖 Chrome 操作。需要保留浏览器操作时请改用其他组合。

资料：
- [Ubuntu 常用快捷键](https://help.ubuntu.com/stable/ubuntu-help/shell-keyboard-shortcuts.html.en)
- [GNOME 常用快捷键](https://help.gnome.org/gnome-help/shell-keyboard-shortcuts.html)
- [Chrome 快捷键](https://support.google.com/chrome/answer/157179?hl=en)

## Wayland

本版本在应用中保存的是**首选组合**，不是已成功注册全局快捷键的声明。请复制设置页的截图命令，在桌面 **设置 → 键盘 → 自定义快捷键** 中为该命令绑定相同组合；修改应用中的组合后也需要同步修改桌面绑定。屏译不会私自改动系统键盘设置，无法验证系统绑定是否完成。也可始终使用主界面截图按钮和桌面菜单截图入口。

## English

Linux defaults to `Ctrl+Shift+D`; Windows keeps `Ctrl+Alt+D`. In **Settings → Appearance & startup**, type a combination or select **Record shortcut**, then **Save and apply**. **Restore default** fills the platform default and also requires saving. Recording supports Ctrl/Alt/Shift plus one A–Z or top-row 0–9 key; Escape cancels. The active binding is released during recording and restored afterwards; failed binding/persistence attempts restore the prior shortcut where possible. Custom settings survive restart.

Ubuntu/GNOME's documented common system shortcuts do not list this combination; user bindings, extensions and other desktops can differ. Chrome uses it to bookmark all tabs, so a global capture binding can take precedence. On Wayland the application only saves the preferred combination: bind the provided capture command in the desktop keyboard settings separately. No system configuration is silently changed.
