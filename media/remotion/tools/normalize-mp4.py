"""Normalize each fresh Remotion export for portable playback.
Convert full-range pixels when needed, master the original score, and write moov first.
"""
from pathlib import Path
import json
import subprocess
import sys

if len(sys.argv) != 2:
    raise SystemExit('Usage: python tools/normalize-mp4.py path.mp4')
path = Path(sys.argv[1]).resolve()
info = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(path)], text=True))
video = next(s for s in info['streams'] if s['codec_type'] == 'video')
assert video['codec_name'] == 'h264', video
output = path.with_name(path.stem + '.normalized.mp4')
args = ['ffmpeg', '-v', 'error', '-y', '-i', str(path), '-map', '0:v:0', '-map', '0:a:0']
if video.get('color_range') == 'pc' or video['pix_fmt'] == 'yuvj420p':
    args += ['-vf', 'scale=in_range=pc:out_range=tv,format=yuv420p', '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-pix_fmt', 'yuv420p', '-color_range', 'tv']
    for key, flag in [('color_space', '-colorspace'), ('color_transfer', '-color_trc'), ('color_primaries', '-color_primaries')]:
        if video.get(key) and video[key] != 'unknown':
            args += [flag, video[key]]
else:
    assert video['pix_fmt'] == 'yuv420p', video
    args += ['-c:v', 'copy']
# Gentle instrumental level; normalization limits peaks and preserves the fades.
args += ['-af', 'loudnorm=I=-22:TP=-2:LRA=9', '-c:a', 'aac', '-b:a', '128k', '-ar', '48000', '-movflags', '+faststart', str(output)]
subprocess.run(args, check=True)
check = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(output)], text=True))
stream = next(s for s in check['streams'] if s['codec_type'] == 'video')
assert stream['pix_fmt'] == 'yuv420p', stream
assert stream['nb_frames'] == video['nb_frames'], stream
output.replace(path)
