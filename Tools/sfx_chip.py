"""8bit 音源 (NES / GB 系) の作り方で効果音を作る (2026-10-25)。

作り方 (調べたこと):
  * ノイズ = 15bit LFSR が 1bit を出す。 出す速さは 16 段階の表から選ぶ (NES: 447kHz 〜 440Hz)。 速さが遅い = 低中域のこもった雑音、 速い = 明るい雑音。
    短周期モード (フィードバックが bit6) にすると 93 ステップの繰り返しになり、 金属的に鳴る。
    参考: https://nesdev.org/wiki/APU_Noise
  * 音量は 4bit (16 段階)。 1/60 秒ごとのフレームの表で動かす (減衰・立ち上がり・点滅)。
  * 矩形波はデューティ比 12.5 / 25 / 50 / 75% の 4 種。 三角波は 32 段の階段で、 音量の調節は無い (鳴る / 鳴らない)。
  * 効果音 = フレームごとの (音の高さ / 速さ, 音量, デューティ) の表。 sfxr 系の「スライド・パンチ・減衰」も、 この表で書ける。
    参考: https://pyfxr.readthedocs.io/en/stable/generating.html

使い方: earshot の仮想環境の python で `python Tools/sfx_chip.py` (Tools/sfx_gba.py のあとに実行すると、 同名の音を上書きする)。
"""
import os
import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfilt
from sfx_tail import add_tail

SR = 32768          # GBA のサンプル再生に近い (NES の APU より細かい)
FR = 60.0
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Resources', 'Game2DSfx')
NOISE_HZ = [447443, 223721, 111860, 55930, 27965, 18643, 13982, 11186, 8860, 7046, 4710, 3523, 2348, 1761, 880, 440]


def _lfsr(short):
    r = 1; out = np.empty(32767 if not short else 4096, dtype=np.int8)
    for i in range(len(out)):
        fb = (r & 1) ^ ((r >> (6 if short else 1)) & 1)
        r = (r >> 1) | (fb << 14); out[i] = 1 if (r & 1) == 0 else -1
    return out


LONG, SHORT = _lfsr(False), _lfsr(True)[:93 * 20]
# 短周期の系列は 93 ステップで繰り返す (初期値によっては 31)。 先頭の 93 を使う
SHORT = np.tile(_lfsr(True)[100:193], 40)


OS = 8             # 8 倍の細かさで作ってから間引く。 更新周期が 32768Hz の格子と噛み合わずに音程のある線 (ピー) が出るのを防ぐ
MULTIBIT = True    # True: 3 本の LFSR を重ねた 8 段階の雑音 (PCM サンプルに近いきめ細かさ)。 False: NES の 1bit
SMOOTH = True      # True: フレーム間を線形に補間して、 60Hz の階段 (ブツブツした質感) にしない。 False: NES そのまま
def frames_to_samples(arr, step=False, sr=None):
    """フレームごとの値の配列 → サンプルごとの配列。 SMOOTH なら補間する (step=True の値は階段のまま)。"""
    sr = sr or SR; a = np.asarray(arr, dtype=float); n = int(round(len(a) * sr / FR))
    if SMOOTH and not step and len(a) > 1:
        return np.interp(np.arange(n) * FR / sr - 0.5, np.arange(len(a)), a)
    idx = np.minimum((np.arange(n) * FR / sr).astype(int), len(a) - 1)
    return a[idx]


def noise(period_idx, vol, short=False, phase=0):
    """period_idx: フレームごとの速さの段階 (0..15)。 short: 短周期モード (bool か、 フレームごとの配列)。 OS 倍の細かさで作って、 アンチエイリアスしてから間引く。"""
    n = max(len(period_idx), len(vol)); period_idx = list(period_idx) + [period_idx[-1]] * (n - len(period_idx)); vol = list(vol) + [0] * (n - len(vol))
    if hasattr(short, '__len__'): short = list(short) + [False] * (n - len(short)); short = short[:n]
    sh_ = SR * OS
    rate = frames_to_samples([NOISE_HZ[int(i)] for i in period_idx], step=True, sr=sh_); v = frames_to_samples(vol, sr=sh_) / 15.0
    sh = frames_to_samples([1 if s else 0 for s in (short if hasattr(short, '__len__') else [short] * n)], step=True, sr=sh_)
    pos = np.cumsum(rate / sh_) + phase
    il = (pos.astype(np.int64)) % len(LONG); is_ = (pos.astype(np.int64)) % len(SHORT)
    base = np.where(sh > 0.5, SHORT[is_], LONG[il]).astype(float)
    if MULTIBIT:
        b2 = np.where(sh > 0.5, SHORT[(is_ + 31) % len(SHORT)], LONG[(il + 7919) % len(LONG)]); b3 = np.where(sh > 0.5, SHORT[(is_ + 61) % len(SHORT)], LONG[(il + 16001) % len(LONG)])
        base = (base + 0.5 * b2 + 0.25 * b3) / 1.75
    from scipy.signal import resample_poly
    return resample_poly(base * v, 1, OS)


def pulse(freq, vol, duty=0.5):
    n = max(len(freq), len(vol)); freq = list(freq) + [freq[-1]] * (n - len(freq)); vol = list(vol) + [0] * (n - len(vol))
    sh_ = SR * OS
    f = frames_to_samples(freq, sr=sh_); v = frames_to_samples(vol, sr=sh_) / 15.0
    d = frames_to_samples(duty if hasattr(duty, '__len__') else [duty] * n, sr=sh_)
    ph = np.cumsum(f / sh_) % 1.0
    from scipy.signal import resample_poly
    return resample_poly(np.where(ph < d, 1.0, -1.0) * v, 1, OS)


def sine(freq, vol):
    """純音 (正弦波)。 澄んだ金属の響き用。 音量はフレーム間を補間する。"""
    n = max(len(freq), len(vol)); freq = list(freq) + [freq[-1]] * (n - len(freq)); vol = list(vol) + [0] * (n - len(vol))
    f = frames_to_samples(freq); v = frames_to_samples(vol) / 15.0
    return np.sin(2 * np.pi * np.cumsum(f / SR)) * v


def tri(freq, on):
    f = frames_to_samples(freq); g = frames_to_samples(on)
    ph = np.cumsum(f / SR) % 1.0
    step = np.floor(np.abs(ph * 2 - 1) * 16) / 15.0 * 2 - 1                       # 16 段の階段
    return step * g


def ramp(a, b, n): return list(np.linspace(a, b, n))
def decay(v0, n, to=0): return [int(round(x)) for x in np.linspace(v0, to, n)]


def mix(*chs):
    n = max(len(c) for c in chs); x = np.zeros(n)
    for c in chs: x[:len(c)] += c
    return x


def shift(x, frames):
    return np.concatenate([np.zeros(int(frames * SR / FR)), x])


def finish(x, peak_db=-6.0, hp=110, lp=None, ref=None, tail=0.14):      # lp: 既定は掛けない (強引な高域カットはしない。 出力の上限はサンプリング周波数の半分 = 16kHz)
    x = sosfilt(butter(2, hp, btype='high', fs=SR, output='sos'), x)
    if lp: x = sosfilt(butter(6, lp, btype='low', fs=SR, output='sos'), x)         # 参考は 5kHz 以上がほぼ無い (GBA の PCM の帯域)
    x = x / ((ref if ref is not None else np.max(np.abs(x))) + 1e-9) * 10 ** (peak_db / 20)      # ref: 規格化の基準 (高音を足す前のピーク。 足した分で低域が小さくならない)
    if tail: x = add_tail(x, SR, wet=tail)                                                  # 余韻 (ブツ切りにしない)
    a = np.abs(x); x = np.where(a > 0.7, np.sign(x) * (0.7 + 0.28 * np.tanh((a - 0.7) / 0.28)), x)      # 0.7 を超えた山だけ軽く丸める (低域は縮めない・最大 0.98)
    n = int(0.01 * SR); x[-n:] *= np.linspace(1, 0, n)
    x = np.round(x * 127) / 127                                   # 8bit (256 段階)
    return x.astype(np.float32)


S = {}
def add(name, x, peak=-6.0, **k): S[name] = finish(x, peak, **k)


def peak_of(x, hp=110, lp=None):
    y = sosfilt(butter(2, hp, btype='high', fs=SR, output='sos'), x); y = sosfilt(butter(6, lp, btype='low', fs=SR, output='sos'), y) if lp else y; return float(np.max(np.abs(y)))


# ── 斬撃のヒット (参考: 約 0.4 秒。 低中域の厚み → 終わりに明るく → 急に切れる) ──
# ノイズの速さの段階を、 遅い (こもる) → 速い (明るい) へ段階的に上げ、 音量は後ろへ向けて保ち、 最後に切る。 頭に 1 フレームの「ザッ」(速いノイズ) と、 三角波の「ドスッ」
def hit(frames=26, bright=1.0, thud=1.0, double=False, weight=1.0, sparkle=0.0):
    pi = [5] + [11] * 3 + [10] * 4 + [9] * 5 + [8] * 5 + [7] * 4 + [7]; pi = pi[:frames] + [7] * max(0, frames - len(pi))
    pi = [max(3, int(round(p - (bright - 1) * 3))) if bright != 1.0 else p for p in pi]
    vol = [15, 15, 14, 14] + [13, 12, 13, 12, 13, 12, 13, 12, 13, 12, 13, 12, 13, 13, 14, 14, 14, 13][:frames - 4]; vol = vol[:frames]
    vol[-2:] = [8, 0]
    ch = noise(pi, vol, short=[False] * 6 + [True, False, True, False] + [False] * (frames - 10))                 # 途中だけ短周期で粒を入れる
    th = tri(ramp(150, 85, 6), [1 * thud] * 5 + [0] * 1) * 0.55 * weight                                   # ドスッ (階段の三角波)
    x = mix(ch, th)
    if double: x = mix(x, shift(mix(noise([6, 11, 10, 9, 8, 8], [15, 14, 13, 12, 10, 0]), tri(ramp(120, 70, 6), [1] * 5 + [0]) * 0.7), 3))
    if sparkle > 0:      # 高音成分: 明るいノイズの「シャッ」+ 短周期ノイズの金属粒 + 高い矩形波 (12.5%) の「キン」。 低域の層はそのまま
        def spk(t0):
            hi = noise([4, 4, 5, 5, 6, 6, 7, 7, 8, 9], decay(15, 10, 3)) * 0.9
            met = noise([5, 6, 5, 6, 7, 6, 7, 8], decay(13, 8, 0), short=True) * 0.7
            kin = pulse(ramp(4700, 3300, 10), decay(14, 10, 0), 0.125) * 0.55
            return shift(mix(hi, met, kin), t0)
        x = mix(x, spk(0) * sparkle)
        if double: x = mix(x, spk(3) * sparkle * 0.85)
    return x

# ザザザシュッ: 「ザ」を 3 回 (勢いを増しながら明るくなる連打) → 「シュ」(明るさが駆け上がる) → 切れる。
def hit_zazaza(nza=3, shu_frames=10, thud=1.0, weight=1.0, sparkle=0.0, double=False, calm=0.0):
    pi, vol, sh = [], [], []
    for k in range(nza):
        u = k / max(1, nza - 1)
        lead = 5 + int(round(2 * calm))                                                                      # 地味: 頭の破裂を明るくしすぎない
        pi += [lead, 10 - int(round(1 * u)), 11 - int(round(2 * u)), 11 - int(round(1 * u))]; vol += [int(round(15 - 4 * calm)), int(12 + 2 * u - 2 * calm), int(9 + 2 * u - 1.5 * calm), int(5 + 3 * u - calm)]
        sh += [False, False, (k % 2 == 1) and calm < 0.5, False]                                                               # 2 回目だけ粒 (短周期) を混ぜる
    shp = [8, 8, 7, 7, 7, 7, 6, 6, 6, 6][:shu_frames]; shp = [p + int(round(2 * calm)) for p in shp]; pi += shp                 # 地味: シュの明るさの上限を下げる
    vol += [int(round(v * (1 - 0.3 * calm))) for v in [13, 14, 14, 14, 13, 13, 12, 11, 9, 0][:shu_frames]]; sh += [False] * shu_frames       # シュ
    ch = noise(pi, vol, short=sh)
    th = mix(tri(ramp(150, 85, 5), [1] * 4 + [0]) * 0.55 * thud * weight, shift(tri(ramp(130, 80, 5), [1] * 4 + [0]) * 0.5 * thud * weight, 4 * (nza - 1) + 4))      # 1 回目と、 シュの頭に「ドスッ」
    x = mix(ch, th)
    if sparkle > 0:
        def spk(t0):
            hi = noise([4, 4, 5, 5, 6, 6, 7, 7, 8, 9], decay(15, 10, 3)) * 0.9
            met = noise([5, 6, 5, 6, 7, 6, 7, 8], decay(13, 8, 0), short=True) * 0.7
            kin = pulse(ramp(4700, 3300, 10), decay(14, 10, 0), 0.125) * 0.55
            return shift(mix(hi, met, kin), t0)
        x = mix(x, spk(0) * sparkle, spk(4 * (nza - 1) + 4) * sparkle)
    return x

# 通常ヒットは単発 (ピークは 1 つだけ): 頭に 1 回の破裂 → 明るいノイズが遅い (こもった) 方へ落ちながら減衰する。 連打・打撃の足し合わせは無し
def hit_single(frames=10, thud=0.5):
    pi = [6, 7, 8, 9, 10, 10, 11, 11, 12, 12][:frames]; vol = [14, 13, 11, 9, 7, 5, 4, 2, 1, 0][:frames]
    return mix(noise(pi, vol), tri(ramp(140, 95, 3), [1, 1, 0]) * thud)
add('hit_slash', hit_single(), -7)
add('hit_crit', hit_zazaza(nza=4, shu_frames=10, thud=1.3, weight=1.3, sparkle=1.0), -8, hp=90, ref=peak_of(hit_zazaza(nza=4, shu_frames=10, thud=1.3, weight=1.3), hp=90))      # 会心: ザザザザ + 高音成分
add('hit_enemy', hit(frames=25, bright=0.7, thud=1.4, weight=1.4), -6, hp=90)

# ── 斧と盾: 剣・短剣 (斬る) とは性質が違う「叩きつける」音。 重くて短く、 鈍い。 重低音 (150Hz 以下) は入れない ──
# 斧: 遅いノイズの鈍い破裂 + 低中域の三角波の「ドン」 + 割れる粒 (短周期ノイズ)。 会心は 2 段 (叩きつけ → 砕ける) で、 余韻の砕け音が続く
def bright_burst(t0=0, amp=1.0):
    """明るいノイズの「ザッ」(高音成分)。 音程は無い。 会心を派手にするのに使う。"""
    return shift(noise([4, 4, 5, 5, 6, 6, 7, 7, 8, 9], decay(15, 10, 3)) * 0.9 * amp, t0)


def rubble(frames=30, count=10, level=1.0, seed=3, start=2):
    """瓦礫が飛び散って落ちる音: 小さな破片の「カツ・コツ」 (なめらかな雑音の短い粒) が、 前半は密に、 後半はまばらに、 小さくなりながら落ちる。
    LFSR の短周期ノイズは使わない (粒が連なると、 ジリジリしたブザーになる)。 粒どうしは 3 フレーム以上空ける。"""
    rng = np.random.default_rng(seed); n = int((frames + 8) * SR / FR) + SR // 4; out = np.zeros(n)
    times = []; tries = 0
    while len(times) < count and tries < 400:
        tries += 1; t = start + int(frames * rng.random() ** 1.7)
        if all(abs(t - u) >= 3 for u in times): times.append(t)
    for t in times:
        lev = level * (1.0 - 0.65 * (t - start) / max(1, frames)) * rng.uniform(0.6, 1.0)
        L = int(0.045 * SR); tt = np.arange(L) / SR
        fc = rng.uniform(900, 3200); q = 3.0
        w = sosfilt(butter(2, [fc / (1 + 1 / q), min(SR / 2 - 100, fc * (1 + 1 / q))], btype='band', fs=SR, output='sos'), rng.standard_normal(L))
        tick = w * np.exp(-tt / rng.uniform(0.008, 0.016)); tick /= (np.max(np.abs(tick)) + 1e-9)
        i = int(t * SR / FR); out[i:i + L] += tick * lev * 0.55
        if rng.random() < 0.25:
            j = i + int((2 + rng.integers(0, 2)) * SR / FR); out[j:j + L] += tick * lev * 0.28                  # 跳ねる
    return out


def axe_burst(frames=27, bright=-1, level=1.0, voices=4, seed_ph=0):
    """斧の命中 (参考: FE 聖魔の光石 16.5〜16.92 秒): 約 0.4 秒の、 広帯域で太い雑音の塊。 低中域 (〜2kHz) の厚みから始まり、 5〜6kHz まで広がり、 縦のしま模様 (約 30ms の粒) があり、 最後に急に切れる。"""
    seq = [12, 12, 11, 11, 11, 11, 10, 10, 10, 10, 9, 9, 9, 9, 9, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8][:frames]            # 速さの段階 = 内容の上限 (12 = 約 1.2kHz … 8 = 約 4.4kHz)
    pi = [int(np.clip(p - bright, 2, 15)) for p in seq]
    pi[0], pi[1] = 4, 5                                                          # 頭の 2 フレームは広帯域の破裂 (命中の瞬間に最大にする)
    fall = [1.0 - 0.5 * i / max(1, frames - 1) for i in range(frames)]            # 命中の瞬間が最大で、 その後は小さくなる (後ろにずれて聞こえない)
    vol = [15.0 * fall[i] for i in range(frames)]                                               # なめらかに小さくなる (毎フレームの音量の切り替え = 30Hz のジリジリは入れない)
    vol[0] = vol[1] = 15.0
    vol = [v * level for v in vol]; vol[-1] = 0
    ph = [0, 977, 4001, 9001][:voices]; gains = [1.0, 0.9, 0.8, 0.7][:voices]
    low = noise([min(15, p + 2) for p in pi], [v * 1.0 for v in vol], phase=seed_ph + 12345) * 0.9                                                     # 低中域の厚み (0〜1kHz) をもう 1 声
    return mix(*[noise(pi, [v * g for v in vol], phase=ph[k] + seed_ph) * 0.6 for k, g in enumerate(gains[:voices])], low)


def hit_axe(crit=False):
    """斧 = パワー: 広帯域で太い雑音の塊 (参考どおり)。 会心は 2 段 (大きな塊 → すぐ次の塊) + 瓦礫が落ちる音。"""
    if not crit: return mix(axe_burst(27), rubble(16, 5, 0.8, seed=3, start=2))      # 瓦礫は命中の塊に重ねる (後ろにずらさない = 目立たせない)
    return mix(axe_burst(27, bright=-1, level=1.0) * 1.15, shift(axe_burst(23, bright=0, level=0.75, seed_ph=311), 10) * 0.55, rubble(30, 11, 0.85, seed=7, start=3), bright_burst(0, 0.9))      # 命中の瞬間 (0 フレーム) に最大。 2 発目は小さい余波 (命中のあとにずれて聞こえないように)
add('hit_axe', hit_axe(False), -5, hp=120)
add('crit_axe', hit_axe(True), -3, hp=120)

# 盾 = チャージとエネルギー。 チャージ: 上がり続ける複数の声 (揺れる主旋律 + 5 度・オクターブ + 段々上がるアルペジオ + 高まる息のノイズ + 増えていく火花) を重ねる。 解放: 下がる複数のビュッ + 広帯域の破裂 + 厚い打撃 + 火花のアルペジオ + 余韻。 金属の響きは入れない。
def charge_shield(n=27, f1=900, rich=1.0):
    f = np.exp(np.linspace(np.log(180), np.log(f1), n))
    vib = 1 + 0.045 * np.sin(np.arange(n) * 2 * np.pi * 7.0 / 60.0 * 1.0) * np.linspace(0.3, 1, n)                              # 揺れ (ビブラート)
    fv = list(f * vib); base = np.linspace(3, 12, n)
    vol = [max(0.0, base[i] - (3 if i % 2 else 0)) if i < n - 1 else 0 for i in range(n)]
    duty = [0.25 if (i // 3) % 2 == 0 else 0.5 for i in range(n)]                                                           # 3 フレームごとに音色が変わる
    main = pulse(fv, vol, duty) * 0.9
    fifth = pulse([x * 1.5 for x in f], [v * 0.55 for v in vol], 0.125) * 0.5                                               # 5 度上
    octv = pulse([x * 2 for x in f], [v * 0.45 for v in vol], 0.5) * 0.45                                                     # 1 オクターブ上
    ratios = [1, 1.25, 1.5, 2, 2.5, 3]; arp_f = []; arp_v = []                                                              # 段々上がるアルペジオ (4 フレームごと)
    for i in range(n):
        k = min(len(ratios) - 1, i // 4); arp_f.append(f[i] * 2 * ratios[k] / 2); arp_v.append((6 + 6 * i / n) if i % 4 < 3 and i < n - 1 else 0)
    arp = pulse(arp_f, arp_v, 0.125) * 0.45 * rich
    breath = noise(list(np.linspace(13, 5, n).astype(int)), list(np.linspace(2, 11, n)[:n - 1]) + [0]) * 0.6                    # 高まる息
    rng = np.random.default_rng(5); sp = [0.0] * n
    for i in range(n):
        if rng.random() < 0.15 + 0.5 * i / n and i < n - 1: sp[i] = 10 + 4 * i / n
    spark = pulse(list(rng.uniform(3000, 5200, n)), sp, 0.125) * 0.35 * rich                                                  # 増えていく火花
    mid_low = tri(fv, list(np.minimum(1.0, base / 12.0)[:n - 1]) + [0]) * 0.8                                            # 中低域の声 (主旋律と同じ高さの三角波。 180Hz から)
    return mix(main, fifth, octv, arp, breath, spark, mid_low)
add('charge_shield', charge_shield(27, 900), -10, hp=120)
add('charge_shield_crit', charge_shield(51, 1700, 1.3), -8, hp=120)

def release_shield(crit=False):
    """命中の瞬間 = 剣・斧と同じ「当たった」手応え (広帯域の破裂 + 中低域の胴) を主役にして、 インパクトの種類・タイミングをそろえる。 エネルギー (下がるビュッ・火花) は、 その上に控えめに重ねる。"""
    zap = lambda f0, f1, n, d=0.25: pulse(ramp(f0, f1, n), decay(15, n, 0), d)
    body = lambda w=1.0: mix(noise([12, 13, 13, 14, 14, 14], [15, 14, 12, 9, 5, 0]) * 1.1 * w, tri(ramp(210, 140, 9), [1] * 8 + [0]) * 1.5 * w)      # 中低域の胴
    tinkle = lambda t0, n=3, a=1.0: shift(mix(*[shift(pulse([4400 * 0.85 ** k] * 3, [13, 8, 3], 0.125) * 0.5 * a, 2 * k) for k in range(n)]), t0)      # 火花 (控えめ)
    if not crit:
        return mix(axe_burst(14, bright=-1, level=1.0), body(), zap(2300, 480, 8) * 0.3, tinkle(1, 3, 0.45))
    return mix(axe_burst(27, bright=-1, level=1.0) * 1.1, body(1.2), shift(body(1.2), 5), shift(axe_burst(16, bright=0, level=0.7, seed_ph=311), 5) * 0.55,
               zap(3000, 330, 12) * 0.35, shift(zap(2000, 330, 10), 3) * 0.25, tinkle(2, 5, 0.6), bright_burst(0, 0.9))
add('hit_shield', release_shield(False), -5.5, hp=120)
add('crit_shield', release_shield(True), -3, hp=120)

# ── 風切り (振りかぶり): 低い山 2 つ = 遅いノイズの音量を 2 回上げ下げ (参考 10.27 / 10.40 秒) ──
# 風切り (振りかぶり): ザラついた LFSR ノイズではなく、 なめらかな雑音を、 中心周波数が駆け上がって落ちる帯域フィルターに通す = 1 回の「ヒュッ」。 持続せず、 山は 1 つ
def swoosh(d=0.22, f_lo=650, f_hi=3200, f_end=1700, peak=0.11, q=2.2, seed=11, sub=0.0, sub_hz=95):
    n = int(d * SR); t = np.arange(n) / SR; rng = np.random.default_rng(seed); w = rng.standard_normal(n)
    fc = np.where(t < peak, f_lo * (f_hi / f_lo) ** (t / peak), f_hi * (f_end / f_hi) ** ((t - peak) / (d - peak)))     # 上がって、 少し落ちる
    out = np.zeros(n); zi = None; blk = 64
    for i in range(0, n, blk):
        f = float(fc[i]); sos = butter(2, [max(80, f / (1 + 1 / q)), min(SR / 2 - 200, f * (1 + 1 / q))], btype='band', fs=SR, output='sos')
        if zi is None: zi = np.zeros((sos.shape[0], 2))
        out[i:i + blk], zi = sosfilt(sos, w[i:i + blk], zi=zi)
    env = np.where(t < peak, (t / peak) ** 1.6, np.exp(-((t - peak) / 0.055) ** 1.0))                             # 立ち上がり → ピーク → 速く消える
    x = out * env
    if sub > 0:      # 重さ: 風切りの山に合わせて膨らむ低い唸り (周波数は山のあいだに少し下がる)
        hz = sub_hz * np.where(t < peak, 1.0, 1.0 - 0.25 * (t - peak) / (d - peak)); x = x / (np.max(np.abs(x)) + 1e-9) + sub * np.sin(2 * np.pi * np.cumsum(hz / SR)) * env
    return x
# 参考 (FE の斧の振り 14.57〜14.85 秒) に合わせる: 低い帯域 (〜1kHz) から始まり、 約 2.2kHz まで広がり、 また狭まる「山」形。 約 0.28 秒
# 厚み: 帯域の違う 3 本の風切りを重ね (低中域・中域・やや上)、 山の頂上を広げ、 軽く圧縮して密度を上げる。 重低音は入れない
def swing_thick(d=0.36):
    layers = [(0.75, 0.7, 11, 1.0), (1.0, 0.8, 12, 1.0), (1.55, 0.9, 13, 0.55)]
    x = sum(swoosh(d=d, f_lo=250 * k, f_hi=820 * k, f_end=280 * k, peak=0.17, q=q, seed=sd) / (np.max(np.abs(swoosh(d=d, f_lo=250 * k, f_hi=820 * k, f_end=280 * k, peak=0.17, q=q, seed=sd))) + 1e-9) * g for k, q, sd, g in layers)
    t = np.arange(len(x)) / SR
    plateau = np.where(t < 0.17, (t / 0.17) ** 0.8, np.where(t < 0.25, 1.0, np.exp(-(t - 0.25) / 0.06)))      # 山の頂上を広げて、 そのあと速く消える
    x = x * plateau
    x = np.tanh(2.2 * x / (np.max(np.abs(x)) + 1e-9)) / np.tanh(2.2)                                           # 軽い圧縮 (密度を上げる)
    return x
# 風切りも、 ほかの音と同じエンジン (LFSR ノイズ + フレームの表) で作る = 質感 (解像度) をそろえる。
# ノイズの速さの段階が、 遅い (こもる) → 速い (広がる) → 遅い と山形に動き、 音量は山の頂上を保って消える。 2 声を重ねて厚みを出す。 重低音は入れない
def swing_chip(voices=3):
    """同じ形の声を、 ノイズ系列の位相をずらして重ねる = 粒が平均化されて、 ザラつきが減る (声が多いほどなめらか)。"""
    pi = [14, 14, 13, 13, 12, 12, 11, 11, 10, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 14]
    vol = [3, 6, 8, 10, 12, 13, 14, 15, 15, 15, 15, 14, 13, 11, 9, 7, 5, 3, 1, 0]
    out = []
    for k in range(voices):
        d = (0, -1, 0)[k % 3]; g = (1.0, 0.7, 0.8)[k % 3]; ph = (0, 977, 4001)[k % 3]                          # 位相をずらすのは 1 声だけ (平均化を弱めて、 粒を残す)。 こもる声は入れない
        out.append(noise([int(np.clip(p + d, 8, 15)) for p in pi], [v * g for v in vol], phase=ph))
    return mix(*out) / np.sqrt(voices / 2.0)
add('slash_swing', swing_chip(), -6, hp=170, lp=9500)      # 風切り (ほかの音と同じ作り)

# ── 防御 (金属): 短周期ノイズ + 高い矩形波の減衰 ──
# 防御 (澄んだ金属の「キィン」): 整数倍 (1:2:3) の純音を、 高いほど速く減衰させる。 頭に 1〜2 フレームだけ明るいノイズの当たり。 不協和 (整数比でない音の重なり・うなり) は作らない
def clang_pure(f0=1568, frames=18):
    """金属寄り: 頭に硬い当たり (短周期ノイズ + 低中域の「ガッ」)、 基音は 1568Hz、 倍音は 1〜6 の整数倍 (不協和にしない)。 高い倍音ほど速く消える。"""
    w = [1.0, 0.8, 0.6, 0.45, 0.3, 0.2]; tau = [9.0, 6.5, 5.0, 3.8, 3.0, 2.2]
    tones = [sine([f0 * (k + 1)] * frames, [15 * np.exp(-i / tau[k]) if i < frames - 1 else 0 for i in range(frames)]) * w[k] for k in range(6)]
    hit = mix(noise([2, 3, 4, 5, 6, 7], [15, 13, 9, 6, 3, 0], short=[True, True, False, False, False, False]) * 0.9,       # 硬い当たり (ガキッ)
              tri([330, 250, 200], [1, 1, 0]) * 0.35)                                                                    # 低中域の「ガッ」
    return mix(*tones, hit)
add('block', clang_pure(), -6, hp=400)

# ── 決定 (GO! / ROLL): 電子音ではなく、 ノイズの小さな「カチッ」×2 ──
add('decide', mix(noise([6, 9, 11, 0, 7, 10], [11, 8, 5, 0, 9, 4]), tri([240, 160], [1, 1]) * 0.25), -14)

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for name, x in S.items():
        sf.write(os.path.join(OUT, name + '.wav'), x, SR, subtype='PCM_16')
    print(len(S), 'chip sounds ->', os.path.normpath(OUT))
