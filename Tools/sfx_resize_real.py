"""音の長さを変える。 短くする: 頭から指定の長さまで取り、 終わりをなだらかに消す。 長くする: 安定している部分 (a〜b 秒) を、 つなぎ目 (等パワーのクロスフェード) を作って 1 回足し、 元の終わり (自然な減衰) は、 そのまま残す。
使い方: python Tools/sfx_resize_real.py <入力wav> <出力wav> <長さ(秒)> [<足す部分の始まり 既定 1.0 秒>]
"""
import sys
import numpy as np, soundfile as sf
src, dst, T = sys.argv[1], sys.argv[2], float(sys.argv[3]); a_s = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0
x, sr = sf.read(src); x = x if x.ndim == 1 else x[:, 0]; T_n = int(T * sr); xf = int(0.1 * sr)
if T_n <= len(x):
    y = x[:T_n].copy(); n = min(int(0.25 * sr), len(y)); y[-n:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, n)))      # 終わりをなだらかに
else:
    ext = T_n - len(x); a = int(a_s * sr); b = a + ext + xf                                                          # 足す部分 [a, b]
    head = x[:b].copy(); tail = x[a:]
    w = np.sin(np.linspace(0, np.pi / 2, xf)); head[-xf:] = head[-xf:] * np.cos(np.linspace(0, np.pi / 2, xf)) + tail[:xf] * w     # つなぎ目 (等パワー)
    y = np.concatenate([head, tail[xf:]])
y = y / (np.max(np.abs(y)) + 1e-9) * 10 ** (-6 / 20)
sf.write(dst, y.astype(np.float32), sr, subtype='PCM_16'); print('保存 %s  %.2f 秒 → %.2f 秒' % (dst, len(x) / sr, len(y) / sr))
