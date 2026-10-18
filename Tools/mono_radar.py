#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵を、 暗い画面に細い橙赤の線が光るレーダー画面のような雰囲気にする (2026-10-10)。

  mono_monitor.py (点灯 / 消灯の 2 値) と違い、 面を塗らず、 線で描く。
    輪郭 (背景に接する画素)   : 明るい線
    面と面の境目              : 暗めの線 (輪郭の約 55%)
    いちばん明るい面          : ごく淡い塗り (約 14%)
    線のまわり 1 ドット         : 淡いにじみ (約 18%)
    画面                      : ほぼ黒の灰緑。 偶数行をわずかに暗くした走査線
  出力は 4 倍して余白付きの確認画像も出す。

使い方: python Tools/mono_radar.py 入力.png [出力フォルダ] [--scale 10]
"""
import argparse
import os

import numpy as np
from PIL import Image

LIT = np.array([222, 96, 42], float)
DARK = np.array([24, 26, 24], float)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("out", nargs="?", default=os.path.join("docs", "design", "supply"))
    ap.add_argument("--scale", type=int, default=10); ap.add_argument("--name", default="stone_radar")
    ap.add_argument("--seam", type=float, default=95, help="面の境目を線にする明るさの差")
    ap.add_argument("--color", default=None, help="点灯色 r,g,b (既定は橙赤)")
    ap.add_argument("--transparent", action="store_true", help="背景を抜く (明るさを不透明度にした RGBA)。 走査線は付けない")
    ap.add_argument("--scan", type=float, default=0.10, help="走査線の暗さ (0 で無し)")
    a = ap.parse_args()
    im = np.asarray(Image.open(a.src).convert("RGB")).astype(int)
    h, w = im.shape[:2]
    tone = (0.299 * im[..., 0] + 0.587 * im[..., 1] + 0.114 * im[..., 2])
    sil = tone > 0
    key = im[..., 0] * 65536 + im[..., 1] * 256 + im[..., 2]
    pad = np.pad(sil, 1)
    edge = sil & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    # 面の境目: 右か下の隣が別の色 (どちらも輪郭の内側)
    seam = np.zeros((h, w), bool)
    big = a.seam                                                        # 明るさの差がこれ以上の境目だけ線にする (細かい階調の境目は無視)
    seam[:, :-1] |= sil[:, :-1] & sil[:, 1:] & (abs(tone[:, :-1] - tone[:, 1:]) >= big)
    seam[:-1, :] |= sil[:-1, :] & sil[1:, :] & (abs(tone[:-1, :] - tone[1:, :]) >= big)
    seam &= ~edge
    top = tone >= tone[sil].max() * 0.95
    level = np.zeros((h, w))
    level[top & ~edge] = 0.14
    level[seam] = np.maximum(level[seam], 0.5)
    level[edge] = 1.0
    line = (level >= 0.5)
    lp = np.pad(line, 1)
    near = (lp[:-2, 1:-1] | lp[2:, 1:-1] | lp[1:-1, :-2] | lp[1:-1, 2:]) & ~line
    level[near] = np.maximum(level[near], 0.18)
    if a.color:
        global LIT
        LIT = np.array([float(v) for v in a.color.split(",")])
    if a.transparent:
        rgba = np.zeros((h, w, 4), np.uint8)
        rgba[..., :3] = LIT.astype(np.uint8)
        rgba[..., 3] = np.round(level * 255).astype(np.uint8)
        img = Image.fromarray(rgba, "RGBA")
        os.makedirs(a.out, exist_ok=True)
        img.save(os.path.join(a.out, a.name + ".png"))
        k = a.scale
        bg = Image.new("RGBA", (w * k, h * k), (12, 16, 26, 255)); bg.alpha_composite(img.resize((w * k, h * k), Image.NEAREST))
        bg.save(os.path.join(a.out, f"{a.name}_x{k}.png"))
        print("wrote", os.path.join(a.out, a.name + ".png"))
        return
    base = np.tile(DARK, (h, w, 1))
    base[::2] *= (1 - a.scan)                                           # 走査線
    out = base + (LIT - base) * level[..., None]
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")
    os.makedirs(a.out, exist_ok=True)
    img.save(os.path.join(a.out, a.name + ".png"))
    k = a.scale
    img.resize((w * k, h * k), Image.NEAREST).save(os.path.join(a.out, f"{a.name}_x{k}.png"))
    print("wrote", os.path.join(a.out, a.name + ".png"))


if __name__ == "__main__":
    main()
