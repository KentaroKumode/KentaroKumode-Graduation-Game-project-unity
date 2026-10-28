"""効果音の余韻を足す。 音が急に切れない (ブツ切りにしない) ためのもの。

余韻は、 音が終わった「後ろ」だけに足す。 音の本体には、 エコー・リバーブを掛けない。
やり方: 音の最後の約 40ms だけを取り出し、 その響き (指数で減衰する、 少し暗い雑音) を、 音の終わりから後ろへ足す。
短い音ほど短く、 長い音ほど少し長い (0.07〜0.2 秒)。
"""
import numpy as np
from scipy.signal import fftconvolve, butter, sosfilt


def add_tail(x, sr, wet=0.5, seed=1, min_tail=0.07, max_tail=0.2, k=0.2):
    dur = len(x) / sr
    tail = float(np.clip(min_tail + k * dur, min_tail, max_tail))
    n_ir = int(tail * sr); t = np.arange(n_ir) / sr; rng = np.random.default_rng(seed)
    ir = rng.standard_normal(n_ir) * np.exp(-t / (tail / 4.6))                                      # 約 -40dB で消える
    ir = sosfilt(butter(2, 5200, btype='low', fs=sr, output='sos'), ir)                               # 響きは少し暗く
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    m = min(len(x), int(0.04 * sr))
    src = x[-m:] * np.hanning(2 * m)[:m]                                                               # 最後の 40ms (頭はなだらかに立ち上げる = 音の本体に響きが混ざらない)
    rev = fftconvolve(src, ir)
    rev *= (wet * np.max(np.abs(x[-m:])) / (np.max(np.abs(rev)) + 1e-9))                               # 最後の音の大きさに対して wet 倍
    out = np.zeros(len(x) - m + len(rev)); out[: len(x)] += x
    out[len(x) - m:] += rev
    return out
