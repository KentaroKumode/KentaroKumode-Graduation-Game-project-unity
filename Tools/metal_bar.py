#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""物資バーの中身: 削れたり汚れたりした黄色い金属の板 (262×8・2026-10-05)。

  箱の絵を加工した版は、 他の UI より細かく見えて浮いた (ユーザー指摘)。 ここでは UI と同じ粗さで、 少数の平らな色だけで描く。

  地   … 琥珀の黄色い金属の平らな板。 8 行を 上: 枠の影 / 明るい縁 / 面 4 行 (同色) / やや暗い 1 行 / 下の縁 に分ける
  汚れ … 上下の縁 (フチ付近の行) だけに、 暗い汚れ・赤茶の錆びた染み・ごく少数の暗い点 (中央付近は塗りムラ程度の明暗の揺らぎだけ・2026-10-05)
  削れ … 黄色い塗装が欠けて、 下の鋼が見える欠け (フチ付近の行だけ)
  傷   … 長く斜めに通る細い線 (暗い溝)

  バーは左へ流れて端がつながるので、 左右の端が連続する絵にする (模様は全部 x を 262 で巻く)。
  黄色が主役のまま読めるよう、 汚れ・削れは控えめにする (--wear で強さを変えられる)。

使い方: python Tools/metal_bar.py [出力.png] [--wear 1.5] [--colors 22] [--seed 7]
"""
import argparse
import os

import numpy as np
from PIL import Image

W, H = 262, 8
# 地: 8 行 (上 → 下)。 UI の琥珀 (236,170,66) と明るい琥珀 (255,230,158) を基準に、 数段に絞る
# 平らな板 (2026-10-05: 膨らんで見えたので、 面を 1 色にして、 上の細い明るい縁と下の細い暗い縁だけ残す)
ROWS = [(152, 106, 42), (250, 204, 108), (238, 172, 66), (238, 172, 66), (238, 172, 66), (238, 172, 66), (222, 158, 58), (146, 98, 38)]   # 縁の影も地の 6 割以上の明るさに抑える (他の GUI くらいの色差)
# 汚れの色は黄色い地の暗い側の色相にそろえる (2026-10-05: 黒・赤橙・青灰が地から浮いたので)。 真っ黒は使わない
# 2026-10-05: 汚れの主張が強かったので、 どれも地 (238,172,66) の明るさの 7 割前後までに抑える (他の GUI の隣り合う色の差と同じ程度)
STEEL = [(168, 136, 92), (184, 154, 108), (200, 172, 124), (214, 190, 146)]      # 欠けて見える鋼: 暖色に染まった明るめの灰
RUST = (192, 124, 54)                                                         # 赤みを抑えた焦げ橙
GRIME = (176, 124, 50)                                                         # 黄土 (地よりやや暗いだけ)
SEAM, SEAM_HI = (96, 58, 22), (255, 214, 120)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dst", nargs="?", default=os.path.join("docs", "design", "supply", "bar_metal.png"))
    ap.add_argument("--wear", type=float, default=1.5); ap.add_argument("--colors", type=int, default=22); ap.add_argument("--seed", type=int, default=7)
    args = ap.parse_args()
    rng = np.random.default_rng(args.seed)
    k = args.wear
    px = np.zeros((H, W, 3), np.uint8)
    for y in range(H): px[y, :] = ROWS[y]

    def put(x, y, col, a=1.0):
        if 0 <= y < H:
            x %= W                                                      # 左右の端をつなぐ
            px[y, x] = (np.array(px[y, x], float) * (1 - a) + np.array(col, float) * a).round().astype(np.uint8)

    # 塗りムラ (中央付近の行 y2〜5): ゆるい波の合成で、 地の明るさを ±4% ほど揺らす。 周期は 262 の約数に近い値にして左右の端をつなぐ
    ph = rng.random(3) * 2 * np.pi
    for x in range(W):
        n = 0.5 * np.sin(2 * np.pi * x * 11 / W + ph[0]) + 0.3 * np.sin(2 * np.pi * x * 29 / W + ph[1]) + 0.2 * np.sin(2 * np.pi * x * 53 / W + ph[2])
        for y in range(2, 6):
            put(x, y, (255, 226, 150) if n > 0 else (204, 142, 54), min(0.08, abs(n) * 0.08 * (1.0 if y in (3, 4) else 0.6)) * (0.6 + 0.4 * k))
    # 汚れ: 上下の縁に溜まる暗い汚れ (長さ 3〜14・縁ほど濃い) と、 そこから 1 行だけ垂れる点
    for _ in range(int(34 * k)):
        x = int(rng.integers(0, W)); ln = int(rng.integers(3, 14)); top = rng.random() < 0.5
        for i in range(ln):
            fade = 1 - abs(i - ln / 2) / (ln / 2 + 1)
            put(x + i, 1 if top else 6, GRIME, 0.45 * fade + 0.15)
            put(x + i, 0 if top else 7, GRIME, 0.5 * fade + 0.2)
            if rng.random() < 0.12 * fade: put(x + i, 2 if top else 5, GRIME, 0.35)
    # 削れ: 塗装が欠けて下の鋼が見える (縁に近い y1〜y2 / y5〜y6・幅 2〜7・高さ 1〜2)。 欠けの下辺に最暗の鋼の縁
    for _ in range(int(11 * k)):
        x = int(rng.integers(0, W)); wd = int(rng.integers(2, 8)); hh = int(rng.integers(1, 3))
        y0 = int(rng.choice([1, 5]))                                      # フチの行だけ (上: y1〜2 / 下: y5〜6)
        for j in range(hh):
            for i in range(wd - j):                                      # 上ほど短い三角気味の欠け
                if rng.random() < 0.9: put(x + i + j, y0 + j, STEEL[2 if rng.random() < 0.6 else 3 if j == 0 else 1])
        for i in range(wd): put(x + i, y0 + hh, STEEL[0], 0.8)           # 欠けの下の縁 (影)
    # 錆びた染み: 欠けの近くの赤茶 (幅 3〜8・縁寄り)
    for _ in range(int(8 * k)):
        x = int(rng.integers(0, W)); wd = int(rng.integers(3, 9)); y0 = int(rng.choice([1, 6]))
        for i in range(wd):
            a = 0.55 * (1 - abs(i - wd / 2) / (wd / 2 + 1))
            put(x + i, y0, RUST, a); put(x + i, y0 + (1 if y0 == 1 else -1), RUST, a * 0.6)
    # 傷: フチ付近の行 (上 y1〜2 / 下 y5〜6) の中だけを斜めに通る細い線。 中央の行へは出ない。 地より暗い黄土の溝 + 上に弱い明るい縁
    for _ in range(int(11 * k)):
        top = rng.random() < 0.5; lo, hi = (1, 2) if top else (5, 6)
        x = int(rng.integers(0, W)); ln = int(rng.integers(10, 34)); dy = 1 if top else -1
        yf = float(lo if top else hi)
        for i in range(ln):
            yf += dy / 9.0                                                           # ゆっくり中へ向かい、 ゾーンを出たら止める
            yy = int(round(yf))
            if yy < lo or yy > hi: break
            if rng.random() < 0.9: put(x + i, yy, (182, 128, 50), 0.8); put(x + i, yy - 1, (252, 214, 124), 0.18)
    # 欠けの小さな点 (暗い・明るい): ごく少数
    for _ in range(int(26 * k)):
        put(int(rng.integers(0, W)), int(rng.choice([1, 2, 5, 6])), GRIME if rng.random() < 0.7 else STEEL[3], 0.7)
    # 色を絞る: 混ぜて増えた色を、 少数の平らな色にまとめる (UI と同じ粗さ・ディザなし)
    q = Image.fromarray(px, "RGB").quantize(colors=args.colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    img = q.convert("RGB").convert("RGBA")
    px = np.asarray(img.convert("RGB"))
    os.makedirs(os.path.dirname(args.dst) or ".", exist_ok=True)
    img.save(args.dst)
    big = Image.new("RGBA", (W * 4, H * 4), (42, 38, 32, 255)); big.alpha_composite(img.resize((W * 4, H * 4), Image.NEAREST))
    big.save(os.path.splitext(args.dst)[0] + "_x4.png")
    print("wrote", args.dst, "(%d×%d) 色 %d" % (W, H, len({tuple(p) for p in px.reshape(-1, 3)})))


if __name__ == "__main__":
    main()
