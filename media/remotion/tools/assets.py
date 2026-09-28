"""Deterministic, project-original sound bed, subtitles and publication metadata.
Only synthetic documentation assets are used. No model, account or TTS API is called.
"""
from __future__ import annotations
import array
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import wave

HERE = Path(__file__).resolve().parents[1]
ROOT = HERE.parents[1]
PUBLIC = HERE / 'public'
OUT = ROOT / 'docs/videos'
STORY = json.loads((HERE / 'src/storyboard.json').read_text(encoding='utf-8'))
MOVIES = [('intro', 'screen-insight-intro-zh-CN'), ('setup', 'screen-insight-setup-zh-CN')]


def run(*args: str) -> None:
    subprocess.run(args, check=True)


def timestamp(seconds: float) -> str:
    ms = round(seconds * 1000)
    return f'{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}'


def prepare() -> None:
    PUBLIC.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    for kind, name in MOVIES:
        lines, start, idx = [], 0.0, 1
        for scene in STORY[kind]:
            slice_length = scene['seconds'] / len(scene['captions'])
            for caption in scene['captions']:
                lines.append(f'{idx}\n{timestamp(start)} --> {timestamp(start + slice_length)}\n{caption}\n')
                idx += 1
                start += slice_length
        (OUT / f'{name}.srt').write_text('\n'.join(lines), encoding='utf-8')
    # Original ambient cue: no samples, borrowed song, voice imitation or network service.
    rate = 24000
    duration = max(sum(scene['seconds'] for scene in STORY[kind]) for kind, _ in MOVIES)
    chords = [(48, 55, 59, 64), (45, 52, 55, 60), (41, 48, 52, 57), (43, 50, 55, 60)]
    melody = [0, 2, 1, 3, 2, 1, 3, 1]
    frequencies = [[440 * 2 ** ((note - 69) / 12) for note in chord] for chord in chords]
    values = array.array('h')
    for i in range(rate * duration):
        t = i / rate
        block = int(t // 8) % len(chords)
        block_t = t % 8
        envelope = min(1.0, block_t / 0.7, (8 - block_t) / 0.9)
        tones = frequencies[block]
        pad = sum(math.sin(2 * math.pi * hz * t) for hz in tones) * .015 * envelope
        note_t = t % .5
        bell_hz = tones[melody[int(t * 2) % 8]] * 2
        bell = math.sin(2 * math.pi * bell_hz * note_t) * math.exp(-note_t * 10) * min(1, note_t / .012) * .045
        beat_t = t % 2
        bass = math.sin(2 * math.pi * tones[0] / 2 * beat_t) * math.exp(-beat_t * 4) * min(1, beat_t / .015) * .05
        fade = min(1, t / 2, (duration - t) / 3)
        values.append(round(max(-1, min(1, (pad + bell + bass) * fade)) * 32767))
    if sys.byteorder != 'little':
        values.byteswap()
    with wave.open(str(PUBLIC / 'score.wav'), 'wb') as output:
        output.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
        output.writeframes(values.tobytes())
    run('ffmpeg', '-v', 'error', '-y', '-i', str(PUBLIC / 'score.wav'), '-ar', '48000', '-ac', '2', '-c:a', 'aac', '-b:a', '128k', str(PUBLIC / 'score.m4a'))
    (PUBLIC / 'score.wav').unlink()


def publish() -> None:
    entries = []
    for kind, name in MOVIES:
        path = OUT / f'{name}.mp4'
        expected = sum(scene['seconds'] for scene in STORY[kind])
        probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(path)], text=True))
        video = next(stream for stream in probe['streams'] if stream['codec_type'] == 'video')
        audio = next(stream for stream in probe['streams'] if stream['codec_type'] == 'audio')
        assert video['width'] == 1920 and video['height'] == 1080, video
        assert video['codec_name'] == 'h264' and video['pix_fmt'] == 'yuv420p', video
        assert audio['codec_name'] == 'aac', audio
        assert abs(float(probe['format']['duration']) - expected) < .2, probe
        assert 200000 < path.stat().st_size < 45 * 1024 * 1024, path.stat().st_size
        assert int(video['nb_frames']) == expected * 30, video
        entries.append({'file': path.name, 'durationSeconds': expected, 'width': 1920, 'height': 1080, 'fps': 30, 'frames': int(video['nb_frames']), 'videoCodec': 'h264', 'audioCodec': 'aac', 'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
        # One representative image per scene, made from the final encoded video.
        qa = ROOT / 'artifacts/video-qa' / kind
        qa.mkdir(parents=True, exist_ok=True)
        start = 0
        for index, scene in enumerate(STORY[kind]):
            run('ffmpeg', '-v', 'error', '-y', '-ss', str(start + min(3, scene['seconds']/2)), '-i', str(path), '-frames:v', '1', '-vf', 'scale=960:540', str(qa / f'{index:02}-{scene["id"]}.jpg'))
            start += scene['seconds']
    (OUT / 'render-manifest.json').write_text(json.dumps({'softwareVersion': STORY['productVersion'], 'softwareCommit': STORY['sourceCommit'], 'videoSourceCommit': os.environ.get('GITHUB_SHA', 'local'), 'renderer': 'Remotion 4.0.506', 'audio': 'Original synthesized instrumental; no spoken narration', 'ui': 'Native Avalonia controls, authored synthetic data; no live downloads or model performance claims', 'videos': entries}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    sums = [f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}' for p in sorted(OUT.iterdir()) if p.is_file() and p.suffix in ('.mp4', '.jpg', '.srt')]
    (OUT / 'SHA256SUMS.txt').write_text('\n'.join(sums) + '\n', encoding='utf-8')
    blocks = {
        'README.md': '''## 软件介绍与设置视频下载

**一键部署本机轻量大模型，让截图翻译、图片描述和参考提示词成为桌面常用能力。二维码由独立的本地解码器处理，不需要模型。**

| 软件介绍 · 60 秒 | 首次设置 · 1 分 50 秒 |
| --- | --- |
| [![软件介绍](docs/videos/intro-poster.jpg)](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4) | [![首次设置教程](docs/videos/setup-poster.jpg)](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4) |
| [下载介绍 MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4) | [下载设置 MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4) |

**点击封面或下方链接下载 MP4，再用本地播放器打开。GitHub 文件预览页不提供这两支视频的在线播放；“文件过大，无法显示”不代表视频损坏。**

Remotion 制作，1080p／30fps，中文字幕与原创配乐，无旁白。使用 v0.7.0 原生界面和合成示例；流程动画不是实测耗时。[下载、播放说明与源码](docs/videos/README.md) · [旧版升级与重启排障](docs/WINDOWS_UPGRADE.md)

''',
        'README.en.md': '''## Download the product and setup videos

**One-click local model setup turns screenshot translation, image description and reference prompts into desktop actions. QR codes use a separate local decoder, not the model.**

| Product introduction · 60 seconds | First-run guide · 1 minute 50 seconds |
| --- | --- |
| [![Product introduction](docs/videos/intro-poster.jpg)](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4) | [![First-run guide](docs/videos/setup-poster.jpg)](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4) |
| [Download introduction MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-intro-zh-CN.mp4) | [Download setup MP4](https://github.com/qingshihuan/pingyi/raw/refs/heads/main/docs/videos/screen-insight-setup-zh-CN.mp4) |

**Click a cover or download link to save the MP4, then open it in a local video player. The GitHub file preview is not an online player for these videos; its file-size warning does not mean the video is damaged.**

Made with Remotion: 1080p/30fps, Chinese captions and an original instrumental score, no voiceover. Native v0.7.0 UI with authored synthetic examples; process animations are not performance measurements. [Downloads, playback instructions and source](docs/videos/README.md) · [Windows upgrade and restart guidance](docs/WINDOWS_UPGRADE.md)

'''
    }
    for filename, block in blocks.items():
        path = ROOT / filename
        text = path.read_text(encoding='utf-8')
        start_marker, end_marker = '<!-- screen-insight-videos:start -->', '<!-- screen-insight-videos:end -->'
        if start_marker in text:
            before, remainder = text.split(start_marker, 1)
            _, after = remainder.split(end_marker, 1)
            text = before + after.lstrip('\n')
        anchor = '<a id="download"></a>'
        assert text.count(anchor) == 1, filename
        text = text.replace(anchor, start_marker + '\n' + block + end_marker + '\n\n' + anchor)
        if filename == 'README.md':
            text = text.replace('当前源码新增自动任务、基础／轻量模式与首次模型配置引导；已发布安装包的功能以对应 Release 说明为准。', 'v0.7.0 已加入自动任务、基础／轻量模式与首次模型配置引导；安装包功能以对应 Release 说明为准。')
        else:
            text = text.replace('The current source adds automatic tasks, Basic / Lightweight modes and first-run model setup.', 'Version 0.7.0 includes automatic tasks, Basic / Lightweight modes and first-run model setup.')
        path.write_text(text, encoding='utf-8')
    notices = ROOT / 'THIRD_PARTY_NOTICES.md'
    text = notices.read_text(encoding='utf-8')
    marker = '## Remotion documentation videos'
    if marker not in text:
        text += '\n' + marker + '''

The two v0.7.0 documentation videos use Remotion 4.0.506 with React, TypeScript and FFmpeg as isolated development tools in `media/remotion/`. Remotion retains its own license (https://www.remotion.dev/license); this does not relicense Remotion under the repository MIT license. No Remotion dependency is included in the desktop app or its installer.

The video layouts, synthesized instrumental score and illustrative text are project-authored. UI frames are rendered from actual Avalonia controls with synthetic inputs, never private screenshots, model outputs or credentials. Existing approved Screen Insight artwork is reused. Noto CJK font family names select runner-installed fonts; no font files are committed or distributed. Videos have burned-in Chinese subtitles plus SRT files, and no spoken voiceover. See `docs/videos/SOURCES.md` for provenance and limitations.
'''
        notices.write_text(text, encoding='utf-8')


if __name__ == '__main__':
    if len(sys.argv) != 2 or sys.argv[1] not in ('prepare', 'publish'):
        raise SystemExit('Usage: python media/remotion/tools/assets.py prepare|publish')
    prepare() if sys.argv[1] == 'prepare' else publish()
