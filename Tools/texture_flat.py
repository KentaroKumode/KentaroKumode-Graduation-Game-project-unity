#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵の大きなベタ塗り (同じ色が広く続く所) に、 周囲になじむ質感を足す (2026-10-11)。

  ベタ塗りの領域を自動で見つけ、 その色の近くにあるパレットの色 (新しい色は作らない) の段 (暗い ↔ 明るい) を使って、 次を重ねる。
    ・縦のグラデーション: 上の縁は 1 段明るく、 下の縁は 1 段暗く (金属の面の丸み・光の当たり方)
    ・むら: ゆるやかな塗りむら (大きさの違う斑。 1 段だけ明暗が動く)
    ・粒: まばらに 1 段だけ明暗が違う画素 (ざらつき)
    ・縁のよごれ: 領域の上下の縁の近くに、 画像の中で使われている錆びの色の短い線を、 ごくまばらに
    ・周囲への寄せ (--blend-surround F): 領域の色を、 領域の外に接する画素 (縁取りの黒・端の部品は除く) の平均色へ F の割合だけ寄せる。 まわりと色相・彩度が離れた「浮いた」ベタ塗りを、 まわりの色に近づける
    ・共通の色への寄せ (--pull-to R,G,B と --pull F): 全部のベタ塗りを、 指定した色 (たとえば、 ほかのバーのベタ塗りの色) へ F の割合だけ寄せる。 同じ素材に見えるようにしたいとき
  領域は --min-area 画素以上の、 同じ色の連結成分。 乱数は --seed で固定 (実行のたびに変わらない)。

使い方: python Tools/texture_flat.py 入力.png 出力.png [--min-area 800] [--seed 7] [--amount 1.0] [--ramp-tol 16]
"""
import argparse
from collections import deque

import numpy as np
from PIL import Image


def lab(rgb):
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    m = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = c @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def components(img, min_area):
    h, w = img.shape[:2]; seen = np.zeros((h, w), bool); out = []
    for y in range(h):
        for x in range(w):
            if seen[y, x]: continue
            col = tuple(img[y, x]); q = deque([(x, y)]); seen[y, x] = True; pts = []
            while q:
                cx, cy = q.popleft(); pts.append((cx, cy))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = cx + dx, cy + dy
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny, nx] and tuple(img[ny, nx]) == col: seen[ny, nx] = True; q.append((nx, ny))
            if len(pts) >= min_area: out.append((col, pts))
    return out


def smooth_noise(w, h, cell_x, cell_y, rng):
    gw, gh = w // cell_x + 2, h // cell_y + 2; g = rng.random((gh, gw)); out = np.zeros((h, w))
    for y in range(h):
        for x in range(w):
            fx, fy = x / cell_x, y / cell_y; ix, iy = int(fx), int(fy); tx, ty = fx - ix, fy - iy
            tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
            out[y, x] = (g[iy, ix] * (1 - tx) + g[iy, ix + 1] * tx) * (1 - ty) + (g[iy + 1, ix] * (1 - tx) + g[iy + 1, ix + 1] * tx) * ty
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst")
    ap.add_argument("--min-area", type=int, default=800); ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--amount", type=float, default=1.0, help="質感の強さ (0 で何もしない)")
    ap.add_argument("--pull-to", default=None, help="全領域を寄せる色 R,G,B"); ap.add_argument("--pull", type=float, default=0.0)
    ap.add_argument("--blend-surround", type=float, default=0.0, help="領域の色を、 周囲の色へ寄せる割合 (0〜1)")
    ap.add_argument("--ramp-tol", type=float, default=11.0, help="段にする色の、 領域の色からの Lab 距離の上限")
    a = ap.parse_args()
    im = Image.open(a.src).convert("RGB"); img = np.asarray(im).copy(); h, w = img.shape[:2]
    pal = np.unique(img.reshape(-1, 3), axis=0); pal = pal[(0.299 * pal[:, 0] + 0.587 * pal[:, 1] + 0.114 * pal[:, 2]) > 10]; pl = lab(pal)
    rng = np.random.default_rng(a.seed); regions = components(img, a.min_area)
    # 錆びの色: 画像の中で、 暖色で彩度が高めの (b* が大きい) 暗めの色
    rust = [tuple(p) for p, L in zip(pal, pl) if L[2] > 14 and 22 < L[0] < 48]          # 暗めの暖色 (明るい橙の強調色は除く)
    for col, pts in regions:
        if 0.299 * col[0] + 0.587 * col[1] + 0.114 * col[2] <= 10: continue                     # 黒の背景は触らない
        lum = lambda t: 0.299 * t[0] + 0.587 * t[1] + 0.114 * t[2]
        if a.pull_to and a.pull > 0:
            tgt = [float(v) for v in a.pull_to.split(",")]; col2 = tuple(int(round(v + (t - v) * a.pull)) for v, t in zip(col, tgt))
            for (x, y) in pts: img[y, x] = col2
            col = col2; pal = np.unique(np.concatenate([pal, np.array([col2])]), axis=0); pl = lab(pal)
        if a.blend_surround > 0:                                                                     # 周囲の色 (縁取りの黒を除く、 領域の外に 4 近傍で接する画素の平均) へ寄せる
            inside0 = set(pts); nb = []
            for (x, y) in pts:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    q = (x + dx, y + dy)
                    if q not in inside0 and 0 <= q[0] < w and 0 <= q[1] < h and lum(img[q[1], q[0]]) > 25: nb.append(img[q[1], q[0]])
            if nb:
                m = np.mean(np.array(nb, float), axis=0); col2 = tuple(int(round(v + (mv - v) * a.blend_surround)) for v, mv in zip(col, m))
                for (x, y) in pts: img[y, x] = col2
                col = col2; pal = np.unique(np.concatenate([pal, np.array([col2])]), axis=0); pl = lab(pal)
        c = lab(np.array(col))
        # 段にする色: 同じ色みの仲間 (Lab の a・b の差が小さい) のうち、 明るさ (L) が近いもの。 3 色に満たなければ、 同じ色相のまま明暗を少しずらした色を足す (新しい色相は作らない)
        ramp = sorted({tuple(p) for p, L in zip(pal, pl) if np.hypot(L[1] - c[1], L[2] - c[2]) <= 7 and abs(L[0] - c[0]) <= a.ramp_tol} | {col}, key=lum)
        if len(ramp) < 3:
            ramp = sorted(set(ramp) | {tuple(int(min(255, max(0, v * k))) for v in col) for k in (0.9, 1.1)}, key=lum)
        base = ramp.index(col)
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys); rw, rh = x1 - x0 + 1, y1 - y0 + 1
        blot = smooth_noise(rw, rh, 9, 4, rng); blot2 = smooth_noise(rw, rh, 31, 6, rng)
        inside = set(pts)
        for (x, y) in pts:
            t = (y - y0) / max(1, rh - 1); shift = 0.0
            if t < 0.18: shift += 1.0                                                            # 上の縁: 明るく
            elif t > 0.78: shift -= 1.0                                                          # 下の縁: 暗く
            shift += (blot[y - y0, x - x0] - 0.5) * 1.5 * a.amount + (blot2[y - y0, x - x0] - 0.5) * 1.0 * a.amount      # むら
            if rng.random() < 0.035 * a.amount: shift += rng.choice([-1.0, 1.0])                 # 粒
            idx = min(len(ramp) - 1, max(0, base + (1 if shift > 0.75 else -1 if shift < -0.75 else 0)))                  # 動くのは 1 段だけ
            img[y, x] = ramp[idx]
        # 縁のよごれ: 領域の上下の縁の近くに、 錆びの短い線をまばらに
        if rust:
            for (x, y) in pts:
                edge = (x, y - 1) not in inside or (x, y + 1) not in inside
                if edge and rng.random() < 0.02 * a.amount:
                    r = rust[int(rng.integers(len(rust)))]; ln = int(rng.integers(1, 4))
                    for k in range(ln):
                        if (x + k, y) in inside: img[y, x + k] = r
    Image.fromarray(img, "RGB").save(a.dst)
    print("領域 %d 個にテクスチャを足した" % len([1 for c, p in regions if 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2] > 10]))


if __name__ == "__main__":
    main()
