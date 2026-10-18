"""dirty_plate.py — 金属の板のドット絵を、 既存の GUI (GOLD の入れ物・操作パネル・土台) と同じ種類の汚れ方で汚す。

使い方:
  python Tools/dirty_plate.py 元.png 出力.png [--seed 1] [--no-protect-lamps]
  python Tools/dirty_plate.py --report 画像.png [...]       # 汚れの統計だけ出す (さび率・色数・輝度のばらつき・縦の垂れ)

既存の GUI の測定値 (2026-10-14・`--report` で再測定できる) を目標にしている:
  さびの画素の割合 約 10〜11% (GOLD の入れ物)・色数 25〜35・輝度のばらつき(σ) 約 25〜31・さびの縦の垂れは平均 2〜3 ドット (最大 13)・さびは縁から 2〜6 ドットの所に多い。
  逆に、 汚れの無い土台 (res_bar_base) はさび率 0.4%・σ 12。
やること (どれも 1 ドット・種つき乱数):
  ① 細かい濃淡: 2×2 の塊の値ノイズを 5 段に区切って輝度にかける (白いざらつきにしない)
  ② さびの染み・垂れ: 灰色の板の、 縁から 2〜6 ドットの所から始まり、 下へ短く垂れる (橙〜茶の 3 色)
  ③ 擦り傷: 斜め (約 −35°) の短い明るい線 (灰色の板だけ)
  ④ 汚れ: 下の縁の近くを、 ディザで暗くする
  ⑤ 塗装のはがれ: 上の縁の近くに、 地金の明るい 1 ドット
  ⑥ さびの板 (茶色の塊): 地金が出た灰色の欠け・暗い油の筋・縁の暗がり
  ⑦ 色数をそろえる: 近い色 (距離 26 以内) は、 数の多い色へ寄せる
触らないもの: 輪郭 (暗い 1 ドット)・窓の中 (暗い所)・ランプ枠 (赤・橙のベタ)。
"""
import argparse, colorsys, math, sys
import numpy as np
from PIL import Image

LAMP_BOXES = [(4, 133, 40, 147), (45, 133, 81, 147)]        # res_bar_extension.png のランプ枠 (x0, y0, x1, y1)
WINDOW = (85, 131, 239, 147)                                  # 窓 (バーが入る所)。 中の暗い所は元から対象外で、 周りの枠だけ軽く汚す
RUST = [(122, 63, 30), (154, 82, 38), (184, 106, 48)]
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]])


def edge_dist(mask, cap=9):
    d = np.zeros(mask.shape, int); cur = mask.copy()
    for k in range(1, cap):
        er = cur.copy(); er[1:] &= cur[:-1]; er[:-1] &= cur[1:]; er[:, 1:] &= cur[:, :-1]; er[:, :-1] &= cur[:, 1:]
        d[cur & ~er] = k; cur = er
    d[cur] = cap
    return d


def rust_mask(rgb, plate):
    out = np.zeros(plate.shape, bool)
    for y, x in zip(*np.nonzero(plate)):
        h, s, v = colorsys.rgb_to_hsv(*(rgb[y, x] / 255.0)); out[y, x] = 0.02 < h < 0.1 and s > 0.35 and v > 0.25
    return out


def report(path, box=None):
    a = np.array(Image.open(path).convert('RGBA')); a = a if box is None else a[box[1]:box[3], box[0]:box[2]]
    m = a[..., 3] > 0; rgb = a[..., :3].astype(int); lum = 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]
    plate = m & (lum > 35); rs = rust_mask(rgb, plate); d = edge_dist(m)
    cols = {tuple(c) for c in rgb[plate]}
    ed = [round(float((rs & (d == k)).sum() / max(1, (plate & (d == k)).sum())), 2) for k in range(2, 7)]
    runs = []
    for x in range(m.shape[1]):
        c = 0
        for y in range(m.shape[0]):
            if rs[y, x]: c += 1
            elif c: runs.append(c); c = 0
        if c: runs.append(c)
    print(f'{path}: 画素 {plate.sum()}  色数 {len(cols)}  さび率 {rs.sum() / max(1, plate.sum()):.1%}  縁から 2〜6 の距離別さび率 {ed}  縦ラン 平均 {np.mean(runs) if runs else 0:.1f} / 最大 {max(runs) if runs else 0}  輝度σ {lum[plate].std():.1f}')


def value_noise(rng, shape, cell=2):
    h, w = shape; g = rng.random(((h + cell - 1) // cell + 1, (w + cell - 1) // cell + 1))
    n = np.kron(g, np.ones((cell, cell)))[:h, :w]
    # 隣り合う塊をなじませる (1 回だけ平均)
    n2 = n.copy(); n2[1:] += n[:-1]; n2[:, 1:] += n[:, :-1]; n2 /= 3
    return n2


def line_pts(x0, y0, x1, y1):
    pts = []; dx, dy = abs(x1 - x0), -abs(y1 - y0); sx = 1 if x0 < x1 else -1; sy = 1 if y0 < y1 else -1; er = dx + dy
    while True:
        pts.append((x0, y0))
        if x0 == x1 and y0 == y1: break
        e2 = 2 * er
        if e2 >= dy: er += dy; x0 += sx
        if e2 <= dx: er += dx; y0 += sy
    return pts


def dirty(src, seed=1, protect_lamps=True):
    rng = np.random.default_rng(seed)
    im = np.array(Image.open(src).convert('RGBA')); H, W = im.shape[:2]
    rgb = im[..., :3].astype(float); a = im[..., 3] > 0
    lum = 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]
    plate = a & (lum > 35)
    prot = np.zeros((H, W), bool)
    if protect_lamps:
        for x0, y0, x1, y1 in LAMP_BOXES: prot[y0 - 1:y1 + 2, x0 - 1:x1 + 2] = True
    hsv = np.zeros((H, W, 3))
    for y, x in zip(*np.nonzero(plate)): hsv[y, x] = colorsys.rgb_to_hsv(*(rgb[y, x] / 255.0))
    brown = plate & (hsv[..., 0] > 0.02) & (hsv[..., 0] < 0.1) & (hsv[..., 1] > 0.3)          # さびの板 (茶色の塊)
    gray = plate & ~brown & (hsv[..., 1] < 0.35)
    d = edge_dist(a)
    work = plate & ~prot
    out = rgb.copy()

    # ① 細かい濃淡 (2×2 の塊・5 段)
    nz = value_noise(rng, (H, W), 2); q = np.floor(nz * 5.0).clip(0, 4)
    fac = np.array([0.90, 0.95, 1.0, 1.04, 1.09])[q.astype(int)]
    amp = np.where(brown, 0.55, 0.9)[..., None]
    out[work] = np.clip(out[work] * (1 + (fac[work][:, None] - 1) * amp[work]), 0, 255)

    # ② さびの染み・垂れ (灰色の板の、 縁から 2〜6 ドット)
    p_tab = {2: 0.026, 3: 0.02, 4: 0.026, 5: 0.022, 6: 0.018}
    for y in range(H):
        for x in range(W):
            if not (gray[y, x] and not prot[y, x] and d[y, x] in p_tab): continue
            if rng.random() > p_tab[d[y, x]]: continue
            tone = RUST[rng.integers(0, 3)]; L = int(min(12, 1 + rng.geometric(0.42))); w2 = rng.random() < 0.35
            for j in range(L):
                yy = y + j
                if yy >= H or not (gray[yy, x] and not prot[yy, x]): break
                t = max(0.25, 0.95 - j * 0.09); out[yy, x] = out[yy, x] * (1 - t) + np.array(tone) * t
                if j == 0 and w2:
                    for xx in (x - 1, x + 1):
                        if 0 <= xx < W and gray[yy, xx] and not prot[yy, xx]: out[yy, xx] = out[yy, xx] * 0.45 + np.array(tone) * 0.55

    # ③ 擦り傷 (斜め・明るい・灰色の板だけ)
    ys, xs = np.nonzero(gray & ~prot)
    for _ in range(int(len(ys) / 300) + 3):
        i = rng.integers(0, len(ys)); x0, y0 = int(xs[i]), int(ys[i]); L = int(rng.integers(4, 11)); ang = math.radians(rng.uniform(-42, -28))
        for px, py in line_pts(x0, y0, int(round(x0 + math.cos(ang) * L)), int(round(y0 + math.sin(ang) * L))):
            if 0 <= px < W and 0 <= py < H and gray[py, px] and not prot[py, px] and d[py, px] >= 2: out[py, px] = np.clip(out[py, px] + 26, 0, 255)

    # ④ 汚れ: 下の縁の近く (下へ数えて 4 ドット以内で透明・暗部に当たる所) をディザで暗く
    down = np.full((H, W), 99)
    for y in range(H - 1, -1, -1):
        for x in range(W):
            if not plate[y, x]: continue
            k = 1
            while k <= 4 and y + k < H and plate[y + k, x]: k += 1
            if k <= 4: down[y, x] = k
    bay = BAYER[np.arange(H)[:, None] % 4, np.arange(W)[None, :] % 4]
    for y, x in zip(*np.nonzero(work & (down <= 4))):
        if (rng.random() * 16) < (3 + (5 - down[y, x])) and bay[y, x] < 7: out[y, x] *= 0.8

    # ⑤ 塗装のはがれ: 上の縁の近くの明るい 1 ドット
    for y, x in zip(*np.nonzero(work & gray)):
        up = 0
        while up < 3 and y - up - 1 >= 0 and plate[y - up - 1, x]: up += 1
        if up <= 1 and y - up - 1 >= 0 and not plate[y - up - 1, x] and rng.random() < 0.07: out[y, x] = np.clip(out[y, x] + 44, 0, 255)

    # ⑥ さびの板: 地金の欠け・暗い油の筋・縁の暗がり
    by, bx = np.nonzero(brown & ~prot)
    for _ in range(int(len(by) / 300) + 2):
        i = rng.integers(0, len(by)); y0, x0 = int(by[i]), int(bx[i]); size = int(rng.integers(1, 4))
        for dy in range(size):
            for dx in range(int(rng.integers(1, 4))):
                yy, xx = y0 + dy, x0 + dx
                if 0 <= yy < H and 0 <= xx < W and brown[yy, xx] and d[yy, xx] >= 2: out[yy, xx] = np.array([118, 112, 104])
    for _ in range(int(len(by) / 320) + 2):
        i = rng.integers(0, len(by)); y0, x0 = int(by[i]), int(bx[i]); L = int(rng.integers(5, 14))
        for j in range(L):
            yy = y0 + j
            if yy < H and brown[yy, x0] and not prot[yy, x0]: out[yy, x0] *= max(0.5, 0.82 - j * 0.015)
    for y, x in zip(*np.nonzero(brown & ~prot & (d >= 2) & (d <= 3))):
        if bay[y, x] < 4: out[y, x] *= 0.86

    # ⑦ 色数をそろえる
    res = np.clip(np.round(out / 6) * 6, 0, 255).astype(int)
    cols, counts = np.unique(res[work].reshape(-1, 3), axis=0, return_counts=True)
    order = np.argsort(-counts); keep = []
    for i in order:
        c = cols[i]
        if not keep or min(np.abs(np.array(keep) - c).sum(axis=1)) > 26: keep.append(c)
    keep = np.array(keep)
    for y, x in zip(*np.nonzero(work)):
        c = res[y, x]; k = keep[np.argmin(np.abs(keep - c).sum(axis=1))]; res[y, x] = k
    final = im.copy()
    final[..., :3] = np.where(work[..., None], res, im[..., :3]).astype(np.uint8)
    return Image.fromarray(final, 'RGBA')


if __name__ == '__main__':
    ap = argparse.ArgumentParser(); ap.add_argument('paths', nargs='*'); ap.add_argument('--seed', type=int, default=1); ap.add_argument('--report', action='store_true'); ap.add_argument('--no-protect-lamps', action='store_true')
    ar = ap.parse_args()
    if ar.report:
        for p in ar.paths: report(p)
        sys.exit()
    if len(ar.paths) != 2: ap.error('元の PNG と出力の PNG を指定する')
    dirty(ar.paths[0], ar.seed, not ar.no_protect_lamps).save(ar.paths[1]); print('出力', ar.paths[1])
