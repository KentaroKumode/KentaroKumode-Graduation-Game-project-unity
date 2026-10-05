#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""枠の縁に乗った色の線 (浮いて見える) を、 形はそのままに、 縁になじませる (2026-10-05)。

  形 (帯と、 左端の上向き・右端の下向きの足) は変えない。 浮いて見える原因は形ではなく、 色が縁の陰影と無関係な平らな純色であること。
  1. 陰影を通す: 元の線の画素の色 (2 行の段差・かすれ) は残したまま、 その位置の縁の明るさに軽く追従させ、 縁の色を少し混ぜる
  2. 縁取り: 帯に接する縁の画素を暗くして、 彫り込まれた溝のような輪郭にする
  3. 影: 帯の右下 1 ドットに、 縁より少し暗い影を落とす
  4. 滲み・剥げ・飛沫: 線のまわりに塗料の淡い滲み (下側に強い)・塗装の剥げ (下の行に多く・上の行は残る・縁の素地が見える欠け)・まばらな飛沫を足す (--bleed / --chip)
  (2026-10-05: 元の色を 1 色の塗りで上書きし、 暗い縁取りまで付けた版は、 ベタ塗りの太い枠線に見えて悪化した。 元の色を残す方式にした)

  入力: 線が乗った合成画像 (例 480×270) と、 枠だけの画像 (272×20)。 枠の位置は --pos x,y (既定 104,247)。

使い方: python Tools/integrate_marks.py 合成.png 枠.png [出力.png] [--pos 104,247] [--mix 0.25] [--follow 0.5] [--outline 1.0] [--shadow 1.0]
"""
import argparse
import colorsys
import os

import numpy as np
from PIL import Image

PAINT = {"red": (214, 88, 70), "orange": (230, 144, 60), "yellow": (236, 200, 84), "blue": (96, 152, 224)}
LIP_REF = 0.299 * 97 + 0.587 * 115 + 0.114 * 122                     # 縁の中央の明るさ (青灰 97,115,122)


def hue_name(rgb):
    h, l, s = colorsys.rgb_to_hls(*[v / 255 for v in rgb])
    hue = h * 360
    if hue < 18 or hue >= 340: return "red"
    if hue < 42: return "orange"
    if hue < 75: return "yellow"
    return "blue"


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("comp"); ap.add_argument("frame"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--pos", default="104,247")
    ap.add_argument("--mix", type=float, default=0.25, help="元の線の色へ混ぜる縁の色の割合 (0 で元のまま)")
    ap.add_argument("--follow", type=float, default=0.5, help="縁の明るさへの追従の強さ (0 で追従なし・1 で縁と同じ比)")
    ap.add_argument("--outline", type=float, default=1.0, help="縁取りの暗さ (縁の色に掛ける・小さいほど暗い)")
    ap.add_argument("--shadow", type=float, default=1.0, help="右下の影の暗さ (縁の色に掛ける)")
    ap.add_argument("--bleed", type=float, default=1.0, help="滲みの強さ (0 で無し)")
    ap.add_argument("--chip", type=float, default=1.0, help="剥げ・飛沫の量 (0 で無し)")
    ap.add_argument("--scale", type=int, default=4)
    args = ap.parse_args()
    fx, fy = [int(v) for v in args.pos.split(",")]
    comp = Image.open(args.comp).convert("RGBA"); frame = Image.open(args.frame).convert("RGBA")
    fw, fh = frame.size
    a = np.asarray(comp).astype(int)
    mark = {}                                                          # (x, y) → 色名
    for y in range(fy, fy + fh):
        for x in range(fx, fx + fw):
            r, g, b = a[y, x, :3]
            if max(r, g, b) - min(r, g, b) > 60 and a[y, x, 3] > 0: mark[(x, y)] = hue_name((r, g, b))
    print("帯の画素:", len(mark), "色:", sorted(set(mark.values())))

    def lip(x, y):
        return frame.getpixel((x - fx, y - fy))[:3]

    def inside(x, y):
        return 0 <= x - fx < fw and 0 <= y - fy < fh and frame.getpixel((x - fx, y - fy))[3] > 0

    out = comp.copy()
    # 1. 元の画素の色 (上の行が明るく・下の行が暗く・ところどころ暗い / 中間の色) を**そのまま残して**、 縁の明るさを軽く通し、 縁の色を少し混ぜる
    #    (全部 1 色の塗りで上書きすると、 元の陰影と段差が消えてベタ塗りになり、 かえって浮いて見える)
    for (x, y), name in mark.items():
        orig = tuple(int(v) for v in a[y, x, :3])
        base = lip(x, y)
        f = (lum(base) / LIP_REF) ** args.follow                        # 縁の明るさの比 (縁の暗い行ほど暗く)
        shaded = tuple(min(255, v * f) for v in orig)
        c = tuple(round(v + (b - v) * args.mix) for v, b in zip(shaded, base))
        out.putpixel((x, y), c + (255,))
    # 1b. 剥げ・滲み・飛沫 (2026-10-05)。 乱数は位置から決まる固定の値 (実行のたびに変わらない)
    def rnd(x, y, salt=0):
        v = ((x * 73856093) ^ (y * 19349663) ^ (salt * 83492791)) & 0xFFFFFF
        return ((v * 2654435761) & 0xFFFFFFFF) / 4294967296.0

    def is_lip(x, y):                                                   # 縁の画素 (窓の黒や枠の外は除く)
        return inside(x, y) and sum(lip(x, y)) > 40

    mean = {}                                                           # 色名ごとの塗料の平均色 (滲む色)
    for (x, y), name in mark.items(): mean.setdefault(name, []).append(a[y, x, :3])
    mean = {k: tuple(np.mean(v, axis=0)) for k, v in mean.items()}
    # 滲み: 線に接する縁の画素を線の色へ少し寄せる。 重力で下側にやや強く・上側は弱く。 距離 2 まで・むらあり
    if args.bleed > 0:
        for (x, y), name in mark.items():
            for dx, dy, w in ((0, 1, 0.26), (0, 2, 0.10), (-1, 0, 0.11), (1, 0, 0.11), (0, -1, 0.07), (-1, 1, 0.10), (1, 1, 0.10)):
                q = (x + dx, y + dy)
                if q in mark or not is_lip(*q): continue
                k = w * args.bleed * (0.55 + 0.9 * rnd(q[0], q[1], 1))
                cur = out.getpixel(q)[:3]
                out.putpixel(q, tuple(round(c + (m - c) * min(0.6, k)) for c, m in zip(cur, mean[name])) + (255,))
    # 剥げ: 線の画素のところどころ (約 10%) を縁の素地に戻す。 続けて 2 ドット剥げることもある。 欠けの上の縁にわずかな明るさ
    chipped = set()
    if args.chip > 0:
        for (x, y), name in mark.items():
            if not ((x - 1, y) in mark and (x + 1, y) in mark): continue            # 足 (縦の画素) と端は剥がさない
            lower = (x, y - 1) in mark                                              # 下の行 (暗い方) は縁に近く剥げやすい・上の行は残りやすい
            if rnd(x, y, 2) < (0.13 if lower else 0.03) * args.chip:
                chipped.add((x, y))
                if lower and rnd(x, y, 3) < 0.4 and (x + 1, y) in mark and (x + 2, y) in mark: chipped.add((x + 1, y))
        for (x, y) in chipped:
            out.putpixel((x, y), tuple(round(v * 1.02) for v in lip(x, y)) + (255,))
            up = (x, y - 1)
            if up not in mark and is_lip(*up):
                out.putpixel(up, tuple(min(255, round(v * 1.10)) for v in out.getpixel(up)[:3]) + (255,))
        # 飛沫: 線の近く (距離 2〜3) に、 ごく淡い塗料の点をまばらに
        for (x, y), name in mark.items():
            for dx, dy in ((0, 2), (0, 3), (2, 1), (-2, 1), (0, -2)):
                q = (x + dx, y + dy)
                if q in mark or not is_lip(*q) or rnd(q[0], q[1], 4) > 0.045 * args.chip: continue
                cur = out.getpixel(q)[:3]
                out.putpixel(q, tuple(round(c + (m - c) * 0.5) for c, m in zip(cur, mean[name])) + (255,))
    # 2. 縁取り (帯に接する縁の画素を暗く) と 3. 右下の影
    mark = {k: v for k, v in mark.items() if k not in chipped}
    for (x, y) in mark:
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            q = (x + dx, y + dy)
            if q in mark or not inside(*q): continue
            b = lip(*q)
            out.putpixel(q, tuple(round(v * args.outline) for v in b) + (255,))
    for (x, y) in mark:
        q = (x + 1, y + 1)
        if q in mark or not inside(*q): continue
        if out.getpixel(q)[:3] != lip(*q): continue                    # 縁取りが付いた画素はそのまま
        out.putpixel(q, tuple(round(v * args.shadow) for v in lip(*q)) + (255,))
    dst = args.dst or os.path.splitext(args.comp)[0] + "_integrated.png"
    out.save(dst)
    crop = out.crop((fx - 4, fy - 4, fx + fw + 4, fy + fh + 4))
    big = Image.new("RGBA", (crop.width * args.scale, crop.height * args.scale), (0, 0, 0, 255))
    big.alpha_composite(crop.resize((crop.width * args.scale, crop.height * args.scale), Image.NEAREST))
    big.save(os.path.splitext(dst)[0] + f"_x{args.scale}.png")
    print("wrote", dst)


if __name__ == "__main__":
    main()
