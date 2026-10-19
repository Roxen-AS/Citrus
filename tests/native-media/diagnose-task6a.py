"""Retain raw backend evidence for Task 6a fixtures; never declares product tests passed."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import platform
import shutil
import struct
import subprocess
import wave

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True, type=Path)
parser.add_argument('--ffprobe', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
ffmpeg, ffprobe, root = args.ffmpeg.resolve(), args.ffprobe.resolve(), args.output.resolve()
root.mkdir(parents=True, exist_ok=False)
results = []


def run(label, executable, arguments):
    command = [str(executable), *map(str, arguments)]
    done = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
    (root / (label + '.stdout')).write_bytes(done.stdout)
    (root / (label + '.stderr')).write_bytes(done.stderr)
    record = {'label': label, 'command': command, 'exit': done.returncode,
              'stdout_bytes': len(done.stdout), 'stderr_bytes': len(done.stderr)}
    results.append(record)
    return done


def fingerprint(path):
    data = path.read_bytes()
    return {'name': path.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'first_128_bytes_hex': data[:128].hex()}


def inspect(label, path, input_args=()):
    if not path.exists():
        return
    run(label + '-probe', ffprobe, ['-v', 'error', '-protocol_whitelist', 'file,pipe',
        *input_args, '-show_streams', '-show_format', '-show_frames', '-show_packets',
        '-of', 'json', path])
    run(label + '-pcm', ffmpeg, ['-hide_banner', '-nostdin', '-loglevel', 'info',
        '-protocol_whitelist', 'file,pipe', *input_args, '-i', path, '-map', '0:a:0?',
        '-vn', '-c:a', 'pcm_s16le', '-f', 's16le', 'pipe:1'])


def encode(label, arguments, path):
    # Match MediaProcess.RunAsync, retaining stdout progress and native diagnostics.
    run(label + '-encode', ffmpeg, ['-hide_banner', '-nostdin', '-nostats', '-loglevel',
        'error', '-y', '-progress', 'pipe:1', '-protocol_whitelist', 'file,pipe', *arguments, path])
    inspect(label, path)


run('ffmpeg-version', ffmpeg, ['-version'])
run('ffprobe-version', ffprobe, ['-version'])
raw_options = ['-f', 's16le', '-ar', '48000', '-ac', '1']
sine = root / 'sine.pcm'
sine.write_bytes(b''.join(struct.pack('<h', int(12000 * math.sin(i * math.pi / 24))) for i in range(4800)))
constant = root / 'constant.pcm'
constant.write_bytes(struct.pack('<h', 10000) * 9600)
steps = root / 'steps.pcm'
steps.write_bytes(struct.pack('<h', 10000) * 24000 + struct.pack('<h', 20000) * 24000)
for label, raw in [('sine', sine), ('constant', constant), ('steps', steps)]:
    inspect(label + '-raw', raw, raw_options)
    # An independently written WAV supplies timestamps through a supported container.
    wav = root / (label + '-written.wav')
    with wave.open(str(wav), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(48000)
        output.writeframes(raw.read_bytes())
    inspect(label + '-written', wav)

encode('sine-old', [*raw_options, '-i', sine, '-c:a', 'pcm_s16le'], root / 'sine-old.wav')
encode('sine-from-wav', ['-i', root / 'sine-written.wav', '-c:a', 'pcm_s16le'], root / 'sine-from-wav.wav')
# Probe the exact private-copy boundary implicated by the original EOF exception.
shutil.copyfile(root / 'sine-old.wav', root / 'sine-old-staged.wav')
inspect('sine-old-staged', root / 'sine-old-staged.wav')

for method in ['old-raw', 'written-wav', 'explicit-filter']:
    source_args = [*raw_options, '-i', constant] if method != 'written-wav' else ['-i', root / 'constant-written.wav']
    timing = ['-itsoffset', '0.25'] if method != 'explicit-filter' else []
    graph = 'color=black:s=16x8:r=25:d=1[v]'
    if method == 'explicit-filter':
        graph += ';[0:a]asetpts=N/SR/TB+0.25/TB[a]'
    encode('offset-mov-' + method, ['-copyts', *timing, *source_args,
        '-filter_complex', graph, '-map', '[v]', '-map', '[a]' if method == 'explicit-filter' else '0:a:0',
        '-c:v', 'mpeg4', '-c:a', 'pcm_s16le'], root / ('offset-' + method + '.mov'))

for offset in ['0', '10', '-0.25']:
    for video in [False, True]:
        for method in ['old-raw', 'written-wav', 'explicit-filter']:
            label = ('av' if video else 'audio') + '-' + offset + '-' + method
            source_args = [*raw_options, '-i', steps] if method != 'written-wav' else ['-i', root / 'steps-written.wav']
            timing = ['-itsoffset', offset] if method != 'explicit-filter' else []
            graph = 'color=black:s=16x8:r=25:d=2[v]' if video else ''
            if method == 'explicit-filter':
                graph += (';' if graph else '') + '[0:a]asetpts=N/SR/TB+(' + offset + ')/TB[a]'
            command = ['-copyts', *timing, *source_args]
            if graph:
                command += ['-filter_complex', graph]
            if video:
                command += ['-map', '[v]', '-map', '[a]' if method == 'explicit-filter' else '0:a:0', '-c:v', 'mpeg4']
            elif method == 'explicit-filter':
                command += ['-map', '[a]']
            encode(label, [*command, '-c:a', 'pcm_s16le', '-avoid_negative_ts', 'disabled'], root / (label + ('.mkv' if video else '.mka')))

colors = root / 'colors.bgra'
colors.write_bytes(b''.join(bytes([c, c, c, 255]) * 128 for c in [20, 60, 100, 160, 220, 240]))
pts = 'settb=1/90000,setpts=900000+if(eq(N\\,0)\\,0\\,if(eq(N\\,1)\\,3600\\,if(eq(N\\,2)\\,9000\\,if(eq(N\\,3)\\,20700\\,if(eq(N\\,4)\\,41400\\,45000)))))'
for mode in ['vfr', 'passthrough']:
    path = root / ('vfr-' + mode + '.mp4')
    run('vfr-' + mode + '-encode', ffmpeg, ['-hide_banner', '-nostdin', '-loglevel', 'info', '-y',
        '-f', 'rawvideo', '-pixel_format', 'bgra', '-video_size', '16x8', '-framerate', '25', '-i', colors,
        '-vf', pts, '-fps_mode', mode, '-enc_time_base', '1:30000', '-c:v', 'mpeg4', '-q:v', '1', '-video_track_timescale', '90000', path])
    run('vfr-' + mode + '-probe', ffprobe, ['-v', 'error', '-show_streams', '-show_frames', '-show_packets', '-of', 'json', path])

(root / 'report.json').write_text(json.dumps({'platform': platform.platform(),
    'executables': [fingerprint(ffmpeg), fingerprint(ffprobe)], 'commands': results,
    'files': [fingerprint(p) for p in sorted(root.iterdir()) if p.is_file()]}, indent=2), encoding='utf-8')
print('Raw diagnostics retained in', root)
print('Command failures:', sum(r['exit'] != 0 for r in results), '(expected for failing fixtures; inspect report.json)')
