"""生成した候補 (Tools/sfx_real/<名前>/*.wav) を、 重ねて 1 つの音にする。
使い方: python Tools/sfx_mix_real.py <名前> <出力名> A=1.0 B=0.8@200 D=0.6   (別のフォルダの候補は <フォルダ名>/<記号>=音量 の形: pot_open/D=0.8 pot_open_slide/B=1.0@50)
(旧: python Tools/sfx_mix_real.py <名前> <出力名> A=1.0 B=0.8@200 D=0.6   (候補の記号=音量 [@遅らせる ms]。 頭 (立ち上がり) を揃えてから、 遅らせる分だけずらして重ねる。 音量は、 最後に -6dB に揃える)
"""
import os, sys
import numpy as np, soundfile as sf

ROOT = os.path.dirname(os.path.abspath(__file__))
name, out = sys.argv[1], sys.argv[2]
parts = [a.split('=') for a in sys.argv[3:]]
sigs = []
for tag, g in parts:
    folder = name
    if '/' in tag: folder, tag = tag.split('/')
    delay_ms = float(g.split('@')[1]) if '@' in g else 0.0; g = g.split('@')[0]
    x, sr = sf.read(os.path.join(ROOT, 'sfx_real', folder, '%s_%s.wav' % (folder, tag)))
    x = x if x.ndim == 1 else x[:, 0]
    on = np.argmax(np.abs(x) > np.max(np.abs(x)) * 0.12)                 # 立ち上がりを揃える
    x = x[max(0, on - int(0.003 * sr)):]
    x = x / (np.max(np.abs(x)) + 1e-9) * float(g)
    x = np.concatenate([np.zeros(int(delay_ms / 1000 * sr)), x])             # 遅らせる
    sigs.append(x)
n = max(len(s) for s in sigs); y = np.zeros(n)
for s in sigs: y[:len(s)] += s
y = y / (np.max(np.abs(y)) + 1e-9) * 10 ** (-6 / 20)
m = int(0.02 * sr); y[-m:] *= np.linspace(1, 0, m)
os.makedirs(os.path.join(ROOT, 'sfx_real', name), exist_ok=True)
dst = os.path.join(ROOT, 'sfx_real', name, '%s_%s.wav' % (name, out))
sf.write(dst, y.astype(np.float32), sr, subtype='PCM_16'); print('保存', dst, '%.2f 秒' % (n / sr))
