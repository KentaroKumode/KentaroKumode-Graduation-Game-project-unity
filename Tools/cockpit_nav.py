#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""航行画面の GUI の叩き台 (2026-10-03)。 **試作であって仕様ではない**。

  ・GUI の格子は 480×270 (1 ドット = 1080p で 4×4 画面 px)
  ・液晶は 256×139 のまま (固定)。 ×8 で画面いっぱいに置き (端は画面の外)、 中に地図を描く
  ・計器は液晶の縁へ張り出す (Highfleet 式)。 筐体に刻印はしない ── 字は光る表示器の中だけ
  ・警告ランプは Tools/lamp_lit.py の書き出し (docs/design/lamp/*_full.png・43×21) を 1 ドット = 1 ドットで貼る

置いたもの: 魔石の方角と距離 (左上) / 気圧計 = 天候変化までの手番 (左) / 天気予報 (気圧計の右) /
            ゴールド (左下) / 物資のゲージ + HALF・CRIT ランプ (下) / HP (右) / 地図⇄戦闘の切り替え (右下)

使い方: python Tools/cockpit_nav.py  → docs/design/cockpit-nav.png (1920×1080)
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cabinet_drawers as cd
import cabinet_fine as cf          # ニキシー管・7 セグ・縦ゲージ・影の描き方 (読み込むだけで main は走らない)
from cabinet_drawers import (ROOT, K, OLV, OLV_T, GUN, GUN_T, KH, KH_T, STEEL, REC, LABEL, RED, C,
                             put, fill, hline, vline, rect_m, round_m, chamfer_m, shape, recess, screw, steel)
import numpy as np
from PIL import Image, ImageDraw, ImageFont

GW, GH = 480, 270                     # GUI の格子
PXS = 4                               # GUI 1 ドット → 画面 px
LW, LH = 256, 139                     # 液晶 (固定)
LCD_SCALE = 8
LCD_OX, LCD_OY = (GW * PXS - LW * LCD_SCALE) // 2, (GH * PXS - LH * LCD_SCALE) // 2   # = (-64, -16)
FONT = ImageFont.truetype(cd.FONT_PATH, 12)
LAMP_DIR = os.path.join(ROOT, "docs", "design", "lamp")

# ── 場面の値 (叩き台の 1 場面) ──
HP, HP_MAX = 31, 40
SUP, SUP_MAX = 620, 1000              # 目減り (HALF) ── 予報は「手番に幅・現象は種類だけ」の精度
GOLD = 250
TURNS_LO, TURNS_HI, TURNS_MAX = 3, 5, 12   # 気圧計: 次の天候変化まで 3〜5 手番 (目盛りは 0〜12)
STONE_LV, STONE_DIR = "near", 1       # 魔石: 近い (1〜2)・東 (0=北 1=東 2=南 3=西)
MODE = "map"                          # 地図 / 戦闘

LC = {"bg": (13, 8, 3), "grid": (34, 22, 9), "dim": (94, 62, 20), "mid": (162, 108, 31), "hi": (240, 170, 66),
      "hot": (255, 224, 160), "red": (255, 74, 44)}
TEXTS = []                            # (x, y, 文字, 色, 倍率) ── 画面の解像度で描く字 (表示器の中だけ)


def text(x, y, s, col, scale=2):
    TEXTS.append((x, y, s, col, scale))


# ════════════════════════════════════════════════════════════
#  液晶 (256×139): 地図
# ════════════════════════════════════════════════════════════
def draw_map():
    im = Image.new("RGB", (LW, LH), LC["bg"]); p = im.load()
    for y in range(2, LH, 6):
        for x in range(2, LW, 6): p[x, y] = LC["grid"]
    rnd = random.Random(7)
    nodes = []
    while len(nodes) < 26:
        q = (rnd.randint(24, LW - 24), rnd.randint(14, LH - 14))
        if all(math.dist(q, n) > 22 for n in nodes): nodes.append(q)
    edges = set()
    for i, a in enumerate(nodes):
        for j in sorted(range(len(nodes)), key=lambda j: math.dist(a, nodes[j]))[1:3]:
            edges.add(tuple(sorted((i, j))))
    def dotted(a, b, col, every=2):
        n = int(math.dist(a, b))
        for k in range(0, n, every):
            t = k / max(1, n); p[int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t)] = col
    for i, j in edges: dotted(nodes[i], nodes[j], LC["dim"])
    me = nodes[min(range(len(nodes)), key=lambda i: math.dist(nodes[i], (70, 82)))]
    dest = nodes[min(range(len(nodes)), key=lambda i: math.dist(nodes[i], (150, 52)))]
    dotted(me, dest, LC["hi"], 3)                                   # 行き先までの点線
    for n in nodes:
        known = math.dist(n, me) < 40
        g = [".#.", "#.#", ".#."] if known else ["#.#", "...", "#.#"]
        for j, row in enumerate(g):
            for i, v in enumerate(row):
                if v == "#": p[n[0] - 1 + i, n[1] - 1 + j] = LC["mid"] if known else LC["dim"]
    for k in range(-4, 5):                                          # 行き先の印
        if abs(k) >= 3: p[dest[0] + k, dest[1] - 4] = p[dest[0] + k, dest[1] + 4] = p[dest[0] - 4, dest[1] + k] = p[dest[0] + 4, dest[1] + k] = LC["hot"]
    for k in range(-3, 4):                                          # 自分: 十字
        if k: p[me[0] + k, me[1]] = p[me[0], me[1] + k] = LC["hot"]
    p[me[0], me[1]] = LC["hot"]
    for r in range(0, 360, 4):                                      # 照明なしの視界 (半径 1 ≒ 12 液晶 px の仮)
        a = math.radians(r); p[int(me[0] + 12 * math.cos(a)), int(me[1] + 12 * math.sin(a))] = LC["dim"]
    fx, fy = me[0] + 34, me[1] - 6                                  # 徘徊エネミーの最後に分かった位置 (薄く)
    for (i, j) in ((0, -2), (-1, -1), (1, -1), (-2, 0), (2, 0), (-1, 0), (0, 0), (1, 0)): p[fx + i, fy + j] = (120, 40, 24)
    return im


# ════════════════════════════════════════════════════════════
#  GUI (480×270)
# ════════════════════════════════════════════════════════════
def disc(cx, cy, r):
    return {(i, j) for j in range(int(cy - r) - 1, int(cy + r) + 2) for i in range(int(cx - r) - 1, int(cx + r) + 2)
            if (i + 0.5 - cx) ** 2 + (j + 0.5 - cy) ** 2 <= r * r}


def ring(cx, cy, r0, r1, col):
    for (i, j) in disc(cx, cy, r1) - disc(cx, cy, r0): put(i, j, col)


def lamp_round(x, y, on, col, dark):
    """丸い表示灯 5×5 (点いている時は中心を白く飛ばす)。"""
    for (i, j) in disc(x + 2.5, y + 2.5, 2.6): put(i, j, col if on else dark)
    if on: put(x + 2, y + 2, (255, 246, 220)); put(x + 1, y + 1, (255, 230, 190))
    for (i, j) in disc(x + 2.5, y + 2.5, 3.6) - disc(x + 2.5, y + 2.5, 2.6): put(i, j, K)


def draw_stone():
    """左上: 魔石。 円い表示面に 4 方向 × 距離 2 段 (遠い 3〜4 / 近い 1〜2)。 方角は近い時だけ点く (2026-10-03 案 A)。
    右上の赤い灯 = 察知されている。"""
    shape(chamfer_m(0, 0, 96, 86, (0, 0, 16, 0)) | chamfer_m(80, 0, 26, 30, (0, 0, 10, 0)), GUN, GUN_T, seed=3, shadow=2)
    cx, cy, R = 44.5, 42.5, 36
    ring(cx, cy, R - 4, R, STEEL["M"]); ring(cx, cy, R - 1, R, K); ring(cx, cy, R - 4, R - 3, STEEL["D"])
    for (i, j) in disc(cx - 1, cy - 1, R - 4) - disc(cx, cy, R - 4): put(i, j, STEEL["H"])
    face = disc(cx, cy, R - 4)
    for (i, j) in face:
        dx, dy = i + 0.5 - cx, j + 0.5 - cy
        d = math.hypot(dx, dy)
        sector = int(((math.degrees(math.atan2(dx, -dy)) + 45) % 360) // 90)   # 0=北 1=東 2=南 3=西 (境は斜め)
        band = "near" if d < 15 else "far" if d < R - 6 else "rim"
        col = (18, 12, 6)
        if abs(abs(dx) - abs(dy)) < 0.8 and d > 4: col = (40, 28, 12)          # 方角の境 (斜め)
        if abs(d - 15) < 0.6 or abs(d - (R - 6)) < 0.6: col = (52, 36, 14)       # 距離の境
        lit = (STONE_LV == "near" and band == "near" and sector == STONE_DIR)
        if lit: col = LC["hi"] if cd.dith(i, j, 0.75) else LC["mid"]
        if STONE_LV == "far" and band == "far": col = (90, 58, 18) if cd.dith(i, j, 0.3) else col
        put(i, j, col)
    for (i, j) in disc(cx, cy, 2): put(i, j, LC["hot"])                        # 自分
    lamp_round(88, 6, False, (255, 70, 44), (70, 22, 18))                      # 察知されている (消灯)
    screw(4, 76); screw(84, 22)


def draw_barometer():
    """左中: 気圧計。 針 = 次の天候変化までの手番の目安、 琥珀の帯 = 予報の幅 (物資が減るほど広がる)。"""
    shape(chamfer_m(0, 90, 82, 80, (0, 12, 12, 0)), KH, KH_T, seed=5, shadow=2)
    cx, cy, R = 40.5, 130.5, 30
    ring(cx, cy, R - 3, R, STEEL["M"]); ring(cx, cy, R - 1, R, K)
    face = disc(cx, cy, R - 3)
    a0, a1 = 225, -45                                                          # 目盛り: 左下 0 → 右下 TURNS_MAX
    ang = lambda t: math.radians(a0 + (a1 - a0) * t / TURNS_MAX)
    for (i, j) in face:
        dx, dy = i + 0.5 - cx, cy - (j + 0.5)
        d = math.hypot(dx, dy)
        a = math.degrees(math.atan2(dy, dx))
        a = a if a >= -90 else a + 360
        t = (a0 - a) / (a0 - a1) * TURNS_MAX
        col = LABEL[2] if d < R - 9 else LABEL[1]
        if R - 9 <= d < R - 4 and 0 <= t <= TURNS_MAX:
            if TURNS_LO <= t <= TURNS_HI: col = C["amber"]                    # 予報の幅
            elif t < 2: col = RED["L"]                                         # すぐ変わる
        put(i, j, col)
    for t in range(TURNS_MAX + 1):                                            # 目盛り
        a = ang(t)
        for r in ((R - 9, R - 8, R - 7, R - 6, R - 5) if t % 3 == 0 else (R - 7, R - 6)):
            put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), K)
    a = ang((TURNS_LO + TURNS_HI) / 2)                                        # 針
    for k in range(0, 2 * (R - 8)):
        r = k * 0.5; put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), (28, 30, 32))
    for (i, j) in disc(cx, cy, 2.2): put(i, j, STEEL["L"])
    put(int(cx) - 1, int(cy) - 1, STEEL["H"])
    screw(4, 160); screw(70, 94)


WEATHER_ICON = {   # 9×9: 雪 (現象の種類だけ分かる段階では「?」付き)
    "snow": ["....#....", ".#..#..#.", "..#.#.#..", "...###...", "#########", "...###...", "..#.#.#..", ".#..#..#.", "....#...."],
}


def draw_forecast():
    """気圧計の右に張り出す表示器: 次の天候 (精度は物資の段階で変わる。 目減り = 種類だけ)。"""
    shape(chamfer_m(74, 112, 74, 34, (0, 8, 8, 0)), GUN, GUN_T, seed=9, shadow=2)
    recess(80, 117, 62, 24, floor=REC[0])
    cf.bitmap(84, 124, WEATHER_ICON["snow"], C["vfd"])
    text(96, 120, "降雪系", C["vfd"])
    text(96, 129, "3〜5手番", C["vfd_dim"])


def draw_gold():
    """左下: ゴールド (ニキシー 4 桁)。"""
    shape(chamfer_m(0, 232, 62, 38, (0, 10, 0, 0)), GUN, GUN_T, seed=11, shadow=2)
    recess(5, 240, 49, 19)
    for k, ch in enumerate(f"{GOLD:04d}"): cf.nixie(7 + k * 11, 242, ch)
    screw(2, 262)


def draw_supply():
    """下: 物資。 7 セグ 4 桁 + 横のゲージ (段の境に印) + HALF / CRIT の警告ランプ。"""
    m = chamfer_m(96, 232, 290, 38, (10, 10, 0, 0))
    shape(m, OLV, OLV_T, seed=13, shadow=2)
    recess(102, 240, 36, 15)
    for k, ch in enumerate(f"{SUP:04d}"): cf.seg7(104 + k * 8, 242, ch, C["amber"])
    n, gx, gy = 36, 144, 242
    recess(gx, gy, n * 4 + 3, 11)
    lit = round(n * SUP / SUP_MAX)
    for s in range(n):
        x = gx + 2 + s * 4
        if s < lit: fill(x, gy + 2, 3, 7, C["amber"]); hline(x, gy + 2, 3, C["amber_hi"])
        else: fill(x, gy + 2, 3, 7, REC[2])
    for f in (0.20, 0.45, 0.75):                                              # 段の境 (CRIT / LOW / HALF)
        x = gx + 2 + round(n * f) * 4 - 1
        vline(x, gy + 12, 3, LABEL[1])
    screw(100, 262); screw(378, 262)


def draw_hp():
    """右: HP (ニキシー 2 桁 + 縦のゲージ)。"""
    shape(chamfer_m(440, 50, 40, 150, (10, 0, 0, 10)), GUN, GUN_T, seed=17, shadow=2)
    recess(446, 56, 27, 19)
    for k, ch in enumerate(f"{HP:02d}"): cf.nixie(448 + k * 11, 58, ch)
    n = 36
    cf.gauge(452, 15, 80, n, round(n * HP / HP_MAX), round(n * HP / HP_MAX), C["hp"], C["hp_hi"], C["hp_pre"])
    screw(444, 186); screw(472, 186)


ICON_MAP = ["#####........", "#...#####....", "#...#...#####", "#.#.#...#...#", "#...#.#.#...#", "#...#...#.#.#",
            "#.#.#...#...#", "#...#.#.#...#", "#...#...#.#.#", "#####...#...#", "....#####...#", "........#####", "............."]
ICON_FIGHT = ["#..........#.", ".#........#..", "..#......#...", "...#....#....", "....#..#.....", ".....##......",
              ".....##......", "....#..#.....", "...#....#....", ".##......##..", ".#........#..", "#..........#.", "............."]


def button(x, y, icon, on):
    """角の丸い押しボタン 26×26。 点いている側は琥珀に光り、 消えている側は暗い。"""
    m = round_m(x, y, 26, 26, 4)
    shape(m, STEEL, (STEEL["M"], STEEL["D"], STEEL["L"]), shadow=2, glint=False)
    inner = round_m(x + 3, y + 3, 20, 20, 3)
    for (i, j) in inner:
        d = math.hypot(i + 0.5 - (x + 13), j + 0.5 - (y + 13)) / 13
        put(i, j, (255, 214, 130) if on and d < 0.35 else (236, 150, 50) if on and d < 0.8
                  else (196, 104, 30) if on else (44, 34, 26))
    cf.bitmap(x + 7, y + 7, icon, (90, 40, 10) if on else (78, 68, 58))


def draw_mode():
    """右下: 地図 ⇄ 戦闘の切り替え。 2 つの押しボタン (今の画面の側が点く)。"""
    shape(chamfer_m(396, 216, 84, 54, (12, 0, 0, 0)), KH, KH_T, seed=19, shadow=2)
    button(404, 230, ICON_MAP, MODE == "map")
    button(446, 230, ICON_FIGHT, MODE == "fight")
    screw(402, 220); screw(472, 220)


def paste_lamps(gui):
    """物資のランプ: 目減り (HALF) なので HALF が点く。 43×21 (光込み) を 1:1 で。"""
    on_half = SUP <= 750 and SUP > 200
    on_crit = SUP <= 200
    for k, (name, on) in enumerate((("half", on_half), ("crit", on_crit))):
        im = Image.open(os.path.join(LAMP_DIR, f"{name}_lamp_{'on' if on else 'off'}_full.png")).convert("RGBA")
        gui.alpha_composite(im, (296 + k * 40 - 3, 238 - 3))


def main():
    lcd = draw_map().resize((LW * LCD_SCALE, LH * LCD_SCALE), Image.NEAREST)
    a = np.asarray(lcd, np.float32)
    scan = np.where((np.arange(a.shape[0]) % LCD_SCALE) >= LCD_SCALE - 2, 0.62, 1.0)[:, None, None]   # 走査線だけ
    lcd = Image.fromarray((a * scan).astype(np.uint8))
    screen = Image.new("RGBA", (GW * PXS, GH * PXS), (0, 0, 0, 255))
    screen.paste(lcd, (LCD_OX, LCD_OY))

    cd.W, cd.H = GW, GH
    cd.img = Image.new("RGBA", (GW, GH), (0, 0, 0, 0)); cd.PX = cd.img.load()
    for fn in (draw_stone, draw_barometer, draw_forecast, draw_gold, draw_supply, draw_hp, draw_mode):
        fn()
    paste_lamps(cd.img)
    gui = cd.img.resize((GW * PXS, GH * PXS), Image.NEAREST)
    for (x, y, s, col, sc) in TEXTS:                                          # 表示器の字 (画面の解像度・1 ドット = sc×sc)
        mk = Image.new("L", (int(FONT.getlength(s)) + 2, 14), 0); dm = ImageDraw.Draw(mk); dm.fontmode = "1"
        dm.text((0, 0), s, font=FONT, fill=255)
        mk = mk.resize((mk.width * sc, mk.height * sc), Image.NEAREST)
        gui.paste(Image.new("RGBA", mk.size, (*col, 255)), (x * PXS, y * PXS), mk)
    screen.alpha_composite(gui)
    out = os.path.join(ROOT, "docs", "design", "cockpit-nav.png")
    screen.convert("RGB").save(out)
    print("wrote", os.path.relpath(out, ROOT))


if __name__ == "__main__":
    main()
