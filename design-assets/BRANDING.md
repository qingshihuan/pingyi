# 截屏释义 · 品牌定稿

2026-09-28 用户确认使用原版图形：原绿色区域改为蓝色，四角取景边框和右侧原深蓝区域统一改为黑色，保留橙色与白色。中文名称为 **截屏释义**，英文显示名为 **Screen Insight**。

## 资源

- `screen-insight-icon-source.png`：1254 × 1254 RGBA 透明背景定稿源图，由原版图标经图像编辑生成。
- `src/PingYi.App/Assets/screen-insight-icon-512.png`：桌面界面／文档资源。
- `src/PingYi.App/Assets/screen-insight-icon.png`：256 px 桌面资源。
- `src/PingYi.App/Assets/screen-insight-icon.ico`：16、24、32、48、64、128、256 px Windows 图标。
- `browser-extension/icon-*.png`：16、32、48、128 px 浏览器图标。

最终编辑提示词要点：只将四角取景边框及右侧深蓝形状改为纯黑；严格保持已确认图标的轮廓、构图、蓝色、橙色、白色和透明背景，不增加新符号、文字、阴影或底板。源图已由用户批准；导出脚本只缩放及编码，不重新绘制或改色。

## 重建尺寸

使用 Node.js 和 sharp，执行 `node scripts/build-brand-icons.cjs`。sharp 可由本地开发环境提供，或通过 `PINGYI_SHARP_MODULE` 指定模块路径。它只用于开发期导出，不加入应用或插件运行依赖。

## 展示与兼容

界面使用 `src/PingYi.App/Styles/ApplePalette.axaml` 的蓝色浅／深主题。插件 `theme.css` 与网页翻译浮窗使用同一套颜色。深色标题栏的图标放在白色圆角底托上以保留黑边辨识度；资产本身保持透明背景。

名称替换只影响对用户显示的名称。命名空间、可执行文件名、GitHub 地址、原生消息主机、扩展公钥／ID、配置及模型目录、凭据存储标识继续沿用 PingYi，已有设置无需迁移。
