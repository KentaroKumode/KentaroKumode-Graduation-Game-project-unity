#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵風の画像 (AI 生成などで格子がずれている・ぼけている) を、きっちりしたドット絵に直す。

  1. 格子の大きさ (見かけの 1 ドットが何 px か) を、色の変わり目の並びから推定する (--cell で指定もできる)
  2. 格子ごとに、中央の色 (中央値) を取る ── 平均だと境目で濁った色が出るので使わない
  3. 色を減らす (--colors)。 パレットは画像自身から作り、 各ドットは**最も近いパレット色**に置き換える (--palette で固定も可)
  4. 背景 (四隅の色に近い連結領域) を透明にする (--keep-bg で残す)
  補正: --sat 1.6 で彩度を上げてから色を減らす (元画像の緑や茶が灰色に潰れている時。 明度は変えない)


使い方:
  python Tools/snap_pixels.py 入力.png [出力.png] [--cell 5.3] [--colors 16] [--keep-bg] [--scale 8] [--sat 1.0]
  → 出力は 1 ドット = 1 px の PNG (透明つき)。 --scale N で N 倍の確認用画像も `*_x{N}.png` に書く
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image


def estimate_cell(a):
    """色の変わり目の位置が、 どの大きさの格子の線にいちばんよく乗るかで格子の大きさを推定する。"""
    best = (-1.0, 0.0)
    for cell in np.arange(2.0, 16.0, 0.05):
        score = 0.0
        for axis in (0, 1):
            e = (np.abs(np.diff(a.astype(int), axis=axis)).sum(2) > 40).sum(1 - axis).astype(float)
            idx = np.arange(len(e)); s_best = 0.0
            for off in np.arange(0, cell, 0.25):
                ph = ((idx + 1 - off) / cell) % 1.0
                s_best = max(s_best, e[np.minimum(ph, 1 - ph) < 0.12].sum() / (e.sum() + 1e-9))
            score += s_best
        score += cell * 0.002                       # 小さい格子ほど偶然に合いやすいので、 わずかに大きい方を優先する
        if score > best[0]: best = (score, cell)
    return best[1]


def best_phase(a, cell, axis):
    e = (np.abs(np.diff(a.astype(int), axis=axis)).sum(2) > 40).sum(1 - axis).astype(float)
    idx = np.arange(len(e)); best = (-1, 0.0)
    for off in np.arange(0, cell, 0.1):
        ph = ((idx + 1 - off) / cell) % 1.0
        s = e[np.minimum(ph, 1 - ph) < 0.12].sum()
        if s > best[0]: best = (s, off)
    return best[1]


def snap(a, cell):
    """格子ごとの中央値の色で、 1 ドット = 1 px の配列にする。"""
    h, w = a.shape[:2]
    ox, oy = best_phase(a, cell, 1), best_phase(a, cell, 0)
    nx, ny = int((w - ox) / cell), int((h - oy) / cell)
    out = np.zeros((ny, nx, 3), np.uint8)
    for j in range(ny):
        y0, y1 = int(round(oy + j * cell)), int(round(oy + (j + 1) * cell))
        for i in range(nx):
            x0, x1 = int(round(ox + i * cell)), int(round(ox + (i + 1) * cell))
            # 格子の縁 (隣の色が混ざる) を避けて、 内側の 60% の中央値を取る
            mx, my = max(0, (x1 - x0) // 5), max(0, (y1 - y0) // 5)
            blk = a[y0 + my:max(y0 + my + 1, y1 - my), x0 + mx:max(x0 + mx + 1, x1 - mx)].reshape(-1, 3)
            out[j, i] = np.median(blk, axis=0).astype(np.uint8)
    return out


def to_lab(rgb):
    """RGB (0-255) → CIE Lab。 色の差を人の見え方に近い距離で測る (RGB の重み付けでは暗いオリーブと灰色が同じに見えた)。"""
    c = np.asarray(rgb, np.float32) / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]], np.float32)
    xyz = c @ m.T / np.array([0.9505, 1.0, 1.089], np.float32)
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def quantize(dots, n, palette=None):
    """色を n 色に減らす。 距離は Lab で測る。 初期のパレットは「すでに選んだ色から一番遠い色」を順に取るので、
    面積の小さい色相 (緑・赤) が、 面積の大きい灰色に食われない。 各ドットは最も近いパレット色になる。"""
    flat = dots.reshape(-1, 3)
    uniq, cnt = np.unique(flat, axis=0, return_counts=True)
    lab = to_lab(uniq)
    if palette is None:
        if len(uniq) <= n:
            palette = uniq
        else:
            wt = np.sqrt(cnt).astype(np.float32)
            idx = [int(wt.argmax())]
            for _ in range(n - 1):
                d = np.min(((lab[:, None, :] - lab[idx][None]) ** 2).sum(2), axis=1)
                idx.append(int((d * wt).argmax()))
            cent = lab[idx].copy()
            for _ in range(30):
                lb = ((lab[:, None, :] - cent[None]) ** 2).sum(2).argmin(1)
                for k in range(n):
                    m = lb == k
                    if m.any(): cent[k] = np.average(lab[m], axis=0, weights=wt[m])
            # 各クラスタの代表は「実在する色」のうち中心に最も近いもの (Lab → RGB の逆変換で色が濁るのを避ける)
            lb = ((lab[:, None, :] - cent[None]) ** 2).sum(2).argmin(1)
            palette = np.array([uniq[np.where(lb == k)[0][(((lab[lb == k] - cent[k]) ** 2).sum(1)).argmin()]]
                                for k in range(n) if (lb == k).any()])
    pl = to_lab(palette)
    d = ((to_lab(flat)[:, None, :] - pl[None]) ** 2).sum(2)
    return palette[d.argmin(1)].reshape(dots.shape).astype(np.uint8), palette


def remove_bg(dots, tol=24):
    """外周からつながっていて、 外周の色に近い領域を透明にする。 背景が単色でなく (放射状のグラデーションなど)
    場所で色が違っても、 隣り合うドットどうしの色が近ければ背景としてたどる (広がりは外周から 1 ドットずつ)。
    輪郭の黒い縁にぶつかると止まるので、 絵の中までは入らない。"""
    h, w = dots.shape[:2]
    d = dots.astype(int)
    seen = np.zeros((h, w), bool)
    border = [(0, x) for x in range(w)] + [(h - 1, x) for x in range(w)] + [(y, 0) for y in range(h)] + [(y, w - 1) for y in range(h)]
    stack = list(border); ref = {}
    for p in border: ref[p] = d[p]
    while stack:
        y, x = stack.pop()
        if seen[y, x]: continue
        seen[y, x] = True
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx]:
                # 隣との色の差が小さく、 暗い輪郭 (明度が低い) でなければ背景として広げる
                if np.abs(d[ny, nx] - d[y, x]).sum() <= tol and d[ny, nx].sum() > 150:
                    stack.append((ny, nx))
    return np.dstack([dots, np.where(seen, 0, 255).astype(np.uint8)])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--cell", type=float, default=0, help="見かけの 1 ドットの px (0 = 自動)")
    ap.add_argument("--colors", type=int, default=16)
    ap.add_argument("--keep-bg", action="store_true")
    ap.add_argument("--scale", type=int, default=8)
    ap.add_argument("--bg-tol", type=int, default=18)
    ap.add_argument("--sat", type=float, default=1.0, help="彩度の倍率 (明度は変えない)")
    args = ap.parse_args()
    a = np.asarray(Image.open(args.src).convert("RGB"))
    cell = args.cell or estimate_cell(a)
    print(f"格子: {cell:.2f} px / ドット")
    dots = snap(a, cell)
    print(f"ドット数: {dots.shape[1]}×{dots.shape[0]}")
    if args.sat != 1.0:
        import colorsys
        f = dots.reshape(-1, 3) / 255.0
        f = np.array([colorsys.hls_to_rgb(h, l, min(1.0, sa * args.sat)) for h, l, sa in (colorsys.rgb_to_hls(*p) for p in f)])
        dots = (f.reshape(dots.shape) * 255).round().astype(np.uint8)
    dots, pal = quantize(dots, args.colors)
    print(f"色: {len(np.unique(dots.reshape(-1, 3), axis=0))}")
    rgba = np.dstack([dots, np.full(dots.shape[:2], 255, np.uint8)]) if args.keep_bg else remove_bg(dots, args.bg_tol)
    dst = args.dst or os.path.splitext(args.src)[0] + "_dot.png"
    im = Image.fromarray(rgba, "RGBA"); im.save(dst)
    big = os.path.splitext(dst)[0] + f"_x{args.scale}.png"
    bgc = Image.new("RGBA", (im.width * args.scale, im.height * args.scale), (58, 58, 62, 255))
    bgc.alpha_composite(im.resize((im.width * args.scale, im.height * args.scale), Image.NEAREST))
    bgc.save(big)
    print("wrote", dst, "/", big)


if __name__ == "__main__":
    main()
