"""Game2D の効果音を、 GBA 時代の RPG (FE など) の音の作りで合成する (2026-10-24)。

実際の音源は使わない。 性質だけ再現する:
  * 低音 (〜150Hz) を出さない。 中域 (300Hz〜4kHz) が前に出る。
  * 矩形波 (デューティ比違い) の「ピッ」という短い電子音 = カーソル・決定・ランプ。
  * 打撃・斬撃は、 短い雑音のバースト + 下がるピッチの矩形波 (胴) + 非整数倍音の金属音 (キン)。
  * サンプリング周波数 22.05kHz、 振幅は 7bit に粗くする (GBA の PCM の粗さ)。

使い方: earshot の仮想環境の python で `python Tools/sfx_gba.py` → Assets/Resources/Game2DSfx/*.wav を上書きする。
元の earshot バンク版は Tools/sfx_src/earshot_v1/ に残してある。
"""
import os
import numpy as np
import soundfile as sf
from sfx_tail import add_tail
from scipy.signal import butter, sosfilt

SR = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Resources', 'Game2DSfx')
RNG = np.random.default_rng(7)


def n_of(d): return int(SR * d)


def noise(d): return RNG.uniform(-1, 1, n_of(d))


def bp(x, lo, hi): return sosfilt(butter(2, [lo, min(hi, SR / 2 - 100)], btype='band', fs=SR, output='sos'), x)
def lp(x, f): return sosfilt(butter(2, f, btype='low', fs=SR, output='sos'), x)
def hp(x, f): return sosfilt(butter(2, f, btype='high', fs=SR, output='sos'), x)


def env(d, a=0.002, tau=0.05, hold=0.0):
    """立ち上がり a 秒 → hold 秒 → 指数で減衰 (tau)。"""
    n = n_of(d); t = np.arange(n) / SR
    e = np.where(t < a, t / max(a, 1e-4), 1.0)
    e = e * np.where(t < a + hold, 1.0, np.exp(-(t - a - hold) / tau))
    return e


def sweep_f(f0, f1, d):
    n = n_of(d); t = np.linspace(0, 1, n)
    return f0 * (f1 / f0) ** t


def sq(f, d, duty=0.5):
    """矩形波。 f は周波数 (数値か、 時間ごとの配列)。"""
    n = n_of(d); f = np.full(n, f, dtype=float) if np.isscalar(f) else f
    ph = np.cumsum(f / SR) % 1.0
    return np.where(ph < duty, 1.0, -1.0)


def tri(f, d):
    n = n_of(d); f = np.full(n, f, dtype=float) if np.isscalar(f) else f
    ph = np.cumsum(f / SR) % 1.0
    return 4 * np.abs(ph - 0.5) - 1


def clang(f, d, tau=0.08, partials=(1.0, 2.76, 5.4, 8.9)):
    """金属音: 非整数倍音の矩形波を重ねて減衰させる。"""
    x = np.zeros(n_of(d))
    for i, p in enumerate(partials):
        x += sq(f * p, d, 0.5) * np.exp(-np.arange(n_of(d)) / SR / (tau / (1 + i * 0.6))) / (1 + i * 0.7)
    return x


def at(buf, x, t0):
    i = n_of(t0); n = min(len(x), len(buf) - i)
    if n > 0: buf[i:i + n] += x[:n]
    return buf


def beeps(notes, gap=0.0, duty=0.5, tau=None):
    """[(周波数, 長さ), ...] を順に鳴らす。"""
    total = sum(d + gap for _, d in notes); buf = np.zeros(n_of(total + 0.05)); t0 = 0.0
    for f, d in notes:
        e = env(d, 0.002, tau or d * 0.6, 0.0) if tau else np.ones(n_of(d))
        e = e * np.minimum(1, np.linspace(1, 0, n_of(d)) * 8 + 0.0)      # 末尾のプチ音よけ
        at(buf, sq(f, d, duty) * e, t0); t0 += d + gap
    return buf


def finish(x, peak_db=-6.0, bits=7, lowcut=120, tail=0.14):
    x = hp(x, lowcut)                                       # 低音は出さない
    x = x / (np.max(np.abs(x)) + 1e-9) * (10 ** (peak_db / 20))
    if tail: x = add_tail(x, SR, wet=tail)                    # 余韻 (ブツ切りにしない)
    lv = 2 ** (bits - 1)
    x = np.round(x * lv) / lv                                # 7bit の粗さ
    fade = n_of(0.01); x[-fade:] *= np.linspace(1, 0, fade)
    return x.astype(np.float32)


def note(name):
    """音名 → 周波数 (A4 = 440)。 例: 'C5' 'Eb5' 'F#6'。"""
    base = {'C': -9, 'D': -7, 'E': -5, 'F': -4, 'G': -2, 'A': 0, 'B': 2}
    s = base[name[0]]; i = 1
    if name[i] == '#': s += 1; i += 1
    elif name[i] == 'b': s -= 1; i += 1
    return 440.0 * 2 ** ((s + 12 * (int(name[i:]) - 4)) / 12)


S = {}


def add(name, x, peak=-6.0, **k): S[name] = finish(x, peak, **k)


# ── UI (カーソル・決定のピッ) ──
add('ui_hover', sq(1760, 0.018, 0.25) * env(0.018, 0.001, 0.012), -14)
add('ui_click', beeps([(1319, 0.028), (1760, 0.04)], tau=0.03), -9)
add('ui_confirm', beeps([(note('A5'), 0.05), (note('D6'), 0.05), (note('A6'), 0.11)], tau=0.06), -9)
add('ui_open', sq(sweep_f(500, 1500, 0.1), 0.1, 0.25) * env(0.1, 0.003, 0.07), -10)
add('ui_close', sq(sweep_f(1500, 480, 0.1), 0.1, 0.25) * env(0.1, 0.003, 0.07), -11)
add('ui_tab', sq(1175, 0.032, 0.125) * env(0.032, 0.001, 0.02), -11)
add('nav_press', beeps([(660, 0.03), (990, 0.06)], tau=0.04), -8)
add('node_hover', sq(2349, 0.012, 0.25) * env(0.012, 0.001, 0.008), -20)
add('wire', beeps([(1568, 0.022), (2093, 0.03)], tau=0.02), -10)
x = np.zeros(n_of(0.09)); at(x, bp(noise(0.02), 900, 3200) * env(0.02, 0.0005, 0.006), 0.0); at(x, lp(bp(noise(0.03), 200, 900), 900) * env(0.03, 0.0005, 0.01) * 0.7, 0.004); at(x, bp(noise(0.02), 1200, 3800) * env(0.02, 0.0005, 0.005) * 0.6, 0.04)
add('decide', x, -14, lowcut=130)      # 決定 (GO! / ROLL): 電子音ではなく、 小さなスイッチの「カチッ」。 雑音のみ
add('count_tick', sq(3000, 0.005, 0.5) * env(0.005, 0.0005, 0.003), -24)
add('pot_open', sq(sweep_f(300, 900, 0.14), 0.14, 0.25) * env(0.14, 0.003, 0.1), -9)
add('pot_close', sq(sweep_f(900, 300, 0.14), 0.14, 0.25) * env(0.14, 0.003, 0.1), -9)
add('gui_in', bp(noise(0.25), 700, 4200) * np.sin(np.linspace(0, np.pi, n_of(0.25))) ** 2, -14)
add('gui_out', bp(noise(0.22), 900, 5000) * np.sin(np.linspace(0, np.pi, n_of(0.22))) ** 2, -14)

# ── 戦闘: 斬撃 = 風切り (shu) → 打撃 (snap) + 胴 (body) + 金属 (tick / ring) ──
def swept(d, bands, peak_at, width):
    """帯域を低い方から順にずらして重ね、 周波数が駆け上がる『シュッ』にする。"""
    n = n_of(d); t = np.arange(n) / SR; x = np.zeros(n)
    for i, (lo, hi) in enumerate(bands):
        c = peak_at * (i + 1) / len(bands) * 1.2
        x += bp(noise(d), lo, hi) * np.exp(-((t - c) / width) ** 2)
    return x
# 参考 (FE 聖魔の光石の必殺集) の斬撃: 約 70ms かけて雑音が膨らみ、 帯域が 2kHz → 4.8kHz へ駆け上がり、 ピークの直後に切れる。 5kHz 以上はほぼ無い。
def rising(d=0.11, peak=0.075, cut=0.095):
    n = n_of(d); t = np.arange(n) / SR; x = np.zeros(n)
    bands = [(900, 1900), (1400, 2600), (1900, 3300), (2400, 4000), (3000, 4800)]
    for i, (lo, hi) in enumerate(bands):
        c = peak * (0.35 + 0.65 * i / (len(bands) - 1))                 # 高い帯域ほど遅れてピーク
        x += bp(noise(d), lo, hi) * np.exp(-((t - c) / 0.03) ** 2) * (0.6 + 0.4 * i / (len(bands) - 1))
    x *= np.clip(t / peak, 0, 1) ** 1.2                                   # 全体の立ち上がり
    x[t > cut] = 0.0                                                      # ピークの直後に切れる
    return x
x = bp(noise(0.12), 600, 5500) * env(0.12, 0.001, 0.03)
add('slash_snap', x, -4)
add('slash_tick', clang(3100, 0.2, 0.06, (1.0, 2.4, 4.1)), -6)
# 肉を裂く: 帯域が高→低へ落ちる雑音 (ザッ) + 湿った粒 (ぐちゅ) + 短い胴
def chop(x, hz):
    """雑音を細かく刻んで、 湿ったざらつきにする。"""
    n = len(x); g = (RNG.uniform(0, 1, n // max(1, int(SR / hz)) + 2) > 0.38).astype(float)
    return x * np.repeat(g, max(1, int(SR / hz)))[:n]
# ザシュッ = ザ (短い破裂・中域) + シュ (駆け上がる雑音) + ッ (切れる)
n = n_of(0.14); x = np.zeros(n)
at(x, lp(bp(noise(0.05), 400, 3200), 4000) * env(0.05, 0.001, 0.02) * 0.9, 0.0)
at(x, chop(bp(noise(0.08), 300, 1800), 700) * env(0.08, 0.001, 0.03) * 0.7, 0.0)
at(x, rising(0.12, 0.07, 0.1), 0.012)
at(x, lp(bp(noise(0.09), 140, 700), 700) * env(0.09, 0.001, 0.035) * 0.9, 0.05)      # 刃が入る低中域の「ドスッ」 (雑音ベース)
add('slash_cut', x, -3, lowcut=130)
add('slash_squelch', x, -4, lowcut=150)
add('slash_body', lp(bp(noise(0.14), 140, 900), 900) * env(0.14, 0.001, 0.05) * 1.0 + tri(sweep_f(260, 140, 0.14), 0.14) * env(0.14, 0.001, 0.04) * 0.15, -8, lowcut=130)      # 肉に当たる鈍い打撃 (雑音ベース。 音程のある電子音は使わない)
add('slash_ring', clang(2093, 0.4, 0.18, (1.0, 2.76, 5.4)), -9)
x = np.zeros(n_of(0.3)); at(x, bp(noise(0.03), 1000, 7000) * env(0.03, 0.001, 0.01), 0); at(x, clang(1480, 0.28, 0.1, (1.0, 2.76, 5.4, 8.9)), 0)
add('block', x, -6)
for i, (lo, hi) in enumerate([(500, 1800), (800, 2600), (350, 1400)], 1):
    x = np.zeros(n_of(0.12)); at(x, bp(noise(0.035), lo, hi) * env(0.035, 0.001, 0.012), 0); at(x, bp(noise(0.035), lo, hi) * env(0.035, 0.001, 0.01) * 0.6, 0.05)
    add('dice_%d' % i, x, -8)

# ── 戦闘: ヒット (参考: FE 聖魔の光石 必殺集 0:10〜 の分析から組み直し・2026-10-25) ──
# 参考の構造: 約 0.4 秒の雑音。 低中域 (〜2kHz) の厚みから始まり、 終わりへ向けて 5kHz まで明るくなり、 ピークの直後に急に切れる。 音程は無い。
def hit_ref(d=0.40, cut=0.37, low=1.0, mid=0.9, high=0.8, grain=0.35):
    n = n_of(d); t = np.arange(n) / SR; x = np.zeros(n)
    atk = np.clip(t / 0.006, 0, 1)
    swell = 0.55 + 0.45 * np.clip(t / (cut * 0.75), 0, 1)                      # 後ろへ向けて強くなる
    x += lp(bp(noise(d), 180, 1700), 1700) * low * np.exp(-t / 0.42) * atk                       # 低中域の厚み
    x += bp(noise(d), 800, 2600) * mid * np.clip((t - 0.05) / 0.12, 0, 1) * np.exp(-((t - cut * 0.7) / 0.2) ** 2) * 0.9      # 中域
    x += bp(noise(d), 2200, 4300) * high * 0.55 * np.clip((t - 0.14) / 0.14, 0, 1) * np.exp(-((t - cut * 0.92) / 0.09) ** 2)      # 終わりに明るくなる
    x *= swell
    g = (RNG.uniform(0, 1, n // int(SR / 480) + 2) > grain + 0.1).astype(float); x *= np.repeat(g, int(SR / 480))[:n] * 0.6 + 0.4       # 粒状のざらつき
    x[t > cut] = 0.0                                                                                  # ピークの直後に切れる
    return x
add('hit_slash', hit_ref(), -6, lowcut=110)
add('hit_crit', hit_ref(0.5, 0.46, low=1.4, mid=1.1, high=1.0), -3, lowcut=100)
add('hit_enemy', hit_ref(0.38, 0.34, low=1.3, mid=0.6, high=0.3), -6, lowcut=100)
# 振りかぶり・風切りの低い山 2 つ (参考 10.27 / 10.40 秒)
xs = np.zeros(n_of(0.3))
for t0, w in ((0.03, 0.045), (0.17, 0.05)):
    tt = np.arange(n_of(0.3)) / SR; xs += lp(bp(noise(0.3), 150, 1500), 1500) * np.exp(-((tt - t0 - 0.05) / w) ** 2)
add('slash_swing', xs, -8, lowcut=110)

# ── 戦闘: 進行 ──
x = np.zeros(n_of(0.62)); at(x, bp(noise(0.5), 600, 6000) * np.linspace(0, 1, n_of(0.5)) ** 2 * 0.7, 0); at(x, sq(sweep_f(200, 1100, 0.5), 0.5, 0.25) * np.linspace(0, 1, n_of(0.5)) ** 2 * 0.4, 0)
at(x, bp(noise(0.1), 500, 5000) * env(0.1, 0.001, 0.04), 0.5); add('battle_start', x, -7)
x = lp(noise(0.9), 1800) * np.linspace(0, 1, n_of(0.9)) ** 2 * 0.6 + tri(sweep_f(150, 620, 0.9), 0.9) * np.linspace(0, 1, n_of(0.9)) ** 2 * 0.6
add('enemy_appear', x, -8)
add('victory', beeps([(note('C5'), 0.09), (note('E5'), 0.09), (note('G5'), 0.09), (note('C6'), 0.32)], tau=0.25, duty=0.25) * 0.8 + 0.0, -8)
add('defeat', beeps([(note('G4'), 0.16), (note('Eb4'), 0.16), (note('C4'), 0.16), (note('G3'), 0.5)], tau=0.4, duty=0.5), -8, lowcut=150)
add('swing', S['slash_swing'].astype(float), -8)

# ── 航行 ──
x = np.zeros(n_of(0.1)); at(x, lp(noise(0.04), 1800) * env(0.04, 0.001, 0.012), 0); at(x, tri(240, 0.04) * env(0.04, 0.001, 0.015), 0); add('step', x, -12, lowcut=150)
x = np.zeros(n_of(0.45))
for i in range(5): at(x, sq(note('G5') * 2 ** (i / 12), 0.025, 0.5) * env(0.025, 0.001, 0.015), i * 0.07)
add('recon', x, -11)
x = np.zeros(n_of(0.6)); at(x, hp(noise(0.5), 1800) * np.linspace(0, 1, n_of(0.5)) * 0.6 + sq(sweep_f(400, 2400, 0.5), 0.5, 0.25) * np.linspace(0, 1, n_of(0.5)) * 0.5, 0); add('flare_launch', x, -9)
x = np.zeros(n_of(0.9)); at(x, bp(noise(0.9), 2000, 9000) * env(0.9, 0.002, 0.22), 0)
for i in range(6): at(x, sq(RNG.uniform(2500, 5200), 0.02, 0.25) * env(0.02, 0.001, 0.012) * 0.7, RNG.uniform(0.02, 0.5))
add('flare_burst', x, -11)
add('lure_set', beeps([(700, 0.03), (700, 0.03)], gap=0.03, tau=0.025), -9)
x = bp(noise(0.16), 500, 6000) * env(0.16, 0.001, 0.05) + sq(sweep_f(1300, 200, 0.2), 0.2, 0.25)[:n_of(0.16)] * env(0.16, 0.001, 0.07) * 0.7
add('lure_hit', x, -8)
n6 = n_of(0.62); g = (np.arange(n6) // n_of(0.055)) % 2 == 0
add('chase_riser', sq(sweep_f(300, 1300, 0.62), 0.62, 0.25) * g * np.linspace(0.3, 1, n6), -9)
x = sq(sweep_f(320, 95, 0.3), 0.3, 0.25) * env(0.3, 0.002, 0.1) + bp(noise(0.3), 400, 3000) * env(0.3, 0.001, 0.05) * 0.6
add('chase_hit', x, -8, lowcut=150)
add('alert', beeps([(1047, 0.06), (1047, 0.07)], gap=0.04, tau=0.05), -8)
add('lost', tri(sweep_f(1200, 320, 0.45), 0.45) * env(0.45, 0.003, 0.3), -12)
x = bp(noise(0.35), 400, 4000) * env(0.35, 0.002, 0.09) + sq(sweep_f(750, 130, 0.35), 0.35, 0.25) * env(0.35, 0.002, 0.12) * 0.7
add('caught', x, -7, lowcut=150)
add('familiar', sq(sweep_f(1800, 500, 0.2), 0.2, 0.125) * env(0.2, 0.002, 0.12), -11)
add('hurt', bp(noise(0.12), 500, 5000) * env(0.12, 0.001, 0.035) + sq(sweep_f(650, 160, 0.12), 0.12, 0.25) * env(0.12, 0.001, 0.045) * 0.7, -7, lowcut=150)
add('floor_up', beeps([(note('C5'), 0.07), (note('G5'), 0.07), (note('C6'), 0.07), (note('E6'), 0.2)], tau=0.12, duty=0.25), -9)
add('radar_1', sq(1397, 0.018, 0.25) * env(0.018, 0.001, 0.012), -16)
add('radar_2', beeps([(1397, 0.02), (1397, 0.02)], gap=0.05, duty=0.25, tau=0.015), -14)
add('radar_3', beeps([(1865, 0.018)] * 4, gap=0.03, duty=0.25, tau=0.012), -11)

# ── 天候・地形 ──
w = n_of(1.1); sh = np.sin(np.linspace(0, np.pi, w)) ** 2
add('wx_fog', lp(noise(1.1), 900) * sh, -22, lowcut=150)
add('wx_sand', bp(noise(1.3), 1000, 5200) * np.sin(np.linspace(0, np.pi, n_of(1.3))) ** 2, -19)
add('wx_snow', bp(noise(1.0), 2200, 6500) * np.sin(np.linspace(0, np.pi, n_of(1.0))) ** 2, -24)
add('wx_hail', bp(noise(0.02), 3000, 8000) * env(0.02, 0.0005, 0.007) + sq(3300, 0.02, 0.25) * env(0.02, 0.0005, 0.006) * 0.5, -17)
x = np.zeros(n_of(1.1))
for t0 in (0.0, 0.14, 0.31, 0.5, 0.74): at(x, lp(noise(0.25), 1400) * env(0.25, 0.002, 0.07) * (1 - t0 * 0.6), t0)
add('crumble', x, -9, lowcut=150)
add('sink', tri(sweep_f(520, 150, 1.1), 1.1) * env(1.1, 0.01, 0.55) * 0.8 + lp(noise(1.1), 1200) * env(1.1, 0.01, 0.4) * 0.5, -10, lowcut=150)

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for name, x in S.items():
        sf.write(os.path.join(OUT, name + '.wav'), x, SR, subtype='PCM_16')
    print(len(S), 'sounds ->', os.path.normpath(OUT))
