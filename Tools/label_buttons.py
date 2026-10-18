#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ボタンの面に、 モチーフ (11x11) と英語の名前 (5x7 のドット字) を描き込む (2026-10-12)。

  面の位置は、 画像から自動で探す (灰色の大きな連結成分 3 つ)。 色は 2 色 (--main と --dim) だけ。
  使い方: python Tools/label_buttons.py 入力.png 出力.png [--names TRAP FLARE RECON]
"""
import argparse
from collections import deque

import numpy as np
from PIL import Image

GLYPH = {                                                           # 高さ 4 のドット字 (幅は 3。 N だけ 4)
 "T": ["111", "010", "010", "010"],
 "R": ["110", "101", "110", "101"],
 "A": ["010", "101", "111", "101"],
 "P": ["110", "101", "110", "100"],
 "F": ["111", "110", "100", "100"],
 "L": ["100", "100", "100", "111"],
 "E": ["111", "110", "100", "111"],
 "C": ["011", "100", "100", "011"],
 "O": ["010", "101", "101", "010"],
 "N": ["1001", "1101", "1011", "1001"],
}
# 11x11 のモチーフ: A = 主な色 / d = 暗い色 / . = なし
ICON = {
 "TRAP": ["..AAAAAAA..",     # 熊の罠: 上の顎 (弧) と、 かみ合う歯、 下の顎 (弧)
          ".A.......A.",
          "A.A.A.A.A.A",
          ".A.A.A.A.A.",
          "A.........A",
          ".A.......A.",
          "..AAAAAAA.."],
 "FLARE": ["A....A....A",     # 照明弾の閃光 (8 方向の光線 + 中心)
           ".A...A...A.",
           "..A..d..A..",
           "...A.d.A...",
           "....AAA....",
           "AAddAAAddAA",
           "....AAA....",
           "...A.d.A...",
           "..A..d..A..",
           ".A...A...A.",
           "A....A....A"],
 "RECON": ["...AAAAA...",     # 目 (偵察)
           ".AAA.....AAA.".strip(".")[:11],
           "AA..ddddd..AA",
           "A..dAAAAAd..A",
           "A..dAAAAAd..A",
           "A..dAAAAAd..A",
           "AA..ddddd..AA",
           ".AAA.....AAA.".strip(".")[:11],
           "...AAAAA..."],
}
ICON["RECON"] = ["...AAAAA...", ".AAA...AAA.", "AA..ddd..AA", "A..dAAAd..A", "A..dAAAd..A", "A..dAAAd..A", "AA..ddd..AA", ".AAA...AAA.", "...AAAAA...", "...........", "..........."][:9]


def faces(arr, tol=6, target=(67, 68, 62), min_n=300):
    h, w = arr.shape[:2]
    mask = (np.abs(arr[..., :3].astype(int) - np.array(target)) <= tol).all(axis=2)
    seen = np.zeros_like(mask); out = []
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not seen[y, x]:
                q = deque([(x, y)]); seen[y, x] = True; pts = []
                while q:
                    cx, cy = q.popleft(); pts.append((cx, cy))
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        nx, ny = cx + dx, cy + dy
                        if 0 <= nx < w and 0 <= ny < h and mask[ny, nx] and not seen[ny, nx]: seen[ny, nx] = True; q.append((nx, ny))
                if len(pts) >= min_n:
                    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; out.append((min(xs), min(ys), max(xs), max(ys)))
    return sorted(out, key=lambda f: f[1])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst"); ap.add_argument("--names", nargs=3, default=["TRAP", "FLARE", "RECON"])
    ap.add_argument("--main", default="158,148,118"); ap.add_argument("--dim", default="104,98,82")
    ap.add_argument("--tints", default=None, help="彫りの底に流す色 (ボタンごと): R,G,B;R,G,B;R,G,B (彫りの陰影はそのまま)")
    ap.add_argument("--tint-amount", type=float, default=0.55)
    ap.add_argument("--style", choices=["engraved", "painted", "flat"], default="engraved", help="engraved = 面に彫り込んだ刻印 / painted = ステンシルの塗装 (擦れつき) / flat = 以前の平らな色")
    a = ap.parse_args()
    main_c = tuple(int(v) for v in a.main.split(",")); dim_c = tuple(int(v) for v in a.dim.split(","))
    im = Image.open(a.src).convert("RGB"); arr = np.asarray(im).copy(); fs = faces(arr)
    assert len(fs) == 3, fs
    orig = arr.copy()
    def mul(c, k): return tuple(max(0, min(255, int(round(v * k)))) for v in c)
    def mix(c, d, t): return tuple(int(round(v + (w - v) * t)) for v, w in zip(c, d))
    def rnd(x, y, salt): v = ((x * 73856093) ^ (y * 19349663) ^ (salt * 83492791)) & 0xFFFFFF; return ((v * 2654435761) & 0xFFFFFFFF) / 4294967296.0
    tints = [tuple(int(v) for v in t.split(",")) for t in a.tints.split(";")] if a.tints else [None] * 3
    for f, name, tint in zip(fs, a.names, tints):
        x0, y0, x1, y1 = f; w, h = x1 - x0 + 1, y1 - y0 + 1
        icon = ICON[name]; iw, ih = 11, len(icon); tw = sum(len(GLYPH[ch][0]) + 1 for ch in name) - 1
        total = iw + 5 + tw; sx = x0 + (w - total) // 2; iy = y0 + (h - ih) // 2; ty = y0 + (h - 4) // 2
        mask = {}                                                         # (x, y) → 0 = 主な画素 / 1 = 暗い画素 (モチーフの d)
        for j, row in enumerate(icon):
            for i, ch in enumerate(row[:11]):
                if ch in "Ad": mask[(sx + i, iy + j)] = 0 if ch == "A" else 1
        tx = sx + iw + 5
        for ch in name:
            for j, row in enumerate(GLYPH[ch]):
                for i, b in enumerate(row):
                    if b == "1": mask[(tx + i, ty + j)] = 0
            tx += len(GLYPH[ch][0]) + 1
        for (x, y), d in mask.items():
            base = tuple(int(v) for v in orig[y, x])
            if a.style == "engraved":                                     # 彫り込み: 面の色のまま、 暗く沈める (溝の底)。 光は上から: 溝の上の壁は影 (底と同化)・下の壁は光を受けて明るい
                k = 0.60 if d == 0 else 0.46
                if rnd(x, y, 3) < 0.10: k += 0.12                          # 埃・擦れで浅くなった所
                c = mul(base, k)
                if tint: c = mix(c, mul(tint, 0.62 if d == 0 else 0.48), a.tint_amount)       # 彫りの底に色を流す (明るさは彫りの陰影のまま)
                arr[y, x] = c
            elif a.style == "painted":                                    # 塗装: 面より少し明るい、 くすんだ塗料。 ところどころ剥げて、 下の面が見える
                if rnd(x, y, 5) < 0.20: continue                           # 剥げ
                paint = mix(base, (176, 170, 150), 0.70 if d == 0 else 0.45)
                arr[y, x] = mul(paint, 0.92 + 0.16 * rnd(x, y, 7))
            else:
                arr[y, x] = mul(main_c if d == 0 else dim_c, 1.0)
        if a.style == "engraved":                                         # 光を受ける壁: 溝のすぐ下 (強い) と右 (弱い) の 1 画素を、 面より明るく。 上と左には置かない (影になる側)
            for (x, y) in mask:
                for (dx, dy), kk in (((0, 1), 1.34), ((1, 0), 1.16)):
                    q = (x + dx, y + dy)
                    if q not in mask and tuple(arr[q[1], q[0]]) == tuple(orig[q[1], q[0]]): arr[q[1], q[0]] = mul(tuple(int(v) for v in orig[q[1], q[0]]), kk)
        if a.style == "painted":                                          # 塗装のにじみ: 塗料のまわりに、 暗い錆びの汚れをまばらに
            for (x, y) in mask:
                q = (x, y + 1)
                if q not in mask and rnd(x, y, 9) < 0.10 and tuple(arr[q[1], q[0]]) == tuple(orig[q[1], q[0]]): arr[q[1], q[0]] = mul(tuple(int(v) for v in orig[q[1], q[0]]), 0.72)
    Image.fromarray(arr, "RGB").save(a.dst)
    print("面:", fs)


if __name__ == "__main__":
    main()
