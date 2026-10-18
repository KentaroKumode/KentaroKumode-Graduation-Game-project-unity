#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵の、 見分けがつかないほど近い色をまとめて、 色数を減らす (2026-10-11)。

  色を画素数の多い順に見て、 すでに残した色のどれかと Lab の色差 (ΔE) が --de 以内なら、 その色へまとめる (画素数の多い方が残る)。 新しい色は作らない。
  形・配置は変わらない。 --keep-dark 以下の輝度 (黒の縁取りや背景) は、 まとめる対象から外す (縁取りと暗い面が混ざらないように)。

使い方: python Tools/merge_colors.py 入力.png 出力.png [--de 6] [--keep-dark 12]
"""
import argparse

import numpy as np
from PIL import Image


def lab(rgb):
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    m = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = c @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst")
    ap.add_argument("--de", type=float, default=6.0); ap.add_argument("--keep-dark", type=float, default=12.0)
    a = ap.parse_args()
    im = Image.open(a.src); mode = im.mode; arr = np.asarray(im).copy(); rgb = arr[..., :3]
    cols, cnt = np.unique(rgb.reshape(-1, 3), axis=0, return_counts=True)
    order = np.argsort(-cnt); L = lab(cols)
    lum = 0.299 * cols[:, 0] + 0.587 * cols[:, 1] + 0.114 * cols[:, 2]
    reps = []; mapping = {}
    for i in order:
        if lum[i] <= a.keep_dark: mapping[tuple(cols[i])] = tuple(cols[i]); continue
        hit = next((j for j in reps if np.linalg.norm(L[i] - L[j]) <= a.de), None)
        if hit is None: reps.append(i); mapping[tuple(cols[i])] = tuple(cols[i])
        else: mapping[tuple(cols[i])] = tuple(cols[hit])
    h, w = rgb.shape[:2]
    for y in range(h):
        for x in range(w):
            rgb[y, x] = mapping[tuple(int(v) for v in rgb[y, x])]
    arr[..., :3] = rgb
    Image.fromarray(arr, mode).save(a.dst)
    print("色数 %d → %d (dE<=%.1f)" % (len(cols), len({tuple(v) for v in mapping.values()}), a.de))


if __name__ == "__main__":
    main()
