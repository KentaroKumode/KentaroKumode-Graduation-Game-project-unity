#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""GUI の試作: 「液晶 256×139 (8×8・画面いっぱい) + その上の引き出しタブだけ 3×3」 (2026-09-27)。 **試作であって仕様ではない**。

  ・液晶は 256×139 のまま (固定)。 1080p で 1 px = 8×8 画面 px → 1536×834 を中央に置く
  ・GUI (引き出しタブ) は細かい格子 640×360 (1 px = 3×3 画面 px) で描く。 液晶 (8×8) との比は 8:3 で、
    両方とも整数倍になるのは 1080p と 4K。 層が別なので比が整数でなくても見た目は崩れない
  ・筐体は描かない。 2026-09-28: 液晶の周りの黒い余白が不評 → 液晶を ×8 で画面いっぱいに (端は画面の外)。
    整数倍は 1080p (×8) と 4K (×16) だけになる。 タブは画面の縁から出て液晶に被さる (Highfleet 式)
  ・文字も GUI の格子に直接置く (12px のドット字 = 1080p で高さ 36 px)。 文字だけ別格子にする例外は不要になった
  ・1 つの層の中では粒度を混ぜない: 液晶の中 (敵・予告・照準) は 8×8、 タブの上 (数字・目盛り・アイコン) は 3×3

部品の描き方 (面取り・材質・落とし影など) は cabinet_drawers.py の道具を使い、 数字や端子の絵は細かい格子用に描き直した。

使い方: python Tools/cabinet_fine.py
  → docs/design/cabinet-fine/*.png (部品・ドット等倍) / cabinet-fine.html (部品を位置指定で重ねる)
    / cabinet-fine*.png (確認用の 1 枚絵・1920×1080)
"""
import base64
import io as _io
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cabinet_drawers as cd
from cabinet_drawers import (ROOT, FONT_PATH, K, OLV, OLV_T, GUN, GUN_T, KH, KH_T, STEEL, BLACK, BLACK_F,
                             RED, HAZ, LABEL, REC, IVORY, C, TERM_C, LCD_FOE, LCD_WHITE, LCD_INK, FIGHT,
                             FOE_ATK, FOE_HP, FOE_MAX, FOE_PRE, HP, HP_MAX, HP_PRE, HOPE, HOPE_MAX, DICE, WIRE,
                             NO_REROLL, TERMS, TERM_SUM, CHG_NOW, CHG_GAIN, FACES, WEAPON, TURN, STAGE,
                             put, fill, hline, vline, rect_m, round_m, chamfer_m, shape, recess, screw,
                             steel, outlined, glyph_pts, cable, paste_icon, item_desc)
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

PX = int(sys.argv[sys.argv.index("--px") + 1]) if "--px" in sys.argv else 3   # GUI 1 px → 1080p の画面 px (3 / 4 / 5)
DRAW_W, DRAW_H = 640, 360                 # 部品を描く座標系 (3×3 のときの格子。 5×5 では重ねる時に寄せ直す)
GW, GH = 1920 // PX, 1080 // PX            # GUI の格子 (3×3: 640×360 / 5×5: 384×216)
SUFFIX = "" if PX == 3 else f"-{PX}x"
# 5×5 では縦に入りきらない部品を短くする（残りは送りで見る）
GAUGE_N, COL_H, INV_N, FACES_N = {3: (45, 176, 6, 10), 4: (45, 176, 6, 10), 5: (30, 130, 4, 8)}[PX]
BEZ_W, BEZ_R = {3: (10, 18), 4: (8, 14), 5: (6, 11)}[PX]          # モニターのフチの太さ・角の丸み（GUI px・画面で約 30 px）
LW, LH = 256, 139                         # 液晶 (固定・1 px = 8×8 画面 px = GUI の 2×2)
SCREEN = PX                               # GUI 1 px → 画面 px
SW, SH = GW * SCREEN, GH * SCREEN          # 画面 (1080p) = 1920×1080
LCD_SCALE = 8                             # 液晶 1 px → 画面 px (2026-09-28: 周りの黒をやめて画面いっぱいに)
LCD_OX, LCD_OY = (SW - LW * LCD_SCALE) // 2, (SH - LH * LCD_SCALE) // 2   # = (-64, -16): 端は画面の外へ出る
LX, LY = LCD_OX / SCREEN, LCD_OY / SCREEN  # GUI 座標での液晶の左上 (HTML の配置用)
VOID = (13, 14, 15)                       # 液晶の外 (筐体なし)
ITEMS = ["工廠の材料箱", "無限モーター", "呼雷粉", "逆さ避雷針", "過負荷チューナー", "魂喰らいの血石"]
FONT = ImageFont.truetype(FONT_PATH, 12)
TEXTS = []


HEAD_SCALE, DESC_SCALE = 3, 2              # 敵の詳細の字: 見出し 1 ドット = 3×3 画面 px / 説明 = 2×2 画面 px (2026-09-28)


def text(x, y, s, col, align="l", scale=None):
    """x, y は GUI 座標 (小数も可)。 scale を渡すと、 その字だけ 1 ドット = scale×scale 画面 px で描く
    (GUI の格子には乗らないが、 画面の画素には整数で乗る)。 None なら GUI の格子の字。"""
    TEXTS.append((x, y, s, col, align, scale))


def tw(s, scale=None):
    """字の幅 (GUI 単位)。"""
    return FONT.getlength(s) * ((scale or PX) / PX)


def lines(x, y, rows, maxw, pitch=14, max_lines=99):
    """GUI の格子に 12px の字を置く。 幅で折り返し、 max_lines を超えたら … で切る。"""
    for s, col in rows:
        if not s: y += 5; continue
        out = []
        while s:
            n = len(s)
            while n > 1 and FONT.getlength(s[:n]) > maxw: n -= 1
            out.append(s[:n]); s = (" " + s[n:]) if n < len(s) else ""
        if len(out) > max_lines: out = out[:max_lines]; out[-1] = out[-1][:-1] + "…"
        for ln in out: text(x, y, ln, col); y += pitch
    return y


# ── 細かい格子用の数字・記号 ─────────────────────────────────
DIG = {
    "0": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "1": ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "2": [".###.", "#...#", "....#", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
    "3": [".###.", "#...#", "....#", "....#", "..##.", "....#", "....#", "#...#", ".###."],
    "4": ["...#.", "..##.", ".#.#.", "#..#.", "#..#.", "#####", "...#.", "...#.", "...#."],
    "5": ["#####", "#....", "#....", "####.", "....#", "....#", "....#", "#...#", ".###."],
    "6": [".###.", "#....", "#....", "####.", "#...#", "#...#", "#...#", "#...#", ".###."],
    "7": ["#####", "....#", "....#", "...#.", "...#.", "..#..", "..#..", "..#..", "..#.."],
    "8": [".###.", "#...#", "#...#", "#...#", ".###.", "#...#", "#...#", "#...#", ".###."],
    "9": [".###.", "#...#", "#...#", "#...#", ".####", "....#", "....#", "....#", ".###."],
}
SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc",
       "7": "abc", "8": "abcdefg", "9": "abcdfg", "-": "g", " ": ""}
TICON = {                                 # 端子の絵板 7×7
    "atk": ["......#", ".....#.", "#...#..", ".#.#...", "..#....", ".#.#...", "#......"],
    "blk": [".#####.", "#######", "#######", "#######", ".#####.", "..###..", "...#..."],
    "chg": ["...##..", "..##...", ".##....", "######.", "...##..", "..##...", ".##...."],
}
PIPS = {1: [(1, 1)], 2: [(0, 0), (2, 2)], 3: [(0, 0), (1, 1), (2, 2)], 4: [(0, 0), (0, 2), (2, 0), (2, 2)],
        5: [(0, 0), (0, 2), (1, 1), (2, 0), (2, 2)], 6: [(0, 0), (1, 0), (2, 0), (0, 2), (1, 2), (2, 2)]}
PIPS[7] = PIPS[6] + [(1, 1)]
PIPS[8] = [(r, c) for r in range(3) for c in range(3) if (r, c) != (1, 1)]
PIPS[9] = [(r, c) for r in range(3) for c in range(3)]


def nixie(x, y, ch):
    """ニキシー管 9×15: 奥に積んだ陰極 (薄い 8) + 点いた数字 5×9。"""
    fill(x, y, 9, 15, REC[1]); put(x, y, K); put(x + 8, y, K); put(x, y + 1, REC[0]); put(x + 8, y + 1, REC[0])
    for j, row in enumerate(DIG["8"]):
        for i, v in enumerate(row):
            if v == "#": put(x + 2 + i, y + 3 + j, C["ghost"])
    for j, row in enumerate(DIG[ch]):
        for i, v in enumerate(row):
            if v == "#": put(x + 2 + i, y + 3 + j, C["nixie"])
    put(x + 1, y + 2, (70, 74, 78))                                # ガラスのきらめき


SEG_OFF = (32, 30, 26)


def seg7(x, y, ch, col=None):
    """7 セグ 6×11 (線 1 px)。 消灯セグメントも残像で描く。"""
    col = col or C["amber"]; on = SEG[ch]
    P = {"a": [(x + i, y) for i in range(1, 5)], "d": [(x + i, y + 10) for i in range(1, 5)],
         "g": [(x + i, y + 5) for i in range(1, 5)], "f": [(x, y + j) for j in range(1, 5)],
         "b": [(x + 5, y + j) for j in range(1, 5)], "e": [(x, y + j) for j in range(6, 10)],
         "c": [(x + 5, y + j) for j in range(6, 10)]}
    for k, pts in P.items():
        for (i, j) in pts: put(i, j, col if k in on else SEG_OFF)   # 線が細いので消灯は暗く (明るいと 7 が 8 に見える)


def bitmap(x, y, rows, col):
    for j, row in enumerate(rows):
        for i, v in enumerate(row):
            if v == "#": put(x + i, y + j, col)


def gauge(gx, gw, top, n, lit, after, c, hi, pre, pitch=3):
    recess(gx, top, gw, n * pitch + 2)
    for s in range(n):
        y = top + 1 + (n - 1 - s) * pitch
        if s < after: fill(gx + 1, y, gw - 2, pitch - 1, c); hline(gx + 1, y, gw - 2, hi)
        elif s < lit: fill(gx + 1, y, gw - 2, pitch - 1, pre)
        else: fill(gx + 1, y, gw - 2, pitch - 1, REC[2])


# ════════════════════════════════════════════════════════════
#  液晶 (256×139・8×8) ── 別の画像に描いて ×8 で貼る
# ════════════════════════════════════════════════════════════
def _mul(x, y, f):
    """影: 不透明な画素は暗くし、 透明な画素には半透明の黒を置く (部品を透明な画像に単独で描くため)。"""
    if not cd.inb(x, y): return
    r, g, b, a = cd.PX[x, y]
    if a == 255: cd.PX[x, y] = (int(r * f), int(g * f), int(b * f), 255)
    else: cd.PX[x, y] = (0, 0, 0, 255 - int((255 - a) * f))


cd.mul = _mul


# ════════════════════════════════════════════════════════════
#  液晶の中の部品 (256×139 の格子・8×8)。 背景は Assets/Materials/day.png をそのまま使う
# ════════════════════════════════════════════════════════════
FOE_X, FOE_Y = 120, 64                                             # 敵 (仮) の位置 (液晶の座標)


def lcd_enemy():
    """敵 (仮): 槍を持つ蜥蜴の戦士の影絵。"""
    ox, oy = FOE_X, FOE_Y
    m = (round_m(ox + 4, oy, 7, 6, 2) | rect_m(ox + 1, oy + 2, 4, 3) | round_m(ox + 3, oy + 5, 9, 9, 2)
         | rect_m(ox + 10, oy + 11, 4, 2) | rect_m(ox + 13, oy + 12, 3, 2) | rect_m(ox + 15, oy + 13, 2, 2)
         | rect_m(ox + 4, oy + 13, 3, 7) | rect_m(ox + 8, oy + 13, 3, 7) | rect_m(ox, oy + 7, 4, 2)
         | rect_m(ox - 1, oy - 3, 1, 23) | rect_m(ox - 2, oy - 4, 3, 2))
    edge = {p for p in m if any((p[0] + dx, p[1] + dy) not in m for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}
    for (x, y) in m:
        if (x, y) in edge: put(x, y, LCD_FOE["o"])
        elif (x - 1, y) in edge or (x, y - 1) in edge: put(x, y, LCD_FOE["l"])
        else: put(x, y, LCD_FOE["b"])
    put(ox + 5, oy + 2, LCD_FOE["e"])


NEXT_STAGE_IN = 3                                                   # 次の段階までの残りターン (T7 → 段階 2 は T10)
# 時計 5×5 (竜頭なし)。 白く塗った文字盤に、 12 時と 3 時を指す針を黒く抜く。 数字 (3×5) と同じ高さにそろえる
# (白い線で縁だけ描くと、 この大きさでは数字の 9 と紛らわしかった)
WATCH = [".###.", "##.##", "##..#", "#####", ".###."]


def lcd_intent():
    """攻撃予告 (Slay the Spire 式): 敵の頭上に剣と数字。 その横にストップウォッチと「次の段階までの残りターン」
    (2026-09-28: 筐体側のエスカレーション計をやめてここへ移した)。"""
    sword = [(5, 0), (6, 0), (4, 1), (5, 1), (6, 1), (3, 2), (4, 2), (5, 2), (0, 2), (1, 3), (2, 3), (3, 3), (4, 3),
             (2, 4), (3, 4), (1, 5), (3, 5), (0, 6), (4, 6)]
    ix, iy = FOE_X - 5, FOE_Y - 13
    atk = str(FOE_ATK)
    wx = ix + 9 + len(atk) * 4 + 3                                   # 攻撃値の右に少し空けて
    watch = [(wx + i, iy + 1 + j) for j, row in enumerate(WATCH) for i, v in enumerate(row) if v == "#"]
    outlined([(ix + a, iy + b) for a, b in sword] + glyph_pts(ix + 9, iy + 1, atk)
             + watch + glyph_pts(wx + 7, iy + 1, str(NEXT_STAGE_IN)), LCD_WHITE)


def lcd_target():
    """照準 (選択中 = 青緑)。 液晶の中のものなので液晶の粒度。"""
    x0, y0, x1, y1 = FOE_X - 5, FOE_Y - 6, FOE_X + 19, FOE_Y + 21
    for (cx, cy, sx, sy) in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):
        for k in range(4):
            put(cx + sx * k, cy, C["sel"]); put(cx, cy + sy * k, C["sel"])
            put(cx + sx * k + sx, cy + sy, LCD_INK); put(cx + sx, cy + sy * k + sy, LCD_INK)


# ════════════════════════════════════════════════════════════
#  GUI (640×360・3×3)
# ════════════════════════════════════════════════════════════
def draw_status():
    """左上: ゴールド / 素材。"""
    shape(chamfer_m(0, 0, 84, 27, (0, 0, 8, 0)), GUN, GUN_T, seed=11, shadow=2)
    recess(4, 4, 47, 19)
    for k, ch in enumerate("0250"): nixie(6 + k * 11, 6, ch)
    recess(54, 4, 25, 19)
    for k, ch in enumerate("07"): nixie(56 + k * 11, 6, ch)


def draw_enemy_sign():
    """上: 敵 HP の札 (鋼の腕で吊る)。 ニキシー 4 桁 + 与ダメ予測 + 目盛り 16 + 名前の表示器。"""
    ox, oy = 244, 4
    for ax in (ox + 10, ox + 137):
        steel(rect_m(ax, 0, 5, oy + 3)); screw(ax + 1, 1, STEEL)
    shape(chamfer_m(ox, oy, 152, 24, (0, 0, 6, 6)) | chamfer_m(ox + 31, oy + 20, 90, 18, (0, 0, 4, 4)),
          GUN, GUN_T, seed=21, shadow=2)
    recess(ox + 3, oy + 3, 47, 19)
    for k, ch in enumerate(f"{FOE_HP:04d}"): nixie(ox + 5 + k * 11, oy + 5, ch)
    recess(ox + 52, oy + 5, 26, 15)
    for k, ch in enumerate(f"-{FOE_PRE}"): seg7(ox + 54 + k * 8, oy + 7, ch, C["atk"])
    n = 16; recess(ox + 80, oy + 8, n * 4 + 3, 9)
    lit = round(n * FOE_HP / FOE_MAX); after = round(n * (FOE_HP - FOE_PRE) / FOE_MAX)
    for s in range(n):
        x = ox + 82 + s * 4
        if s < after: fill(x, oy + 10, 3, 5, C["hp"]); hline(x, oy + 10, 3, C["hp_hi"])
        elif s < lit: fill(x, oy + 10, 3, 5, C["hp_pre"])
        else: fill(x, oy + 10, 3, 5, REC[2])
    recess(ox + 34, oy + 22, 84, 14, floor=REC[0])
    text(ox + 76, oy + 23, "蛇鱗の戦士", C["vfd"], "c")


def draw_dial():
    """右上: エスカレーション計 (アナログ)。 5 ターンごとの目盛り + 1 ターンごとの小目盛り。"""
    shape(chamfer_m(574, 0, 66, 44, (0, 0, 0, 10)), KH, KH_T, seed=71, shadow=2)
    cx, cy, R, TMAX = 608.0, 34.0, 24.0, 25
    band = [None, C["amber"], (232, 124, 52), (204, 60, 46), (122, 30, 30)]
    recess(581, 6, 55, 30)
    for j in range(9, 35):
        for i in range(583, 634):
            dx, dy = i + 0.5 - cx, j + 0.5 - cy
            d = math.hypot(dx, dy)
            if j == 34 and abs(dx) < R: put(i, j, K); continue
            if dy > 0 or d >= R: continue
            if d >= R - 1: put(i, j, K); continue
            t = (180 - math.degrees(math.atan2(-dy, dx))) / 180 * TMAX
            st = min(4, int(t // 5))
            if d >= R - 5 and band[st]: put(i, j, band[st])
            else: put(i, j, LABEL[2] if d < R - 8 else LABEL[1])
    for T in range(0, TMAX + 1):
        a = math.radians(180 - T / TMAX * 180)
        rs = (14.5, 15.5, 16.5, 17.5, 18.5) if T % 5 == 0 else (17.0, 18.0)
        for r in rs: put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), K)
    a = math.radians(180 - TURN / TMAX * 180)
    for k in range(0, 44):
        r = k * 0.5
        put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), RED["M"])
    fill(607, 32, 3, 3, STEEL["L"]); put(607, 32, STEEL["H"]); put(609, 34, STEEL["D"])
    put(592, 20, (255, 255, 255)); put(593, 19, LABEL[2]); put(591, 21, LABEL[2])
    for s in range(4):
        fill(596 + s * 7, 38, 5, 3, band[s + 1] if s < STAGE else REC[2])


def draw_right():
    """右: ゲージのタブ。 HP の数値の小板が左へ段になって出る。 左の耳が握り。"""
    m = (chamfer_m(594, 48, 46, COL_H, (8, 0, 0, 8)) | chamfer_m(560, 52, 70, 23, (5, 0, 0, 5))
         | chamfer_m(586, 110, 10, 40, (4, 0, 0, 4)))
    shape(m, GUN, GUN_T, seed=31, shadow=2)
    recess(564, 54, 36, 19)
    for k, ch in enumerate(f"{HP:03d}"): nixie(566 + k * 11, 56, ch)
    recess(601, 56, 26, 15)
    for k, ch in enumerate(f"-{HP_PRE}"): seg7(603 + k * 8, 58, ch, C["hp_hi"])
    cd.grip(588, 116, 4, 28, vertical=True)
    n = GAUGE_N
    gauge(600, 9, 80, n, round(n * HP / HP_MAX), round(n * (HP - HP_PRE) / HP_MAX), C["hp"], C["hp_hi"], C["hp_pre"])
    gauge(611, 6, 80, n, 0, 0, C["shield"], C["shield_hi"], C["shield_hi"])
    gauge(619, 8, 80, n, round(n * HOPE / HOPE_MAX), round(n * HOPE / HOPE_MAX), C["hope"], C["hope_hi"], C["hope_hi"])
    screw(631, 84); screw(631, 48 + COL_H - 12)
    for hy in (60, 48 + COL_H - 10): steel(rect_m(636, hy, 4, 6))


def draw_left():
    """左: 所持のタブ。 上段 = 武器 32px の専用スロット、 下段 = 16px の所持品を 1 列で送る。 右の耳が握り。"""
    hl = INV_N * 18 + 4                                            # 所持品の列の高さ
    m = (chamfer_m(0, 34, 48, 52, (0, 6, 0, 0)) | chamfer_m(0, 82, 32, hl + 16, (0, 0, 6, 0))
         | chamfer_m(30, 130, 9, 40, (0, 4, 4, 0)))
    shape(m, GUN, GUN_T, seed=41, shadow=2)
    cd.grip(32, 136, 4, 28, vertical=True)
    shape(chamfer_m(3, 37, 42, 42, (0, 4, 0, 4)), OLV, OLV_T, seed=42, shadow=1)
    recess(5, 39, 38, 38, floor=REC[0])
    paste_icon(WEAPON, 8, 42)
    recess(4, 84, 20, hl, floor=REC[0])
    for k, name in enumerate(ITEMS[:INV_N]):
        paste_icon(name, 6, 86 + k * 18)
        if k: hline(6, 85 + k * 18, 16, REC[1])
    recess(25, 84, 4, hl); fill(26, 86, 2, max(8, hl * 40 // 112), STEEL["M"]); put(26, 86, STEEL["H"])
    for hy in (40, 84 + hl - 12): steel(rect_m(0, hy, 4, 6))


def draw_faces():
    """左下: ダイスの面 (9×9)。 素の 6 面 = 白、 パーツ = 琥珀、 下の点 = Tier。"""
    fw = FACES_N * 11 + 6
    shape(chamfer_m(0, 334, fw + 8, 26, (0, 6, 0, 0)), OLV, OLV_T, seed=51, shadow=2)
    recess(3, 338, fw, 18)
    for n, (f, tier) in enumerate(FACES[:FACES_N]):
        cx, cy = 5 + n * 11, 340
        body, top, bot, side = ((C["amber"], C["amber_hi"], (170, 112, 40), (200, 136, 50)) if tier
                                else (IVORY[2], IVORY[3], IVORY[0], IVORY[1]))
        fill(cx, cy, 9, 9, body); hline(cx, cy, 9, top); hline(cx, cy + 8, 9, bot); vline(cx + 8, cy + 1, 8, side)
        for (r, c) in PIPS[f]: put(cx + 1 + c * 3, cy + 1 + r * 3, C["pip"]); put(cx + 2 + c * 3, cy + 1 + r * 3, C["pip"])
        for t in range(tier): fill(cx + 1 + t * 2, cy + 10, 1, 2, C["amber"])
    fill(fw, 340, 2, 12, STEEL["M"]); put(fw, 340, STEEL["H"])


def draw_board():
    """下: 戦闘盤 (下から引き上げる)。 端子 ×3 + 特殊 (蓋) / 配線 / ソケット ×5 / 充電ゲージ。"""
    ox, oy = 240, 298
    m = (chamfer_m(ox, oy, 160, 62, (8, 8, 0, 0)) | chamfer_m(ox + 50, oy - 7, 60, 10, (4, 4, 0, 0))
         | rect_m(ox - 5, oy + 18, 6, 44) | rect_m(ox + 159, oy + 18, 6, 44))
    shape(m, GUN, GUN_T, seed=61, shadow=2)
    steel(rect_m(ox + 53, oy - 6, 4, 6)); steel(rect_m(ox + 103, oy - 6, 4, 6))
    cd.grip(ox + 58, oy - 5, 44, 4)
    ty = oy + 5
    TX = {"atk": ox + 5, "blk": ox + 40, "chg": ox + 75}
    for n, t in enumerate(TERMS):
        tx = TX[t]
        shape(chamfer_m(tx, ty, 33, 17, (3, 0, 3, 0)), OLV, OLV_T, seed=50 + n)
        recess(tx + 3, ty + 2, 11, 13); bitmap(tx + 5, ty + 5, TICON[t], TERM_C[t])
        for k in range(2):
            recess(tx + 15 + k * 8, ty + 2, 8, 13)
            seg7(tx + 16 + k * 8, ty + 3, f"{TERM_SUM[t]:02d}"[k])
    shape(chamfer_m(ox + 110, ty, 33, 17, (3, 0, 3, 0)), OLV, OLV_T, seed=54)   # 特殊端子 (未購入 = 蓋)
    screw(ox + 125, ty + 7)
    by = ty + 19
    recess(ox + 5, by, 138, 9)
    SOCK = tuple(ox + 15 + 24 * k for k in range(5))
    ROWS = {"atk": by + 2, "blk": by + 4, "chg": by + 6}
    sock_x = [sx + 10 for sx in SOCK]; term_x = {t: TX[t] + 16 for t in TERMS}
    for t in TERMS:
        xs = [sock_x[i] for i, w in enumerate(WIRE) if w == t] + [term_x[t]]
        hline(min(xs), ROWS[t], max(xs) - min(xs) + 1, TERM_C[t])
        vline(term_x[t], by, ROWS[t] - by + 1, TERM_C[t])
    for i, w in enumerate(WIRE):
        vline(sock_x[i], ROWS[w], by + 9 - ROWS[w], TERM_C[w])
    sy = by + 12
    for n, sx in enumerate(SOCK):
        recess(sx, sy, 20, 20)
        pips = set(PIPS[DICE[n]])
        for r in range(3):
            for c in range(3):
                lx, ly = sx + 3 + c * 5, sy + 3 + r * 5
                if (r, c) in pips: fill(lx, ly, 4, 4, C["amber"]); hline(lx, ly, 4, C["amber_hi"]); put(lx, ly + 1, C["amber_hi"])
                else: fill(lx, ly, 4, 4, REC[2])
        shape(round_m(sx + 15, sy - 3, 5, 5, 1), BLACK_F, (BLACK[2], BLACK[1], BLACK[3]), shadow=1, glint=False)
        fill(sx + 16, sy - 2, 3, 3, BLACK[1]); put(sx + 17, sy - 1, C["ghost"] if n in NO_REROLL else C["hope_hi"])
    sx = SOCK[2]
    for p in range(sx - 1, sx + 21): put(p, sy - 1, C["sel"]); put(p, sy + 20, C["sel"])
    for q in range(sy - 1, sy + 21): put(sx - 1, q, C["sel"]); put(sx + 20, q, C["sel"])
    put(sx + 17, sy - 1, C["hope_hi"])
    recess(ox + 147, ty, 9, 42)
    for s in range(10):
        y = ty + 1 + (9 - s) * 4
        if s < CHG_NOW: fill(ox + 148, y, 7, 3, C["chg"]); hline(ox + 148, y, 7, C["chg_hi"])
        elif s < min(10, CHG_NOW + CHG_GAIN): fill(ox + 148, y, 7, 3, C["chg_pre"])
        else: fill(ox + 148, y, 7, 3, REC[2])


def draw_fight():
    """右下: FIGHT! (字は 5×7 を 2 倍 = 線 2 px。 GUI の格子の上なので粒度は混ざらない)。"""
    shape(chamfer_m(534, 322, 106, 38, (10, 0, 0, 0)), KH, KH_T, seed=81, shadow=2)
    for j in range(351, 357):
        for i in range(540, 636):
            put(i, j, HAZ[0] if ((i + j) // 3) % 2 == 0 else HAZ[1])
    hline(540, 350, 96, K); hline(540, 357, 96, K)
    recess(544, 326, 92, 22, floor=(90, 22, 20))
    fill(546, 328, 88, 18, (228, 58, 46))
    hline(546, 328, 88, (255, 132, 110)); hline(546, 345, 88, (170, 38, 30))
    x = 546 + (88 - 58) // 2
    for ch in "FIGHT!":
        rows = FIGHT[ch]
        for j, row in enumerate(rows):
            for i, v in enumerate(row):
                if v == "#": fill(x + i * 2, 330 + j * 2, 2, 2, (255, 236, 220))
        x += (len(rows[0]) + 1) * 2


def draw_enemy_detail():
    """敵の詳細: 敵をクリック → 照準 → 右のタブの下から滑り出る (琥珀の表示器・12px)。"""
    if PX >= 4: return draw_enemy_detail_compact()
    shape(chamfer_m(362, 76, 236, 144, (8, 0, 0, 8)) | chamfer_m(354, 120, 10, 40, (4, 0, 0, 4)), GUN, GUN_T, seed=91, shadow=2)
    cd.grip(356, 126, 4, 28, vertical=True)
    recess(368, 80, 224, 136, floor=REC[0])
    fill(588, 82, 2, 132, REC[1]); fill(588, 82, 2, 40, STEEL["M"])
    lines(374, 84, [
        ("蛇鱗の戦士", C["vfd_hi"]), (f"HP {FOE_HP}/{FOE_MAX}", C["vfd"]), ("攻撃 11 + 出目 1〜7", C["vfd"]),
        (f"T{TURN} 段階1 ×1.2 次まで3T", C["vfd"]), ("", None),
        ("硬鱗", C["vfd_hi"]), (" 受けるダメージを-2（最低0）", C["vfd_dim"]),
        ("尾撃", C["vfd_hi"]), (" ロール敗北時、相手に1の固定ダメージ", C["vfd_dim"]),
    ], maxw=212)


def draw_enemy_detail_compact():
    """GUI が大きい時 (4×4 以上) の敵の詳細: 字の大きさはそのまま、 行数を詰めて枠を小さくする。
    1 行目 = 名前と HP / 2 行目 = 攻撃の内訳と段階 / 以降 = パッシブ 1 つにつき 1 行 (説明は入る分だけ・続きは「…」。
    全文はそのパッシブを選んだ時に出す想定)。"""
    # 字: 見出し (名前・HP・攻撃・パッシブ名) は 1 ドット 3×3 画面 px、 説明は 2×2 画面 px (大きさの差を 1.5 倍に抑える)
    HS, DS = HEAD_SCALE, DESC_SCALE
    RH, DL = 11, 13 * DS / PX                                       # 見出しの行の高さ・説明の行の高さ (GUI 単位)
    w = 168                                                        # 幅は選んだ敵（画面中央）にかぶらない範囲
    x0, y0 = 594 - w, 76                                           # 右のゲージの下から出る
    L, Rr = x0 + 10, x0 + w - 10
    passives = []
    for name, desc in (("硬鱗", "受けるダメージを-2（最低0）"), ("尾撃", "ロール敗北時、相手に1の固定ダメージ")):
        room, out, rest = (Rr - (L + tw(name, HS) + 4)) * PX / DS, [], desc   # 説明は名前の右に 2 行まで
        while rest and len(out) < 2:
            n = len(rest)
            while n > 1 and FONT.getlength(rest[:n]) > room: n -= 1
            out.append(rest[:n]); rest = rest[n:]
        if rest: out[-1] = out[-1][:-1] + "…"
        passives.append((name, out, max(RH, len(out) * DL + 2)))
    h = int(RH * 2 + sum(p[2] for p in passives) + 6)
    shape(chamfer_m(x0, y0, w, h + 8, (6, 0, 0, 6)) | chamfer_m(x0 - 8, y0 + (h - 24) // 2, 10, 32, (4, 0, 0, 4)), GUN, GUN_T, seed=91, shadow=2)
    cd.grip(x0 - 6, y0 + (h - 24) // 2 + 4, 4, 24, vertical=True)
    recess(x0 + 5, y0 + 4, w - 10, h, floor=REC[0])
    y = y0 + 7
    text(L, y, "蛇鱗の戦士", C["vfd_hi"], scale=HS); text(Rr, y, f"HP {FOE_HP}/{FOE_MAX}", C["vfd"], "r", scale=HS); y += RH
    text(L, y, "攻撃 11+出目1〜7  ×1.2  あと3T", C["vfd"], scale=HS); y += RH
    for name, out, ph in passives:
        text(L, y + (ph - RH) / 2, name, C["vfd_hi"], scale=HS)
        x = L + tw(name, HS) + 4
        top = y + (ph - len(out) * DL) / 2 - 0.5
        for k, ln in enumerate(out): text(x, top + k * DL, ln, C["vfd_dim"], scale=DS)
        y += ph


def draw_item_names():
    """所持品の名前: 左のタブの耳を引く → その下から滑り出る。 行はアイコンと揃える (18 px の行に 12px の字は 1 行だけ)。"""
    shape(chamfer_m(20, 82, 186, INV_N * 18 + 8, (0, 8, 8, 0)) | chamfer_m(204, 106, 10, 40, (0, 4, 4, 0)), GUN, GUN_T, seed=95, shadow=2)
    cd.grip(206, 112, 4, 28, vertical=True)
    recess(36, 84, 166, INV_N * 18 + 4, floor=REC[0])
    for k, name in enumerate(ITEMS[:INV_N]):
        if k: hline(37, 84 + k * 18, 164, REC[1])
        lines(40, 87 + k * 18, [(name, C["vfd_hi"])], maxw=158, max_lines=1)


LCD_BG = "../../Assets/Materials/day.png"                         # 液晶の背景 (HTML からの相対パス)
OUT_DIR = f"cabinet-fine{SUFFIX}"                                   # 部品の書き出し先 (docs/design/ の下)

# ── モニターのフチ（2026-09-28）: 黒い樹脂の枠。 映る部分は角を丸めてブラウン管らしく ──
BEZ = {"D": (14, 15, 17), "M": (30, 32, 35), "L": (52, 55, 59), "H": (120, 124, 128)}
BEZ_T = ((26, 28, 31), (22, 24, 27), (31, 33, 36))


def draw_bezel():
    inner = round_m(BEZ_W, BEZ_W, GW - BEZ_W * 2, GH - BEZ_W * 2, BEZ_R)
    shape(rect_m(0, 0, GW, GH) - inner, BEZ, BEZ_T, seed=7, shadow=0, glint=False)
    for (x, y) in inner:                                            # 画面との境目: 画面が一段奥にある
        if any((x + dx, y + dy) not in inner for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))): put(x, y, (4, 4, 5))
    fill(470, GH - 6, 4, 2, (80, 210, 100)); put(470, GH - 6, (190, 255, 200))   # 電源ランプ


# ── ブラウン管の効果: 液晶の層だけに掛ける（手前のタブと枠はくっきりのまま） ──
#    本番では液晶の映像を映す面のシェーダー 1 枚で同じことをする（Built-in RP のままでよい）
CRT = {"curve": 0.045, "scan": 0.62, "mask": 0.9, "bloom": 0.35, "vignette": 0.30, "chroma": 0.0022}


def crt(full):
    """full: 液晶を ×8 にした画像 (2048×1112・RGB)。 画面 (1920×1080) の見た目を返す。"""
    a = np.asarray(full.convert("RGB"), np.float32) / 255.0
    fh, fw = a.shape[:2]
    scan = np.where((np.arange(fh) % LCD_SCALE) >= LCD_SCALE - 2, CRT["scan"], 1.0).astype(np.float32)[:, None, None]
    mask = np.full((1, fw, 3), CRT["mask"], np.float32)             # 蛍光体の縦縞 (R / G / B)
    for c in range(3): mask[0, np.arange(fw) % 3 == c, c] = 1.0
    b = a * scan * mask
    lum = a.max(-1, keepdims=True)
    bright = Image.fromarray((np.clip((lum - 0.55) / 0.45, 0, 1) * a * 255).astype(np.uint8))
    b = b + np.asarray(bright.filter(ImageFilter.GaussianBlur(10)), np.float32) / 255.0 * CRT["bloom"]   # にじみ
    yy, xx = np.mgrid[0:SH, 0:SW].astype(np.float32)
    u, v = (xx - SW / 2) / (SW / 2), (yy - SH / 2) / (SH / 2)
    r2 = u * u + v * v
    out = np.zeros((SH, SW, 3), np.float32)
    for c, dc in enumerate((1 + CRT["chroma"], 1.0, 1 - CRT["chroma"])):   # 赤と青をわずかにずらす
        f = (1 + CRT["curve"] * r2) * dc                            # 画面のふくらみ
        sx = (SW / 2 + u * f * SW / 2 - LCD_OX).astype(np.int32)
        sy = (SH / 2 + v * f * SH / 2 - LCD_OY).astype(np.int32)
        ok = (sx >= 0) & (sx < fw) & (sy >= 0) & (sy < fh)
        out[..., c][ok] = b[sy[ok], sx[ok], c]
    out *= np.clip(1 - CRT["vignette"] * r2 ** 1.4, 0, 1)[..., None]   # 周辺減光
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")

# 部品: (ファイル名, 層, 描く関数)。 並び順 = 重なり順 (後ろほど手前)
PARTS = [
    ("lcd_enemy", "lcd", lcd_enemy), ("lcd_intent", "lcd", lcd_intent), ("lcd_target", "lcd", lcd_target),
    ("bezel", "gui", draw_bezel),                                   # モニターのフチ（液晶の上・タブの下）
    # ケーブルは 2026-09-28 に撤去（「謎のケーブルはいらない」）
    ("enemy_sign", "gui", draw_enemy_sign), ("board", "gui", draw_board),   # エスカレーション計は 2026-09-28 に撤去
    ("fight", "gui", draw_fight), ("faces", "gui", draw_faces),
    ("drawer_enemy_detail", "gui", draw_enemy_detail), ("gauges", "gui", draw_right),
    ("drawer_item_names", "gui", draw_item_names), ("inventory", "gui", draw_left), ("status", "gui", draw_status),
]
ANCHOR = {"status": (0, 0), "enemy_sign": (0.5, 0), "dial": (1, 0), "gauges": (1, 0), "inventory": (0, 0),
          "faces": (0, 1), "board": (0.5, 1), "fight": (1, 1), "drawer_item_names": (0, 0),
          "drawer_enemy_detail": (1, 0, 0, -24 if PX == 5 else 0)}
STATES = {
    "combat": ("戦闘 (標準)", f"cabinet-fine{SUFFIX}.png", {"lcd_target", "drawer_enemy_detail", "drawer_item_names"}),
    "target": ("敵を選択", f"cabinet-fine{SUFFIX}-target.png", {"drawer_item_names"}),
    "items": ("所持品を開く", f"cabinet-fine{SUFFIX}-items.png", {"lcd_target", "drawer_enemy_detail"}),
}   # 3 つ目 = その状態で出さない部品


def render_part(layer, fn):
    """部品を 1 つ、 透明な画像に単独で描き、 使っている範囲だけ切り出す。 文字もその部品に焼き込む。"""
    w, h = (LW, LH) if layer == "lcd" else (GW, GH) if fn is draw_bezel else (DRAW_W, DRAW_H)
    cd.W, cd.H = w, h
    cd.img = Image.new("RGBA", (w, h), (0, 0, 0, 0)); cd.PX = cd.img.load(); TEXTS.clear()
    fn()
    d = ImageDraw.Draw(cd.img); d.fontmode = "1"
    for (x, y, s, col, align, scale) in TEXTS:
        if scale: continue
        if align == "c": x -= int(FONT.getlength(s)) // 2
        if align == "r": x -= int(FONT.getlength(s))
        d.text((x, y), s, font=FONT, fill=col)
    box = cd.img.getbbox()
    x, y = box[0], box[1]
    img, sub = cd.img.crop(box), 1
    scaled = [t for t in TEXTS if t[5]]
    if scaled:                                                      # 倍率の違う字: 部品を画面の解像度にしてから描く
        sub = PX
        img = img.resize((img.width * PX, img.height * PX), Image.NEAREST)
        for (tx, ty, s, col, align, scale) in scaled:
            m = Image.new("L", (int(FONT.getlength(s)) + 2, 14), 0); dm = ImageDraw.Draw(m); dm.fontmode = "1"
            dm.text((0, 0), s, font=FONT, fill=255)
            m = m.resize((m.width * scale, m.height * scale), Image.NEAREST)
            sx, sy = round(tx * PX) - box[0] * PX, round(ty * PX) - box[1] * PX
            if align == "r": sx -= int(FONT.getlength(s)) * scale
            if align == "c": sx -= int(FONT.getlength(s)) * scale // 2
            img.paste(Image.new("RGBA", m.size, (*col, 255)), (sx, sy), m)
    if layer == "gui" and fn is not draw_bezel:                      # 描いた座標系から今の格子へ寄せ直す
        for name, (pn, pl, pf) in ((n, t) for n, t in [(q[0], q) for q in PARTS]):
            if pf is fn:
                a = ANCHOR.get(name, (0, 0)); x += round(a[0] * (GW - DRAW_W)) + (a[2] if len(a) > 2 else 0)
                y += round(a[1] * (GH - DRAW_H)) + (a[3] if len(a) > 3 else 0); break
    img.sub = sub                                                   # 部品の画像 1 px ＝ GUI の 1/sub px
    return img, x, y


def lcd_full(parts, hidden):
    """液晶の層だけを ×8 で重ねた画像 (2048×1112)。 画面の外へ出る端も含む。"""
    full = Image.open(os.path.join(ROOT, "Assets", "Materials", "day.png")).convert("RGBA").resize((LW * LCD_SCALE, LH * LCD_SCALE), Image.NEAREST)
    for name, layer, im, x, y in parts:
        if layer == "lcd" and name not in hidden:
            full.alpha_composite(im.resize((im.width * LCD_SCALE, im.height * LCD_SCALE), Image.NEAREST), (x * LCD_SCALE, y * LCD_SCALE))
    return full


def flatten(parts, hidden, screen=None):
    """部品を重ねた 1 枚絵 (確認用・1920×1080)。 screen を渡すとそれを液晶の見た目に使う (ブラウン管の効果込み)。"""
    if screen is None:
        base = Image.new("RGBA", (SW, SH), (*VOID, 255))
        base.alpha_composite(lcd_full(parts, hidden).crop((-LCD_OX, -LCD_OY, -LCD_OX + SW, -LCD_OY + SH)))
    else:
        base = screen.copy()
    for name, layer, im, x, y in parts:
        if name in hidden or layer == "lcd": continue
        f = SCREEN // getattr(im, "sub", 1)
        base.alpha_composite(im.resize((im.width * f, im.height * f), Image.NEAREST), (x * SCREEN, y * SCREEN))
    return base.convert("RGB")


HTML = """<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><title>DICE BOUND — 細かい GUI (試作)</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
  :root { --bg:#f8f7f2; --ink:#1a1a1a; --ink-3:#7a7a7a; --line:#d4d1c8; --grid:#e8e5db; --panel:#fff; --accent:#a15a2a; }
  @media (prefers-color-scheme: dark) { :root { --bg:#14161a; --ink:#e8e6e0; --ink-3:#7d7a72; --line:#333740; --grid:#262a32; --panel:#1c1f24; --accent:#e2c58a; } }
  * { box-sizing:border-box; } html,body { margin:0; padding:0; background:var(--bg); color:var(--ink); }
  body { font-family:"SF Mono","Consolas","Hiragino Kaku Gothic ProN","Yu Gothic",monospace; font-size:13px; line-height:1.6; }
  .doc { max-width:1220px; margin:0 auto; padding:32px 24px 80px; }
  header { border-bottom:2px solid var(--ink); padding-bottom:16px; margin-bottom:20px; }
  h1 { font-size:22px; margin:0 0 4px; } header .sub { color:var(--ink-3); font-size:12px; }
  code { background:var(--panel); border:1px solid var(--line); padding:1px 5px; font-size:12px; }
  .warn { background:var(--panel); border-left:3px solid var(--accent); padding:10px 14px; margin:12px 0; font-size:12px; }
  .bar { display:flex; flex-wrap:wrap; gap:14px; align-items:center; margin:12px 0 8px; }
  .grp { display:inline-flex; gap:6px; align-items:center; }
  .bar label { display:inline-flex; gap:5px; align-items:center; color:var(--ink-3); cursor:pointer; }
  button { font:inherit; background:var(--panel); color:var(--ink); border:1px solid var(--line); padding:3px 11px; border-radius:3px; cursor:pointer; }
  button[aria-pressed="true"] { background:var(--accent); color:var(--bg); border-color:var(--accent); }
  button:focus-visible, input:focus-visible { outline:2px solid var(--accent); outline-offset:2px; }
  .hint { color:var(--ink-3); font-size:11px; }
  .stage { overflow:auto; border:1px solid var(--line); padding:16px; background:var(--grid); }
  .stage:fullscreen { padding:0; border:0; background:#000; display:flex; align-items:center; justify-content:center; overflow:hidden; }
  .view { position:relative; margin:0 auto; background:__VOID__; overflow:hidden; }
  .view img { position:absolute; image-rendering:pixelated; user-select:none; -webkit-user-drag:none; }
  .view.outline img { outline:1px solid rgba(80,160,255,.9); outline-offset:-1px; }
  table { border-collapse:collapse; width:100%; font-size:12px; margin:8px 0; }
  th,td { border:1px solid var(--line); padding:4px 8px; text-align:left; }
  td.n { font-variant-numeric:tabular-nums; white-space:nowrap; }
  .tw { overflow-x:auto; }
</style></head><body><div class="doc">
<header><h1>DICE BOUND — 細かい GUI (試作)</h1>
<div class="sub">液晶 256×139 (1 px = 8×8 画面 px) · GUI 640×360 (1 px = 3×3 画面 px) · 部品は <code>__OUT__/</code> の PNG を位置指定で重ねている · 生成元 <code>Tools/cabinet_fine.py</code></div></header>
<div class="warn"><b>試作であって仕様ではない。</b> 液晶は 256×139 のまま (固定) で、 ×8 にして画面いっぱいに広げた (端の左右 8・上下 2 ドットは画面の外)。 その上の引き出しタブだけを細かい格子 (3×3) で描いた。 筐体は無し。
部品の PNG はドット等倍 (GUI の部品 = 640×360 の格子、 液晶の部品 = 256×139 の格子)。 落とし影は半透明の黒で各部品に含む。 文字は今の中身を焼き込んだもの (仮の字は MS ゴシック)。
液晶の背景は <code>Assets/Materials/day.png</code> を直接参照している。</div>
<div class="bar"><span class="grp"><span style="color:var(--ink-3)">状態</span><span id="sb"></span></span>
<span class="grp"><span style="color:var(--ink-3)">倍率</span><span id="zb"></span></span>
<button id="fs" type="button">全画面 (F)</button>
<label><input type="checkbox" id="crt" checked> ブラウン管の効果</label>
<label><input type="checkbox" id="ol"> 部品の範囲を表示</label>
<span class="hint">全画面中: 1 / 2 / 3 で状態を切り替え、 Esc で戻る</span></div>
<div class="stage" id="stage"><div class="view" id="view"></div></div>
<h2 style="font-size:15px;margin:28px 0 8px">部品</h2>
<div class="tw"><table><tr><th>ファイル</th><th>層</th><th>位置 (x, y)</th><th>大きさ</th><th>出さない状態</th></tr>__ROWS__</table></div>
<script>
const M=__MANIFEST__, S=__STATES__;
const GW=__GW__, GH=__GH__, LX=__LX__, LY=__LY__, LS=__LS__;   // 液晶 1 px ＝ GUI の LS px (8/3)
const view=document.getElementById('view'), stage=document.getElementById('stage'), keys=Object.keys(S);
let zoom=__PX__*0.5, st=keys[0];
const imgs=M.map(p=>{ const i=document.createElement('img'); i.src=p.src; i.alt=p.name; i.title=p.name; i.dataset.name=p.name; view.appendChild(i); return [p,i]; });
function draw(){
  const k = document.fullscreenElement ? Math.min(screen.width/GW, screen.height/GH) : zoom;
  view.style.width=(GW*k)+'px'; view.style.height=(GH*k)+'px';
  for(const [p,i] of imgs){
    const s = p.layer==='gui' ? 1 : p.layer==='screen' ? 1/__PX__ : LS, ox = p.layer==='lcd' ? LX : 0, oy = p.layer==='lcd' ? LY : 0;
    i.style.left=((ox+p.x*s)*k)+'px'; i.style.top=((oy+p.y*s)*k)+'px';
    i.style.width=(p.w*s*k)+'px'; i.style.height=(p.h*s*k)+'px'; i.style.zIndex=p.z;
    const crtOn = document.getElementById('crt').checked;
    i.hidden = p.layer==='screen' ? !(crtOn && p.state===st) : (p.layer==='lcd' && crtOn) || S[st].hidden.includes(p.name);
  }
}
function group(el, items, cur, on){ el.innerHTML=''; items.forEach(([k,l])=>{const b=document.createElement('button'); b.type='button'; b.textContent=l;
 b.setAttribute('aria-pressed',k===cur); b.onclick=()=>{on(k); group(el,items,k,on);}; el.appendChild(b);}); }
const stateItems=()=>keys.map(k=>[k,S[k].name]);
group(document.getElementById('zb'), [[__PX__*0.5,'1080p の 50%'],[__PX__*0.75,'75%'],[__PX__,'100% (実寸)']], zoom, z=>{zoom=z; draw();});
group(document.getElementById('sb'), stateItems(), st, k=>{st=k; draw();});
function toggleFs(){ if(document.fullscreenElement) document.exitFullscreen(); else stage.requestFullscreen(); }
document.getElementById('fs').onclick=toggleFs; stage.ondblclick=toggleFs;
document.addEventListener('fullscreenchange', draw); window.addEventListener('resize', draw);
document.getElementById('ol').onchange=e=>view.classList.toggle('outline', e.target.checked);
document.getElementById('crt').onchange=draw;
document.addEventListener('keydown',e=>{ if(e.key==='f'||e.key==='F'){ toggleFs(); return; }
 const n=parseInt(e.key,10)-1; if(n>=0&&n<keys.length){ st=keys[n]; draw(); group(document.getElementById('sb'), stateItems(), st, k=>{st=k; draw();}); } });
draw();
</script></div></body></html>"""


if __name__ == "__main__":
    dd = os.path.join(ROOT, "docs", "design")
    od = os.path.join(dd, OUT_DIR)
    os.makedirs(od, exist_ok=True)
    for f in os.listdir(od):                                        # 前回の書き出しを消す (部品名の変更で古い物が残らないように)
        if f.endswith(".png"): os.remove(os.path.join(od, f))
    rendered = []
    manifest = [{"name": "lcd_bg", "layer": "lcd", "src": LCD_BG, "x": 0, "y": 0, "w": LW, "h": LH, "z": 0}]
    for z, (name, layer, fn) in enumerate(PARTS, start=1):
        im, x, y = render_part(layer, fn)
        im.save(os.path.join(od, name + ".png"))
        rendered.append((name, layer, im, x, y))
        manifest.append({"name": name, "layer": layer, "src": f"{OUT_DIR}/{name}.png", "x": x, "y": y,
                         "w": im.width / getattr(im, "sub", 1), "h": im.height / getattr(im, "sub", 1), "z": z})
    print(f"部品: {len(rendered)} 枚 → {os.path.relpath(od, ROOT)}/")
    states = {}
    for key, (name, fn, hidden) in STATES.items():
        scr = crt(lcd_full(rendered, hidden))                           # ブラウン管の見本（液晶の層だけ・画面の解像度）
        scr.convert("RGB").save(os.path.join(od, f"screen_crt_{key}.png"))
        manifest.append({"name": f"screen_crt_{key}", "layer": "screen", "state": key, "src": f"{OUT_DIR}/screen_crt_{key}.png",
                         "x": 0, "y": 0, "w": SW, "h": SH, "z": 1})
        big = flatten(rendered, hidden, screen=scr)
        big.save(os.path.join(dd, fn))
        states[key] = {"name": name, "hidden": sorted(hidden)}
        print(f"PNG : docs/design/{fn}  {big.width}×{big.height} (確認用の 1 枚絵)")
    rows = "".join(
        f"<tr><td><code>{m['src'].split('/')[-1]}</code></td><td>{ {'lcd': '液晶 (256×139)', 'gui': f'GUI ({GW}×{GH})', 'screen': '見本: ブラウン管の効果込みの液晶 (1920×1080)'}[m['layer']] }</td>"
        f"<td class='n'>{m['x']}, {m['y']}</td><td class='n'>{m['w']}×{m['h']}</td>"
        f"<td>{('その状態のときだけ' if m['layer'] == 'screen' else '') or ' / '.join(STATES[k][0] for k in STATES if m['name'] in STATES[k][2]) or '—'}</td></tr>" for m in manifest)
    html = (HTML.replace("__MANIFEST__", json.dumps(manifest, ensure_ascii=False))
            .replace("__STATES__", json.dumps(states, ensure_ascii=False))
            .replace("__ROWS__", rows).replace("__OUT__", OUT_DIR)
            .replace("__GW__", str(GW)).replace("__GH__", str(GH)).replace("__PX__", str(PX)).replace("__LX__", repr(LX)).replace("__LY__", repr(LY)).replace("__LS__", repr(LCD_SCALE / SCREEN)).replace("__VOID__", "#%02x%02x%02x" % VOID))
    with open(os.path.join(dd, f"cabinet-fine{SUFFIX}.html"), "w", encoding="utf-8") as f:
        f.write(html)
    print(f"HTML: docs/design/cabinet-fine{SUFFIX}.html (部品を重ねて表示・GUI 1 px = {PX}×{PX})")
