#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""筐体パネルの質感テスト (2026-09-27)。 **ドット絵ではなく連続階調の試作**。

目的は 1 つ: 「Highfleet のような実在感を、 手続き的にどこまで出せるか」の上限を見る。
寸法・配置は本番と無関係の見本で、 判断材料にするためだけのもの。

手法: 高さマップを作って勾配から陰影を出す (法線ライティングの簡易版)。
  ① 研磨目の金属地 → ② 塗装 → ③ 縁の剥げ → ④ ネジ・パネル継ぎ目 → ⑤ 計器 → ⑥ 汚れ
"""
import numpy as np
from PIL import Image, ImageFilter, ImageDraw

W, H = 640, 360
rng = np.random.default_rng(7)


def blur(a, r):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), "L")
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r)), dtype=np.float32) / 255.0


def noise(scale, octaves=4):
    """値ノイズ: 粗い乱数をぼかして重ねる。"""
    out = np.zeros((H, W), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        n = rng.random((H, W)).astype(np.float32)
        out += blur(n, scale / (2 ** o)) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def shade(height, light=(-0.55, -0.7, 0.45)):
    """高さマップの勾配から陰影。 光源は左上・やや手前。"""
    gy, gx = np.gradient(height.astype(np.float32))
    nz = 1.0 / np.sqrt(gx * gx + gy * gy + 1.0)
    nx, ny = -gx * nz, -gy * nz
    lx, ly, lz = light
    l = np.sqrt(lx * lx + ly * ly + lz * lz)
    d = (nx * lx + ny * ly + nz * lz) / l
    return np.clip(d, -1, 1)


# ── ① 研磨目の金属地 ─────────────────────────────────────
metal = noise(2.0, 3)
streak = rng.random((H, W)).astype(np.float32)
streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8), "L")
                    .filter(ImageFilter.GaussianBlur(0.6)), np.float32) / 255.0
# 横方向に伸ばして研磨目にする
k = 24
pad = np.pad(streak, ((0, 0), (k, k)), mode="reflect")
streak = np.stack([pad[:, i:i + W] for i in range(2 * k)]).mean(0)
metal = 0.55 + 0.25 * metal + 1.10 * (streak - 0.5)

# ── ② 塗装 (くすんだ緑灰。 塗膜のムラを乗せる) ───────────────
paint_rgb = np.array([0.36, 0.38, 0.33], np.float32)
mottle = noise(9.0, 4)

# ── ③ 高さマップ: 継ぎ目・ネジ・皿の窪み ────────────────────
height = np.zeros((H, W), np.float32)
Y, X = np.mgrid[0:H, 0:W].astype(np.float32)

# パネル継ぎ目 (縦 2 本・横 1 本)
for x in (206, 430):
    height += -0.9 * np.exp(-((X - x) ** 2) / 4.0)
height += -0.9 * np.exp(-((Y - 250) ** 2) / 4.0)
# 計器を落とし込む窪み
gcx, gcy, gr = 120, 130, 84
rad = np.sqrt((X - gcx) ** 2 + (Y - gcy) ** 2)
height += -1.6 / (1 + np.exp(np.clip((gr - rad) * 1.2, -60, 60)))
# ネジ
SCREWS = [(36, 36), (604, 36), (36, 324), (604, 324), (206, 40), (206, 250), (430, 40), (430, 250)]
for (sx, sy) in SCREWS:
    r = np.sqrt((X - sx) ** 2 + (Y - sy) ** 2)
    height += 1.1 * np.exp(-(r ** 2) / 40.0)          # 頭の盛り上がり
    height += -1.6 * np.exp(-(r ** 2) / 6.0)          # 皿の窪み
height += 0.8 * (noise(3.0, 3) - 0.5)                # 板の微細な起伏

sh = shade(blur(height * 0.25 + 0.5, 0.7) * 8 - 4)   # 振幅を潰さない

# ── ④ 塗装の剥げ: 縁と継ぎ目まわりで下地の金属が出る ──────────
edge = np.minimum.reduce([X, Y, W - 1 - X, H - 1 - Y]) / 26.0
wear = np.clip(1.2 - edge, 0, 1) * (noise(4.0, 4) ** 1.4) * 2.4
for (sx, sy) in SCREWS:                                # ネジ周りも擦れる
    r = np.sqrt((X - sx) ** 2 + (Y - sy) ** 2)
    wear += 0.9 * np.exp(-(r ** 2) / 300.0) * noise(3.0, 2)
wear = np.clip(wear * (0.5 + mottle) * 1.8, 0, 1)

base = paint_rgb[None, None, :] * (0.75 + 0.5 * mottle)[..., None]
metal_rgb = np.stack([metal * 0.82, metal * 0.80, metal * 0.74], -1)
rgb = base * (1 - wear[..., None]) + metal_rgb * wear[..., None]

# 陰影を乗せる
rgb *= (0.42 + 1.05 * (sh * 0.5 + 0.5))[..., None]
# 上からの環境光 (上が明るい)
rgb *= (1.06 - 0.22 * (Y / H))[..., None]

# ── ⑤ 計器 (ガラス・目盛り・針) ──────────────────────────
face = (rad < gr - 8).astype(np.float32)
face_s = blur(face, 1.2)
dial = np.array([0.07, 0.075, 0.08], np.float32)
rgb = rgb * (1 - face_s[..., None]) + dial[None, None, :] * face_s[..., None]
# 目盛り
img = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")
dr = ImageDraw.Draw(img)
import math
for i in range(41):
    a = math.radians(-210 + i * 240 / 40)
    r0 = gr - 14 if i % 5 == 0 else gr - 20
    dr.line([(gcx + math.cos(a) * r0, gcy + math.sin(a) * r0),
             (gcx + math.cos(a) * (gr - 24), gcy + math.sin(a) * (gr - 24))],
            fill=(214, 210, 196) if i % 5 == 0 else (150, 147, 138), width=2 if i % 5 == 0 else 1)
# 赤帯 (危険域)
dr.arc([gcx - gr + 12, gcy - gr + 12, gcx + gr - 12, gcy + gr - 12], -30, 30, fill=(168, 52, 40), width=5)
# 針
na = math.radians(-210 + 240 * 0.62)
dr.line([(gcx - math.cos(na) * 14, gcy - math.sin(na) * 14),
         (gcx + math.cos(na) * (gr - 30), gcy + math.sin(na) * (gr - 30))], fill=(228, 96, 62), width=3)
dr.ellipse([gcx - 7, gcy - 7, gcx + 7, gcy + 7], fill=(40, 38, 36), outline=(96, 92, 86))
# ラベル (テープ)
dr.rectangle([250, 300, 392, 330], fill=(206, 190, 150))
dr.rectangle([250, 300, 392, 330], outline=(170, 154, 116))
for i in range(6):
    dr.line([(262 + i * 22, 312), (262 + i * 22 + 14, 312)], fill=(58, 52, 44), width=3)
    dr.line([(262 + i * 22, 320), (262 + i * 22 + 9, 320)], fill=(58, 52, 44), width=2)
rgb = np.asarray(img, np.float32) / 255.0

# ガラスの反射 (左上から斜めの帯)
glassband = np.clip(1 - np.abs((X - gcx) * 0.7 + (Y - gcy) * 0.9 + 46) / 40.0, 0, 1)
rgb += (glassband * face_s * 0.16)[..., None]
# ガラスの縁の落ち影
ring = np.exp(-((rad - (gr - 6)) ** 2) / 26.0) * (rad < gr)
rgb -= (ring * 0.35)[..., None]

# ── ⑥ 汚れ・擦り傷・周辺減光 ─────────────────────────────
grime = noise(16.0, 5)
rgb *= (0.86 + 0.24 * grime)[..., None]
scratch = (noise(1.2, 2) > 0.72).astype(np.float32) * (noise(30.0, 3) > 0.52)
rgb += (blur(scratch, 0.4) * 0.10)[..., None]
vig = 1 - 0.30 * ((X - W / 2) ** 2 / (W / 2) ** 2 + (Y - H / 2) ** 2 / (H / 2) ** 2)
rgb *= vig[..., None]
rgb += (rng.random((H, W, 3)).astype(np.float32) - 0.5) * 0.015     # フィルムグレイン

out = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")

if __name__ == "__main__":
    import sys
    p = sys.argv[1] if len(sys.argv) > 1 else "panel_test.png"
    out.save(p)
    print(f"{p}  {W}×{H}")
