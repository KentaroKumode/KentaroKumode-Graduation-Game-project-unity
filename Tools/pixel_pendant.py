#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""アイコンの上の金具から、 鎖を 2 本伸ばしてペンダントのように吊る。 左右に揺れるアニメーションも作る (2026-10-05)。

  vee  : 金具の上の吊り輪から、 鎖が左右の斜め上へ開く V 字。 先は画像の上の角 (小さな端の輪)。 浅く (横に広く) 長く
  lens : 左右へ膨らんだ 2 本の鎖が、 上の一点 (吊り輪) で合流するひし形

鎖は幅 2 ドット。 色は台座と同じ青灰 4 段 (左が明・右が暗)。 輪の継ぎ目に暗い 1 ドットを置く。 線はわずかにたわませる (チェーンの重さ)。

アニメーション (--frames N): ペンダント (吊り輪 + 石) が**縦軸まわりに**左右へ少し回る (コインがくるっと回る向き・振り子ではない)。
  横幅が cos(角度) に縮み、 結晶は枠より奥まっているので視差で枠に対してずれ、 奥へ回る側は 1 段暗く・手前に来る側は 1 段明るい。
  鎖の上端は固定。 角度は sin 波 (±--swing 度)・1 周期 N コマ。 出力は連番 / 横並びシート / 確認用 GIF。

使い方:
  静止:        python Tools/pixel_pendant.py アイコン.png [出力.png] [--mode vee|lens] [--rise 16] [--spread 38] [--scale 8]
  アニメ:      python Tools/pixel_pendant.py アイコン.png 出力名 --frames 12 --swing 30
"""
import argparse
import math
import os

from PIL import Image

H, M, D, K = (131, 151, 159), (97, 115, 122), (65, 77, 82), (49, 52, 61)
RING = [".MHM.", "H...D", "H...D", "M...K", ".KDK."]       # 吊り輪 (5×5)
C = {"H": H, "M": M, "D": D, "K": K}


def put(img, x, y, col):
    if 0 <= x < img.width and 0 <= y < img.height: img.putpixel((x, y), col + (255,))


def ring(img, x0, y0):
    for j, row in enumerate(RING):
        for i, ch in enumerate(row):
            if ch != ".": put(img, x0 + i, y0 + j, C[ch])


def chain(img, p0, p1, sag, sag_x=0):
    """p0 → p1 の鎖。 二次曲線でわずかにたわませる (sag = 真ん中の下がり・sag_x = 横への膨らみ)。 幅 2 ドット・輪の継ぎ目に暗い点。"""
    (x0, y0), (x1, y1) = p0, p1
    n = int(max(abs(x1 - x0), abs(y1 - y0)) * 1.6) + 2
    pts, last = [], None
    for k in range(n + 1):
        t = k / n
        x = x0 + (x1 - x0) * t + sag_x * math.sin(math.pi * t)
        y = y0 + (y1 - y0) * t + sag * math.sin(math.pi * t)
        q = (int(round(x)), int(round(y)))
        if q != last: pts.append(q); last = q
    for i, (x, y) in enumerate(pts):
        seam = (i % 6) in (4, 5)                        # 6 ドットごとに 2 ドットの継ぎ目 (輪と輪のあいだ)
        left, right = (K, K) if seam else (H if i % 6 < 2 else M, D if i % 6 < 2 else K)
        put(img, x, y, left); put(img, x + 1, y, right)
    return pts


METAL = [(22, 25, 32), (49, 52, 61), (65, 77, 82), (97, 115, 122), (131, 151, 159)]      # 輪郭 + 台座の青灰 4 段 (暗 → 明)
CRYSTAL = [(30, 42, 78), (52, 82, 138), (96, 142, 196), (190, 218, 238)]                    # 結晶の青 4 段 (暗 → 明)


def is_crystal(px):
    return px[3] > 0 and px[2] - px[0] > 25                          # 青い画素 = 結晶


def tone(px, step):
    """段を step だけ明るく (+) / 暗く (-) する。 台座は台座の段・結晶は結晶の段の中で動く (新しい色を作らない)。"""
    cols = CRYSTAL if is_crystal(px) else METAL
    try: k = cols.index(tuple(px[:3]))
    except ValueError: return px
    return cols[max(0, min(len(cols) - 1, k + step))] + (px[3],)


def yaw(layer, axis, phi_deg, depth=1.5):
    """縦軸まわりの回転 (コインがくるっと回る向き)。 横幅を cos に縮め、 結晶 (枠より奥まっている) は視差で枠に対してずらす。
    奥へ回る側は 1 段暗く・手前に来る側は 1 段明るくする。 ドットの行はそのまま (縦は縮めない)。 phi > 0 で右側が奥へ回る。"""
    phi = math.radians(phi_deg)
    c, sn = math.cos(phi), math.sin(phi)
    w, h = layer.size
    src = layer.load()
    a = axis + 0.5                                                    # 軸の連続座標 (ドットの中心)
    out = Image.new("RGBA", layer.size, (0, 0, 0, 0)); dst = out.load()
    for part_crystal in (True, False):                                # 結晶を先に (枠が手前なので後から重ねる)
        off = -depth * sn if part_crystal else 0.0                    # 結晶は z = -depth: x' = x·cos + z·sin ── 枠より sin·depth だけずれる
        for y in range(h):
            for x in range(w):
                xs = int(math.floor(a + (x + 0.5 - a - off) / c))
                if not (0 <= xs < w): continue
                px = src[xs, y]
                if px[3] == 0 or is_crystal(px) != part_crystal: continue
                side = (x + 0.5 - a) * sn                              # > 0: 奥へ回る側
                step = -1 if (side > 0.8 and abs(sn) > 0.25) else 1 if (side < -0.8 and abs(sn) > 0.25) else 0
                dst[x, y] = tone(px, step)
    return out


def build(icon, mode="vee", rise=16, spread=38, phi=0.0):
    """1 コマ。 phi = 縦軸まわりの回転角 (度・右側が奥へ回る向きが正)。"""
    pad = spread + 4 if mode == "vee" else spread + 6
    W = icon.width + 2 * pad
    cx = W // 2
    ring_y, icon_y = rise, rise + 3
    H_ = icon_y + icon.height
    # ペンダント (吊り輪 + 石) を別の層に描いて、 縦軸 (中心の列) まわりに回す。 鎖の上端は固定
    pend = Image.new("RGBA", (W, H_), (0, 0, 0, 0))
    ring(pend, cx - 2, ring_y)
    pend.alpha_composite(icon, (cx - 9, icon_y))
    if phi: pend = yaw(pend, cx, phi)
    dx_ring = 0                                                   # 吊り輪は軸の上にあるので位置は動かない (横幅だけ縮む)
    img = Image.new("RGBA", (W, H_), (0, 0, 0, 0))
    if mode == "vee":
        for sgn in (-1, 1):
            ex = cx + sgn * spread
            chain(img, (cx + sgn * 2 + dx_ring, ring_y + 1), (ex, 1), sag=2)
            for j in range(3):                                                 # 端の小さな輪 (3×3)
                for i in range(3):
                    if (i, j) != (1, 1): put(img, ex - 1 + i, j, M if (i + j) % 2 else H)
    else:
        chain(img, (cx - 2 + dx_ring, ring_y + 1), (cx - 1, 5), sag=0, sag_x=-spread)
        chain(img, (cx + 2 + dx_ring, ring_y + 1), (cx + 1, 5), sag=0, sag_x=spread)
        ring(img, cx - 2, 0)
    img.alpha_composite(pend)
    return img


def save_x(img, path, scale, bg=(42, 38, 32, 255)):
    big = Image.new("RGBA", (img.width * scale, img.height * scale), bg)
    big.alpha_composite(img.resize((img.width * scale, img.height * scale), Image.NEAREST))
    big.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--mode", choices=("vee", "lens"), default="vee")
    ap.add_argument("--rise", type=int, default=16, help="吊り輪から上端までの高さ (小さいほど V が浅い)")
    ap.add_argument("--spread", type=int, default=38, help="V 字の開き (中心から左右の端まで) / ひし形は最大の膨らみ")
    ap.add_argument("--scale", type=int, default=8)
    ap.add_argument("--frames", type=int, default=0, help="アニメーションのコマ数 (0 で静止)")
    ap.add_argument("--swing", type=float, default=30.0, help="縦軸まわりの回転の振れ幅 (度・左右にこの角度まで)")
    ap.add_argument("--ms", type=int, default=90, help="1 コマの長さ (ミリ秒・GIF 用)")
    args = ap.parse_args()
    icon = Image.open(args.src).convert("RGBA")
    base = args.dst or os.path.splitext(args.src)[0] + f"_pendant_{args.mode}"
    if args.frames <= 0:
        img = build(icon, args.mode, args.rise, args.spread, 0.0)
        img.save(base + ".png"); save_x(img, base + f"_x{args.scale}.png", args.scale)
        print("wrote", base + ".png", "(%d×%d)" % img.size); return
    n = args.frames
    frames = [build(icon, args.mode, args.rise, args.spread, args.swing * math.sin(2 * math.pi * i / n)) for i in range(n)]
    w, h = frames[0].size
    os.makedirs(base + "_frames", exist_ok=True)
    strip = Image.new("RGBA", (w * n, h), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        f.save(os.path.join(base + "_frames", f"f{i:02d}.png")); strip.paste(f, (w * i, 0))
    strip.save(base + "_strip.png")
    bgs = []
    for f in frames:                                              # 確認用 GIF: 暗い背景に 4 倍 (GIF は半透明を持てないため)
        bg = Image.new("RGBA", (w * 4, h * 4), (42, 38, 32, 255)); bg.alpha_composite(f.resize((w * 4, h * 4), Image.NEAREST))
        bgs.append(bg.convert("P", palette=Image.ADAPTIVE))
    bgs[0].save(base + "_preview.gif", save_all=True, append_images=bgs[1:], duration=args.ms, loop=0, disposal=2)
    print("wrote %d frames (%d×%d) → %s_frames/ / _strip.png / _preview.gif" % (n, w, h, base))


if __name__ == "__main__":
    main()
