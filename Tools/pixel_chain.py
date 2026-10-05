#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""アイコンの上の金具から鎖を伸ばし、 一点から吊り下げられた形のドット絵にする (2026-10-05)。

  ・上端に小さな吊り輪 (幅 5)。 そこから、 正面向きの輪 (幅 3・高さ 5・中央に 1 ドットの穴) と
    横向きの輪 (幅 1) を交互に重ねて下ろし、 最後の輪が金具の軸 (幅 3) の先にかぶる
  ・色は台座と同じ青灰 4 段 (明 H / 中 M / 暗 D / 最暗 K)。 左上が明るく右下が暗い (光は左上から)
  ・横向きの輪を先に描き、 正面の輪を後から重ねる ── 穴の中に横向きの輪が見え、 輪の下辺が横向きの輪を隠す

使い方: python Tools/pixel_chain.py アイコン.png [出力.png] [--links 5] [--scale 8]
  --links = 吊り輪の下に連ねる正面向きの輪の数 (多いほど長い)
"""
import argparse
import os

from PIL import Image

H, M, D, K = (131, 151, 159), (97, 115, 122), (65, 77, 82), (49, 52, 61)
FACE = ["MHM", "H.D", "H.D", "H.D", "KDK"]              # 正面向きの輪 (3×5)
EDGE = ["M", "H", "M", "D", "K"]                        # 横向きの輪 (1×5)
RING = [".MHM.", "H...D", "H...D", "M...K", ".KDK."]    # 吊り輪 (5×5)
C = {"H": H, "M": M, "D": D, "K": K}
CX = 9                                                  # 中心の列 (金具の軸の中心)
PITCH = 6                                               # 正面の輪どうしの間隔 (間に横向きの輪が 1 つ入る)


def put(img, x0, y0, rows):
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            if ch != ".": img.putpixel((x0 + i, y0 + j), C[ch] + (255,))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--links", type=int, default=5); ap.add_argument("--scale", type=int, default=8)
    args = ap.parse_args()
    icon = Image.open(args.src).convert("RGBA")
    k = args.links                                       # 吊り輪の下に正面の輪が k 個
    icon_y = PITCH * k + 3                               # 最後の正面の輪の下 2 行を金具の軸の先がかぶる
    img = Image.new("RGBA", (icon.width, icon_y + icon.height), (0, 0, 0, 0))
    for n in range(k + 1):                               # 横向きの輪を先に (吊り輪と各正面の輪の間)
        put(img, CX, PITCH * n + 3, [c for c in EDGE])
    put(img, CX - 2, 0, RING)                            # 吊り輪
    for n in range(1, k + 1):                            # 正面の輪
        put(img, CX - 1, PITCH * n, FACE)
    img.alpha_composite(icon, (0, icon_y))               # 金具 (石の吊り下げ) を最後にかぶせる
    dst = args.dst or os.path.splitext(args.src)[0] + "_hanging.png"
    img.save(dst)
    big = Image.new("RGBA", (img.width * args.scale, img.height * args.scale), (42, 38, 32, 255))
    big.alpha_composite(img.resize((img.width * args.scale, img.height * args.scale), Image.NEAREST))
    big.save(os.path.splitext(dst)[0] + f"_x{args.scale}.png")
    print("wrote", dst, "(%d×%d)" % img.size)


if __name__ == "__main__":
    main()
