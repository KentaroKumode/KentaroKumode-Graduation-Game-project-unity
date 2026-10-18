#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵の明るすぎるハイライトを、 絵全体の明るさの範囲へ収める (2026-10-11)。

  明るさ (輝度) が --knee を超えた画素だけ、 超えた分を --ratio 倍に圧縮する。 色相と彩度の比は保つ (画素の RGB に同じ倍率を掛ける)。
  それ以下の画素は一切変えない。 ドット絵なので、 圧縮の結果できる色は元の色と 1 対 1 に対応する (色数は増えない)。

使い方: python Tools/tame_highlights.py 入力.png [出力.png] [--knee 130] [--ratio 0.35]
"""
import argparse
import os

import numpy as np
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--knee", type=float, default=130); ap.add_argument("--ratio", type=float, default=0.35)
    ap.add_argument("--scale", type=int, default=6)
    a = ap.parse_args()
    im = Image.open(a.src).convert("RGBA"); arr = np.asarray(im).astype(float)
    lum = 0.299 * arr[..., 0] + 0.587 * arr[..., 1] + 0.114 * arr[..., 2]
    new = np.where(lum > a.knee, a.knee + (lum - a.knee) * a.ratio, lum)
    k = np.where(lum > 0, new / np.maximum(lum, 1e-6), 1.0)
    out = arr.copy(); out[..., :3] = np.clip(arr[..., :3] * k[..., None], 0, 255)
    res = Image.fromarray(out.round().astype(np.uint8), "RGBA")
    dst = a.dst or os.path.splitext(a.src)[0] + "_tamed.png"
    res.save(dst)
    w, h = im.size; s = a.scale
    sheet = Image.new("RGBA", (w * s * 2 + 12, h * s), (42, 38, 32, 255))
    sheet.alpha_composite(im.resize((w * s, h * s), Image.NEAREST), (0, 0)); sheet.alpha_composite(res.resize((w * s, h * s), Image.NEAREST), (w * s + 12, 0))
    sheet.save(os.path.splitext(dst)[0] + "_compare.png")
    n = len({tuple(p) for p in np.asarray(im).reshape(-1, 4)}), len({tuple(p) for p in np.asarray(res).reshape(-1, 4)})
    print("色数 %d -> %d / 最も明るい輝度 %.0f -> %.0f" % (n[0], n[1], lum.max(), (0.299 * out[..., 0] + 0.587 * out[..., 1] + 0.114 * out[..., 2]).max()))


if __name__ == "__main__":
    main()
