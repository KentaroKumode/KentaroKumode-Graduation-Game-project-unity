#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""筐体面の下絵 (2026-09-27 改訂 4: Slots and Daggers の作り方を分析して規則を移したもの)。

**寸法の正本は docs/design/cabinet-layout.html §2 のゾーン座標表。** ゾーンの位置と大きさは変えていない。
**ピクセル格子は 480×270 の 1 つだけ** (LCD と共通。 1 px = 4×4 画面 px)。

参照ゲームの資産を解析して得た**規則だけ**を使っている。 画像は 1 枚も持ち込んでいない
(抽出物はプロジェクトの外の一時フォルダで観察しただけ)。 得た規則:
  1. **土台の絵は平ら**。 階調で立体感を作らない。 陰影と暖色は実行時の光の仕事
     ── 参照の機械本体は 5 色 (縁取り / 影 / 本体 / 明るい面 / きらめき) しか使っていない
  2. **枠は 4 px の断面**: 外の縁取り 1 + 帯 2 (上左が明・下右が暗) + 内の縁取り 1。
     角は斜めに面取りし、 **長い辺にわざと 1 px の段差**を入れる (手作りの揺らぎ)
  3. **面は 28×28 のタイル**。 1 段だけ暗い色で破線の内枠と四隅の飾りを刻む
  4. きらめきは面取りした角に集中させ、 面積の 2% 程度に抑える
  5. 丸ネジと、 画面枠沿いのドーム状のリベット
色の値は同じ原理で自前に決めた (緑がかったオリーブ・低彩度・ほぼ黒の縁取り)。
"""
import base64
import io as _io
import os
from PIL import Image

GW, GH = 480, 270
S = 1                       # 格子 = ピクセル格子。 LCD と共通なので 1 から変えない
W, H = GW, GH

# ── 色: 機械本体の 5 色 + 面と背板と窪みの近似色 ─────────────
K = (24, 24, 21)            # 縁取り
D = (70, 76, 60)            # 影
M = (94, 99, 84)            # 本体
L = (110, 116, 98)          # 明るい面
HI = (160, 164, 140)        # きらめき (面積 2% 程度)
SURF = ((88, 93, 80), (80, 85, 73), (97, 102, 88))      # 部品の面: 地 / 刻み / 点
BASE = ((64, 69, 58), (57, 62, 52), (72, 77, 65))       # 背板: 地 / 刻み / 点
REC = ((22, 23, 20), (31, 33, 28), (43, 46, 39))        # 窪み: 最暗 / 底 / 縁
C = {
    "amber": (236, 170, 66), "amber_hi": (255, 230, 158), "amber_lo": (122, 84, 30),
    "ghost": (46, 42, 30),
    "nixie": (255, 142, 44), "nixie_hi": (255, 214, 150), "nixie_glow": (86, 40, 14),
    "foe": (178, 54, 42), "foe_hi": (232, 108, 84),
    "hp": (212, 82, 50), "hp_hi": (255, 150, 104),
    "shield": (104, 154, 188), "shield_hi": (174, 212, 238),
    "hope": (226, 194, 104), "hope_hi": (255, 236, 172),
    "lcd": (12, 14, 12), "lcd_hi": (30, 36, 32),
    "label": (176, 170, 140), "label_lo": (132, 126, 100), "spine": (150, 62, 48),
}
IVORY = ((96, 90, 72), (140, 134, 110), (180, 174, 148), (214, 208, 184), (236, 232, 214))
BRASS = ((52, 44, 26), (88, 74, 40), (126, 106, 60), (164, 140, 84), (204, 180, 118))

img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
PX = img.load()
BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def inb(x, y): return 0 <= x < W and 0 <= y < H
def put(x, y, c):
    if inb(x, y): PX[x, y] = (*c, 255) if len(c) == 3 else c
def fill(x, y, w, h, c):
    for j in range(y, y + h):
        for i in range(x, x + w): put(i, j, c)
def hline(x, y, w, c): fill(x, y, w, 1, c)
def vline(x, y, h, c): fill(x, y, 1, h, c)
def dith(i, j, t): return t > (BAYER[j % 4][i % 4] + 0.5) / 16.0
def mul(x, y, f):
    if inb(x, y):
        c = PX[x, y]
        if c[3]: PX[x, y] = (int(c[0] * f), int(c[1] * f), int(c[2] * f), c[3])


def rnd(seed):
    s = (seed * 2654435761 + 97) % 2147483647
    while True:
        s = (s * 1103515245 + 12345) % 2147483648
        yield s


# ── 面のタイル (規則 3) ────────────────────────────────────
def tile(x, y, w, h, pal=SURF, cell=28, seed=1, specks=1.0):
    """平らな地に、 1 段暗い色で破線の内枠と四隅の飾りを刻む。 28×28 ごとに繰り返す。"""
    base, cut, dot = pal
    fill(x, y, w, h, base)
    for ty in range(y, y + h, cell):
        for tx in range(x, x + w, cell):
            cw, chh = min(cell, x + w - tx), min(cell, y + h - ty)
            if cw < 12 or chh < 12:
                continue
            # 破線の内枠 (2 px 内側・角は飾りに譲る)
            for i in range(tx + 7, tx + cw - 7, 2):
                put(i, ty + 2, cut); put(i, ty + chh - 3, cut)
            for j in range(ty + 7, ty + chh - 7, 2):
                put(tx + 2, j, cut); put(tx + cw - 3, j, cut)
            # 四隅の飾り: 二重の L 字 + 点
            for (cx, cy, sx, sy) in ((tx + 2, ty + 2, 1, 1), (tx + cw - 3, ty + 2, -1, 1),
                                     (tx + 2, ty + chh - 3, 1, -1), (tx + cw - 3, ty + chh - 3, -1, -1)):
                for k in range(4):
                    put(cx + sx * k, cy, cut); put(cx, cy + sy * k, cut)
                put(cx + sx * 2, cy + sy * 2, cut); put(cx + sx * 3, cy + sy * 2, cut)
                put(cx + sx * 2, cy + sy * 3, cut)
    r = rnd(seed)
    for _ in range(int(w * h / 110 * specks)):              # まばらな点 (摩耗)
        put(x + next(r) % w, y + next(r) % h, dot if next(r) % 3 else cut)


# ── 枠 (規則 2・4) ─────────────────────────────────────────
def frame(x, y, w, h, seed=0, ch=2, notch=True, inner=False):
    """4 px 断面の枠。 外の縁取り → 帯 2 (上左が明・下右が暗) → [内の縁取り]。
    角は斜めに面取り、 長い辺に 1 px の段差、 きらめきは面取りした角に集める。"""
    keep = {}
    for (cx, cy, sx, sy) in ((x, y, 1, 1), (x + w - 1, y, -1, 1), (x, y + h - 1, 1, -1), (x + w - 1, y + h - 1, -1, -1)):
        for i in range(ch):
            for j in range(ch - i):
                if inb(cx + sx * i, cy + sy * j): keep[(cx + sx * i, cy + sy * j)] = PX[cx + sx * i, cy + sy * j]
    rings = [(K, K), (L, M), (M, D)] + ([(K, K)] if inner else [])
    for k, (tl, br) in enumerate(rings):
        hline(x + k, y + k, w - 2 * k, tl); vline(x + k, y + k, h - 2 * k, tl)
        hline(x + k, y + h - 1 - k, w - 2 * k, br); vline(x + w - 1 - k, y + k, h - 2 * k, br)
    for p, c in keep.items(): PX[p] = c                      # 面取り: 角を下地へ戻す
    for (cx, cy, sx, sy) in ((x, y, 1, 1), (x + w - 1, y, -1, 1), (x, y + h - 1, 1, -1), (x + w - 1, y + h - 1, -1, -1)):
        for i in range(ch + 1):
            put(cx + sx * i, cy + sy * (ch - i), K)
    # きらめき: 左上と右下の面取りに集める (参照の Frame A と同じ置き方)
    put(x + ch, y + 1, HI); put(x + 1, y + ch, HI); put(x + ch + 1, y + 1, L)
    put(x + w - 1 - ch, y + h - 2, HI); put(x + w - 2, y + h - 1 - ch, HI)
    r = rnd(seed or x * 31 + y * 17)
    for _ in range(max(1, w // 14)):                         # 帯の上のまばらなきらめき
        put(x + 3 + next(r) % max(1, w - 6), y + 1, HI)
    for _ in range(max(1, h // 14)):
        put(x + 1, y + 3 + next(r) % max(1, h - 6), HI)
    # 段差 (規則 2): 右辺か下辺の長い方に、 外形が 1 px 内へ入る区間を作る
    if notch and max(w, h) >= 30:
        if h >= w:
            j0 = y + h // 3 + next(r) % max(1, h // 4); ln = 6 + next(r) % 10
            for j in range(j0, min(j0 + ln, y + h - ch - 2)):
                put(x + w - 1, j, D); put(x + w - 2, j, K)
        else:
            i0 = x + w // 3 + next(r) % max(1, w // 4); ln = 8 + next(r) % 12
            for i in range(i0, min(i0 + ln, x + w - ch - 2)):
                put(i, y + h - 1, D); put(i, y + h - 2, K)


def shadow(x, y, w, h, d=2):
    """落とし影 (右と下)。 **ライトの無い Built-in RP なので影だけは焼き込む**。"""
    for k in range(1, d + 1):
        f = 0.45 if k == 1 else 0.68
        for i in range(x + k, x + w + k):
            if k == 1 or dith(i, y + h - 1 + k, 0.5): mul(i, y + h - 1 + k, f)
        for j in range(y + k, y + h - 1 + k):
            if k == 1 or dith(x + w - 1 + k, j, 0.5): mul(x + w - 1 + k, j, f)


def module(x, y, w, h, seed=0, d=2):
    """部品の囲い = 影 + 面のタイル + 枠。"""
    if d: shadow(x, y, w, h, d)
    tile(x + 3, y + 3, w - 6, h - 6, SURF, seed=seed or x + y)
    frame(x, y, w, h, seed=seed)


def recess(x, y, w, h, dashed=False, brackets=False):
    """窪み: 縁取り 1 + 底。 上左の内側に影、 下右の縁が光る。"""
    fill(x, y, w, h, REC[1])
    hline(x, y, w, K); vline(x, y, h, K)
    hline(x + 1, y + 1, w - 2, REC[0]); vline(x + 1, y + 1, h - 2, REC[0])
    hline(x + 1, y + h - 1, w - 1, D); vline(x + w - 1, y + 1, h - 1, D)
    if dashed and w >= 14 and h >= 14:
        for i in range(x + 5, x + w - 5, 2):
            put(i, y + 3, REC[2]); put(i, y + h - 4, REC[2])
        for j in range(y + 5, y + h - 5, 2):
            put(x + 3, j, REC[2]); put(x + w - 4, j, REC[2])
    if brackets and w >= 12 and h >= 12:
        for (cx, cy, sx, sy) in ((x + 3, y + 3, 1, 1), (x + w - 4, y + 3, -1, 1),
                                 (x + 3, y + h - 4, 1, -1), (x + w - 4, y + h - 4, -1, -1)):
            for k in range(3):
                put(cx + sx * k, cy, REC[2]); put(cx, cy + sy * k, REC[2])


def screw(x, y):
    """丸ネジ 5×5: 縁取り + 本体 + 斜めの溝 (溝がきらめきを兼ねる)。"""
    pat = [".KKK.", "KLLMK", "KLHDK", "KMDDK", ".KKK."]
    col = {"K": K, "L": L, "M": M, "D": D, "H": HI}
    for j, row in enumerate(pat):
        for i, v in enumerate(row):
            if v != ".": put(x + i, y + j, col[v])


def dome(x, y):
    """ドーム状のリベット 4×4 (画面枠沿い)。"""
    pat = [".KK.", "KHLK", "KMDK", ".KK."]
    col = {"K": K, "L": L, "M": M, "D": D, "H": HI}
    for j, row in enumerate(pat):
        for i, v in enumerate(row):
            if v != ".": put(x + i, y + j, col[v])


def rivet(x, y):
    """リベット 2×2 (仕様 ㉒)。"""
    put(x, y, HI); put(x + 1, y, M); put(x, y + 1, M); put(x + 1, y + 1, K)


# ── 数字 (ニキシーと 7 セグ) ─────────────────────────────────
NIXIE = {
    "0": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "1": ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "2": [".###.", "#...#", "....#", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
    "3": [".###.", "#...#", "....#", "....#", "..##.", "....#", "....#", "#...#", ".###."],
    "4": ["...#.", "..##.", ".#.#.", "#..#.", "#..#.", "#####", "...#.", "...#.", "...#."],
    "5": ["#####", "#....", "#....", "####.", "....#", "....#", "....#", "#...#", ".###."],
    "6": [".###.", "#....", "#....", "####.", "#...#", "#...#", "#...#", "#...#", ".###."],
    "7": ["#####", "....#", "...#.", "...#.", "..#..", "..#..", ".#...", ".#...", ".#..."],
    "8": [".###.", "#...#", "#...#", "#...#", ".###.", "#...#", "#...#", "#...#", ".###."],
    "9": [".###.", "#...#", "#...#", "#...#", ".####", "....#", "....#", "....#", ".###."],
}
SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc",
       "5": "afgcd", "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcdfg"}


def seg7(x, y, ch):
    on = SEG[ch]
    parts = {"a": [(x + 2 + i, y + 1) for i in range(4)],
             "f": [(x + 1, y + 2 + i) for i in range(3)], "b": [(x + 6, y + 2 + i) for i in range(3)],
             "g": [(x + 2 + i, y + 5) for i in range(4)],
             "e": [(x + 1, y + 6 + i) for i in range(4)], "c": [(x + 6, y + 6 + i) for i in range(4)],
             "d": [(x + 2 + i, y + 10) for i in range(4)]}
    for k, pts in parts.items():
        for (i, j) in pts: put(i, j, C["amber"] if k in on else C["ghost"])


def tube(x, y, ch):
    fill(x, y, 10, 16, REC[1])
    for j in range(y + 2, y + 15):                          # 網状の陽極
        for i in range(x + 1, x + 9):
            if (i + j) % 3 == 0: put(i, j, REC[2])
    for (i, j) in ((x, y), (x + 9, y), (x, y + 1), (x + 9, y + 1)): put(i, j, K)
    vline(x + 1, y + 2, 12, (44, 48, 42))
    gx, gy, glyph = x + 3, y + 4, NIXIE[ch]
    for j, row in enumerate(glyph):
        for i, v in enumerate(row):
            if v != "#": continue
            for (di, dj) in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                cx, cy = gx + i + di, gy + j + dj
                if 0 <= cy - gy < 9 and 0 <= cx - gx < 5 and glyph[cy - gy][cx - gx] == "#": continue
                if x < cx < x + 9 and y < cy < y + 15: put(cx, cy, C["nixie_glow"])
    for j, row in enumerate(glyph):
        for i, v in enumerate(row):
            if v == "#": put(gx + i, gy + j, C["nixie"])
    put(gx + 1, gy, C["nixie_hi"])


PIPS = {1: [(1, 1)], 2: [(0, 0), (2, 2)], 3: [(0, 0), (1, 1), (2, 2)],
        4: [(0, 0), (0, 2), (2, 0), (2, 2)], 5: [(0, 0), (0, 2), (1, 1), (2, 0), (2, 2)],
        6: [(0, 0), (1, 0), (2, 0), (0, 2), (1, 2), (2, 2)]}


def led(x, y, lit):
    if lit:
        fill(x, y, 4, 4, C["amber"]); hline(x, y, 4, C["amber_hi"]); put(x + 3, y + 3, C["amber_lo"])
    else:
        fill(x, y, 4, 4, REC[2]); put(x, y, (52, 56, 47)); put(x + 3, y + 3, REC[0])


# ════════════════════════════════════════════════════════════
#  ①筐体面 (10,5 / 460×260) ── 外殻 + 背板
# ════════════════════════════════════════════════════════════
for k, a in enumerate((150, 118, 86, 56, 28)):          # 卓への落ち影 (筐体の外・半透明)
    for j in range(5 + k + 2, 265 + k + 1):
        if k < 2 or dith(470 + k, j, 0.6): put(470 + k, j, (0, 0, 0, a))
    for i in range(10 + k + 2, 470 + k + 1):
        if k < 2 or dith(i, 265 + k, 0.6): put(i, 265 + k, (0, 0, 0, a))

tile(13, 8, 454, 222, BASE, seed=5, specks=0.8)          # 背板 (部品より一段暗い面)
frame(10, 5, 460, 260, seed=3, ch=3, notch=False)       # 外殻 (背板を囲む枠)
for (sx_, sy_) in ((15, 10), (460, 10), (15, 222), (460, 222), (99, 10), (370, 10), (99, 222)):
    screw(sx_, sy_)

# 空き面: 点検蓋 (右の帯 / 下段トレイとボタン台の間) と通気スリット
recess(432, 34, 30, 104); tile(433, 35, 28, 102, SURF, seed=41)
frame(432, 34, 30, 104, seed=41, notch=False)
for (sx_, sy_) in ((436, 38), (453, 38), (436, 130), (453, 130)): screw(sx_, sy_)
for k in range(9):
    hline(436, 160 + k * 6, 23, K); hline(436, 161 + k * 6, 23, BASE[2])
for k in range(8):
    hline(22, 188 + k * 5, 80, K); hline(22, 189 + k * 5, 80, BASE[2])
shadow(338, 180, 40, 40, 1); tile(341, 183, 34, 34, SURF, seed=43); frame(338, 180, 40, 40, seed=43, notch=False)
for (sx_, sy_) in ((342, 184), (369, 184), (342, 211), (369, 211)): screw(sx_, sy_)

# ── ㉒下部帯 (10,230 / 460×35) ── 手前の縁 ──────────────────
tile(13, 233, 454, 29, SURF, seed=17, specks=0.6)
frame(10, 230, 460, 35, seed=19, ch=3, notch=False)
for i in range(28, 466, 36):                               # リベット 2×2 (36 間隔)
    rivet(i, 234); rivet(i, 259)
for k in range(3):                                         # 通気 3 本 (40×1・間 2)
    hline(220, 242 + k * 3, 40, K); hline(220, 243 + k * 3, 40, L)

# ── ②GUI レール収納スリット (175,6 / 130×4) + 取っ手 15×2 (232,7) ──
recess(175, 6, 130, 4)
hline(232, 7, 15, HI); hline(232, 8, 15, M); put(246, 8, D)

# ════════════════════════════════════════════════════════════
#  画面の囲い (⑥⑦ + ③ + ④)
# ════════════════════════════════════════════════════════════
module(113, 9, 255, 163, seed=11, d=3)
for j in range(26, 160, 9):                                # 枠沿いのドームリベット列
    dome(115, j); dome(360, j)

# ③敵 HP 横バー (124,12 / 231×9) 内 229×7 / 46 セグ (4+1)・左から消灯
recess(124, 12, 231, 9)
for s in range(46):
    x = 125 + s * 5
    if s < 34: fill(x, 13, 4, 7, C["foe"]); hline(x, 13, 4, C["foe_hi"])
    else:      fill(x, 13, 4, 7, REC[2]); put(x, 13, C["ghost"])

# ④処理中ランプ (361,13 / 5×5・角1落とし)
fill(362, 13, 3, 5, C["amber"]); fill(361, 14, 5, 3, C["amber"])
put(362, 14, C["amber_hi"]); put(363, 14, C["amber_hi"]); put(364, 16, C["amber_lo"])
for (i, j) in ((361, 13), (365, 13), (361, 17), (365, 17)): put(i, j, K)

# ⑥LCD ベゼル (120,21 / 239×147・枠厚 4) = 4 px 断面の枠そのもの / ⑦スクリーン (124,25 / 231×139)
frame(120, 21, 239, 147, seed=13, ch=2, notch=False, inner=True)
fill(124, 25, 231, 139, C["lcd"])
hline(124, 25, 231, REC[0]); vline(124, 25, 139, REC[0])
for k in range(34):                                        # ガラスの光条 (斜めに 2 本)
    put(132 + k, 62 - k, C["lcd_hi"])
    if k < 30: put(134 + k, 66 - k, C["lcd_hi"])

# ════════════════════════════════════════════════════════════
#  左の計器 (⑧⑨⑩)
# ════════════════════════════════════════════════════════════
module(15, 24, 84, 110, seed=21)
screw(84, 29); screw(84, 122); screw(76, 60)
recess(20, 28, 50, 18)                                     # ⑧ニキシー: ゴールド
for k, ch in enumerate("0250"): tube(22 + k * 12, 29, ch)
recess(20, 53, 26, 18)                                     # ⑨ニキシー: 素材
for k, ch in enumerate("07"): tube(22 + k * 12, 54, ch)
# ⑩スピーカーグリル (20,80 / 74×50) 孔4×4・ピッチ12・6×4 @ (25,85)
recess(20, 80, 74, 50)
fill(22, 82, 71, 47, D)
for r in range(4):
    for c in range(6):
        hx, hy = 25 + c * 12, 85 + r * 12
        fill(hx, hy, 4, 4, K)
        put(hx, hy, D); put(hx + 3, hy, D); put(hx, hy + 3, M); put(hx + 3, hy + 3, M)
        hline(hx + 1, hy + 4, 2, L)

# ════════════════════════════════════════════════════════════
#  左下のスロット台 (⑪⑫)
# ════════════════════════════════════════════════════════════
module(15, 145, 97, 32, seed=31)
screw(76, 150); screw(76, 167)
# ⑪武器カセットスロット (20,150 / 55×22) カセット38×15 @ (28,153)
recess(20, 150, 55, 22, dashed=False, brackets=True)
shadow(28, 153, 38, 15, 1)
fill(28, 153, 38, 15, M); frame(28, 153, 38, 15, seed=33, ch=1, notch=False)
fill(29, 154, 3, 13, C["spine"]); hline(29, 154, 3, (206, 110, 90))      # 背表紙 = 武器種
for n in range(3):                                          # 縦ノッチ 3×8 ×Tier
    fill(36 + n * 6, 156, 3, 8, K); hline(36 + n * 6, 163, 3, L)
fill(54, 156, 9, 9, C["label"]); hline(54, 156, 9, (200, 194, 164))
hline(55, 159, 6, C["label_lo"]); hline(55, 161, 4, C["label_lo"])
# ⑫装備ダイス窪み (83,149 / 24×24) ダイス17×17 @ (86,152)
recess(83, 149, 24, 24)
shadow(86, 152, 17, 17, 1)
fill(86, 152, 17, 17, IVORY[3]); hline(86, 152, 17, IVORY[4]); vline(86, 152, 17, IVORY[4])
hline(86, 168, 17, IVORY[1]); vline(102, 152, 17, IVORY[2])
for (r, c) in PIPS[5]:
    px_, py_ = 86 + 3 + c * 4, 152 + 3 + r * 4
    fill(px_, py_, 3, 3, (56, 50, 40)); put(px_, py_, (34, 30, 24)); hline(px_, py_ + 3, 3, IVORY[4])

# ════════════════════════════════════════════════════════════
#  右のゲージ柱 (⑬⑭⑮) と ⑤ターンランプ
# ════════════════════════════════════════════════════════════
module(385, 26, 43, 140, seed=51)


def gauge(gx, gw, inner, c, hi, n):
    recess(gx, 30, gw, 132)
    for s in range(26):
        y = 157 - s * 5
        if s < n: fill(gx + 1, y, inner, 4, c); hline(gx + 1, y, inner, hi)
        else:     fill(gx + 1, y, inner, 4, REC[2]); put(gx + 1, y, C["ghost"])


gauge(390, 11, 9, C["hp"], C["hp_hi"], 19)
gauge(405, 6, 4, C["shield"], C["shield_hi"], 7)
gauge(415, 8, 6, C["hope"], C["hope_hi"], 14)

# ⑤ターンランプ (406,8 / 18×18) リング1 + ドーム10×10 角1落とし
shadow(406, 8, 18, 18, 2)
fill(406, 8, 18, 18, M); frame(406, 8, 18, 18, seed=55, ch=2, notch=False)
recess(409, 11, 12, 12)
fill(411, 12, 8, 10, C["amber"]); fill(410, 13, 10, 8, C["amber"])
fill(412, 13, 3, 2, C["amber_hi"]); put(411, 14, C["amber_hi"])
hline(412, 21, 6, C["amber_lo"]); vline(419, 15, 5, C["amber_lo"])

# ════════════════════════════════════════════════════════════
#  下段のトレイ (⑯⑰⑱⑲⑳) ── 画面の囲いと下部帯に重なる手前の部品
# ════════════════════════════════════════════════════════════
module(150, 168, 180, 66, seed=61, d=3)
for (sx_, sy_) in ((154, 172), (321, 172), (154, 226), (321, 226)): screw(sx_, sy_)

TERM = (156, 200, 244)
for n, tx in enumerate(TERM):                              # ⑯配線端子 ×3
    shadow(tx, 172, 35, 20, 1)
    tile(tx + 3, 175, 29, 14, SURF, seed=70 + n, specks=0.4)
    frame(tx, 172, 35, 20, seed=70 + n, ch=1, notch=False)
    for k, cx in enumerate((8, 19)):
        recess(tx + cx, 176, 8, 12)
        seg7(tx + cx, 176, ("12", "07", "33")[n][k])
shadow(288, 172, 35, 20, 1)                                # ⑰予備端子枠 (面一の蓋)
tile(291, 175, 29, 14, SURF, seed=74, specks=0.4)
frame(288, 172, 35, 20, seed=74, ch=1, notch=False)
for i in range(292, 319, 2): put(i, 176, SURF[1]); put(i, 187, SURF[1])
for j in range(176, 188, 2): put(292, j, SURF[1]); put(318, j, SURF[1])
screw(303, 179)

# ⑱経路 LED バスレーン (156,192 / 167×9)  §5: ソケット上辺中央 → 縦 → 横 → 端子下辺 の L 字 ×15
recess(156, 192, 167, 9)
SOCK = (162, 194, 226, 258, 290)
ROWS = (194, 196, 198)
sock_x = [sx + 14 for sx in SOCK]
term_x = [tx + 17 for tx in TERM]
for r, ty in enumerate(ROWS):
    x0 = min(sock_x + [term_x[r]]); x1 = max(sock_x + [term_x[r]])
    hline(x0, ty, x1 - x0 + 1, REC[0])
for x in sock_x: vline(x, ROWS[0], 199 - ROWS[0] + 1, REC[0])
for r, x in enumerate(term_x): vline(x, 193, ROWS[r] - 193 + 1, REC[0])
lit_s, lit_t = 1, 0                                        # 点灯中の経路: ダイス 2 → 端子 1
for y in range(ROWS[lit_t], 200): put(sock_x[lit_s], y, C["amber"])
for x in range(term_x[lit_t], sock_x[lit_s] + 1): put(x, ROWS[lit_t], C["amber"])
put(term_x[lit_t], 193, C["amber_hi"])

FACES = (5, 3, 6, 2, 4)                                    # ⑲ソケット ×5 / ⑳リロール小ボタン ×5
for n, sx in enumerate(SOCK):
    recess(sx, 202, 28, 28, dashed=True, brackets=True)
    pips = set(PIPS[FACES[n]])
    for r in range(3):
        for c in range(3):
            led(sx + 5 + c * 7, 202 + 5 + r * 7, (r, c) in pips)
    shadow(sx + 23, 199, 6, 6, 1)
    fill(sx + 23, 199, 6, 6, M); frame(sx + 23, 199, 6, 6, seed=80 + n, ch=1, notch=False)
    fill(sx + 25, 201, 2, 2, BRASS[4])

# ════════════════════════════════════════════════════════════
#  ㉑ボタン台 (394,202 / 28×28) ボタン24×24 @ (396,204)・角2落とし
# ════════════════════════════════════════════════════════════
module(389, 197, 38, 37, seed=91, d=3)
recess(394, 202, 28, 28)
shadow(396, 204, 24, 24, 1)
fill(396, 204, 24, 24, BRASS[2]); frame(396, 204, 24, 24, seed=93, ch=2, notch=False)
fill(399, 207, 18, 18, BRASS[3])
hline(399, 207, 18, BRASS[4]); vline(399, 207, 18, BRASS[4])
fill(403, 211, 10, 10, BRASS[1]); hline(403, 220, 10, BRASS[3])      # 中央の凹み


# ============================================================
#  確認用 HTML (ゾーン枠は §2 の表から引く)
# ============================================================
ZONES = [
    (1, "筐体面", 10, 5, 460, 260), (2, "GUIレール収納スリット", 175, 6, 130, 4),
    (3, "敵HP横バー", 124, 12, 231, 9), (4, "処理中ランプ", 361, 13, 5, 5),
    (5, "ターンランプ", 406, 8, 18, 18), (6, "LCDベゼル", 120, 21, 239, 147),
    (7, "LCDスクリーン", 124, 25, 231, 139), (8, "ニキシー: ゴールド", 20, 28, 50, 18),
    (9, "ニキシー: 素材", 20, 53, 26, 18), (10, "スピーカーグリル", 20, 80, 74, 50),
    (11, "武器カセットスロット", 20, 150, 55, 22), (12, "装備ダイス窪み", 83, 149, 24, 24),
    (13, "自HPバー", 390, 30, 11, 132), (14, "シールドバー", 405, 30, 6, 132),
    (15, "希望ゲージ", 415, 30, 8, 132),
    (16, "配線端子1", 156, 172, 35, 20), (16, "配線端子2", 200, 172, 35, 20),
    (16, "配線端子3", 244, 172, 35, 20), (17, "予備端子枠", 288, 172, 35, 20),
    (18, "経路LEDバスレーン", 156, 192, 167, 9),
    (19, "ダイスソケット1", 162, 202, 28, 28), (19, "ダイスソケット2", 194, 202, 28, 28),
    (19, "ダイスソケット3", 226, 202, 28, 28), (19, "ダイスソケット4", 258, 202, 28, 28),
    (19, "ダイスソケット5", 290, 202, 28, 28),
    (21, "ボタン井戸", 394, 202, 28, 28), (22, "下部帯", 10, 230, 460, 35),
]

HTML = """<!doctype html>
<html lang="ja">
<head>
<meta charset="utf-8">
<title>DICE BOUND — 筐体面 下絵</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
  :root { --bg:#f8f7f2; --ink:#1a1a1a; --ink-2:#4a4a4a; --ink-3:#7a7a7a;
          --line:#d4d1c8; --grid:#e8e5db; --panel:#fff; --accent:#a15a2a; --dim:#2b6cb0; }
  @media (prefers-color-scheme: dark) {
    :root { --bg:#14161a; --ink:#e8e6e0; --ink-2:#b5b3ad; --ink-3:#7d7a72;
            --line:#333740; --grid:#262a32; --panel:#1c1f24; --accent:#e2c58a; --dim:#7ea1d1; } }
  * { box-sizing: border-box; }
  html, body { margin:0; padding:0; background:var(--bg); color:var(--ink); }
  body { font-family:"SF Mono","Consolas","Hiragino Kaku Gothic ProN","Yu Gothic",monospace; font-size:13px; line-height:1.6; }
  .doc { max-width:1220px; margin:0 auto; padding:32px 24px 80px; }
  header { border-bottom:2px solid var(--ink); padding-bottom:16px; margin-bottom:28px; }
  h1 { font-size:22px; margin:0 0 4px; }
  header .sub { color:var(--ink-3); font-size:12px; letter-spacing:.06em; }
  h2 { font-size:15px; margin:36px 0 12px; padding-bottom:4px; border-bottom:1px solid var(--line);
       color:var(--accent); display:flex; gap:8px; align-items:baseline; }
  h2 .num { background:var(--accent); color:var(--bg); padding:2px 8px; font-size:11px; }
  code { background:var(--panel); border:1px solid var(--line); padding:1px 5px; font-size:12px; }
  .card { background:var(--panel); border:1px solid var(--line); padding:14px; border-radius:4px; margin:12px 0; }
  .warn { background:var(--panel); border-left:3px solid var(--accent); padding:10px 14px; margin:12px 0; font-size:12px; }
  .caption { text-align:center; color:var(--ink-3); font-size:11px; margin-top:8px; }
  .bar { display:flex; flex-wrap:wrap; gap:16px; align-items:center; margin:12px 0 8px; }
  .bar label { display:inline-flex; gap:5px; align-items:center; color:var(--ink-2); cursor:pointer; }
  button { font:inherit; background:var(--panel); color:var(--ink); border:1px solid var(--line);
           padding:3px 11px; border-radius:3px; cursor:pointer; }
  button[aria-pressed="true"] { background:var(--accent); color:var(--bg); border-color:var(--accent); }
  .stage { overflow:auto; border:1px solid var(--line); padding:16px; background:var(--grid); }
  .frame { position:relative; margin:0 auto; image-rendering:pixelated; }
  .frame img { display:block; width:100%; height:100%; image-rendering:pixelated; }
  .frame.checker { background-image:
      linear-gradient(45deg,#999 25%,transparent 25%), linear-gradient(-45deg,#999 25%,transparent 25%),
      linear-gradient(45deg,transparent 75%,#999 75%), linear-gradient(-45deg,transparent 75%,#999 75%);
      background-size:16px 16px; background-position:0 0,0 8px,8px -8px,-8px 0; }
  .frame.dark { background:#0b0c0e; } .frame.light { background:#dcd8cf; }
  .zone { position:absolute; outline:1px solid var(--dim); outline-offset:-1px; }
  .zone b { position:absolute; top:0; left:0; background:var(--dim); color:#fff;
            font-size:9px; line-height:1.2; padding:0 2px; font-weight:400; white-space:nowrap; }
  .sw { display:inline-block; width:13px; height:13px; border:1px solid var(--line); vertical-align:-2px; margin-right:5px; }
  .pal { display:grid; grid-template-columns:repeat(auto-fill,minmax(190px,1fr)); gap:4px 16px; font-size:12px; }
  table { border-collapse:collapse; font-size:12px; margin:8px 0; }
  th,td { border:1px solid var(--line); padding:4px 9px; text-align:left; }
</style>
</head>
<body>
<div class="doc">
<header>
  <h1>DICE BOUND — 筐体面 下絵</h1>
  <div class="sub">配置の格子 480×270 dot · 絵の解像度 __RES__ (格子 ×__S__) · 生成元 <code>Tools/cabinet_art.py</code></div>
</header>

<div class="warn">
  <b>これは下絵 (改訂 4)。</b> 仕上げは人が上から描く前提。 <b>寸法の正本は
  <a href="cabinet-layout.html">cabinet-layout.html</a> §2</b> で、 ゾーンの位置と大きさは変えていない。<br>
  <b>Slots and Daggers の作り方を分析して、 規則だけを移した</b> (画像は 1 枚も持ち込んでいない):
  土台は平ら・機械本体は 5 色 / 枠は 4 px 断面 (縁取り 1 + 帯 2 + 内の縁取り 1)・角の面取り・長い辺の 1 px の段差 /
  面は 28×28 のタイルに 1 段暗い色で飾りを刻む / きらめきは面取りした角に集める。
  <b>暖色と陰影は土台に入れず、 光の層で乗せる</b> (参照は URP の Light2D、 うちは Built-in RP なのでスプライトの重ね合わせで代替)。<br>
  この頁は <code>python Tools/cabinet_art.py</code> で上書きされる。
</div>

<h2><span class="num">§1</span> 表示</h2>
<div class="bar">
  <span style="color:var(--ink-3)">倍率</span><span id="zoombar"></span>
  <label><input type="checkbox" id="zones"> ゾーン枠を重ねる</label>
  <label><input type="checkbox" id="lit"> 光の層 (参考)</label>
  <span style="color:var(--ink-3);margin-left:8px">背景</span>
  <button data-bg="dark" aria-pressed="true">暗</button>
  <button data-bg="checker" aria-pressed="false">透過</button>
  <button data-bg="light" aria-pressed="false">明</button>
</div>
<div class="stage"><div class="frame dark" id="frame"><img src="__SRC__" alt="筐体面 下絵"><img id="litimg" src="__LIT__" alt="光の層を重ねた参考" hidden style="position:absolute;inset:0;image-rendering:auto"><div id="ov"></div></div></div>
<div class="caption" id="cap"></div>

<h2><span class="num">§2</span> ピクセル格子</h2>
<div class="card">
  <b>画面全体でピクセルの大きさは 1 種類。</b> 確定寸法の 480×270 dot がそのままピクセル格子で、
  LCD の中身も同じ格子に乗る。
  <table>
    <tr><th></th><th>格子</th><th>1 px の大きさ (1920×1080 時)</th></tr>
    <tr><td>筐体面 (この下絵)</td><td>480×270</td><td>4×4 画面 px</td></tr>
    <tr><td>LCD の中身</td><td>231×139 (ゾーン 7)</td><td>4×4 画面 px</td></tr>
  </table>
  LCD の RT は <code>Assets/Settings/LcdProfile.asset</code> が 924×556 のままなので、
  <b>231×139 へ変更して ×4 表示する</b>必要がある (924÷4 = 231, 556÷4 = 139)。<br>
  粒度の差は<b>ピクセルの大きさではなく密度</b>で出す ── 筐体は同じ格子に細部を詰め、
  画面の中身は大きな塊と少ない色数で描く。
</div>

<h2><span class="num">§3</span> いま描かれている状態</h2>
<div class="card">
  待機中。 敵 HP 46 セグ中 34 / 自 HP 19 / シールド 7 / 希望 14 / LCD 消灯 /
  ゴールド 0250・素材 07 (ニキシー) / 端子 12・07・33 / ダイス 5・3・6・2・4 /
  経路はダイス 2 → 端子 1 が流動点灯の途中 (§5 の L 字経路、 15 本すべて彫ってある)。<br>
  <b>筐体感の作り方</b>: 一枚板にゾーンを彫るのではなく、 <b>独立した部品を背板に組み付ける</b>。
  部品は厚い枠を持ち、 右下へ影を落とし、 手前の部品 (下段トレイ・ボタン台) が奥の部品
  (画面の囲い・下部帯) に重なる。 光源は左上に固定。
</div>

<h2><span class="num">§4</span> 光の層 (参考・未決)</h2>
<div class="card">
  上の「光の層」は<b>ゲームの絵ではなく参考画像</b> (<code>Tools/cabinet_light.py</code>)。
  参照資料の見た目は半分がドット絵、 半分が実行時の光の処理でできていて、
  後者は描いて作るものではないので下絵には入れていない。 入っているのは 4 つ:
  画面の光の回り込み / 明るいもののにじみ (ブルーム) / 上からのランプの光だまり / 暖色の色調補正と粒状感。
  画面の中身は仮の橙のグラデーション。<br>
  <b>未決の論点が 2 つ</b>:
  ① 光の層を掛けると<b>画面上のピクセルパーフェクトは崩れる</b> (にじみは格子に乗らない。 下のドットは格子どおりのまま)。
  ② CLAUDE.md の規約で Post Processing パッケージは使っていないので、 入れるなら<b>自前の軽いカメラエフェクト</b>になる。
</div>

<h2><span class="num">§5</span> パレット</h2>
<div class="card pal">__PAL__</div>

<script>
const ZONES = __ZONES__, S = __S__;
const frame=document.getElementById('frame'), ov=document.getElementById('ov'),
      cap=document.getElementById('cap'), zb=document.getElementById('zoombar');
let zoom=2;
[2,3,4].forEach(z=>{const b=document.createElement('button');
  b.textContent='×'+z; b.setAttribute('aria-pressed',z===zoom);
  b.onclick=()=>{zoom=z;draw();[...zb.children].forEach(c=>c.setAttribute('aria-pressed',c.textContent==='×'+z));};
  zb.appendChild(b);});
document.querySelectorAll('[data-bg]').forEach(b=>b.onclick=()=>{
  frame.className='frame '+b.dataset.bg;
  document.querySelectorAll('[data-bg]').forEach(o=>o.setAttribute('aria-pressed',o===b));});
document.getElementById('zones').onchange=draw;
document.getElementById('lit').onchange=e=>{document.getElementById('litimg').hidden=!e.target.checked;};
function draw(){
  const k=S*zoom;
  frame.style.width=(480*k)+'px'; frame.style.height=(270*k)+'px';
  cap.textContent='絵 '+(480*S)+'×'+(270*S)+' px を ×'+zoom+' 表示 ('+(480*k)+'×'+(270*k)+' px)';
  ov.innerHTML='';
  if(!document.getElementById('zones').checked) return;
  for(const [n,name,x,y,w,h] of ZONES){
    const e=document.createElement('div'); e.className='zone';
    e.style.cssText=`left:${x*k}px;top:${y*k}px;width:${w*k}px;height:${h*k}px`;
    e.innerHTML='<b>'+n+' '+name+'</b>'; ov.appendChild(e);}
}
draw();
</script>
</div></body></html>
"""


def write_html(path, png_bytes, lit_bytes=b""):
    src = "data:image/png;base64," + base64.b64encode(png_bytes).decode("ascii")
    items = ([("縁取り", K), ("影", D), ("本体", M), ("明るい面", L), ("きらめき", HI)]
             + [(f"部品の面 {i}", c) for i, c in enumerate(SURF)]
             + [(f"背板 {i}", c) for i, c in enumerate(BASE)]
             + [(f"窪み {i}", c) for i, c in enumerate(REC)]
             + sorted((k, v) for k, v in C.items()))
    pal = "".join(
        f'<div><span class="sw" style="background:rgb({c[0]},{c[1]},{c[2]})"></span>'
        f'{k} <span style="color:var(--ink-3)">#{c[0]:02x}{c[1]:02x}{c[2]:02x}</span></div>'
        for k, c in items)
    zones = "[" + ",".join(f'[{n},"{nm}",{x},{y},{w},{h}]' for n, nm, x, y, w, h in ZONES) + "]"
    lit = "data:image/jpeg;base64," + base64.b64encode(lit_bytes).decode("ascii") if lit_bytes else ""
    html = (HTML.replace("__LIT__", lit).replace("__SRC__", src).replace("__PAL__", pal).replace("__ZONES__", zones)
                .replace("__RES__", f"{W}×{H}").replace("__S__", str(S)))
    with open(path, "w", encoding="utf-8") as f:
        f.write(html)


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    dd = os.path.join(root, "docs", "design")
    png = os.path.join(dd, "cabinet-face.png")
    img.save(png)
    buf = _io.BytesIO(); img.save(buf, "PNG")
    htm = os.path.join(dd, "cabinet-face.html")
    # 光の層の参考画像 (下絵を読んで作るので、 下絵を書いた後に呼ぶ)
    import importlib.util
    spec = importlib.util.spec_from_file_location("cabinet_light", os.path.join(root, "Tools", "cabinet_light.py"))
    light = importlib.util.module_from_spec(spec); spec.loader.exec_module(light)
    light.main()
    with open(light.OUT, "rb") as f:
        lit_bytes = f.read()
    write_html(htm, buf.getvalue(), lit_bytes)
    print(f"PNG : {os.path.relpath(png, root)}  {W}×{H} px  (格子 {GW}×{GH} dot ×{S})")
    print(f"HTML: {os.path.relpath(htm, root)}  {os.path.getsize(htm)/1024:.0f} KB")
