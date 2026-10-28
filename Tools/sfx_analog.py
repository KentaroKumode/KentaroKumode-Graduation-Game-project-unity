"""画面の外 (筐体の部品) の効果音を、 実在の物が出す音の作りで合成する (2026-10-25)。

画面の中 (液晶の中: マップ・戦闘の演出・地図上のできごと) = 8bit 音源 (Tools/sfx_gba.py と Tools/sfx_chip.py)。
画面の外 (筐体の部品: ゴールドポット・パネルのボタン・数字ドラム・レーダー・GUI の出し入れ・メニュー・ダイス) = ここ。
8bit の粒 (LFSR ノイズ・矩形波) は使わず、 なめらかな雑音と、 減衰する正弦波 (共鳴) だけで作る。
  * クリック = 短い雑音のバースト (硬い当たり) + 減衰する共鳴 (プラスチック・金属の箱) + 沈み込む低い「コトッ」。
  * 金属の蓋・留め金 = 重い「ガコン」 (低い共鳴 + 雑音) と、 軽い「カチッ」 (高い共鳴)。
  * コイン = 金属の共鳴 (2.9kHz 付近 + 少し高い倍音) の短い減衰。
  * レーダー = 正弦波のソナーの「ピン」。
使い方: earshot の仮想環境の python で、 sfx_gba.py → sfx_chip.py → sfx_analog.py の順に実行する (同名の音を上書きする)。
"""
import os
import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfilt
from sfx_tail import add_tail

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Resources', 'Game2DSfx')
RNG = np.random.default_rng(21)


def tt(d): return np.arange(int(d * SR)) / SR
def bp(x, lo, hi): return sosfilt(butter(2, [lo, min(hi, SR / 2 - 200)], btype='band', fs=SR, output='sos'), x)
def lp(x, f): return sosfilt(butter(2, f, btype='low', fs=SR, output='sos'), x)
def hp(x, f): return sosfilt(butter(2, f, btype='high', fs=SR, output='sos'), x)


def ring(f, tau, d, amp=1.0, phase=0.0):
    """減衰する共鳴 (正弦波)。"""
    t = tt(d); return amp * np.sin(2 * np.pi * f * t + phase) * np.exp(-t / tau)


def burst(lo, hi, tau, d=0.04, amp=1.0):
    """雑音のバースト (硬い当たり)。 なめらかなガウス雑音。"""
    t = tt(d); return amp * bp(RNG.standard_normal(len(t)), lo, hi) * np.exp(-t / tau)


def put(buf, x, t0):
    i = int(t0 * SR); n = min(len(x), len(buf) - i)
    if n > 0: buf[i:i + n] += x[:n]
    return buf


def click(size=1.0, bright=1.0, d=0.08):
    """機械式スイッチの「カチッ」: 当たり (雑音) + 筐体の共鳴 2 つ + 底に当たる小さな 2 つ目の当たり。"""
    x = np.zeros(int(d * SR))
    put(x, burst(1800 * bright, 7000 * bright, 0.0015, amp=1.0 * size), 0)
    put(x, ring(1900 * bright, 0.004, d, 0.38 * size), 0); put(x, ring(950 * bright, 0.006, d, 0.32 * size), 0)
    put(x, ring(230, 0.018, d, 0.4 * size), 0.002)
    put(x, burst(1200, 5000, 0.001, amp=0.5 * size), 0.009)
    return x


def finish(x, peak_db, tail=0.5, hp_hz=70):
    x = hp(x, hp_hz)
    x = x / (np.max(np.abs(x)) + 1e-9) * 10 ** (peak_db / 20)
    if tail: x = add_tail(x, SR, wet=tail)
    n = int(0.01 * SR); x[-n:] *= np.linspace(1, 0, n)
    return x.astype(np.float32)


S = {}
def add(name, x, peak, **k): S[name] = finish(x, peak, **k)


# ── パネルのボタン・メニュー (機械式スイッチ) ──
add('nav_press', click(1.2, 1.0, 0.1), -6)
add('ui_hover', burst(2500, 8000, 0.0012, 0.03, 0.8) + ring(3200, 0.004, 0.03, 0.4), -20, tail=0.3)
add('ui_click', click(0.9, 1.1, 0.09), -9)
x = np.zeros(int(0.3 * SR)); put(x, click(0.8, 1.1, 0.09), 0); put(x, click(1.0, 1.35, 0.09), 0.07); put(x, ring(2600, 0.06, 0.2, 0.25), 0.08); add('ui_confirm', x, -9)
x = np.zeros(int(0.2 * SR)); put(x, click(0.8, 1.0, 0.09), 0); put(x, bp(RNG.standard_normal(int(0.1 * SR)), 900, 3500) * np.sin(np.linspace(0, np.pi, int(0.1 * SR))) ** 2 * 0.25, 0.02); add('ui_open', x, -10)
x = np.zeros(int(0.2 * SR)); put(x, bp(RNG.standard_normal(int(0.1 * SR)), 900, 3500) * np.sin(np.linspace(0, np.pi, int(0.1 * SR))) ** 2 * 0.25, 0); put(x, click(0.8, 0.9, 0.09), 0.09); add('ui_close', x, -10)
x = np.zeros(int(0.12 * SR)); put(x, click(0.55, 1.5, 0.06), 0); put(x, click(0.45, 1.7, 0.06), 0.035); add('ui_tab', x, -12)
x = np.zeros(int(0.2 * SR)); put(x, click(1.1, 0.9, 0.1), 0); put(x, click(1.0, 0.8, 0.1), 0.045); put(x, ring(520, 0.03, 0.15, 0.35), 0.05); add('decide', x, -9)
add('wire', click(0.7, 1.5, 0.06), -11)
add('count_tick', burst(1500, 6000, 0.001, 0.02, 0.8) + ring(2400, 0.004, 0.02, 0.4), -24, tail=0.2)

# ── ダイス (机の上の樹脂の立方体): 当たり + 短い共鳴 + 跳ね返りの小さな 2 つ目 ──
for i, (f, ph) in enumerate([(1250, 0.0), (1650, 0.015), (1050, -0.01)], 1):
    x = np.zeros(int(0.2 * SR)); put(x, burst(1500, 6000, 0.0015, amp=1.0), 0); put(x, ring(f, 0.004, 0.2, 0.28), 0); put(x, ring(f * 0.6, 0.006, 0.2, 0.22), 0)
    put(x, burst(1400, 5500, 0.0012, amp=0.55), 0.045 + ph); put(x, ring(f * 1.1, 0.004, 0.2, 0.15), 0.045 + ph)
    add('dice_%d' % i, x, -9)

# ── ゴールドポット (金属の入れ物): 蓋の留め金と、 重い「ガコン」 ──
def thunk(size=1.0):
    d = 0.22; x = np.zeros(int(d * SR))
    put(x, ring(160, 0.045, d, 1.0 * size), 0); put(x, ring(330, 0.03, d, 0.6 * size), 0)                  # 金属の箱の低い共鳴
    put(x, burst(300, 1800, 0.012, amp=0.9 * size), 0); put(x, burst(2500, 8000, 0.002, amp=0.5 * size), 0)       # 重い当たり + 硬い当たり
    return x
x = np.zeros(int(0.5 * SR)); put(x, click(0.7, 0.8, 0.08), 0); put(x, bp(RNG.standard_normal(int(0.16 * SR)), 500, 2600) * np.sin(np.linspace(0, np.pi, int(0.16 * SR))) ** 2 * 0.3, 0.03); put(x, thunk(1.0), 0.16); add('pot_open', x, -6)
x = np.zeros(int(0.5 * SR)); put(x, bp(RNG.standard_normal(int(0.14 * SR)), 500, 2600) * np.sin(np.linspace(0, np.pi, int(0.14 * SR))) ** 2 * 0.3, 0); put(x, thunk(1.2), 0.13); put(x, click(0.8, 0.8, 0.08), 0.25); add('pot_close', x, -6)

# ── GUI の出し入れ (金属の板が滑る): なめらかな擦れ + 終わりの軽い当たり ──
def slide(d, f0, f1, thump_at):
    n = int(d * SR); w = RNG.standard_normal(n); out = np.zeros(n); blk = 128; fc = np.exp(np.linspace(np.log(f0), np.log(f1), n))
    zi = None
    for i in range(0, n, blk):
        f = float(fc[i]); sos = butter(2, [f * 0.6, min(SR / 2 - 200, f * 1.7)], btype='band', fs=SR, output='sos')
        if zi is None: zi = np.zeros((sos.shape[0], 2))
        out[i:i + blk], zi = sosfilt(sos, w[i:i + blk], zi=zi)
    out *= np.sin(np.linspace(0, np.pi, n)) ** 1.5 * 0.3
    x = np.zeros(int((d + 0.15) * SR)); x[:n] += out; put(x, ring(190, 0.03, 0.15, 0.5), thump_at); put(x, burst(500, 3000, 0.006, amp=0.4), thump_at)
    return x
add('gui_in', slide(0.22, 700, 2400, 0.2), -13); add('gui_out', slide(0.2, 2400, 700, 0.18), -13)

# ── レーダー (ソナーの「ピン」): なめらかな正弦波 ──
def ping(f, d=0.5, amp=1.0):
    t = tt(d); e = np.minimum(1, t / 0.004) * np.exp(-t / 0.12); return amp * np.sin(2 * np.pi * f * t) * e + 0.3 * amp * np.sin(2 * np.pi * f * 2 * t) * e * np.exp(-t / 0.05)
add('radar_1', ping(1700, 0.45, 0.6), -16)
x = np.zeros(int(0.5 * SR)); put(x, ping(1850, 0.35), 0); put(x, ping(1850, 0.35, 0.8), 0.14); add('radar_2', x, -13)
x = np.zeros(int(0.5 * SR)); put(x, ping(2300, 0.3), 0); put(x, ping(2300, 0.3, 0.9), 0.08); put(x, ping(2300, 0.3, 0.8), 0.16); add('radar_3', x, -11)

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for name, x in S.items():
        sf.write(os.path.join(OUT, name + '.wav'), x, SR, subtype='PCM_16')
    print(len(S), 'analog sounds ->', os.path.normpath(OUT))
