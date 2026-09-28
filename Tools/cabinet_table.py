#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""筐体の試作 (別案): 「木のテーブルに置かれた液晶と本体 + ケーブルで繋いだ周辺機器」 (2026-09-27)。
**試作であって仕様ではない**。 引き出し版 (Tools/cabinet_drawers.py) と見比べるための別案。

  ・**液晶のサイズは固定 (256×139 = day.png と同じ)**。 テーブルと機器の場所は描画範囲を広げて作る
  ・描画範囲 = 384×216 (1080p で 1 px = 5×5 画面 px)。 液晶は画面の幅の 2/3。
    代わりに 720p / 1440p では整数倍にならない (1080p ×5 / 4K ×10 は整数)
  ・文字は 1080p で 2×2 画面 px の格子 (960×540) に置く (絵の格子とは整数倍でないが見た目には影響しない)
  ・周辺機器 (引き出し版のタブ) はテーブルに置いた別々の箱で、 ケーブルで本体の前面の端子に繋がる
  ・敵 HP の札だけはモニターの上端に挟んで留める。 光の層は無し (影は落とし影だけ)

部品の描き方 (面取り・材質・ニキシー管・計器など) は cabinet_drawers.py の道具をそのまま使う。

使い方: python Tools/cabinet_table.py  → docs/design/cabinet-table.png (1920×1080) / cabinet-table.html
"""
import base64
import io as _io
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cabinet_drawers as cd
from cabinet_drawers import (ROOT, FONT_PATH, K, OLV, OLV_T, GUN, GUN_T, KH, KH_T, STEEL, BLACK, BLACK_F,
                             RED, HAZ, LABEL, REC, IVORY, C, TERM_C, LCD_FOE, LCD_WHITE, PIPS, FIGHT,
                             FOE_ATK, FOE_HP, FOE_MAX, FOE_PRE, HP, HP_MAX, HP_PRE, HOPE, HOPE_MAX, DICE, WIRE,
                             NO_REROLL, TERMS, TERM_SUM, CHG_NOW, CHG_GAIN, FACES, WEAPON, ITEMS, TURN, STAGE,
                             put, fill, hline, vline, dith, rect_m, round_m, chamfer_m, shape, recess, screw,
                             dome, steel, tube, seg7, icon, outlined, glyph_pts, gauge, cable, paste_icon)
from PIL import Image, ImageDraw, ImageFont

W, H = 384, 216                                                  # 描画範囲 (液晶の周りにテーブルの場所を足した)
SCALE = 5                                                        # 1080p での 1 px の大きさ
LCD_X, LCD_Y, LCD_W, LCD_H = 64, 14, 256, 139                    # 液晶 (固定)
TEXTS = []                                                       # (x, y, 文字, 色, 寄せ) ── 1920×1080 の座標

WOOD = {"seam": (44, 27, 17), "d": (78, 49, 30), "m": (98, 62, 38), "l": (118, 77, 47), "h": (140, 96, 60)}


def text(x, y, s, col, align="l"):
    TEXTS.append((x, y, s, col, align))


def draw_table():
    """天板: 高さ 14 の板を横に張る。 板ごとに色味を少し変え、 木目の筋・継ぎ目・節・釘を描く。"""
    rnd = random.Random(7)
    PLANK = 14
    for p in range(H // PLANK + 1):
        y0 = p * PLANK
        tone = rnd.choice((-6, -3, 0, 3, 5))
        fill(0, y0, W, PLANK, tuple(max(0, min(255, c + tone)) for c in WOOD["m"]))
        for _ in range(32):                                        # 木目の筋 (途中で 1 段ずれる)
            gy = y0 + rnd.randint(2, PLANK - 2)
            gx = rnd.randint(-20, W)
            ln = rnd.randint(10, 70)
            col = WOOD["d"] if rnd.random() < 0.65 else WOOD["l"]
            step = rnd.randint(ln // 3, ln - 1)
            for i in range(ln):
                yy = gy + (1 if i > step and gy + 1 < y0 + PLANK - 1 else 0)
                if dith(gx + i, yy, 0.85): put(gx + i, yy, col)
        if rnd.random() < 0.5:                                     # 節
            kx, ky = rnd.randint(10, W - 10), y0 + PLANK // 2
            for i in range(-3, 4):
                for j in range(-2, 3):
                    r = (i / 3.2) ** 2 + (j / 2.2) ** 2
                    if 0.45 < r <= 1.0: put(kx + i, ky + j, WOOD["d"])
                    elif r <= 0.45: put(kx + i, ky + j, WOOD["seam"])
        hline(0, y0, W, WOOD["seam"]); hline(0, y0 + 1, W, WOOD["h"])   # 板の継ぎ目
        for _ in range(rnd.randint(1, 2)):                         # 板の端の突き合わせ + 釘
            jx = rnd.randint(20, W - 20)
            vline(jx, y0 + 1, PLANK - 1, WOOD["seam"]); vline(jx + 1, y0 + 2, PLANK - 2, WOOD["l"])
            put(jx - 2, y0 + 4, WOOD["seam"]); put(jx + 3, y0 + 4, WOOD["seam"])


def draw_lcd():
    """液晶: day.png (256×139) を 1:1 で。 敵は仮の影絵 (引き出し版と同じ位置)。"""
    cd.img.paste(Image.open(os.path.join(ROOT, "Assets", "Materials", "day.png")).convert("RGBA"), (LCD_X, LCD_Y))
    ox, oy = LCD_X + 120, LCD_Y + 64
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
    sword = [(5, 0), (6, 0), (4, 1), (5, 1), (6, 1), (3, 2), (4, 2), (5, 2), (0, 2), (1, 3), (2, 3), (3, 3), (4, 3),
             (2, 4), (3, 4), (1, 5), (3, 5), (0, 6), (4, 6)]
    ix, iy = ox - 1, oy - 13                                     # 攻撃予告 (Slay the Spire 式)
    outlined([(ix + a, iy + b) for a, b in sword] + glyph_pts(ix + 9, iy + 1, str(FOE_ATK)), LCD_WHITE)


PORTS_L, PORTS_R, PORT_Y = (60, 74, 88, 102), (272, 286, 300, 314), 177


def draw_body():
    """本体: カーキの横長の箱。 前面に通気口・ケーブルの端子・電源ランプ。 モニターはこの上に載る。"""
    shape(chamfer_m(48, 158, 288, 27, (3, 3, 0, 0)), KH, KH_T, seed=101, shadow=3)
    for k in range(3):
        hline(54, 165 + k * 3, 36, REC[0]); hline(54, 166 + k * 3, 36, KH["L"])
        hline(294, 165 + k * 3, 36, REC[0]); hline(294, 166 + k * 3, 36, KH["L"])
    for px in PORTS_L + PORTS_R:
        recess(px, PORT_Y, 7, 6); put(px + 3, PORT_Y + 2, STEEL["L"]); put(px + 3, PORT_Y + 3, STEEL["D"])
    fill(118, 178, 2, 2, C["chg"]); put(118, 178, C["chg_hi"])     # 電源ランプ


def draw_monitor():
    """モニター: 青鋼の筐体 + 黒いベゼル + 顎 (つまみ)。 本体の上に載る。"""
    lcd = rect_m(LCD_X, LCD_Y, LCD_W, LCD_H)
    shape(chamfer_m(LCD_X - 6, LCD_Y - 6, LCD_W + 12, LCD_H + 18, (4, 4, 0, 0)) - lcd, OLV, OLV_T, seed=102, shadow=3)
    shape(round_m(LCD_X - 3, LCD_Y - 3, LCD_W + 6, LCD_H + 6, 2) - lcd, BLACK_F, (BLACK[1], BLACK[0], BLACK[2]),
          seed=103, shadow=0, glint=True)
    hline(LCD_X, LCD_Y, LCD_W, K); vline(LCD_X, LCD_Y, LCD_H, K)
    hline(LCD_X, LCD_Y + 1, LCD_W, (0, 0, 0)); vline(LCD_X + 1, LCD_Y, LCD_H, (0, 0, 0))
    for kx in (290, 298, 306):                                     # 顎のつまみ
        shape(round_m(kx, LCD_Y + LCD_H + 4, 5, 5, 1), BLACK_F, (BLACK[2], BLACK[1], BLACK[3]), shadow=1, glint=True)
    dome(LCD_X - 4, LCD_Y + LCD_H + 6); dome(LCD_X + LCD_W + 2, LCD_Y + LCD_H + 6)


def draw_status(ox=4, oy=4):
    """ゴールド / 素材の小箱。"""
    shape(chamfer_m(ox, oy, 47, 16, (2, 2, 2, 2)), GUN, GUN_T, seed=11, shadow=3)
    recess(ox + 3, oy + 3, 26, 10)
    for k, ch in enumerate("0250"): tube(ox + 4 + k * 6, oy + 4, ch)
    recess(ox + 31, oy + 3, 14, 10)
    for k, ch in enumerate("07"): tube(ox + 32 + k * 6, oy + 4, ch)


def draw_reader(ox=6, oy=26):
    """武器と所持品の読み取り機 (上段 = 武器 32px、 下段 = 16px の所持品を 1 列で送る)。"""
    shape(chamfer_m(ox, oy, 44, 42, (2, 4, 0, 2)) | chamfer_m(ox, oy + 38, 33, 86, (0, 0, 4, 2)), GUN, GUN_T, seed=41, shadow=3)
    shape(chamfer_m(ox + 2, oy + 2, 40, 40, (0, 3, 0, 3)), OLV, OLV_T, seed=42, shadow=1)
    recess(ox + 4, oy + 4, 36, 36, floor=REC[0])
    paste_icon(WEAPON, ox + 6, oy + 6)
    recess(ox + 4, oy + 44, 20, 74, floor=REC[0])
    for k, name in enumerate(ITEMS[:4]):
        paste_icon(name, ox + 6, oy + 46 + k * 18)
        if k: hline(ox + 6, oy + 45 + k * 18, 16, REC[1])
    recess(ox + 25, oy + 44, 4, 74); fill(ox + 26, oy + 46, 2, 26, STEEL["M"]); put(ox + 26, oy + 46, STEEL["H"])
    steel(rect_m(ox + 29, oy + 100, 4, 6))                         # ケーブルの口


def draw_faces(ox=4, oy=190):
    """ダイスの面の小箱。"""
    shape(chamfer_m(ox, oy, 88, 19, (3, 3, 2, 2)), OLV, OLV_T, seed=51, shadow=3)
    recess(ox + 2, oy + 3, 84, 13)
    for n, (f, tier) in enumerate(FACES):
        cx, cy = ox + 4 + n * 8, oy + 5
        if tier:
            fill(cx, cy, 7, 7, C["amber"]); hline(cx, cy, 7, C["amber_hi"])
            hline(cx, cy + 6, 7, (170, 112, 40)); vline(cx + 6, cy + 1, 6, (200, 136, 50))
        else:
            fill(cx, cy, 7, 7, IVORY[2]); hline(cx, cy, 7, IVORY[3])
            hline(cx, cy + 6, 7, IVORY[0]); vline(cx + 6, cy + 1, 6, IVORY[1])
        for (r, c) in PIPS[f]: put(cx + 1 + c * 2, cy + 1 + r * 2, C["pip"])
        for t in range(tier): put(cx + 1 + t * 2, cy + 7, C["amber"])
    fill(ox + 82, oy + 5, 3, 8, STEEL["M"]); put(ox + 82, oy + 5, STEEL["H"])


def draw_enemy_sign(ox=138, oy=0):
    """敵 HP の札。 モニターの上端に鋼のクリップで挟んで留める。"""
    shape(chamfer_m(ox, oy, 107, 14, (0, 0, 4, 4)) | chamfer_m(ox + 28, oy + 11, 52, 10, (0, 0, 3, 3)),
          GUN, GUN_T, seed=21, shadow=3)
    for ax in (ox + 8, ox + 94):
        steel(rect_m(ax, oy + 12, 5, 4)); hline(ax, oy + 15, 5, K)
    recess(ox + 3, oy + 2, 26, 10)
    for k, ch in enumerate(f"{FOE_HP:04d}"): tube(ox + 4 + k * 6, oy + 3, ch)
    recess(ox + 31, oy + 2, 17, 10)
    for k, ch in enumerate(f"-{FOE_PRE}"): seg7(ox + 32 + k * 5, oy + 4, ch, C["atk"])
    recess(ox + 50, oy + 4, 53, 6)
    n = 12; lit = round(n * FOE_HP / FOE_MAX); after = round(n * (FOE_HP - FOE_PRE) / FOE_MAX)
    for s in range(n):
        x = ox + 52 + s * 4
        if s < after: fill(x, oy + 5, 3, 3, C["hp"]); put(x, oy + 5, C["hp_hi"])
        elif s < lit: fill(x, oy + 5, 3, 3, C["hp_pre"])
        else: fill(x, oy + 5, 3, 3, REC[2])
    recess(ox + 30, oy + 13, 48, 6, floor=REC[0])
    text((ox + 54) * SCALE, (oy + 13) * SCALE + 3, "蛇鱗の戦士", C["vfd"], "c")


def draw_dial(ox=334, oy=4):
    """エスカレーション計の箱 (カーキ)。 針 = 今のターン、 色帯 = 段階、 ランプ = 到達段階。"""
    shape(chamfer_m(ox, oy, 44, 30, (3, 3, 2, 2)), KH, KH_T, seed=71, shadow=3)
    cx, cy, R, TMAX = ox + 22.0, oy + 22.0, 15.0, 25
    band = [None, C["amber"], (232, 124, 52), (204, 60, 46), (122, 30, 30)]
    recess(ox + 4, oy + 4, 36, 20)
    for j in range(oy + 6, oy + 23):
        for i in range(ox + 6, ox + 39):
            dx, dy = i + 0.5 - cx, j + 0.5 - cy
            d = math.hypot(dx, dy)
            if j == oy + 22 and abs(dx) < R: put(i, j, K); continue
            if dy > 0 or d >= R: continue
            if d >= R - 1: put(i, j, K); continue
            t = (180 - math.degrees(math.atan2(-dy, dx))) / 180 * TMAX
            st = min(4, int(t // 5))
            if d >= R - 3.5 and band[st]: put(i, j, band[st])
            else: put(i, j, LABEL[2] if d < R - 5 else LABEL[1])
    for T in range(0, TMAX + 1, 5):
        a = math.radians(180 - T / TMAX * 180)
        for r in (9.0, 9.5, 10.0, 10.5, 11.0):
            put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), K)
    a = math.radians(180 - TURN / TMAX * 180)
    for k in range(0, 25):
        r = k * 0.5
        put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), RED["M"])
    fill(ox + 21, oy + 20, 2, 2, STEEL["L"]); put(ox + 21, oy + 20, STEEL["H"])
    put(ox + 11, oy + 13, LABEL[2]); put(ox + 12, oy + 12, (255, 255, 255))
    for s in range(4):
        fill(ox + 13 + s * 5, oy + 25, 3, 2, band[s + 1] if s < STAGE else REC[2])


def draw_gauges(ox=336, oy=40):
    """ゲージの塔 (自 HP の数値 + 被ダメ予測 + HP / シールド / 希望)。"""
    shape(chamfer_m(ox, oy, 42, 106, (3, 3, 3, 3)), GUN, GUN_T, seed=31, shadow=3)
    recess(ox + 3, oy + 3, 20, 10)
    for k, ch in enumerate(f"{HP:03d}"): tube(ox + 4 + k * 6, oy + 4, ch)
    recess(ox + 24, oy + 3, 15, 10)
    for k, ch in enumerate(f"-{HP_PRE}"): seg7(ox + 25 + k * 5, oy + 5, ch, C["hp_hi"])
    top = oy + 17
    gauge(ox + 5, 7, top, 27, round(27 * HP / HP_MAX), round(27 * (HP - HP_PRE) / HP_MAX), C["hp"], C["hp_hi"], C["hp_pre"])
    gauge(ox + 14, 5, top, 27, 0, 0, C["shield"], C["shield_hi"], C["shield_hi"])
    gauge(ox + 21, 6, top, 27, round(27 * HOPE / HOPE_MAX), round(27 * HOPE / HOPE_MAX), C["hope"], C["hope_hi"], C["hope_hi"])
    screw(ox + 32, oy + 20); screw(ox + 32, oy + 96)
    steel(rect_m(ox + 1, oy + 86, 4, 6))                           # ケーブルの口


def draw_board(ox=134, oy=170):
    """戦闘盤 (配線端子 + ソケット + 充電ゲージ)。 本体の前に置く。"""
    shape(chamfer_m(ox, oy, 117, 42, (5, 5, 2, 2)), GUN, GUN_T, seed=61, shadow=3)
    TX = {"atk": ox + 4, "blk": ox + 28, "chg": ox + 52}
    ty = oy + 4
    for n, t in enumerate(TERMS):
        tx = TX[t]
        shape(chamfer_m(tx, ty, 23, 11, (2, 0, 2, 0)), OLV, OLV_T, seed=50 + n)
        recess(tx + 2, ty + 1, 7, 9); icon(tx + 3, ty + 3, t, TERM_C[t])
        for k in range(2):
            recess(tx + 9 + k * 6, ty + 1, 6, 9)
            seg7(tx + 10 + k * 6, ty + 2, f"{TERM_SUM[t]:02d}"[k])
    shape(chamfer_m(ox + 76, ty, 23, 11, (2, 0, 2, 0)), OLV, OLV_T, seed=54)   # 特殊端子 (未購入 = 蓋)
    screw(ox + 86, ty + 4)
    by = oy + 16
    recess(ox + 4, by, 95, 7)
    SOCK = tuple(ox + 11 + 16 * k for k in range(5))
    ROWS = {"atk": by + 1, "blk": by + 3, "chg": by + 5}
    sock_x = [sx + 7 for sx in SOCK]; term_x = {t: TX[t] + 11 for t in TERMS}
    for t in TERMS:
        xs = [sock_x[i] for i, w in enumerate(WIRE) if w == t] + [term_x[t]]
        hline(min(xs), ROWS[t], max(xs) - min(xs) + 1, TERM_C[t])
        vline(term_x[t], by, ROWS[t] - by + 1, TERM_C[t])
    for i, w in enumerate(WIRE):
        vline(sock_x[i], ROWS[w], by + 7 - ROWS[w], TERM_C[w])
    sy = oy + 25
    for n, sx in enumerate(SOCK):
        recess(sx, sy, 14, 14)
        pips = set(PIPS[DICE[n]])
        for r in range(3):
            for c in range(3):
                lx, ly = sx + 3 + c * 3, sy + 3 + r * 3
                if (r, c) in pips: fill(lx, ly, 2, 2, C["amber"]); put(lx, ly, C["amber_hi"])
                else: fill(lx, ly, 2, 2, REC[2])
        shape(rect_m(sx + 11, sy - 2, 3, 3), BLACK_F, (BLACK[2], BLACK[1], BLACK[3]), shadow=1, glint=False)
        put(sx + 12, sy - 1, C["ghost"] if n in NO_REROLL else C["hope_hi"])
    sx = SOCK[2]
    for p in range(sx - 1, sx + 15): put(p, sy - 1, C["sel"]); put(p, sy + 14, C["sel"])
    for q in range(sy - 1, sy + 15): put(sx - 1, q, C["sel"]); put(sx + 14, q, C["sel"])
    put(sx + 12, sy - 1, C["hope_hi"])
    recess(ox + 103, oy + 5, 9, 32)
    for s in range(10):
        y = oy + 6 + (9 - s) * 3
        if s < CHG_NOW: fill(ox + 104, y, 7, 2, C["chg"]); hline(ox + 104, y, 7, C["chg_hi"])
        elif s < min(10, CHG_NOW + CHG_GAIN): fill(ox + 104, y, 7, 2, C["chg_pre"])
        else: fill(ox + 104, y, 7, 2, REC[2])


def draw_fight(ox=310, oy=184):
    """FIGHT! のボタン箱 (カーキ)。"""
    shape(chamfer_m(ox, oy, 68, 28, (4, 4, 2, 2)), KH, KH_T, seed=81, shadow=3)
    for j in range(oy + 20, oy + 25):
        for i in range(ox + 4, ox + 64):
            put(i, j, HAZ[0] if ((i + j) // 2) % 2 == 0 else HAZ[1])
    hline(ox + 4, oy + 19, 60, K); hline(ox + 4, oy + 25, 60, K)
    recess(ox + 4, oy + 3, 60, 14, floor=(90, 22, 20))
    fill(ox + 6, oy + 5, 56, 10, (228, 58, 46))
    hline(ox + 6, oy + 5, 56, (255, 132, 110)); hline(ox + 6, oy + 14, 56, (170, 38, 30))
    x = ox + 6 + (56 - 29) // 2
    for ch in "FIGHT!":
        rows = FIGHT[ch]
        for j, row in enumerate(rows):
            for i, v in enumerate(row):
                if v == "#": put(x + i, oy + 6 + j, (255, 236, 220))
        x += len(rows[0]) + 1


def draw_cables():
    """各周辺機器 → 本体の前面の端子。 テーブルの上を這わせる (機器の下に潜る所は隠れる)。"""
    py = PORT_Y + 3
    cable((37, 129), (PORTS_L[0] + 3, py), 8)                      # 読み取り機
    cable((50, 12), (PORTS_L[1] + 3, py), 16, clamps=(0.55,))     # ゴールド / 素材
    cable((70, 190), (PORTS_L[2] + 3, py), 4)                      # ダイスの面
    cable((134, 195), (PORTS_L[3] + 3, py), 4)                     # 戦闘盤
    cable((337, 129), (PORTS_R[3] + 3, py), 8)                     # ゲージの塔
    cable((312, 196), (PORTS_R[2] + 3, py), 4)                     # FIGHT
    cable((334, 30), (PORTS_R[0] + 3, py), 24, clamps=(0.5,))     # 計器
    cable((244, 6), (334, 14), 8)                                  # 敵の札 → 計器


def render():
    cd.W, cd.H = W, H                                              # 道具の境界判定を広げた描画範囲に合わせる
    cd.img = Image.new("RGBA", (W, H), (0, 0, 0, 255)); cd.PX = cd.img.load(); TEXTS.clear()
    draw_table()
    draw_cables()
    draw_body()
    draw_monitor()
    draw_lcd()
    draw_enemy_sign()
    draw_status()
    draw_reader()
    draw_faces()
    draw_dial()
    draw_gauges()
    draw_board()
    draw_fight()
    big = cd.img.convert("RGB").resize((W * SCALE, H * SCALE), Image.NEAREST)
    font = ImageFont.truetype(FONT_PATH, 12)
    for (x, y, s, col, align) in TEXTS:                            # 文字: 12px のドット字を 2 倍 (2×2 画面 px)
        w = int(font.getlength(s)) + 2
        t = Image.new("L", (w, 14), 0); d = ImageDraw.Draw(t); d.fontmode = "1"; d.text((0, 0), s, font=font, fill=255)
        t = t.resize((w * 2, 28), Image.NEAREST)
        if align == "c": x -= w
        big.paste(Image.new("RGB", t.size, col), (x, y), t)
    return big


HTML = """<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><title>DICE BOUND — テーブル版の筐体 (試作)</title>
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
  .bar { display:flex; gap:6px; align-items:center; margin:12px 0 8px; }
  button { font:inherit; background:var(--panel); color:var(--ink); border:1px solid var(--line); padding:3px 11px; border-radius:3px; cursor:pointer; }
  button[aria-pressed="true"] { background:var(--accent); color:var(--bg); border-color:var(--accent); }
  .stage { overflow:auto; border:1px solid var(--line); padding:16px; background:var(--grid); }
  .stage img { display:block; margin:0 auto; image-rendering:pixelated; }
</style></head><body><div class="doc">
<header><h1>DICE BOUND — テーブル版の筐体 (試作・別案)</h1>
<div class="sub">描画範囲 384×216 (1080p で 1 px = 5×5 画面 px) · 液晶 256×139 (固定) · 文字は 2×2 画面 px · 生成元 <code>Tools/cabinet_table.py</code> · 引き出し版は <a href="cabinet-drawers.html">cabinet-drawers.html</a></div></header>
<div class="warn"><b>試作であって仕様ではない。</b> 木のテーブルに本体とモニターを置き、 周辺機器 (引き出し版のタブ) を別々の箱にしてケーブルで本体の端子に繋いだ。
<b>液晶は 256×139 のまま</b>で、 テーブルの場所は描画範囲を 384×216 に広げて作った。 1080p では ×5、 4K では ×10 の整数倍 (720p / 1440p は整数にならない)。 光の層は無し。</div>
<div class="bar"><span style="color:var(--ink-3)">倍率</span><span id="zb"></span></div>
<div class="stage"><img id="pic" src="__SRC__" alt="テーブル版の筐体"></div>
<script>
const pic=document.getElementById('pic'), zb=document.getElementById('zb'); let zoom=0.5;
[[0.5,'1080p の 50%'],[0.75,'75%'],[1,'100% (実寸)']].forEach(([z,l])=>{const b=document.createElement('button'); b.textContent=l;
 b.setAttribute('aria-pressed',z===zoom); b.onclick=()=>{zoom=z; draw(); [...zb.children].forEach(c=>c.setAttribute('aria-pressed',c===b));}; zb.appendChild(b);});
function draw(){ pic.style.width=(1920*zoom)+'px'; pic.style.height=(1080*zoom)+'px'; } draw();
</script></div></body></html>"""


if __name__ == "__main__":
    dd = os.path.join(ROOT, "docs", "design")
    big = render()
    png = os.path.join(dd, "cabinet-table.png"); big.save(png)
    buf = _io.BytesIO(); big.save(buf, "PNG")
    with open(os.path.join(dd, "cabinet-table.html"), "w", encoding="utf-8") as f:
        f.write(HTML.replace("__SRC__", "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")))
    print(f"PNG : {os.path.relpath(png, ROOT)}  {big.width}×{big.height}")
