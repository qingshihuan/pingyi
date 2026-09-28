# 截屏释义 · 视频中心

两支视频以 **v0.7.0** 为界面基准，用 Remotion 制作。均为 **1920×1080、30fps、H.264／AAC**，中文字幕和原创配乐，**无旁白**。每支视频还附独立 SRT 字幕和封面。

## 软件介绍 · 60 秒

[![截屏释义软件介绍](intro-poster.jpg)](screen-insight-intro-zh-CN.mp4)

**[播放／下载 MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4)** · [字幕](screen-insight-intro-zh-CN.srt)

核心表达：把轻量大模型部署在本机，让文字翻译、图片理解和参考提示词成为桌面动作；二维码则使用独立本地解码器。强调一次截图、自动推荐、同图手动纠正和轻量模式，而不是承诺所有任务都由模型执行或永远不会误判。

## 首次设置 · 1 分 50 秒

[![截屏释义首次设置](setup-poster.jpg)](screen-insight-setup-zh-CN.mp4)

**[播放／下载 MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4)** · [字幕](screen-insight-setup-zh-CN.srt)

教程依次展示：选择程序包、旧完全版覆盖升级、首次模型与后端选择、一键下载配置、已有本机服务和轻量模式、自动截图、手动纠正，以及下载和待重启排障。下载进度只说明阶段顺序，不模拟或承诺实际速度。

GitHub 或当前浏览器不提供内嵌播放时，请用上面的 MP4 链接下载后播放。两个 MP4 均保存在仓库，不依赖会过期的 Actions 工件链接。也可将本目录和文件下载后，用本地 HTTP 服务打开 [双视频播放页](index.html)。

## 维护与来源

[Remotion 可编辑源码](../../media/remotion/README.md) · [分镜／字幕脚本](../../media/remotion/src/storyboard.json) · [素材与事实来源](SOURCES.md) · [渲染清单](render-manifest.json) · [SHA-256](SHA256SUMS.txt) · [Windows 升级说明](../WINDOWS_UPGRADE.md)

**演示边界：**画面包含真实 Avalonia 控件渲染与明确标注的合成示例，不使用私人桌面内容，也没有把模型下载／推理做成实测录屏。模型和硬件选项以实际软件为准。当前仅发布完全版，“基础”和“轻量”是程序内的模式。本次视频更新不改动 v0.7.0 安装器，也不发布新的软件版本。
