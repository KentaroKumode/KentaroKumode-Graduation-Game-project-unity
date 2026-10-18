#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵を、 古い 2 色表示 (点灯 / 消灯) のモニターに映したような絵にする (2026-10-10)。

  各画素を明るさで「点灯」か「消灯」の 2 値にする。 色は点灯色と消灯色の 2 色だけ。
    threshold : しきい値で切る (面がベタに残る・陰影は落ちる)
    どちらも、 輪郭 (背景に接する画素) は必ず点灯
    dither    : 4×4 のバイエル行列で、 中間の明るさを網点にする (陰影が残る・古い 1 ビット画面の質感)
  点灯色: radar (暗い画面に橙赤の線・参考画像 21.png から採色) / green (緑の蛍光体) / amber (琥珀) / white (白黒)

使い方: python Tools/mono_monitor.py 入力.png [出力フォルダ] [--scale 10]
"""
import argparse
import os

import numpy as np
from PIL import Image

PHOS = {"radar": ((222, 96, 42), (24, 26, 24)), "green": ((51, 255, 102), (4, 20, 10)), "amber": ((255, 176, 0), (20, 12, 0)), "white": ((232, 232, 232), (0, 0, 0))}
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 + 1 / 32.0


def tone(im):
    a = np.asarray(im.convert("RGB")).astype(float)
    return (0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]) / 255.0


def to_mono(im, mode, cut):
    t = tone(im)
    if mode == "threshold": on = t >= cut
    else:
        h, w = t.shape
        bx = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w]
        on = t > bx * 0.9                                            # 中間の明るさは網点になる
    # 輪郭 (背景に接する画素) は必ず点灯: 影の面が消えても形が読める
    sil = t > 0
    pad = np.pad(sil, 1)
    edge = sil & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    return on | edge


def paint(on, col):
    lit, dark = PHOS[col]
    arr = np.where(on[..., None], np.array(lit), np.array(dark)).astype(np.uint8)
    return Image.fromarray(arr, "RGB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("out", nargs="?", default=os.path.join("docs", "design", "supply"))
    ap.add_argument("--scale", type=int, default=10); ap.add_argument("--cut", type=float, default=0.38)
    ap.add_argument("--name", default="stone_mono")
    a = ap.parse_args()
    im = Image.open(a.src); os.makedirs(a.out, exist_ok=True)
    cols, modes = list(PHOS), ("threshold", "dither")
    w, h = im.size; k = a.scale; pad = 12
    sheet = Image.new("RGB", ((w * k + pad) * len(cols) + pad, (h * k + pad) * len(modes) + pad), (42, 38, 32))
    for r, m in enumerate(modes):
        on = to_mono(im, m, a.cut)
        for c, col in enumerate(cols):
            p = paint(on, col)
            p.save(os.path.join(a.out, f"{a.name}_{m}_{col}.png"))
            sheet.paste(p.resize((w * k, h * k), Image.NEAREST), (pad + c * (w * k + pad), pad + r * (h * k + pad)))
    sheet.save(os.path.join(a.out, f"{a.name}_sheet.png"))
    print("wrote", os.path.join(a.out, f"{a.name}_sheet.png"))


if __name__ == "__main__":
    main()
