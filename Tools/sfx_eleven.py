"""ElevenLabs の効果音生成で、 画面の外の音の候補を作る (2026-10-25)。

使い方: python Tools/sfx_eleven.py <名前> <長さ(秒)> "<説明文 A>" "<説明文 B>" ...
  → Tools/sfx_real/<名前>/<名前>_A.wav, _B.wav ... に、 加工済みの候補を保存する。 (加工: 前後の無音を切る・音量をそろえる・終わりをなだらかに)
キー: 環境変数 ELEVENLABS_API_KEY (チャットやファイルには書かない)。
費用の目安: 長さ指定あり = 1 秒あたり 20 クレジット。
"""
import json, os, subprocess, sys, tempfile, urllib.error, urllib.request
import numpy as np
import soundfile as sf

ROOT = os.path.dirname(os.path.abspath(__file__))
FFMPEG = 'ffmpeg'


def generate(text, dur, influence=0.6):
    key = os.environ['ELEVENLABS_API_KEY']
    body = json.dumps({'text': text, 'duration_seconds': dur, 'prompt_influence': influence}).encode()
    req = urllib.request.Request('https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128', data=body,
                                 headers={'xi-api-key': key, 'Content-Type': 'application/json'})
    return urllib.request.urlopen(req, timeout=120).read()


def process(mp3_bytes, out_wav, peak_db=-6.0, head_ms=4, tail_db=-48, tail_ms=40):
    with tempfile.NamedTemporaryFile(suffix='.mp3', delete=False) as f: f.write(mp3_bytes); tmp = f.name
    subprocess.run([FFMPEG, '-v', 'error', '-y', '-i', tmp, '-ac', '1', '-ar', '44100', tmp + '.wav'], check=True)
    x, sr = sf.read(tmp + '.wav'); os.remove(tmp); os.remove(tmp + '.wav')
    a = np.abs(x); pk = a.max()
    on = np.where(a > pk * 10 ** (-38 / 20))[0]; end = np.where(a > pk * 10 ** (tail_db / 20))[0]
    s0 = max(0, on[0] - int(head_ms / 1000 * sr)); s1 = min(len(x), end[-1] + int(tail_ms / 1000 * sr))
    x = x[s0:s1]; x = x / (np.max(np.abs(x)) + 1e-9) * 10 ** (peak_db / 20)
    n = int(0.02 * sr); x[-n:] *= np.linspace(1, 0, n)
    sf.write(out_wav, x.astype(np.float32), sr, subtype='PCM_16')
    return len(x) / sr


if __name__ == '__main__':
    name, dur = sys.argv[1], float(sys.argv[2]); prompts = sys.argv[3:]
    d = os.path.join(ROOT, 'sfx_real', name); os.makedirs(d, exist_ok=True)
    for i, text in enumerate(prompts):
        tag = 'ABCDEFGHIJKLMNOP'[i]; out = os.path.join(d, '%s_%s.wav' % (name, tag))
        try:
            length = process(generate(text, dur), out)
            print('%s: 保存 %s (%.2f 秒)  | %s' % (tag, os.path.basename(out), length, text))
        except urllib.error.HTTPError as e:
            print(tag, 'HTTP', e.code, e.read().decode('utf-8', 'ignore')[:200])
