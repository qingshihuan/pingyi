# 自定义截图快捷键 / Custom capture shortcuts

## 默认组合与冲突

从 v0.5.2 起，Linux 默认使用 `Ctrl+Shift+D`；Windows 保持 `Ctrl+Alt+D`。

Ubuntu/GNOME 官方常用桌面快捷键表没有列出 `Ctrl+Shift+D`，因此采用它作为 Linux 默认候选，并不等于所有 Linux 桌面、用户配置和应用都没有占用。Firefox 的“收藏所有标签页”也使用此组合；应用内快捷键通常不会造成 X11 全局注册失败，但可能被全局截图键截获。需要该浏览器操作时，请为屏译选择另一组合。

X11/Windows 在应用快捷键时实际注册并检查占用。冲突时保留错误提示，恢复旧组合；按钮截图不依赖注册成功。这个检查只反映当前注册状态，不是对所有程序内部快捷键的扫描，也无法保证未来启动的程序不会影响按键。

参考：
- Ubuntu: https://help.ubuntu.com/stable/ubuntu-help/shell-keyboard-shortcuts.html.en
- GNOME: https://help.gnome.org/gnome-help/shell-keyboard-shortcuts.html
- Firefox: https://support.mozilla.org/en-US/kb/keyboard-shortcuts-perform-firefox-tasks-quickly

## 设置方法

打开 **设置 → 外观与启动 → 截图快捷键**。可直接输入组合，也可点击“录制快捷键”后按下组合并松开主键。支持 `Ctrl`、`Alt`、`Shift` 中至少一个修饰键，加一个 `A–Z` 或 `0–9`；大小写和修饰键顺序自动规范化。当前原生后端不支持 Super/Win、功能键、标点键或多段序列，输入框会明确提示无效格式。

录制期间暂停屏译自己注册的全局按键，防止录入当前组合时误触截图。松开主键后恢复原绑定；录制结果只是待应用值。Esc、Tab、取消按钮、切换到其他控件、窗口失活或关窗都会取消录制并恢复原组合。Ctrl+S 在录制期间是待录入组合，不会触发设置保存。

点击 **应用快捷键** 后才注册并单独保存该组合；不会提交其他尚未保存的表单输入。新组合注册失败，或设置写入失败时，恢复之前的按键。若恢复也失败，则明确显示错误，而不是宣称旧键已经可用。应用成功后主窗口和托盘提示同步更新，重启后继续使用保存的组合。

**恢复默认** 只修改待应用值，还需点击“应用快捷键”。普通的“保存并应用”仍可保存整个设置表单。

## Wayland

Wayland 的实际全局绑定由桌面系统管理。此版本没有实现 XDG GlobalShortcuts portal，也不会修改 Ubuntu/GNOME/KDE 的系统键盘配置。

此页允许编辑和“保存偏好快捷键”，但保存成功不代表桌面已经注册了组合。复制同页的截图命令，在系统 **键盘 → 自定义快捷键** 中绑定 `Ctrl+Shift+D` 或其他可用组合。此命令会使用当前安装版本，并带 `--capture`，支持首次启动和向已有进程转发。

系统已占用的组合可能在到达应用的录制控件之前被截获；这种情况下直接输入偏好组合，再在系统设置中调整实际绑定。屏译无法暂停由桌面系统或其他程序持有的绑定。系统截图门户和主界面按钮仍可独立使用。

## 升级兼容与隐私

Schema 10 迁移未标记自定义的旧 Linux 默认值：schema 8 及更早的 `Ctrl+Alt+D`，以及 schema 9 及更早的 `Ctrl+Alt+Shift+D`。其他组合保留；schema 9 中的 `Ctrl+Alt+D` 也保留，因为在上一版中它已可能是用户主动选择。Windows 默认不变。

新增 `hotkeyIsCustomized` 记录显式自定义。旧版本没有该标记，因此“用户恰好选了与旧默认完全相同的键”无法从旧配置中辨别；这种情况会随默认值迁移，可在新版本重新选择并应用。该限制不影响其他自定义组合、端点、模型、语言、凭据或启动偏好。

录制仅在用户主动打开的设置窗口中处理快捷键事件。不会记录键盘输入历史、截图正文、识别文本、译文或凭据；默认本地 OCR/翻译仍保持离线，不增加遥测或运行时依赖。

## 验证范围

单元测试覆盖格式规范化、无效输入、平台/版本迁移、先注册后保存、注册或持久化失败的回滚以及回滚失败报告。Avalonia 测试覆盖控件、语言、校验和录制取消。Linux 原生回归在隔离 Xvfb/Openbox 中操作真实应用，覆盖默认迁移、点击录制/应用、自定义键触发、旧键释放、进程重启后持久化和恢复默认；另以真实 X11 抢占制造冲突并验证回滚后旧键可触发。

这些测试不等同于所有物理桌面、输入法、键盘布局、多屏混合 DPI 或显卡环境的实机验收。门户 fixture 仅测试 D-Bus 协议行为，不代表 GNOME/Wayland 系统授权界面的实机验收。

## English

Linux defaults to **Ctrl+Shift+D** in v0.5.2; Windows stays on **Ctrl+Alt+D**. The combination is not listed in Ubuntu/GNOME's common desktop defaults, but user bindings and application-local commands may overlap: Firefox uses it to bookmark all tabs. Native registration checks global grabs, not every application's internal commands.

Open **Settings → Appearance & startup → Capture shortcut**. Type a combination or choose **Record shortcut**, press and release Ctrl/Alt/Shift plus A–Z or 0–9, then choose **Apply shortcut**. Recording suspends only PingYi's own registered grab. Esc, Tab, loss of focus or closing cancels and restores it. **Restore default** changes the draft; Apply commits it. Registration or file-write failure restores the previous binding, and rollback failures are reported. No restart is required; the saved combination persists across restarts.

On **Wayland**, **Save preferred key** stores a preference only. Copy the capture command and bind it in your desktop's keyboard settings. Existing compositor grabs can intercept recording; type the preferred combination instead. This release does not implement GlobalShortcuts portal or alter system keybindings. The screenshot button remains independent.

Legacy known defaults migrate; other custom bindings and unrelated preferences are preserved. Old versions did not record whether a default-looking combination was explicitly customized, so that case cannot be distinguished during migration. New explicit customizations carry a flag. No keyboard history, telemetry, new runtime dependencies or changes to offline OCR/translation are introduced.
