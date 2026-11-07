"""ドット絵の法線マップとライトマスクを作る (2026-11-07)。

Pixel Art Lighter v3.5.22 (MIT License, Copyright (c) 2026 Red Hat Sprite) の生成の手順
(generateTexture) を Python に移したもの。 表記は THIRD_PARTY_NOTICES.md。

考え方 (元の道具の説明より):
  - ドット絵の色の違いは陰影・輪郭・素材の境目であって、 必ずしも奥行きの違いではない。
  - 同じ色でつながったまとまりを 1 つの面として扱い、 面の向きを数段階に丸める (ぼやけた膨らみにしない)。
  - 暗い細い輪郭線は、 隣の面と同じ向きにする (輪郭は絵の様式で、 形の段差ではない)。

使い方:
  python Tools/pixel_normals.py Assets/gui2d/hp_bar_left.png [他の png ...] --out Assets/gui2d/lit
  → <名前>_normal.png (RGB = 法線 x,y,z を 0..255 に・A = 元の透明度)・<名前>_mask.png (光の受けやすさ・灰色)・
    <名前>_emit.png (自分で光る画素の印・照明から外す)
出力は Pixel Art Lighter に読み込んで、 向きの違う面を手で直せる (Modify)。
"""
import argparse, math, os, sys
from collections import deque
import numpy as np
from PIL import Image

PRESETS = {  # 元の道具の presetDefaults と同じ
    "architecture": dict(strength=100, facets=20, floor=30, cavity=25, edge=1),
    "object":       dict(strength=100, facets=35, floor=20, cavity=35, edge=3),
    "terrain":      dict(strength=110, facets=50, floor=35, cavity=20, edge=3),
    "character":    dict(strength=65,  facets=15, floor=30, cavity=20, edge=2),
}


def _unit(v):
    n = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) or 1.0
    return [v[0] / n, v[1] / n, v[2] / n]


def emissive(rgba, min_sat=0.45, min_val=0.55):
    """自分で光る画素 (LED・ランプ・表示器) の印。 彩度が高く明るい画素を 255、 他を 0 (2026-11-07・移植で足した手順)。
    照明の計算から外し、 元の色のまま出すために使う。 印は手で直してよい (灰色の絵)。"""
    c = rgba[..., :3].astype(np.float32) / 255
    mx, mn = c.max(-1), c.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    on = (sat >= min_sat) & (mx >= min_val) & (rgba[..., 3] > 0)
    out = np.zeros_like(rgba)
    out[..., :3] = (on * 255).astype(np.uint8)[..., None]
    out[..., 3] = rgba[..., 3]
    return out


def generate(rgba, kind="normal", preset="object", merge=True, strength=None, normal_strength=100,
             edge_width=None, facets=None, floor=None, ceiling=255, cavity=None, small_flat=24):
    p = PRESETS[preset]
    strength = p["strength"] if strength is None else strength
    facets = p["facets"] if facets is None else facets
    floor = p["floor"] if floor is None else floor
    cavity = p["cavity"] if cavity is None else cavity
    edge_width = p["edge"] if edge_width is None else edge_width

    h, w = rgba.shape[:2]
    N = w * h
    a = rgba.reshape(-1, 4).astype(np.int32)
    fg = a[:, 3] > 0
    lum = (0.2126 * a[:, 0] + 0.7152 * a[:, 1] + 0.0722 * a[:, 2]) / 255.0
    lo = lum[fg].min() if fg.any() else 0.0
    hi = lum[fg].max() if fg.any() else 1.0
    rng = hi - lo

    def adjacent(i):
        x, y = i % w, i // w
        out = []
        if x: out.append(i - 1)
        if x < w - 1: out.append(i + 1)
        if y: out.append(i - w)
        if y < h - 1: out.append(i + w)
        return out

    # 同じ色でつながったまとまり
    labels = np.full(N, -1, np.int32)
    clusters = []
    same = lambda i, j: a[i, 0] == a[j, 0] and a[i, 1] == a[j, 1] and a[i, 2] == a[j, 2]
    for i in range(N):
        if not fg[i] or labels[i] >= 0: continue
        cid = len(clusters); labels[i] = cid
        q = deque([i]); px = []
        while q:
            c = q.popleft(); px.append(c)
            for n in adjacent(c):
                if fg[n] and labels[n] < 0 and same(n, i):
                    labels[n] = cid; q.append(n)
        clusters.append(dict(pixels=px, value=lum[i]))

    # 縁からの距離
    dist = np.full(N, N, np.float32)
    q = deque()
    for i in range(N):
        if fg[i] and (i % w == 0 or i % w == w - 1 or i < w or i >= N - w or any(not fg[n] for n in adjacent(i))):
            dist[i] = 1; q.append(i)
    while q:
        c = q.popleft()
        for n in adjacent(c):
            if fg[n] and dist[n] > dist[c] + 1:
                dist[n] = dist[c] + 1; q.append(n)

    normalized = lambda i: (lum[i] - lo) / rng if rng > 1e-5 else 0.75

    # 細い暗い輪郭線
    thin = np.zeros(N, bool)
    if merge:
        for cl in clusters:
            if cl["value"] > lo + rng * 0.24: continue
            interior = 0
            for c in cl["pixels"]:
                adj = adjacent(c)
                if len(adj) == 4 and all(labels[n] == labels[c] for n in adj): interior += 1
            if interior / len(cl["pixels"]) < 0.15:
                for c in cl["pixels"]: thin[c] = True

    out = np.zeros((N, 4), np.uint8)
    if kind == "mask":
        s = strength / 100.0; fl = floor / 255.0; cav = cavity / 100.0
        for cl in clusters:
            value = cl["value"]
            t = (value - lo) / rng if rng > 1e-5 else 0.75
            receive = fl + (1 - fl) * (t ** 1.15)
            if cav and t < 0.35:
                surrounded = 0
                for c in cl["pixels"]:
                    higher = total = 0
                    for n in adjacent(c):
                        if fg[n] and labels[n] != labels[c]:
                            total += 1
                            if lum[n] > value + rng * 0.15: higher += 1
                    if total and higher / total > 0.5: surrounded += 1
                receive *= 1 - cav * min(1, surrounded / max(1, math.sqrt(len(cl["pixels"]))))
            receive = 1 - s + s * receive
            v = int(round(255 * max(0, min(ceiling / 255.0, receive))))
            for c in cl["pixels"]:
                out[c, :3] = v
    else:
        scale = strength / 100.0
        field = np.zeros(N, np.float32)
        conv_k = 0 if preset == "architecture" else 0.12 if preset == "terrain" else 0.65
        lum_k = {"architecture": 3.5, "terrain": 4, "character": 1.2}.get(preset, 2.1)
        cap = 8 if preset == "character" else 16
        for i in range(N):
            if fg[i]:
                field[i] = math.sqrt(min(dist[i], cap)) * conv_k + normalized(i) * lum_k
        for _ in range(edge_width):
            bl = np.zeros(N, np.float32)
            for i in range(N):
                if not fg[i]: continue
                ssum = field[i] * 2; cnt = 2
                for n in adjacent(i):
                    if fg[n]: ssum += field[n]; cnt += 1
                bl[i] = ssum / cnt
            field = bl

        def grad(i):
            x, y = i % w, i // w
            at = lambda n: field[n] if 0 <= n < N and fg[n] else field[i]
            dx = (at(i + 1) if x < w - 1 else field[i]) - (at(i - 1) if x else field[i])
            dy = (at(i + w) if y < h - 1 else field[i]) - (at(i - w) if y else field[i])
            return _unit([-dx * scale, dy * scale, 1])

        dirs = np.zeros((N, 3), np.float32)
        for i in range(N):
            if fg[i]: dirs[i] = grad(i)

        def quantize(v):
            v = _unit(v)
            tilt = math.hypot(v[0], v[1])
            if tilt < 0.12: return [0.0, 0.0, 1.0]
            ang = round(math.atan2(v[1], v[0]) / (math.pi / 4)) * math.pi / 4
            if preset == "architecture": el = min(0.9, 0.6 * scale)
            else: el = 0.24 if tilt < 0.3 else 0.48 if tilt < 0.6 else 0.72 if tilt < 0.85 else 0.9
            return [math.cos(ang) * el, math.sin(ang) * el, math.sqrt(1 - el * el)]

        for cl in clusters:
            px = cl["pixels"]
            s = [0.0, 0.0, 0.0]
            for c in px:
                if preset == "architecture" and math.hypot(dirs[c, 0], dirs[c, 1]) < 0.06: continue
                s[0] += dirs[c, 0]; s[1] += dirs[c, 1]; s[2] += dirs[c, 2]
            if s[2] == 0: s = [0, 0, 1]
            avg = _unit(s)
            groups = {}
            if facets > 0 and len(px) >= round(180 - facets * 1.1):
                for c in px:
                    dx, dy = dirs[c, 0], dirs[c, 1]
                    key = round(math.atan2(dy, dx) / (math.pi / 2)) if math.hypot(dx, dy) > 0.35 else 9
                    groups.setdefault(key, []).append(c)
            supported = [g for g in groups.values() if len(g) >= max(16, len(px) * 0.16)]
            allow = False
            if len(supported) >= 2:
                means = []
                for g in supported:
                    m = [0.0, 0.0, 0.0]
                    for c in g: m[0] += dirs[c, 0]; m[1] += dirs[c, 1]; m[2] += dirs[c, 2]
                    means.append(_unit(m))
                for ii in range(len(means)):
                    for jj in range(ii):
                        if sum(means[ii][k] * means[jj][k] for k in range(3)) < 0.8 - facets * 0.001: allow = True
            base = quantize(avg)
            for c in px: dirs[c] = base
            if allow:
                for g in supported:
                    m = [0.0, 0.0, 0.0]
                    for c in g:
                        v = grad(c); m[0] += v[0]; m[1] += v[1]; m[2] += v[2]
                    v = quantize(m)
                    for c in g: dirs[c] = v

        # 小さなまとまり (文字・細かい刻み) は正面向きにそろえる (2026-11-07・移植で足した手順)。
        #   元の道具のままだと、 刻印の文字が面ごとに細かく傾き、 光が動くとちらつく。
        for cl in clusters:
            if len(cl["pixels"]) <= small_flat:
                for c in cl["pixels"]: dirs[c] = [0.0, 0.0, 1.0]

        if merge:  # 輪郭線へ隣の面の向きを流し込む
            assigned = np.zeros(N, bool); q = deque()
            for i in range(N):
                if fg[i] and not thin[i]: assigned[i] = True; q.append(i)
            while q:
                c = q.popleft()
                for n in adjacent(c):
                    if fg[n] and not assigned[n]:
                        assigned[n] = True; dirs[n] = dirs[c]; q.append(n)

        seen = np.zeros(N, bool)  # 2 画素以下の向きの島は隣へ寄せる
        for i in range(N):
            if not fg[i] or seen[i]: continue
            grp = []; q = deque([i]); seen[i] = True
            while q:
                c = q.popleft(); grp.append(c)
                for n in adjacent(c):
                    if fg[n] and not seen[n] and abs(dirs[n, 0] - dirs[i, 0]) < 0.001 and abs(dirs[n, 1] - dirs[i, 1]) < 0.001:
                        seen[n] = True; q.append(n)
            if len(grp) <= 2:
                gs = set(grp)
                nb = [n for c in grp for n in adjacent(c) if fg[n] and n not in gs]
                if nb:
                    for c in grp: dirs[c] = dirs[nb[0]]

        gain = normal_strength / 100.0
        for i in range(N):
            nv = _unit([dirs[i, 0] * gain, dirs[i, 1] * gain, dirs[i, 2]]) if fg[i] else [0.0, 0.0, 1.0]
            out[i, :3] = [int(round((c + 1) * 127.5)) for c in nv]
    out[:, 3] = a[:, 3]
    return out.reshape(h, w, 4)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("--out", required=True)
    ap.add_argument("--preset", default="object", choices=sorted(PRESETS))
    ap.add_argument("--no-merge", action="store_true")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    for path in args.inputs:
        rgba = np.array(Image.open(path).convert("RGBA"))
        stem = os.path.splitext(os.path.basename(path))[0]
        n = generate(rgba, "normal", args.preset, merge=not args.no_merge)
        m = generate(rgba, "mask", args.preset, merge=not args.no_merge)
        Image.fromarray(n, "RGBA").save(os.path.join(args.out, stem + "_normal.png"))
        Image.fromarray(m, "RGBA").save(os.path.join(args.out, stem + "_mask.png"))
        Image.fromarray(emissive(rgba), "RGBA").save(os.path.join(args.out, stem + "_emit.png"))
        print("ok", stem, rgba.shape[1], "x", rgba.shape[0], flush=True)


if __name__ == "__main__":
    main()
