#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""筐体の絵柄テスト: 絵画的ドット絵 (2026-09-27)。

参照した方向: 内側の画面は粗く単色寄り / 外側の機械は細かく情報量が多い。
**画布は 320×180 dot** で、 表示は ×3 (960×540) / ×6 (1920×1080)。
アイテムアイコンの 16×16 より細かい格子なので、 「機械のほうが現実側」という
粒度の差がそのまま出る。

ここで試すのは機械本体だけ (机・小物・枝の影は範囲外)。
"""
import math
import sys
from PIL import Image, ImageDraw

W, H = 320, 180
P = {
    "bg":       (26, 22, 18),
    "bg2":      (38, 31, 24),
    "body":     (58, 58, 46),
    "body_hi":  (92, 92, 74),
    "body_lo":  (34, 34, 27),
    "edge":     (16, 15, 12),
    "inset":    (44, 44, 35),
    "inset_lo": (26, 26, 21),
    "rivet":    (118, 116, 92),
    "rivet_lo": (30, 30, 24),
    "amber":    (232, 168, 62),
    "amber_hi": (255, 226, 150),
    "amber_lo": (128, 88, 32),
    "screen":   (30, 18, 10),
    "glow1":    (122, 52, 22),
    "glow2":    (196, 96, 30),
    "glow3":    (246, 160, 54),
    "sil":      (18, 12, 10),
    "gauge":    (226, 176, 70),
    "gauge_lo": (58, 44, 22),
}
img = Image.new("RGB", (W, H), P["bg"])
d = ImageDraw.Draw(img)


def px(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        img.putpixel((x, y), P[c] if isinstance(c, str) else c)


def rect(x, y, w, h, c):
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=P[c] if isinstance(c, str) else c)


def panel(x, y, w, h, base="body", hi="body_hi", lo="body_lo", r=2):
    """角を落とした金属パネル。 上辺に 1 dot の明、 下辺に 1 dot の暗。"""
    rect(x, y, w, h, base)
    for k in range(r):                      # 角落とし
        for i in range(r - k):
            for (cx, cy) in ((x + i, y + k), (x + w - 1 - i, y + k),
                             (x + i, y + h - 1 - k), (x + w - 1 - i, y + h - 1 - k)):
                px(cx, cy, "bg2")
    rect(x + r, y, w - 2 * r, 1, hi)
    rect(x, y + r, 1, h - 2 * r, hi)
    rect(x + r, y + h - 1, w - 2 * r, 1, lo)
    rect(x + w - 1, y + r, 1, h - 2 * r, lo)
    # 面の縦グラデーション (上が明るい) をディザで
    for j in range(y + 1, y + h - 1):
        t = (j - y) / h
        if t < 0.34:
            for i in range(x + 1, x + w - 1):
                if (i + j) % 2 == 0:
                    px(i, j, "body_hi" if t < 0.16 else "body")


def inset(x, y, w, h, fill="inset"):
    """落とし込み: 上/左が暗、 下/右が明。"""
    rect(x, y, w, h, fill)
    rect(x, y, w, 1, "edge"); rect(x, y, 1, h, "edge")
    rect(x, y + h - 1, w, 1, "body_hi"); rect(x + w - 1, y, 1, h, "body_hi")


def rivet(x, y):
    rect(x, y, 2, 2, "rivet_lo")
    px(x, y, "rivet")


# ── 背景: 机の面 (木目は範囲外なので暗い床だけ) ──────────────
for j in range(H):
    for i in range(W):
        t = 1 - ((i - W / 2) ** 2 / (W / 2) ** 2 + (j - H / 2) ** 2 / (H / 2) ** 2) * 0.55
        c = P["bg2"] if (i * 7 + j * 3) % 29 < 2 else P["bg"]
        img.putpixel((i, j), tuple(max(0, int(v * t)) for v in c))

# ── 機械本体 ────────────────────────────────────────────
panel(10, 8, 300, 150, r=3)
for i in range(16, 306, 14):          # 上下のリベット列
    rivet(i, 11); rivet(i, 153)

# ── 画面 (内側は粗く・単色寄り) ───────────────────────────
SX, SY, SW, SH = 36, 18, 156, 84
inset(SX - 4, SY - 4, SW + 8, SH + 8)
for i in range(SX - 4, SX + SW + 4, 10):     # ベゼルの電球列
    rect(i, SY - 7, 4, 3, "amber_lo")
    rect(i + 1, SY - 7, 2, 2, "amber_hi")
rect(SX, SY, SW, SH, "screen")
# 画面の中身: 2 dot 格子の粗い絵 (奥の光 → 手前の影)
for j in range(0, SH, 2):
    for i in range(0, SW, 2):
        cx, cy = i - SW * 0.5, j - SH * 0.62
        r = math.hypot(cx * 0.9, cy * 1.6)
        c = "glow3" if r < 16 else "glow2" if r < 30 else "glow1" if r < 52 else "screen"
        if c != "screen" and (i // 2 + j // 2) % 7 == 0:
            c = "glow2" if c == "glow3" else "glow1"
        rect(SX + i, SY + j, 2, 2, c)
for i in range(0, SW, 2):             # 地面の稜線
    h = 6 + int(4 * math.sin(i * 0.11)) + (3 if (i // 2) % 5 == 0 else 0)
    rect(SX + i, SY + SH - h, 2, h, "sil")
rect(SX + SW // 2 - 9, SY + 30, 18, 34, "sil")     # 手前の人影 (板状)
rect(SX + SW // 2 - 3, SY + 24, 6, 8, "sil")
rect(SX + 2, SY + 2, SW - 4, 1, (58, 30, 16))      # 画面のガラス反射
rect(SX + 2, SY + 3, 40, 1, (48, 26, 14))

# ── 右の表示パネル ──────────────────────────────────────
panel(200, 14, 108, 22, r=2)
rect(206, 21, 96, 1, "amber_lo")
for k, wdt in enumerate((7, 5, 9, 4, 7, 6, 8, 5, 7, 4, 6, 9)):   # 文字列に見せる帯
    rect(207 + sum((7, 5, 9, 4, 7, 6, 8, 5, 7, 4, 6, 9)[:k]) + k * 2, 18, wdt, 5, "amber")
inset(200, 40, 70, 34)
rect(204, 44, 62, 5, "amber_lo"); rect(204, 44, 34, 5, "amber")

# ── 縦ゲージ ────────────────────────────────────────────
inset(278, 40, 16, 62)
for s in range(19):
    y = 99 - s * 3
    rect(281, y, 10, 2, "gauge" if s < 13 else "gauge_lo")
    if s < 13:
        rect(281, y, 10, 1, "amber_hi")

# ── 横ゲージ ────────────────────────────────────────────
inset(60, 108, 180, 12)
rect(63, 111, 174, 6, "gauge_lo")
rect(63, 111, 118, 6, "gauge")
rect(63, 111, 118, 1, "amber_hi")
for i in range(63, 237, 6):
    rect(i, 111, 1, 6, "inset_lo")

# ── 装備スロット ×5 ─────────────────────────────────────
for n in range(5):
    x = 52 + n * 44
    panel(x, 124, 40, 28, r=2)
    inset(x + 4, 127, 32, 22, "inset_lo")
    for (cx, cy) in ((x + 5, 128), (x + 34, 128), (x + 5, 147), (x + 34, 147)):
        px(cx, cy, "amber_lo")          # 隅の刻み
    if n in (1, 2, 3):                   # 中身の入っているスロット
        rect(x + 14, 132, 12, 14, "body_lo")
        rect(x + 14, 132, 12, 1, "amber_lo")
        rect(x + 19, 134, 2, 10, "amber")

# ── 左右の空きパネル + 下部のボタン ───────────────────────
panel(14, 124, 32, 28, r=2)
panel(274, 124, 32, 28, r=2)
panel(112, 162, 96, 16, r=2)
rect(118, 167, 84, 1, "amber_lo")
for k, wdt in enumerate((9, 7, 5, 9)):
    rect(132 + k * 14, 165, wdt, 6, "amber_hi")

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "machine_test.png"
    scale = int(sys.argv[sys.argv.index("--scale") + 1]) if "--scale" in sys.argv else 3
    img.resize((W * scale, H * scale), Image.NEAREST).save(out)
    print(f"{out}  {W*scale}×{H*scale}  (dot {W}×{H} · ×{scale})")
