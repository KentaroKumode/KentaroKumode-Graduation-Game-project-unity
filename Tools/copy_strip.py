#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵の横長の帯 (バーなど) を、 別の行へコピーする。 コピーした帯の錆び・ハイライトの位置が同じにならないよう、 左右反転と、 帯の中での巡回ずらしができる (2026-10-11)。

  --rows Y0 Y1      コピー元の行 (Y0〜Y1 を含む)
  --dst-y Y         コピー先の先頭の行
  --x X0 X1         コピーする列の範囲 (両端を含む)。 外の画素 (端の部品・アイコン) は触らない
  --mirror          帯を左右反転する
  --shift N         帯を左へ N 画素、 範囲の中で巡回してずらす (はみ出した分は反対側へ回る)

使い方: python Tools/copy_strip.py 入力.png 出力.png --rows 79 95 --dst-y 97 --x 36 240 --mirror --shift 37
"""
import argparse

import numpy as np
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst")
    ap.add_argument("--rows", nargs=2, type=int, required=True); ap.add_argument("--dst-y", type=int, required=True)
    ap.add_argument("--x", nargs=2, type=int, required=True)
    ap.add_argument("--mirror", action="store_true"); ap.add_argument("--shift", type=int, default=0)
    a = ap.parse_args()
    im = Image.open(a.src); mode = im.mode; arr = np.asarray(im).copy()
    y0, y1 = a.rows; x0, x1 = a.x
    strip = arr[y0:y1 + 1, x0:x1 + 1].copy()
    if a.mirror: strip = strip[:, ::-1]
    if a.shift: strip = np.roll(strip, -a.shift, axis=1)
    arr[a.dst_y:a.dst_y + (y1 - y0 + 1), x0:x1 + 1] = strip
    Image.fromarray(arr, mode).save(a.dst)
    print("行 %d〜%d を、 行 %d 以降へコピー (列 %d〜%d・反転=%s・ずらし=%d)" % (y0, y1, a.dst_y, x0, x1, a.mirror, a.shift))


if __name__ == "__main__":
    main()
