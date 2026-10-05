#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""バーを、周りの枠 (窓) になじませる (2026-10-05)。 バー側だけを加工する。

入力: 枠 (窓が黒く抜けたもの) とバー (窓と同じ大きさ) の 2 枚。 窓の位置は枠の黒い領域から自動で測る。
  1. 色を枠の青灰へ寄せる: 彩度を落とし、 影の側を青へ混ぜる (明るい側の暖色は残して「光る中身」にする)
  2. 窓の上端に枠が落とす影 (上 2 ドット) と、 下端のガラスの縁の照り返し (下 1 ドット) を、 枠の色から取って重ねる
  3. 窓の左右の端を、 枠側の縁の暗さへ落とす (端の 2 ドットを暗く) ── 平らな絵が窓の奥にあるように見せる
  4. 使い込み (--wear): ガラスの引っかき傷・端に溜まる汚れ・左上からの照り返しの筋・埃の点を重ねる (液晶に見えないように)。
     傷の位置は固定 (乱数の種を決めてある。 バーの中身が変わっても同じ場所に出る)

使い方: python Tools/bar_blend.py 枠.png バー.png [出力.png] [--tint 0.45] [--sat 0.7]
  → 出力は枠にバーを重ねた確認用 (窓の位置のまま)。 --bar-only でバーだけの PNG を出す。
"""
import argparse
import colorsys
import os

import numpy as np
from PIL import Image


def find_window(frame):
    """枠 (RGBA) の黒い穴 (中の窓) の外接矩形。 外周の透明・黒を除いて、 枠に囲まれた黒だけを取る。"""
    a = np.asarray(frame.convert("RGB")).astype(int)
    black = a.sum(2) == 0
    h, w = black.shape
    seen = np.zeros_like(black)
    stack = [(0, 0), (0, w - 1), (h - 1, 0), (h - 1, w - 1)]
    while stack:                                  # 外から届く黒 (外周の背景) を除く
        y, x = stack.pop()
        if not (0 <= y < h and 0 <= x < w) or seen[y, x] or not black[y, x]: continue
        seen[y, x] = True
        stack += [(y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)]
    hole = black & ~seen
    ys, xs = np.where(hole)
    return xs.min(), ys.min(), xs.max(), ys.max()


def wear(out, strength, shift=0, scratch=True):
    """使い込みの層。 out は (h, w, 4)。 k = 強さ (0 で無し)。 位置は固定の乱数で決める。"""
    if strength <= 0: return out
    h, w = out.shape[:2]
    rng = np.random.default_rng(20261005)
    def mix(y, x, col, a):
        if 0 <= y < h and 0 <= x < w and out[y, x, 3] > 0:
            out[y, x, :3] = out[y, x, :3] * (1 - a * strength) + np.array(col, float) * a * strength
    # 端の汚れ: 点ではなく面 ── 端から内側へ、 暗く濁った色が薄れていく (右端のほうが濃い・縦にむらがある)
    for x in range(w):
        for side, dist in ((0, x), (1, w - 1 - x)):
            reach = 30 if side == 0 else 46
            if dist >= reach: continue
            base = (1 - dist / reach) ** 1.3 * (0.65 if side == 0 else 0.85)
            for y in range(h):
                mix(y, x if side == 0 else w - 1 - dist, (16, 12, 10), base * (0.75 + 0.25 * np.sin(y * 1.7 + x * 0.3)))
    # フチの汚れ (2026-10-05 追加・「もっとガッツリ」): 上下の縁に沿って暗い汚れが溜まり、 ところどころ内側へ垂れる。
    # 縁ほど濃く、 横方向にむらがある。 縁際はほぼ黒、 中へ向かって濁った茶になる
    for x in range(w):
        n1 = 0.5 + 0.5 * np.sin(x * 0.21) * np.sin(x * 0.047 + 1.3)         # ゆるいむら
        n2 = 0.5 + 0.5 * np.sin(x * 1.9 + 0.7)                             # 細かいむら
        top = 1.8 + 1.8 * n1 + 0.6 * n2                                      # 上の縁から垂れる深さ (中心 2 行は残す)
        bot = 1.6 + 1.5 * (1 - n1) + 0.6 * n2
        for y in range(h):
            dt, db = y / max(top, 0.1), (h - 1 - y) / max(bot, 0.1)
            if dt < 1: mix(y, x, (8, 6, 4), (1 - dt) ** 0.7 * 0.92)
            if db < 1: mix(y, x, (9, 7, 4), (1 - db) ** 0.7 * 0.95)
    # 垂れ筋: 縁から中へ細く 3〜6 ドット垂れる汚れ (上から 14 本・下から 10 本・位置固定)
    for j in range(22):
        x = (6 + int(j * 11.9 + 5 * np.sin(j * 2.3)) + shift) % w; ln = 3 + (j * 7) % 4
        for i in range(ln): mix(i, x, (14, 9, 5), 0.85 - i * 0.12)
    for j in range(16):
        x = (14 + int(j * 16.3 + 5 * np.sin(j * 1.7)) + shift) % w; ln = 2 + (j * 5) % 3
        for i in range(ln): mix(h - 1 - i, x, (14, 9, 5), 0.85 - i * 0.13)
    # 錆びた染み: 縁の近くにある赤茶の濁り (大きさ 3〜9 ドットの塊)
    for x0, y0, r in [((a + shift) % w, b, c) for a, b, c in ((30, 0, 6), (88, 7, 5), (131, 0, 8), (176, 7, 6), (233, 0, 7), (255, 7, 4))]:
        for y in range(h):
            for x in range(x0 - r, x0 + r + 1):
                d = np.hypot((x - x0) / r, (y - y0) / max(2.0, r * 0.35))
                if d < 1: mix(y, x, (110, 48, 20), (1 - d) * 0.62)
    # こびりつき: 縁際のまばらな暗い塊 (1〜2 ドット)
    for _ in range(90):
        x = int(rng.integers(0, w)); y = int(rng.choice([0, 1, h - 2, h - 1]))
        mix(y, x, (6, 5, 5), 0.85)
        if rng.random() < 0.5: mix(y, x + 1, (6, 5, 5), 0.6)
    # 引っかき傷: 数本だけ、 長く斜めに通す。 暗い溝 (汚れと同じ系統) と、 そのすぐ上のごく弱い縁の光 (2026-10-05: 明るい青灰の筋は浮いたので暗色に)
    for x, y, ln, dy in ([((a + shift) % (w - 72), b, c, d) for a, b, c, d in ((14, 6, 58, -1), (96, 1, 40, 1), (150, 7, 70, -1), (212, 2, 46, 1))] if scratch else []):
        yf = float(y)
        for i in range(ln):
            yf += dy * 0.2 + (rng.random() - 0.5) * 0.45             # 傾きにゆらぎ (階段状にならないよう小数で進める)
            yy, xx = int(round(yf)), x + i
            fade = 1.0 if 4 <= i < ln - 6 else 0.5                    # 始まりと終わりは薄れる
            if rng.random() < 0.88:                                   # ところどころ途切れる
                mix(yy, xx, (5, 4, 4), 0.85 * fade)                   # 傷の溝 (暗い・汚れと同じ系統)
                mix(yy - 1, xx, (120, 96, 70), 0.14 * fade)           # 溝の上の縁がわずかに光を拾う (暖色・控えめ)
    # 照り返し: 左上から右下へ走る薄い光の帯 (幅 3・斜め)。 傷とは別の層
    for x0 in (int(w * 0.20), int(w * 0.62)):
        for y in range(h):
            for dx in range(4):
                mix(y, x0 + y * 2 + dx, (210, 230, 245), 0.16 if dx in (1, 2) else 0.07)
    # 欠け: 角の小さな欠け 2 か所 (上端と下端に 1 ドットの暗い切り欠き)
    for x, y in ((57, 0), (58, 0), (203, h - 1), (204, h - 1), (204, h - 2)):
        mix(y, x, (8, 8, 10), 0.85)
    return out


def blend(frame, bar, tint=0.15, sat=0.92, wear_k=1.0, shift=0, scratch=True, flat=0.0, floor=0):
    x0, y0, x1, y1 = find_window(frame)
    f = np.asarray(frame.convert("RGB")).astype(float)
    shadow = f[y0 - 1, (x0 + x1) // 2]            # 窓の真上の枠の色 (影の側)
    glass = f[y1 + 1, (x0 + x1) // 2]             # 窓の真下の枠の色 (縁の照り返し)
    wall = f[y0 + 1, x0 - 2]                       # 窓の左の枠の色
    out = np.asarray(bar.convert("RGBA")).astype(float).copy()
    h, w = out.shape[:2]
    cool = shadow / 255.0                           # 青灰 (枠の影の色)
    for y in range(h):
        for x in range(w):
            r, g, b, al = out[y, x]
            if al == 0: continue
            hh, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
            s *= sat                                # 彩度を落とす
            nr, ng, nb = colorsys.hls_to_rgb(hh, l, s)
            k = tint * (1.0 - l) ** 1.2            # 暗い所ほど青灰へ寄せる (明るい所の暖色は残る)
            out[y, x, :3] = [(nr * (1 - k) + cool[0] * k * (0.5 + l)) * 255, (ng * (1 - k) + cool[1] * k * (0.5 + l)) * 255,
                             (nb * (1 - k) + cool[2] * k * (0.5 + l)) * 255]
    # 窓の上端: 枠の影 (上 2 ドットを枠の影色へ 55% / 25% 混ぜる)
    for dy, k in ((0, 0.55), (1, 0.25)):
        out[dy, :, :3] = out[dy, :, :3] * (1 - k) + shadow * k
    # 窓の下端: ガラスの縁の照り返し (下 1 ドットを枠の明るい色へ 35% 混ぜる)
    out[h - 1, :, :3] = out[h - 1, :, :3] * (1 - 0.35) + glass * 0.35
    # 左右の端: 窓の奥へ落ちる暗さ (端の 2 ドット)
    for dx, k in ((0, 0.45), (1, 0.2)):
        for xx in (dx, w - 1 - dx):
            out[:, xx, :3] = out[:, xx, :3] * (1 - k) + wall * 0.35 * k
    out = wear(out, wear_k, shift, scratch)
    # 窓の内壁: バーの外周 1 ドットを、 枠の内側の青灰を暗くした色で囲む (バーが窓に「はまる」。 黒い隙間の中に浮かない)
    inner = shadow * 0.55
    out[0, :, :3] = inner * 0.9; out[h - 1, :, :3] = inner * 1.15
    out[:, 0, :3] = inner; out[:, w - 1, :3] = inner * 1.05
    if flat > 0:                                   # 色の平均化: 全体の平均色へ寄せる (コントラストを下げる)
        m = out[out[:, :, 3] > 0][:, :3].mean(0)
        out[:, :, :3] = m + (out[:, :, :3] - m) * (1 - flat)
    if floor > 0:                                  # 暗さの下限: 真っ黒を作らない (青みを少し残す)
        out[:, :, :3] = np.maximum(out[:, :, :3], np.array([floor, floor * 1.15, floor * 1.25]))
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA"), (x0, y0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("frame"); ap.add_argument("bar"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--tint", type=float, default=0.15); ap.add_argument("--sat", type=float, default=0.92)
    ap.add_argument("--bar-only", action="store_true")
    ap.add_argument("--wear", type=float, default=1.0, help="使い込みの強さ (0 で無し)")
    ap.add_argument("--no-scratch", action="store_true", help="傷を入れない")
    ap.add_argument("--flat", type=float, default=0.0, help="色を平均色へ寄せる割合 (0〜1)")
    ap.add_argument("--floor", type=float, default=0, help="暗さの下限 (R の値・G と B は少し高く)")
    ap.add_argument("--shift", type=int, default=0, help="傷・垂れ筋・染みの位置を横へずらす (バーと同じ位置にならないように・裏地は 97 など)")
    args = ap.parse_args()
    frame = Image.open(args.frame).convert("RGBA")
    bar = Image.open(args.bar).convert("RGBA")
    x0, y0, x1, y1 = find_window(frame)
    # バーが窓より大きければ窓の位置の切り出し、 同じ画像の座標系 (バー側が全画面) ならその位置を切り出す
    if bar.size == frame.size: bar = bar.crop((x0, y0, x1 + 1, y1 + 1))
    out, pos = blend(frame, bar, args.tint, args.sat, args.wear, args.shift, not args.no_scratch, args.flat, args.floor)
    dst = args.dst or os.path.splitext(args.bar)[0] + "_blend.png"
    if args.bar_only:
        out.save(dst)
    else:
        comp = frame.copy(); comp.alpha_composite(out, (x0, y0)); comp.save(dst)
    print("窓:", (x0, y0, x1, y1), "->", dst)


if __name__ == "__main__":
    main()
