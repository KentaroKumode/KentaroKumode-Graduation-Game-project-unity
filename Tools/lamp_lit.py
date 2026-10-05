"""警告ランプ (HALF / CRIT) の点灯画像を作る。

元画像 (Assets/Resources/Icons/Items/*_lamp_on|off.png・37×15) の層を色で読み分け、
点灯側だけ描き直す。大きさは変えない (ピクセル要素は拡大で状態を表さない)。

  外枠 … 元の点灯画像のまま (2026-10-03 ユーザー指摘: 消灯時の暗い金属に替えたのは誤り)
  レンズ … 中心が明るく縁へ落ちる明暗 (中の電球)
  文字 … 中心ほど明るい芯。 白に飛ばさずレンズの色味を残す (2026-10-03: 白すぎて浮いた)。
         消灯時はレンズの色へ寄せる (元絵は文字だけ黒く浮く)。
        文字に接するレンズは滲み (ブルーム) で明るく

  光   … {kind}_lamp_glow.png。 レンズだけを光源に、 同じ 37×15 の格子の上でぼかして段に量子化し、
         四方に GLOW_PAD ドット広げた画像に焼く。 加算で重ねる (枠の上にも光が乗る・レンズの上には乗せない)。
         ブラウザのぼかしは格子に乗らずドット絵と解像度が合わないので使わない。

使い方: python Tools/lamp_lit.py  → docs/design/lamp/ に on/off/glow と 12 倍の見本
"""
import colorsys
import math
import os
from PIL import Image, ImageFilter

SRC = os.path.join("Assets", "Resources", "Icons", "Items")
OUT = os.path.join("docs", "design", "lamp")
GLOW_PAD = 3                                   # 光がランプの外へ広がるドット数 (2026-10-03: 3 に限定)
GLOW_STEPS = (0.0, 0.10, 0.20, 0.32, 0.46, 0.62)   # 光の濃さの段 (ドット絵なので連続にしない)
GLOW_COLOR = {"half": (255, 150, 40), "crit": (255, 54, 30)}

# 色は (中心, 中間, 縁, 陰, 滲み, 文字の芯, 文字の外側, 枠の内側の照り返し)
PALETTE = {
    # 2026-10-03: Highfleet の REFUELING 灯を参考に、 中心を白に近い黄色まで飛ばし、 縁は濃い橙で締める
    "half": dict(c=(255, 180, 64), m=(248, 142, 34), e=(222, 104, 18), ring=(150, 64, 10),
                 bloom=(255, 210, 120), core=(255, 240, 196), text=(255, 220, 156), spill=(150, 104, 52)),
    "crit": dict(c=(255, 112, 84), m=(240, 54, 36), e=(196, 26, 18), ring=(120, 14, 10),
                 bloom=(255, 160, 130), core=(255, 214, 196), text=(255, 182, 160), spill=(140, 62, 50)),
}


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


def classify(on):
    """点灯側の元画像の色を読み分ける。 最も明るい色が文字、 残りで彩度の高い 2 色のうち
    多い方がレンズ・少ない方がレンズの陰 (縁の列と文字の上下の段)、 それ以外が枠。 (明るさ順だと CRIT で縁とレンズが入れ替わる)"""
    from collections import Counter
    px = on.load()
    cnt = Counter(px[x, y] for x in range(on.width) for y in range(on.height) if px[x, y][3] > 0)
    text = max(cnt, key=lambda c: 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2])
    sat = sorted((c for c in cnt if c != text), key=lambda c: -(max(c[:3]) - min(c[:3])))[:2]
    fill, ring = sorted(sat, key=lambda c: -cnt[c])
    role = {}
    for y in range(on.height):
        for x in range(on.width):
            c = px[x, y]
            role[x, y] = ("none" if c[3] == 0 else "text" if c == text else
                          "fill" if c == fill else "shade" if c == ring else "frame")
    return role


def lit(kind):
    on = Image.open(os.path.join(SRC, f"{kind}_lamp_on.png")).convert("RGBA")
    off = Image.open(os.path.join(SRC, f"{kind}_lamp_off.png")).convert("RGBA")
    P = PALETTE[kind]
    role = classify(on)
    W, H = on.size
    lens = [p for p, r in role.items() if r in ("text", "fill", "shade")]
    x0, x1 = min(p[0] for p in lens), max(p[0] for p in lens)
    y0, y1 = min(p[1] for p in lens), max(p[1] for p in lens)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    rx, ry = (x1 - x0) / 2 + 0.5, (y1 - y0) / 2 + 0.5

    def d(x, y):   # レンズの中心からの楕円距離 0..1
        return min(1.0, math.hypot((x - cx) / rx, (y - cy) / ry))

    out = off.copy()
    o, srcon = out.load(), on.load()
    for (x, y), r in role.items():
        t = d(x, y)
        if r == "text":
            o[x, y] = lerp(P["core"], P["text"], min(1, t * 1.25))
        elif r in ("fill", "shade"):
            base = lerp(P["c"], P["m"], t / 0.55) if t < 0.55 else lerp(P["m"], P["e"], (t - 0.55) / 0.45)
            if r == "shade":   # 元絵の陰はそのまま陰として残す (縁へ落ちる暗さ)
                base = lerp(base[:3], P["ring"], 0.45)
            else:              # 文字に辺で接するレンズだけ、 ごく弱く滲ませる (強いと文字が読めない)
                near_text = any(role.get((x + dx, y + dy)) == "text" for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                if near_text and t < 0.7:
                    base = lerp(base[:3], P["bloom"], 0.1)
            o[x, y] = base
        elif r == "frame":
            o[x, y] = srcon[x, y]   # 枠は元の点灯画像のまま
    # 白飛び: 文字だけでなく、 光っている面全体を中心ほど白へ寄せる (文字の周りのドットも一緒に飛ぶ)
    for (x, y), r in role.items():
        if r in ("text", "fill", "shade"):
            w = (HOT_MAX_TEXT if r == "text" else HOT_MAX) * max(0.0, 1 - d(x, y) / HOT_REACH)
            if w > 0:
                o[x, y] = lerp(o[x, y][:3], (255, 255, 250) if r == "text" else HOT, w)
    return out, dim_off(off, role), glow(kind, role, W, H)


HOT = (255, 250, 232)   # 点灯時の白飛びの色
HOT_MAX = 0.6           # 中心での白飛びの強さ (文字の周り)
HOT_MAX_TEXT = 0.85     # 同 (文字)。 周りより強くして、 飛んでも文字が読める差を残す
HOT_REACH = 0.75        # 白飛びが届く範囲 (光る面の中心からの楕円距離 0..1)
OFF_TEXT_BLEND = 0.55   # 消灯時の文字をレンズの色へ寄せる割合 (元絵は文字だけ黒く浮く)
OFF_DARK = 0.6          # 消灯時のランプ全体の明るさの倍率
OFF_SAT = 2.0           # 消灯時の彩度の倍率 (明度は変えない)


def dim_off(off, role):
    """消灯画像の文字を、 消灯したレンズの平均色へ寄せる (刻まれた文字が薄く見える程度)。"""
    out = off.copy()
    o = out.load()
    lens = [o[p] for p, r in role.items() if r == "fill"]
    avg = tuple(sum(c[i] for c in lens) / len(lens) for i in range(3))
    for p, r in role.items():
        if r == "text":
            o[p] = lerp(o[p][:3], avg, OFF_TEXT_BLEND)
    for p, r in role.items():
        if r != "none":
            c = o[p]
            o[p] = tuple(round(v * OFF_DARK) for v in c[:3]) + (255,)
            h, l, sat = colorsys.rgb_to_hls(*(v / 255 for v in o[p][:3]))
            o[p] = tuple(round(v * 255) for v in colorsys.hls_to_rgb(h, l, min(1.0, sat * OFF_SAT))) + (255,)
    return out


def glow(kind, role, W, H):
    """レンズだけを光源にした光の層 (RGBA・(W+2·PAD)×(H+2·PAD))。 レンズの上は 0。"""
    pad = GLOW_PAD
    src = Image.new("L", (W + 2 * pad, H + 2 * pad), 0)
    sp = src.load()
    for (x, y), r in role.items():
        if r in ("text", "fill"):
            sp[x + pad, y + pad] = 255
        elif r == "shade":
            sp[x + pad, y + pad] = 170
    near = src.filter(ImageFilter.GaussianBlur(1.6)).load()
    far = src.filter(ImageFilter.GaussianBlur(5.5)).load()
    out = Image.new("RGBA", src.size, (0, 0, 0, 0))
    op = out.load()
    col = GLOW_COLOR[kind]
    body = [(x + pad, y + pad) for (x, y), r in role.items() if r != "none"]
    for y in range(src.height):
        for x in range(src.width):
            if role.get((x - pad, y - pad)) in ("text", "fill", "shade"):
                continue
            a = min(1.0, near[x, y] / 255 * 1.2 + far[x, y] / 255 * 1.6)
            # ランプの外は距離で落とし、 GLOW_PAD の外周で 0 に近づける (四角く切れないように)
            if role.get((x - pad, y - pad), "none") == "none":
                dist = min(math.hypot(x - bx, y - by) for bx, by in body)
                a *= max(0.0, 1 - dist / (pad + 1))
            q = max(v for v in GLOW_STEPS if v <= a)    # 段に切り捨てる
            if q > 0:
                op[x, y] = col + (round(q * 255),)
    return out


PANEL = (42, 38, 32)   # 見本の板の色 (試作 HTML の .lamps と同じ)


def add(base, layer):
    """加算 (CSS の plus-lighter と同じ)。 layer の不透明度を掛けて足す。"""
    b, l = base.copy(), layer.load()
    bp = b.load()
    for y in range(b.height):
        for x in range(b.width):
            r, g, bl, a = l[x, y]
            if a:
                k = a / 255
                p = bp[x, y]
                bp[x, y] = (min(255, round(p[0] + r * k)), min(255, round(p[1] + g * k)), min(255, round(p[2] + bl * k)), 255)
    return b


def padded(im, size):
    """透明の余白を四方に足して size にする (消灯の書き出し用)。"""
    t = Image.new("RGBA", size, (0, 0, 0, 0))
    t.alpha_composite(im, (GLOW_PAD, GLOW_PAD))
    return t


def baked(on, gl):
    """光を焼き込んだ点灯画像。 ランプの上は加算 (枠が照らされる)、 ランプの外は光の色を不透明度付きで置く。"""
    t = padded(on, gl.size)
    tp, gp = t.load(), gl.load()
    for y in range(t.height):
        for x in range(t.width):
            r, g, b, a = gp[x, y]
            if not a:
                continue
            k = a / 255
            p = tp[x, y]
            if p[3]:
                tp[x, y] = (min(255, round(p[0] + r * k)), min(255, round(p[1] + g * k)), min(255, round(p[2] + b * k)), p[3])
            else:
                tp[x, y] = (r, g, b, a)
    return t


def lit_with_glow(on, gl):
    """見本用: 光の層 (加算) の上に点灯画像を重ねたもの。 大きさは光の層と同じ。"""
    t = Image.new("RGBA", gl.size, PANEL + (255,))
    t.alpha_composite(on, (GLOW_PAD, GLOW_PAD))
    return add(t, gl)


def main():
    os.makedirs(OUT, exist_ok=True)
    sheet = []
    for kind in ("half", "crit"):
        on, off, gl = lit(kind)
        on.save(os.path.join(OUT, f"{kind}_lamp_on.png"))
        off.save(os.path.join(OUT, f"{kind}_lamp_off.png"))
        gl.save(os.path.join(OUT, f"{kind}_lamp_glow.png"))
        # 書き出し用: 光まで焼き込んだ点灯と、 同じ大きさ (四方 GLOW_PAD) の消灯。 差し替えるだけで位置が揃う
        full_on, full_off = baked(on, gl), padded(off, gl.size)
        full_on.save(os.path.join(OUT, f"{kind}_lamp_on_full.png"))
        full_off.save(os.path.join(OUT, f"{kind}_lamp_off_full.png"))
        orig = Image.open(os.path.join(SRC, f"{kind}_lamp_on.png")).convert("RGBA")
        sheet.append((orig, lit_with_glow(on, gl), off))
    # 見本: 行 = half / crit、 列 = 元の点灯 / 加工した点灯 (光込み) / 消灯 (8 倍・板の色の上)
    S, G, pad = 8, 6, GLOW_PAD
    w, h = sheet[0][0].size
    cw, ch = (w + 2 * pad) * S, (h + 2 * pad) * S
    img = Image.new("RGBA", (3 * cw + 4 * G, 2 * ch + 3 * G), (20, 16, 12, 255))
    for r, row in enumerate(sheet):
        for c, im in enumerate(row):
            tile = Image.new("RGBA", (w + 2 * pad, h + 2 * pad), PANEL + (255,))
            if im.size == tile.size:
                tile = im          # 光込みの見本は板の上に合成済み
            else:
                tile.alpha_composite(im, (pad, pad))
            img.alpha_composite(tile.resize((cw, ch), Image.NEAREST), (G + c * (cw + G), G + r * (ch + G)))
    img.save(os.path.join(OUT, "preview.png"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
