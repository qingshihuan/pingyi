# Windows：升级、旧版与待重启处理

适用范围：当前正式版 v0.7.0。本文根据当前安装脚本及 Inno Setup 官方行为核对；不是已经加入新安装器功能的公告。

## 是否先卸载旧版？

**旧完全版通常不需要先卸载。**先从系统托盘退出旧版，再运行新完全版安装程序，沿用原安装目录完成覆盖升级。界面、模式或品牌名称变化，并不等于安装身份变化。

v0.5.2 完全版与 v0.7.0 的安装身份均为 `{AD4A31EC-4A26-41B1-B86A-D4A7360C8687}`，默认目录均为 `PingYi Complete`。Inno Setup 通过 AppId 识别同一应用，默认重新使用已有安装目录；当前 `[Files]` 会更新新版本包含的程序文件。不需要先运行卸载器，也不会因此自动下载模型。

已有完全版的模型、配置及凭据存储仍在原有数据位置。应用启动时按 schema 11 迁移设置，不强制重设提供商、语言或快捷键；被移除的纠错 OCR ID 转为直接视觉 OCR。旧用户不会强制弹出首次引导，可在主页打开“模型配置引导”。重要配置建议先自行备份；不要把备份或凭据上传到仓库。

## 旧标准版是另一条路径

旧标准版使用不同 AppId `{CB72A4A2-277B-4763-9AC2-DF0B17107579}` 和 `PingYi` 目录，不能当作同一个完全版进行覆盖。当前安装包不会自动卸载它，也不会自动迁移其数据和凭据。

可以先退出旧标准版，再安装完全版、配置并确认可用；确认不再使用旧版后再卸载旧程序。保留需要的数据，不要同时运行两版争抢快捷键。程序内的“轻量模式”不是旧标准版，也不是一个独立安装包。

## 安装包已经自动做什么？

- 对同一 AppId 的完全版复用已有安装目录和卸载记录，并覆盖新包包含的文件。
- Inno Setup 的 Restart Manager 默认会检测需要更新的文件是否被应用占用；交互安装时可提示用户允许关闭相关进程。是否能关闭仍取决于进程行为，不能保证托盘程序和模型进程一定自行退出，提前退出最稳妥。
- 应用启动时负责兼容设置迁移；这是应用逻辑，不是卸载器重建数据。

当前没有自动卸载旧标准版、跨版本目录清扫、删除用户模型或强制杀进程的自定义代码。不能把默认覆盖安装称为已经实现了“任意旧版一键无损迁移”。新包不再包含的旧文件也不会仅因 `[Files]` 更新就自动全部删除；未来需要清理时应列出已确认废弃的精确路径，而不是清空整个安装目录。

不建议为“新版差异大”而默认先卸载。自动卸载再安装会增加配置迁移、失败回滚及待重启处理的风险。需要增加跨安装身份迁移时，应单独实现可确认、可回退的流程，不能假定旧版数据已经转移。

## 安装前要求重启，选“以后重启”为什么会退出？

这是“Preparing to Install”之前的拦截，不是安装完成后询问重启。Inno Setup 检查到待重启的文件重命名／删除操作与本次目标文件匹配时，会阻止安装。选择以后重启意味着退出安装；不能把它作为忽略警告继续安装的选项。

先保存工作，从 Windows 开始菜单选择“重启”，然后重新运行安装包。不要清空整个 `PendingFileRenameOperations` 注册表值，也不要强制覆盖正在等待重启处理的旧文件。

暂时不能重启时，可尝试把对应的 `*-win-x64.zip` **完整解压到新的独立目录**再运行 `PingYi.App.exe`，不要解压覆盖受影响的旧目录。这不会修复系统待重启状态，也不表示免除底层运行依赖。

重启后仍受阻，可记录安装日志：

```powershell
.\PingYi-Complete-0.7.0-win-x64-setup.exe /NORESTART "/LOG=$env:TEMP\ScreenInsight-setup.log"
```

`/NORESTART` 防止安装器自行重启，不跳过前置检查。日志里搜索 `Found pending rename or delete that matches one of our files:` 以定位实际文件。日志可能含用户目录，分享前先检查并打码。

## 官方依据与核对范围

仓库：`packaging/windows/PingYi.iss` 的 v0.5.2／v0.7.0；`src/PingYi.Infrastructure/AppDataPaths.cs`、`JsonSettingsStore.cs`、`src/PingYi.Core/AppSettings.cs`。

- AppId: https://jrsoftware.org/ishelp/topic_setup_appid.htm
- 复用已有目录: https://jrsoftware.org/ishelp/topic_setup_usepreviousappdir.htm
- 关闭占用程序: https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm
- 命令行与日志: https://jrsoftware.org/ishelp/topic_setupcmdline.htm
- 对应打包器的前置检查实现: https://github.com/jrsoftware/issrc/blob/is-6_7_1/Projects/Src/Setup.MainFunc.pas

本次没有在用户电脑执行卸载、重启、注册表编辑或进程终止，也没有修改安装器或发布新软件版本。
