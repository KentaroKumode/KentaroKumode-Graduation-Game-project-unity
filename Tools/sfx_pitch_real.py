"""音をしり上がりにピッチアップする (再生速度を、 だんだん上げる。 音程が上がり、 そのぶん、 音は少し短くなる)。
使い方: python Tools/sfx_pitch_real.py <入力wav> <出力wav> <終わりの速度比 (例 1.4 = 約 +5.8 半音)> [<始まりの速度比 既定 1.0>]
"""
import sys
import numpy as np, soundfile as sf
src, dst, r1 = sys.argv[1], sys.argv[2], float(sys.argv[3]); r0 = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0
x, sr = sf.read(src); x = x if x.ndim == 1 else x[:, 0]
# 出力の i 番目のサンプルは、 入力の位置 p(i) を読む。 p の進み (速度) は r0 → r1 へ直線で上がる
n_est = int(len(x) / ((r0 + r1) / 2) * 1.2); i = np.arange(n_est); rate = r0 + (r1 - r0) * np.clip(i / (len(x) / ((r0 + r1) / 2)), 0, 1)
p = np.cumsum(rate); keep = p < len(x) - 1; p = p[keep]
y = np.interp(p, np.arange(len(x)), x)
m = int(0.012 * sr); y[-m:] *= np.linspace(1, 0, m)
y = y / (np.max(np.abs(y)) + 1e-9) * 10 ** (-6 / 20)
sf.write(dst, y.astype(np.float32), sr, subtype='PCM_16'); print('保存', dst, '%.2f 秒 → %.2f 秒、 ピッチ %+.1f 半音まで' % (len(x) / sr, len(y) / sr, 12 * np.log2(r1 / r0)))
