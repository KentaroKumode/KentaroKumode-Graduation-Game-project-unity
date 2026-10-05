#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""写実的なドット絵アイコン (色数が多く、 グラデーションがある) を、 他の UI (少数の平らな色・暗い 1 ドットの輪郭) になじませる (2026-10-05)。

  1. 背景を透明にする (元が透明ならそのまま・無ければ外周から続く黒)
  2. 画素を 「金属 (彩度が低い)」 と 「結晶 (青い)」 に分ける。 暗い輪郭はそのまま輪郭の色へ
  3. それぞれ、 明るさで少数の段に割り当てる (金属 = 枠の青灰の 4 段 / 結晶 = くすませた青の 4 段)。 グラデーションは平らな面になる
  4. 孤立した 1 画素の段違い (ノイズ) を、 周りの多数派へならす

使い方: python Tools/harmonize_icon.py 入力.png [出力.png] [--levels 4] [--despeckle 1] [--scale 12]
"""
import argparse
import colorsys
import os

import numpy as np
from PIL import Image

OUTLINE = (22, 25, 32)                                                  # 箱の絵・バーと同じ輪郭 (ほぼ黒の青)
METAL = [(49, 52, 61), (65, 77, 82), (97, 115, 122), (131, 151, 159)]   # 枠 (frame) の青灰 4 段 (暗 → 明)
CRYSTAL = [(30, 42, 78), (52, 82, 138), (96, 142, 196), (190, 218, 238)]  # くすませた青 4 段 (暗 → 明)


def remove_bg(a):
    """外周から続く純黒の領域を背景として透明にする。"""
    h, w = a.shape[:2]
    black = (a[:, :, :3].sum(2) == 0)
    seen = np.zeros((h, w), bool)
    stack = [(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)]
    while stack:
        y, x = stack.pop()
        if not (0 <= y < h and 0 <= x < w) or seen[y, x] or not black[y, x]: continue
        seen[y, x] = True
        stack += [(y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)]
    return seen


def classify(rgb):
    """0 = 輪郭 / 1 = 金属 / 2 = 結晶。 青い (色相 200〜260°・彩度が高い) ものを結晶、 暗すぎるものを輪郭、 他を金属とする。"""
    r, g, b = [v / 255 for v in rgb]
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    hue = h * 360
    if l < 0.13: return 0
    if 190 <= hue <= 270 and s > 0.28: return 2
    return 1


def posterize(vals, n):
    """明るさの値の列を、 分位で n 段に割り当てる (段の番号 0〜n-1)。"""
    if len(vals) == 0: return np.array([], int)
    qs = np.quantile(vals, np.linspace(0, 1, n + 1)[1:-1])
    return np.searchsorted(qs, vals, side="right")


def despeckle(level, cls, passes):
    """同じ種類 (cls) の中で、 周りの 8 近傍の多数派と違う孤立した段を、 多数派へならす。"""
    h, w = level.shape
    for _ in range(passes):
        out = level.copy()
        for y in range(h):
            for x in range(w):
                if cls[y, x] == 0: continue
                nb = [level[yy, xx] for yy in range(max(0, y - 1), min(h, y + 2)) for xx in range(max(0, x - 1), min(w, x + 2))
                      if (yy, xx) != (y, x) and cls[yy, xx] == cls[y, x]]
                if len(nb) >= 5:
                    vals, counts = np.unique(nb, return_counts=True)
                    top = vals[counts.argmax()]
                    if counts.max() >= 5 and level[y, x] != top: out[y, x] = top
        level = out
    return level


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--levels", type=int, default=4); ap.add_argument("--despeckle", type=int, default=1)
    ap.add_argument("--scale", type=int, default=12)
    args = ap.parse_args()
    a = np.asarray(Image.open(args.src).convert("RGBA")).astype(int)
    h, w = a.shape[:2]
    bg = (a[:, :, 3] == 0) if (a[:, :, 3] == 0).any() else remove_bg(a)      # 透明な画素があればそれが背景・無ければ外周の黒を背景にする
    cls = np.zeros((h, w), int)
    lum = np.zeros((h, w))
    for y in range(h):
        for x in range(w):
            if bg[y, x]: continue
            cls[y, x] = classify(a[y, x, :3])
            lum[y, x] = 0.299 * a[y, x, 0] + 0.587 * a[y, x, 1] + 0.114 * a[y, x, 2]
    level = np.zeros((h, w), int)
    for c in (1, 2):
        m = (cls == c) & ~bg
        level[m] = np.clip(posterize(lum[m], args.levels), 0, args.levels - 1)
    level = despeckle(level, np.where(bg, 0, cls), args.despeckle)
    out = np.zeros((h, w, 4), np.uint8)
    for y in range(h):
        for x in range(w):
            if bg[y, x]: continue
            c = cls[y, x]
            col = OUTLINE if c == 0 else (METAL if c == 1 else CRYSTAL)[min(level[y, x], 3)]
            out[y, x] = (*col, 255)
    dst = args.dst or os.path.splitext(args.src)[0] + "_harmonized.png"
    Image.fromarray(out, "RGBA").save(dst)
    big = Image.new("RGBA", (w * args.scale, h * args.scale), (58, 56, 52, 255))
    big.alpha_composite(Image.fromarray(out, "RGBA").resize((w * args.scale, h * args.scale), Image.NEAREST))
    big.save(os.path.splitext(dst)[0] + f"_x{args.scale}.png")
    print("wrote", dst, "(%d×%d)" % (w, h), " 色:", len({tuple(p) for p in out.reshape(-1, 4) if p[3]}))


if __name__ == "__main__":
    main()
