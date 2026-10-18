#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ドット絵 (src) の色を、 参照の絵 (ref) のカラーパレットの雰囲気に寄せる (2026-10-11)。

  やり方 (ドット絵なので、 色の対応表を作って 1 色ずつ置き換える。 画素の形・配置は一切変えない):
    1. ref の不透明な画素から、 パレット (使われた色と枚数) を取る。 枚数が全体の --min-share 未満の色 (一点だけの強調色など) は除く。 --avoid-hue で除く色相を指定できる
    2. src の色を明るさ (Lab の L) の順に並べ、 画素数で重みをつけた累積分布を、 ref のパレットの明るさの分布へ合わせる (分位点の対応)。
       → src の暗い色は ref の暗い色へ、 明るい色は明るい色へ。 明暗の段数と順序は保たれる
    3. 合わせた明るさの近く (--tol 以内) にある ref の色のうち、 src の色みに最も近いものを選ぶ (灰色は灰色、 暖色は暖色へ)
    4. 黒 (輝度 --black 以下) は、 背景や縁取りなので、 そのまま残す
  オプション:
    --keep-light R   合わせた明るさと、 元の明るさを混ぜる (0 = ref の分布に完全に合わせる / 1 = 元の明るさのまま)。 ref が暗いと、 全体が沈むのを和らげる
    --accent ref...  彩度が --accent-chroma 以上で、 色相 (Lab の角度) が --accent-hue (度・既定は全部) の範囲の色は、 この絵のパレット (例: 金貨) から、 明るさが近い色を選ぶ (アイコンの色だけを別にしたいとき。
                     例: 青緑のアイコンだけを金貨の色にするなら --accent-hue 160 280)

  出力: 変換後の PNG、 対応表付きの確認画像 (<出力>_report.png)

使い方: python Tools/palette_transfer.py src.png ref1.png [ref2.png ...] -o 出力.png [--tol 10] [--min-share 0.004] [--black 8]
"""
import argparse
import os

import numpy as np
from PIL import Image


def srgb_to_lab(rgb):
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    m = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = c @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def luma(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def weighted_colors(arr, mask):
    flat = arr[mask].reshape(-1, 3)
    cols, cnt = np.unique(flat, axis=0, return_counts=True)
    return cols, cnt


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("refs", nargs="+"); ap.add_argument("-o", "--out", required=True)
    ap.add_argument("--tol", type=float, default=10.0, help="明るさ (L) の許容幅")
    ap.add_argument("--min-share", type=float, default=0.004, help="パレットに入れる最小の割合")
    ap.add_argument("--black", type=float, default=8.0, help="これ以下の輝度は黒として、 そのまま残す")
    ap.add_argument("--scale", type=int, default=4)
    ap.add_argument("--keep-light", type=float, default=0.0)
    ap.add_argument("--accent", nargs="*", default=[]); ap.add_argument("--accent-chroma", type=float, default=22.0)
    ap.add_argument("--accent-hue", nargs=2, type=float, default=[0.0, 360.0])
    a = ap.parse_args()
    # ref のパレット
    pal_c, pal_n = [], []
    for r in a.refs:
        im = np.asarray(Image.open(r).convert("RGBA"))
        c, n = weighted_colors(im[..., :3], im[..., 3] > 0)
        pal_c.append(c); pal_n.append(n)
    pc = np.concatenate(pal_c); pn = np.concatenate(pal_n)
    cols, inv = np.unique(pc, axis=0, return_inverse=True); cnt = np.zeros(len(cols)); np.add.at(cnt, inv.ravel(), pn)
    keep = (cnt / cnt.sum() >= a.min_share) & (np.array([luma(c) for c in cols]) > a.black)
    P, Pn = cols[keep], cnt[keep]; PL = srgb_to_lab(P)
    # src の色
    sim = Image.open(a.src); has_alpha = sim.mode == "RGBA"; sarr = np.asarray(sim.convert("RGBA")).copy()
    scol, scnt = weighted_colors(sarr[..., :3], sarr[..., 3] > 0)
    dark = np.array([luma(c) <= a.black for c in scol]); mv = ~dark
    S, Sn = scol[mv], scnt[mv]; SL = srgb_to_lab(S)
    order = np.argsort(SL[:, 0]); cum = (np.cumsum(Sn[order]) - Sn[order] / 2) / Sn.sum()          # src の明るさの順位 (0〜1)
    po = np.argsort(PL[:, 0]); pcum = np.cumsum(Pn[po]) / Pn.sum()
    ac = []
    for r_ in a.accent:
        im = np.asarray(Image.open(r_).convert("RGBA")); c, n = weighted_colors(im[..., :3], im[..., 3] > 0); ac += [tuple(x) for x, m in zip(c, n) if luma(x) > a.black]
    acL = srgb_to_lab(np.array(ac)) if ac else None
    mapping = {}
    for rank, k in enumerate(order):
        hue = (np.degrees(np.arctan2(SL[k, 2], SL[k, 1])) + 360) % 360
        if acL is not None and np.hypot(SL[k, 1], SL[k, 2]) >= a.accent_chroma and a.accent_hue[0] <= hue <= a.accent_hue[1]:                      # 彩度の高い色 = アイコンの色: 金貨の色から明るさが近いものへ
            mapping[tuple(S[k])] = ac[int(np.argmin(np.abs(acL[:, 0] - SL[k, 0])))]; continue
        q = np.searchsorted(pcum, cum[rank]); q = min(q, len(po) - 1); target_L = (1 - a.keep_light) * PL[po[q], 0] + a.keep_light * SL[k, 0]      # 分位点の対応
        near = [j for j in range(len(P)) if abs(PL[j, 0] - target_L) <= a.tol]
        if not near: near = [int(np.argmin(np.abs(PL[:, 0] - target_L)))]
        j = min(near, key=lambda j: np.hypot(PL[j, 1] - SL[k, 1], PL[j, 2] - SL[k, 2]) + 0.15 * abs(PL[j, 0] - target_L))   # 色みが近いもの
        mapping[tuple(S[k])] = tuple(P[j])
    out = sarr.copy()
    for y in range(out.shape[0]):
        for x in range(out.shape[1]):
            t = tuple(int(v) for v in out[y, x, :3])
            if t in mapping: out[y, x, :3] = mapping[t]
    res = Image.fromarray(out, "RGBA") if has_alpha else Image.fromarray(out[..., :3], "RGB")
    res.save(a.out)
    # 確認画像: 対応表 (左 = src の色 → 右 = 置き換えた色) と、 前後の絵
    sw = 14; rows = sorted(mapping.items(), key=lambda kv: luma(kv[0]))
    rep = Image.new("RGB", (sim.width * a.scale * 2 + 20 + (sw * 2 + 40), max(sim.height * a.scale, (sw + 4) * len(rows) + 8)), (40, 40, 40))
    for i, (s_, t_) in enumerate(rows):
        for dx in range(sw):
            for dy in range(sw): rep.putpixel((sim.width * a.scale * 2 + 20 + dx, 4 + i * (sw + 4) + dy), s_); rep.putpixel((sim.width * a.scale * 2 + 20 + sw + 8 + dx, 4 + i * (sw + 4) + dy), t_)
    rep.paste(sim.convert("RGB").resize((sim.width * a.scale, sim.height * a.scale), Image.NEAREST), (0, 0))
    rep.paste(res.convert("RGB").resize((sim.width * a.scale, sim.height * a.scale), Image.NEAREST), (sim.width * a.scale + 10, 0))
    rep.save(os.path.splitext(a.out)[0] + "_report.png")
    print("参照パレット %d 色 / src %d 色 → 対応 %d" % (len(P), len(S), len(mapping)))


if __name__ == "__main__":
    main()
