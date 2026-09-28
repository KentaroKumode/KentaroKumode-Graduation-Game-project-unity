#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""筐体面に「光の層」を重ねた見本を作る (2026-09-27)。 **ゲームの絵ではなく参考画像**。

参照資料の見た目は半分がドット絵、 半分が実行時の光の処理でできている:
  ・画面の光が周りの枠へ回り込む (スピル)
  ・明るいものの周りの柔らかいにじみ (ブルーム)
  ・ランプの光だまり、 暖色への色調補正、 粒状感
これらは描いて作るものではないので、 下絵 (cabinet-face.png) には入れていない。
この台本は「実行時にこれを掛けたらこう見える」を確かめるためだけのもの。

**注意: 光の層を掛けるとピクセルパーフェクトは画面上で崩れる** (にじみは格子に乗らない)。
下のドットは格子どおりのまま、 その上に柔らかい光が乗る ── 参照資料もこの構造。
また CLAUDE.md の規約で Post Processing パッケージは使っていないので、
実際に入れるなら自前の軽いカメラエフェクトになる (採否は未決)。

画面の中身は仮の橙のグラデーション (参照資料の夕焼けの場面を模したもの)。

使い方: python Tools/cabinet_light.py   → docs/design/cabinet-face-lit.jpg (960×540)
"""
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "docs", "design", "cabinet-face.png")
OUT = os.path.join(ROOT, "docs", "design", "cabinet-face-lit.jpg")
UP = 2                                   # 960×540 で光を計算する (下のドットは最近傍で拡大)

LCD = (124, 25, 231, 139)                # ゾーン 7 (格子座標)


def blur(a, r):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r)), dtype=np.float32) / 255.0


def main():
    src = Image.open(SRC).convert("RGBA")
    table = Image.new("RGBA", src.size, (24, 18, 13, 255))     # 卓 (仮の暗い木の色)
    table.alpha_composite(src)
    a = np.asarray(table.convert("RGB"), dtype=np.float32) / 255.0
    H, W = a.shape[:2]
    Y, X = np.mgrid[0:H, 0:W].astype(np.float32)

    # 画面の中身 (仮): 橙の光源が中央やや上。 参照資料と同じく単色で強いコントラスト
    x0, y0, w, h = LCD
    scr = (X >= x0) & (X < x0 + w) & (Y >= y0) & (Y < y0 + h)
    r = np.sqrt(((X - (x0 + w * 0.52)) / (w * 0.55)) ** 2 + ((Y - (y0 + h * 0.42)) / (h * 0.62)) ** 2)
    g = np.clip(1.08 - r, 0, 1) ** 1.3
    content = np.stack([0.20 + 0.78 * g, 0.07 + 0.46 * g, 0.03 + 0.14 * g], -1)
    content = np.floor(content * 10) / 10                    # 段を付けてドット絵らしく
    a[scr] = content[scr]

    up = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((W * UP, H * UP), Image.NEAREST),
                    dtype=np.float32) / 255.0
    Hu, Wu = up.shape[:2]
    Yu, Xu = np.mgrid[0:Hu, 0:Wu].astype(np.float32)
    scr_u = np.asarray(Image.fromarray(scr.astype(np.uint8) * 255).resize((Wu, Hu), Image.NEAREST)) > 0

    # ① 画面の光の回り込み: 画面の発光を大きくぼかし、 画面の外にだけ足す
    em = np.where(scr_u[..., None], up, 0)
    spill = blur(em, 34) * 1.35 + blur(em, 10) * 0.45
    out = up + np.where(scr_u[..., None], 0, spill)

    # ② ブルーム: 明るい画素だけを取り出してぼかし、 足し戻す
    lum = up.max(-1)
    bright = (np.clip((lum - 0.58) / 0.42, 0, 1)[..., None]) * up
    out += blur(bright, 5) * 0.55 + blur(bright, 18) * 0.65

    # ③ ランプの光 (上から・暖色): 参照の Light2D に相当。 土台は冷たいオリーブなので、
    #    暖色はここで乗算して入れる。 中心は明るく、 周りへ落ちる。
    d2 = ((Xu - Wu * 0.49) / (Wu * 0.60)) ** 2 + ((Yu - Hu * 0.40) / (Hu * 0.80)) ** 2
    pool = np.clip(1.34 - 0.95 * d2, 0.26, 1.30)
    warm = np.array([1.00, 0.84, 0.58], np.float32)            # 白熱灯の色
    light = pool[..., None] * (warm * 0.85 + 0.15)
    out *= light
    # ④ 粒状感 (参照は 1920×1080 の粒状感+周辺減光のテクスチャを重ねている)
    rng = np.random.default_rng(1)
    out += (rng.random((Hu, Wu, 1)).astype(np.float32) - 0.5) * 0.03

    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(OUT, quality=90)
    print(f"{os.path.relpath(OUT, ROOT)}  {Wu}×{Hu}  (参考画像・ゲームの絵ではない)")


if __name__ == "__main__":
    main()
