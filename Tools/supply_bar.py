#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""物資のバー (横 268 × 高さ 16) の試作 (2026-10-05)。 素材 = docs/design/supply/crate_dot.png (Tools/snap_pixels.py の出力)。

  ・中身は「棚に詰まった箱」。 山の絵から箱を切り出して左から並べる。 物資が減ると右端の箱から空になる
  ・段の境目 (75% / 45% / 20%) に仕切りの板。 区画ごとに棚の奥の板の色味が変わる (FULL → CRIT で冷たく暗く)
  ・減った直後の箱は残光 (1 ドット明るい縁) → 空の棚。 見積もり (行き先までの消費) は斜線の影で先端から左へ
  ・すべて 1 ドット = 1 ドット。 拡大は確認用だけ

使い方: python Tools/supply_bar.py [物資]  → docs/design/supply/bar.png (等倍) と bar_x4.png (確認用)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "docs", "design", "supply", "crate_dot.png")
OUT = os.path.join(ROOT, "docs", "design", "supply")
W, H = 268, 16
SUP_MAX = 1000
STAGES = [(0.75, "FULL"), (0.45, "HALF"), (0.20, "LOW")]      # 区画の境 (上限に対する割合)

# 箱の絵 (2026-10-05): 山の絵 (crate_dot.png) から切り出すと隣の箱の縁を巻き込んで汚れたので、
# 素材のパレットと質感 (輪郭 1 ドット・上と左が明るく下と右が暗い・まばらな傷) で高さ 10 に打ち直した。
# 文字: o=輪郭 / 1=最も明るい面 / 2=明るい面 / 3=面 / 4=暗い面 / 5=最も暗い面 / s=傷 / r=赤い帯 / .=透明
CRATE_ART = {
    "brown": ["oooooooooooo", "o1222222223o", "o2333333344o", "o2333s33344o", "o23333333s4o", "o2333333344o", "o2s33333344o", "o3444444455o", "o4555555555o", "oooooooooooo"],
    "green": ["ooooooooo", "o1222223o", "o2333334o", "o23s3334o", "o2333334o", "o233s334o", "o2333334o", "o3444445o", "o4555555o", "ooooooooo"],
    "tan":   ["ooooooooooooooo", "o1222222222223o", "o2333333333344o", "o233s3333333s4o", "o2333333333344o", "o3444444444455o", "o4555555555555o", "ooooooooooooooo"],
    "white": ["oooooooooo", "o11222222o", "o22333r34o", "o2333r334o", "o2333r344o", "o3444r455o", "o4555r555o", "oooooooooo"],
}
CRATE_PAL = {   # 素材 (crate_dot.png) の実在する色
    "brown": {"o": (22, 25, 32), "1": (216, 178, 145), "2": (179, 141, 113), "3": (137, 107, 88), "4": (100, 80, 70), "5": (67, 65, 63), "s": (100, 80, 70)},
    "green": {"o": (22, 25, 32), "1": (131, 126, 99), "2": (90, 90, 75), "3": (73, 76, 62), "4": (59, 62, 54), "5": (49, 52, 61), "s": (131, 126, 99)},
    "tan":   {"o": (22, 25, 32), "1": (216, 178, 145), "2": (174, 158, 147), "3": (137, 107, 88), "4": (100, 80, 70), "5": (67, 65, 63), "s": (179, 141, 113)},
    "white": {"o": (22, 25, 32), "1": (169, 167, 170), "2": (126, 119, 119), "3": (126, 119, 119), "4": (89, 87, 89), "5": (67, 65, 63), "r": (146, 74, 73), "s": (169, 167, 170)},
}

PAL = {
    "frame_d": (14, 15, 18), "frame_m": (49, 52, 61), "frame_l": (90, 90, 96), "frame_h": (169, 167, 170),
    "back": [(34, 36, 42), (30, 31, 38), (28, 26, 30), (30, 22, 24)],        # 区画ごとの棚の奥 (FULL → CRIT)
    "back_hi": [(44, 47, 55), (40, 41, 50), (38, 34, 38), (46, 28, 30)],
    "plank": (22, 25, 32), "plank_hi": (67, 65, 63),
    "after": (255, 236, 190), "est": (12, 12, 16),
}


def load():
    out = {}
    for name, rows in CRATE_ART.items():
        im = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0)); px = im.load()
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch != ".": px[x, y] = CRATE_PAL[name][ch] + (255,)
        out[name] = im
    return out


def stage_of(sup):
    f = sup / SUP_MAX
    for i, (t, _) in enumerate(STAGES):
        if f > t: return i
    return 3


def layout(crates):
    """棚の箱の並び (左から)。 箱の幅の合計が中身の幅を超えるまで、 4 種を決まった順で繰り返す。"""
    order = ["brown", "green", "tan", "white", "green", "brown", "white", "tan"]
    x, out, i = 3, [], 0
    inner = W - 3 - 2
    while True:
        c = crates[order[i % len(order)]]
        if x + c.width + 1 > inner: break
        out.append((x, order[i % len(order)])); x += c.width + 1; i += 1
    return out, x


def draw(sup, est=0, after=None):
    """sup: 今の物資。 est: 行き先までの消費の見積もり (先端から左へ影)。 after: 直前の物資 (減った分を残光で)。"""
    crates = load()
    bar = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(bar)
    # 枠 (縁取り 1 + 上左が明・下右が暗の帯 1)
    d.rectangle([0, 0, W - 1, H - 1], fill=PAL["frame_d"])
    d.rectangle([1, 1, W - 2, H - 2], fill=PAL["frame_l"])
    d.rectangle([2, 2, W - 2, H - 2], fill=PAL["frame_m"])
    d.line([(1, 1), (W - 2, 1)], fill=PAL["frame_h"]); d.line([(1, 1), (1, H - 2)], fill=PAL["frame_h"])
    ix0, iy0, ix1, iy1 = 3, 3, W - 4, H - 4                       # 中身: 262×10
    # 棚の奥 (区画ごとに色味を変える)
    for x in range(ix0, ix1 + 1):
        f = (x - ix0) / (ix1 - ix0 + 1)
        st = 3 if f < STAGES[2][0] else 2 if f < STAGES[1][0] else 1 if f < STAGES[0][0] else 0
        for y in range(iy0, iy1 + 1):
            bar.putpixel((x, y), PAL["back"][st] + (255,))
        bar.putpixel((x, iy0), PAL["back_hi"][st] + (255,))
    # 箱を左から並べる。 物資の割合 f で、 並べた箱のうち左側 f の分だけが入っている
    boxes, used = layout(crates)
    full_x = ix0 + int(round((ix1 - ix0 + 1) * sup / SUP_MAX))
    prev_x = ix0 + int(round((ix1 - ix0 + 1) * (after if after is not None else sup) / SUP_MAX))
    floor_y = iy1                                                  # 箱は棚の底に置く
    for x, name in boxes:
        c = crates[name]; ox = ix0 + x - 3
        top = floor_y - c.height + 1
        if ox + c.width <= full_x:
            bar.alpha_composite(c, (ox, top))
        elif ox < full_x:                                          # 境目の箱は半分だけ (右側を切る)
            bar.alpha_composite(c.crop((0, 0, full_x - ox, c.height)), (ox, top))
        elif ox < prev_x:                                          # 減ったばかりの箱: 残光 (輪郭だけ明るい)
            ghost = Image.new("RGBA", c.size, (0, 0, 0, 0))
            m = np.asarray(c)[:, :, 3] > 0
            gp = ghost.load()
            for yy in range(c.height):
                for xx in range(c.width):
                    if m[yy, xx]: gp[xx, yy] = PAL["after"] + (140,)
            bar.alpha_composite(ghost, (ox, top))
    # 棚板 (底の 1 ドット)
    for x in range(ix0, ix1 + 1): bar.putpixel((x, iy1), PAL["plank"] + (255,))
    # 仕切りの板 (段の境目) ── 縦の線 + 上の小さな爪
    for t, _ in STAGES:
        x = ix0 + int(round((ix1 - ix0 + 1) * t))
        for y in range(iy0, iy1 + 1): bar.putpixel((x, y), PAL["plank"] + (255,)); bar.putpixel((x + 1, y), PAL["plank_hi"] + (255,)) if False else None
        bar.putpixel((x - 1, iy0), PAL["plank_hi"] + (255,)); bar.putpixel((x + 1, iy0), PAL["plank_hi"] + (255,))
    # 見積もりの影: 今の先端から左へ est ぶん、 斜線
    if est > 0:
        ex = max(ix0, full_x - int(round((ix1 - ix0 + 1) * est / SUP_MAX)))
        for x in range(ex, full_x):
            for y in range(iy0 + 1, iy1):
                if (x + y) % 3 == 0: bar.putpixel((x, y), PAL["est"] + (255,))
    # 角を落とす
    for (x, y) in ((0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1)): bar.putpixel((x, y), (0, 0, 0, 0))
    return bar


def main():
    """バーだけを出す。 引数: 物資 (既定 620)。 python Tools/supply_bar.py [物資]"""
    os.makedirs(OUT, exist_ok=True)
    sup = int(sys.argv[1]) if len(sys.argv) > 1 else 620
    bar = draw(sup)
    bar.save(os.path.join(OUT, "bar.png"))
    big = Image.new("RGBA", (W * 4, H * 4), (58, 56, 52, 255))
    big.alpha_composite(bar.resize((W * 4, H * 4), Image.NEAREST))
    big.save(os.path.join(OUT, "bar_x4.png"))
    print("wrote bar.png (%d×%d) / bar_x4.png  物資 %d" % (W, H, sup))


if __name__ == "__main__":
    main()
