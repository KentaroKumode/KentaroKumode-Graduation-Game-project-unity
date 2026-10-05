#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""物資バーの枠 (272×20) の縁に、 区間ごとの色の目印を描いた PNG を出す (2026-10-05)。

  CRIT = 赤 (物資 0〜200) / HALF = 黄 (201〜750・HALF ランプの範囲) / FULL = 青 (751〜1000)
  窓 (x5〜266・y6〜13) の下の枠の縁 (y15・16 の 2 行) に、 区間ごとの帯。 境目 (x58・x202) に暗い縦の仕切り。
  今いる区間 (--active) の帯は明るく、 ほかは枠の青灰になじむ暗さ。

  出力 (docs/design/supply/ の中):
    bar_marks_{crit,half,full}.png          目印だけ (透明背景・272×20)  ── 枠の上に重ねる用
    bar_frame_marked_{crit,half,full}.png   枠 + 目印                   ── そのまま差し替える用
    bar_marks_x4.png                        確認用 (3 状態を縦に並べた 4 倍)

  区間の幅は物資の上限に対する割合 (バーの列 = ceil(物資 × 262 / 上限))。 上限 1000 なら CRIT 53 列・HALF 144 列・FULL 65 列。

使い方: python Tools/bar_marks.py 枠.png [--max 1000]
"""
import argparse
import math
import os

from PIL import Image

WX, WW = 5, 262                                    # 窓の左端と幅 (枠 14.png)
FRAME_MID = (97, 115, 122)                         # 枠の青灰 (暗い区間はこの色へ寄せる)
ZONES = (("crit", (232, 70, 56)), ("half", (246, 208, 70)), ("full", (96, 160, 240)))
DIVIDER = (28, 32, 36)


def bands(v, sup_max):
    return max(0, min(WW, math.ceil(v * WW / sup_max)))


def marks(active, sup_max):
    """目印だけの画像 (透明背景)。 active = 明るくする区間 ('crit' / 'half' / 'full')。"""
    img = Image.new("RGBA", (272, 20), (0, 0, 0, 0))
    edges = [0, bands(200, sup_max), bands(750, sup_max), WW]
    for i, (name, col) in enumerate(ZONES):
        x0, x1 = edges[i], edges[i + 1]
        k = 1.0 if name == active else 0.42
        for y, b in ((15, 1.0), (16, 0.72)):
            c = tuple(round(f + (v * b - f) * k) for v, f in zip(col, FRAME_MID))
            for x in range(WX + x0, WX + x1): img.putpixel((x, y), c + (255,))
        if i > 0:
            for y in range(14, 18): img.putpixel((WX + x0, y), DIVIDER + (255,))
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("frame"); ap.add_argument("--max", type=int, default=1000, dest="sup_max")
    ap.add_argument("--out", default=os.path.join("docs", "design", "supply"))
    args = ap.parse_args()
    frame = Image.open(args.frame).convert("RGBA")
    os.makedirs(args.out, exist_ok=True)
    sheet = Image.new("RGBA", (272 * 4, (20 * 4 + 6) * 3), (42, 38, 32, 255))
    for k, (name, _) in enumerate(ZONES):
        m = marks(name, args.sup_max)
        m.save(os.path.join(args.out, f"bar_marks_{name}.png"))
        comp = frame.copy(); comp.alpha_composite(m)
        comp.save(os.path.join(args.out, f"bar_frame_marked_{name}.png"))
        sheet.alpha_composite(comp.resize((272 * 4, 20 * 4), Image.NEAREST), (0, k * (20 * 4 + 6)))
    sheet.save(os.path.join(args.out, "bar_marks_x4.png"))
    e = [0, bands(200, args.sup_max), bands(750, args.sup_max), WW]
    print("区間の列: CRIT %d / HALF %d / FULL %d (計 %d)" % (e[1], e[2] - e[1], e[3] - e[2], WW))


if __name__ == "__main__":
    main()
