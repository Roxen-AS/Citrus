#!/usr/bin/env python3
"""Independent real-file smoke test for the exact shipping PE binaries (Windows, or explicit Wine)."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import wave

parser = argparse.ArgumentParser()
parser.add_argument('--directory', type=Path, required=True)
parser.add_argument('--wine', type=Path)
args = parser.parse_args()
root = args.directory.resolve()


def native_path(path):
    path = str(Path(path).resolve())
    return 'Z:' + path.replace('/', '\\') if args.wine else path


def run(tool, arguments, check=True):
    command = ([str(args.wine)] if args.wine else []) + [str(root / (tool + '.exe'))] + arguments
    result = subprocess.run(command, capture_output=True, text=True, timeout=120)
    if check and result.returncode:
        raise RuntimeError(f'{tool} exit {result.returncode}: {result.stderr[-8000:]}')
    return result


def encode(arguments, output):
    return run('ffmpeg', ['-hide_banner', '-v', 'error', '-nostdin', '-y', '-protocol_whitelist', 'file,pipe'] + arguments + ['-threads', '2', native_path(output)])


manifest = root.parent / 'media-sha256.json'
published = manifest.exists()
if not published: manifest = root.parent / 'sha256.json'
for file, expected in json.loads(manifest.read_text()).items():
    relative = file.replace('win-x64/', 'media/', 1) if published else file
    actual = hashlib.sha256((root.parent / relative).read_bytes()).hexdigest()
    assert actual == expected, f'Hash mismatch: {file}'
print('PASS: all distributed artifact SHA256 hashes', flush=True)
version = run('ffmpeg', ['-version']).stdout
assert all(flag in version for flag in ['--enable-shared', '--disable-static', '--disable-gpl', '--disable-nonfree', '--disable-network', '--disable-autodetect', '--enable-mediafoundation', '--enable-d3d11va'])
print(version.splitlines()[0], flush=True)
protocols = run('ffmpeg', ['-hide_banner', '-protocols']).stdout
listed = {line.strip() for line in protocols.splitlines() if line.startswith('  ')}
assert listed == {'file', 'pipe'}, protocols
print('PASS: protocols exactly file, pipe', flush=True)
encoders = run('ffmpeg', ['-hide_banner', '-encoders']).stdout
expected_encoders = ['mpeg4', 'aac', 'libvpx-vp9', 'libopus', 'libvorbis', 'libmp3lame', 'flac', 'pcm_s16le', 'pcm_s16be', 'wmv2', 'wmav2', 'gif', 'h264_mf']
names = {line.split()[1] for line in encoders.splitlines() if len(line.split()) >= 2 and len(line.split()[0]) == 6}
assert set(expected_encoders) <= names, set(expected_encoders) - names
assert 'libx264' not in names and 'libx265' not in names
print('PASS: required encoder catalogue; no software x264/x265', flush=True)

with tempfile.TemporaryDirectory(prefix='Citrus media ü ') as temporary:
    temp = Path(temporary)
    raw = temp / "-pattern ' 日本語.yuv"
    tone = temp / "-tone ' café.wav"
    picture = bytearray()
    for frame in range(24):
        picture.extend(bytes(200 if (x // 10 + frame) % 5 == 0 else 40 for y in range(120) for x in range(160)))
        picture.extend(bytes([128]) * (160 * 120 // 2))
    raw.write_bytes(picture)
    with wave.open(str(tone), 'wb') as wav:
        wav.setparams((1, 2, 48000, 0, 'NONE', 'not compressed'))
        wav.writeframes(b''.join(struct.pack('<h', int(4000 * math.sin(2 * math.pi * 440 * n / 48000))) for n in range(48000)))
    video_input = ['-f', 'rawvideo', '-pixel_format', 'yuv420p', '-video_size', '160x120', '-framerate', '24', '-i', native_path(raw)]
    presets = [
        ('mp4', 'mpeg4', 'aac'), ('mov', 'mpeg4', 'aac'), ('mkv', 'mpeg4', 'aac'), ('webm', 'libvpx-vp9', 'libopus'),
        ('avi', 'mpeg4', 'libmp3lame'), ('wmv', 'wmv2', 'wmav2'), ('gif', 'gif', None),
        ('mp3', None, 'libmp3lame'), ('m4a', None, 'aac'), ('wav', None, 'pcm_s16le'), ('flac', None, 'flac'),
        ('ogg', None, 'libvorbis'), ('opus', None, 'libopus'), ('aiff', None, 'pcm_s16be'), ('wma', None, 'wmav2')]
    for extension, video, audio in presets:
        options = video_input.copy() if video else []
        if audio:
            options += ['-i', native_path(tone), '-c:a', audio]
            if audio in {'wmav2', 'aac', 'libopus', 'libmp3lame', 'libvorbis'}:
                options += ['-ar', '48000', '-ac', '2', '-b:a', '192k']
        if video:
            options += ['-c:v', video]
            if video == 'libvpx-vp9': options += ['-deadline', 'realtime', '-cpu-used', '8']
        output = temp / ('copy.' + extension)
        encode(options, output)
        metadata = json.loads(run('ffprobe', ['-v', 'error', '-show_streams', '-show_format', '-of', 'json', native_path(output)]).stdout)
        assert .85 <= float(metadata['format']['duration']) <= 1.3, metadata
        kinds = {s['codec_type'] for s in metadata['streams']}
        assert ('video' in kinds) == bool(video), metadata
        assert ('audio' in kinds) == bool(audio), metadata
        print('PASS:', extension, [(s['codec_type'], s['codec_name']) for s in metadata['streams']], flush=True)
    mf = run('ffmpeg', ['-hide_banner', '-v', 'error', '-y'] + video_input + ['-frames:v', '2', '-c:v', 'h264_mf', '-hw_encoding', '0', native_path(temp / 'mf.mp4')], check=False)
    print('OPTIONAL h264_mf encode:', 'available' if mf.returncode == 0 else 'unavailable; native MPEG-4 fallback required', flush=True)
    if mf.returncode:
        print(mf.stderr[-1200:], flush=True)
print('PASS: exact Windows media binary smoke (' + ('Wine compatibility only' if args.wine else 'native Windows') + ')', flush=True)
