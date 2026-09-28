#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""筐体の試作: 「液晶ほぼ全面 + 引き出しの情報タブ」方式 (2026-09-27)。 **試作であって仕様ではない**。

**絵の格子は画面全体で 1 つ: 320×180** (2026-09-27 に 240×135 から変更)。 液晶も筐体も同じ大きさのピクセル。
  720p ×4 / 1080p ×6 / 1440p ×8 / 4K ×12 と、 どの解像度でも整数倍になる。
  液晶は day.png と同じ 256×139 を中央に置き、 外は筐体の鋼板 (継ぎ目・リベット・通気口) + 黒いベゼル。
  計器は Highfleet 式に液晶との境をまたいで張り出す (帯に整列させない)。 計器は液晶の上に張り出す機械で、 輪郭は面取り・段・耳で崩し、 材質を混ぜ、 ケーブルで繋ぐ。
  液晶の外に帯を作って計器を整列させた版は「モニターの額縁」に見えて撤回 (2026-09-27)。
**文字だけは例外で 3 倍細かい格子 (960×540)** に置く (2026-09-27 決定)。 字はドット字にして絵の格子と馴染ませる。
  仮の字は MS ゴシック 12px の埋め込みビットマップ (配布不可)。 本番は PixelMplus12 / DotGothic16 等の自由な字に替える。
  筐体に世界観の文字を刻まない (画中画の原則) ので、 文字は印刷ではなく**表示器 (琥珀の蛍光表示) に出す**。

発想: Highfleet のように液晶 (ゲーム世界) を画面いっぱいに置き、 手前に物理的な引き出し
(情報タブ) を被せる。 Papers, Please のように引き出して使う。
  ・引き出しは、 演出や表示が変わるときに自動で開き、 プレイヤーも好きに開け閉めできる
  ・敵の攻撃予告は液晶の中 (敵の頭上に剣と数字)。 エスカレーションは筐体側のアナログ計器
  ・敵をクリックすると照準が付き、 右の引き出しの下から敵の詳細が出る (状態 "target")
  ・所持品: 武器は 32px の専用スロット、 その下に 16px のアイテムが 1 列で並び送れる。 握りで名前と効果が出る (状態 "items")

色は Highfleet 寄りの冷たい工業色 (真鍮と革は使わない)。 光の層は無し。
形の規則は Slots and Daggers の分析 (メモリ project-cabinet-art-style-sd) を低解像度向けに縮めたもの:
  枠 = 縁取り 1 + 帯 1 (上左が明・下右が暗) / 面は平ら (まばらな点だけ) / きらめきは角に集める。

使い方: python Tools/cabinet_drawers.py
  → docs/design/cabinet-drawers.png (戦闘・標準) / cabinet-drawers-target.png (敵を選択) / cabinet-drawers.html
  PNG は文字込みの 960×540 (絵の 1 px = 3×3)。 比較用に 240×135 版の最終画像を cabinet-drawers-240.png に残す。
"""
import base64
import io as _io
import math
import os
from PIL import Image, ImageDraw, ImageFont

W, H = 320, 180                     # 絵の格子 (液晶と共通)
TS = 3                              # 文字の格子の倍率 (960×540)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONT_PATH = "C:/Windows/Fonts/msgothic.ttc"          # 仮の字 (上の注意を参照)

# ── 色: Highfleet 寄りの冷たい工業色 ────────────────────────
K = (16, 18, 19)
OLV = {"D": (58, 66, 70), "M": (82, 92, 97), "L": (106, 117, 122), "H": (176, 186, 190)}    # 枠 (青みの鋼板)
OLV_T = ((80, 89, 94), (71, 80, 85), (90, 99, 104))
GUN = {"D": (30, 34, 37), "M": (44, 49, 53), "L": (60, 66, 71), "H": (116, 126, 132)}       # 引き出しの面
GUN_T = ((41, 46, 50), (35, 40, 43), (49, 54, 58))
STEEL = {"D": (72, 80, 86), "M": (116, 125, 131), "L": (162, 170, 174), "H": (222, 228, 230)}
BLACK = ((10, 11, 12), (22, 24, 26), (36, 39, 42), (62, 66, 70))
BLACK_F = {"D": BLACK[0], "M": BLACK[1], "L": BLACK[3], "H": BLACK[3]}
RED = {"D": (96, 26, 22), "M": (158, 44, 34), "L": (206, 76, 56), "H": (244, 152, 124)}
HAZ = ((214, 168, 40), (20, 20, 20))
LABEL = ((170, 168, 156), (210, 208, 196), (234, 232, 222))
REC = ((11, 12, 13), (19, 21, 23), (31, 34, 37))
IVORY = ((120, 114, 94), (174, 168, 144), (216, 210, 188), (240, 236, 220))

# 状態の色 (2026-09-27 試案): 端子の種類 = そのゲージと同じ色 / 振り直し可 = 希望の黄 /
# 選択中 = 青緑 / 予測 = 同じ色の淡い版 (実機では点滅) / 押せない = 消灯
C = {
    "amber": (236, 170, 66), "amber_hi": (255, 230, 158), "ghost": (44, 40, 32),
    "nixie": (255, 146, 48), "nixie_hi": (255, 216, 152),
    "hp": (206, 52, 58), "hp_hi": (250, 128, 128), "hp_pre": (246, 190, 186),
    "atk": (240, 112, 44), "atk_hi": (255, 186, 130),
    "shield": (96, 158, 204), "shield_hi": (176, 218, 240),
    "hope": (230, 198, 100), "hope_hi": (255, 238, 176),
    "chg": (160, 214, 70), "chg_hi": (222, 248, 160), "chg_pre": (78, 104, 44),   # 得る分は暗く (失う分は淡く)
    "sel": (96, 214, 200), "spine": (160, 60, 46), "pip": (58, 52, 42),
    "vfd": (255, 176, 84), "vfd_dim": (196, 120, 56), "vfd_hi": (255, 222, 160),
}
TERM_C = {"atk": C["atk"], "blk": C["shield"], "chg": C["chg"]}
LCD_INK, LCD_WHITE = (16, 14, 20), (248, 244, 232)      # 液晶の中の記号 (予告など)
LCD_FOE = {"o": (20, 24, 38), "b": (46, 56, 80), "l": (86, 100, 128), "e": (255, 190, 90)}

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
img = PX = None
TEXT = []                           # (x, y, 文字, 色, 寄せ) ── x, y は文字の格子 (960×540)


def inb(x, y): return 0 <= x < W and 0 <= y < H
def put(x, y, c):
    if inb(x, y): PX[x, y] = (*c, 255)
def fill(x, y, w, h, c):
    for j in range(y, y + h):
        for i in range(x, x + w): put(i, j, c)
def hline(x, y, w, c): fill(x, y, w, 1, c)
def vline(x, y, h, c): fill(x, y, 1, h, c)
def dith(i, j, t): return t > (BAYER[j % 4][i % 4] + 0.5) / 16.0
def mul(x, y, f):
    if inb(x, y):
        c = PX[x, y]; PX[x, y] = (int(c[0] * f), int(c[1] * f), int(c[2] * f), 255)
def h32(x, y, s=0):
    v = (x * 73856093) ^ (y * 19349663) ^ (s * 83492791)
    return (v ^ (v >> 13)) * 1274126177 & 0xFFFFFFFF


# ════════════════════════════════════════════════════════════
#  形 (マスク) ── 任意の輪郭を描く道具
# ════════════════════════════════════════════════════════════
def rect_m(x, y, w, h):
    return {(i, j) for j in range(y, y + h) for i in range(x, x + w)}


def round_m(x, y, w, h, r):
    """角を落とした矩形。 r=1 は角 1 px、 r>=2 は円弧で落とす。"""
    m = rect_m(x, y, w, h)
    if r <= 0: return m
    if r == 1:
        for p in ((x, y), (x + w - 1, y), (x, y + h - 1), (x + w - 1, y + h - 1)): m.discard(p)
        return m
    for (cx, cy) in ((x + r - 1, y + r - 1), (x + w - r, y + r - 1), (x + r - 1, y + h - r), (x + w - r, y + h - r)):
        for j in range(cy - r + 1, cy + r):
            for i in range(cx - r + 1, cx + r):
                corner = (i < x + r - 1 or i > x + w - r) and (j < y + r - 1 or j > y + h - r)
                if corner and (i - cx) ** 2 + (j - cy) ** 2 > (r - 0.5) ** 2:
                    m.discard((i, j))
    return m


def face_px(x, y, pal, seed):
    """面: 平らな地 + まばらな点。
    旧版は 14×14 のタイルの四隅に L 字を刻んでいたが、 この粒度では「#」に見えて雑音になったので撤去 (2026-09-27)。"""
    base, cut, dot = pal
    r = h32(x, y, seed) % 997
    if r < 7: return dot
    if r < 10: return cut
    return base


def shape(mask, fam, tpal, seed=0, shadow=1, glint=True):
    """任意の輪郭の部品。 縁取り 1 + 帯 1 (上左が明・下右が暗) + 面 + 落とし影。"""
    if not mask: return
    if shadow:
        for (x, y) in mask:
            for k in range(1, shadow + 1):
                q = (x + k, y + k)
                if q not in mask and (k == 1 or dith(q[0], q[1], 0.5)):
                    mul(q[0], q[1], 0.50 if k == 1 else 0.72)
    edge = {p for p in mask if any((p[0] + dx, p[1] + dy) not in mask for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}
    for (x, y) in mask:
        if (x, y) in edge:
            put(x, y, K); continue
        lit = (x, y - 1) in edge or (x - 1, y) in edge
        drk = (x, y + 1) in edge or (x + 1, y) in edge
        if lit and not drk: put(x, y, fam["L"])
        elif drk and not lit: put(x, y, fam["D"])
        elif lit and drk: put(x, y, fam["M"])
        else: put(x, y, face_px(x, y, tpal, seed))
    if glint:                                    # きらめき: 左上の角に 1 点
        for (x, y) in mask:
            if (x, y) not in edge and (x - 1, y) in edge and (x, y - 1) in edge:
                put(x, y, fam["H"])


def recess(x, y, w, h, floor=None):
    """窪み: 縁取り 1 + 底。 上の内側に影、 下右の縁が光る。"""
    fill(x, y, w, h, floor or REC[1])
    hline(x, y, w, K); vline(x, y, h, K)
    hline(x + 1, y + h - 1, w - 1, GUN["L"]); vline(x + w - 1, y + 1, h - 1, GUN["M"])
    if w > 3 and h > 3:
        hline(x + 1, y + 1, w - 2, REC[0])


def screw(x, y, fam=OLV):
    """ネジ 3×3。"""
    for j, row in enumerate(["KMK", "MHD", "KDK"]):
        for i, v in enumerate(row): put(x + i, y + j, K if v == "K" else fam[v])


def dome(x, y, fam=OLV):
    """リベット 2×2。"""
    put(x, y, fam["H"]); put(x + 1, y, fam["M"]); put(x, y + 1, fam["M"]); put(x + 1, y + 1, K)


def grip(x, y, w, h, vertical=False):
    """引き手 (黒いベークライトの握り)。 溝を刻む。"""
    fill(x, y, w, h, BLACK[2])
    for (i, j) in ((x, y), (x + w - 1, y), (x, y + h - 1), (x + w - 1, y + h - 1)): put(i, j, K)
    if vertical:
        vline(x, y + 1, h - 2, BLACK[3]); vline(x + w - 1, y + 1, h - 2, BLACK[0])
        for j in range(y + 2, y + h - 2, 2): hline(x + 1, j, w - 2, BLACK[1])
    else:
        hline(x + 1, y, w - 2, BLACK[3]); hline(x + 1, y + h - 1, w - 2, BLACK[0])
        for i in range(x + 2, x + w - 2, 2): vline(i, y + 1, h - 2, BLACK[1])


def steel(mask, shadow=1):
    shape(mask, STEEL, (STEEL["M"], STEEL["D"], STEEL["L"]), shadow=shadow, glint=False)


def text(x, y, s, color=None, align="l"):
    """文字は 4 倍の格子に置く。 x, y は文字の格子の座標。"""
    TEXT.append((x, y, s, color or C["vfd"], align))


# ── 数字・記号 ──────────────────────────────────────────────
GLYPH = {
    "0": ["###", "#.#", "#.#", "#.#", "###"], "1": [".#.", "##.", ".#.", ".#.", "###"],
    "2": ["###", "..#", "###", "#..", "###"], "3": ["###", "..#", "###", "..#", "###"],
    "4": ["#.#", "#.#", "###", "..#", "..#"], "5": ["###", "#..", "###", "..#", "###"],
    "6": ["###", "#..", "###", "#.#", "###"], "7": ["###", "..#", "..#", ".#.", ".#."],
    "8": ["###", "#.#", "###", "#.#", "###"], "9": ["###", "#.#", "###", "..#", "###"],
}
SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc",
       "7": "abc", "8": "abcdefg", "9": "abcdfg", "-": "g", " ": ""}


def tube(x, y, ch):
    """ニキシー管 5×8: ガラスの中に 3×5 の字。"""
    fill(x, y, 5, 8, REC[1]); put(x, y, K); put(x + 4, y, K)
    for j, row in enumerate(GLYPH[ch]):
        for i, v in enumerate(row):
            if v == "#": put(x + 1 + i, y + 2 + j, C["nixie"])
    put(x + 1, y + 2, C["nixie_hi"])


def seg7(x, y, ch, col=None):
    """7 セグ 4×7。 消灯セグメントも残像で描く。"""
    on = SEG[ch]; col = col or C["amber"]
    parts = {"a": [(x + 1, y), (x + 2, y)], "f": [(x, y + 1), (x, y + 2)], "b": [(x + 3, y + 1), (x + 3, y + 2)],
             "g": [(x + 1, y + 3), (x + 2, y + 3)], "e": [(x, y + 4), (x, y + 5)], "c": [(x + 3, y + 4), (x + 3, y + 5)],
             "d": [(x + 1, y + 6), (x + 2, y + 6)]}
    for k, pts in parts.items():
        for (i, j) in pts: put(i, j, col if k in on else C["ghost"])


# 出目の目 (3×3 の升目。 (行, 列))。 出目パーツは 1〜9 なので 7〜9 もある
PIPS = {1: [(1, 1)], 2: [(0, 0), (2, 2)], 3: [(0, 0), (1, 1), (2, 2)], 4: [(0, 0), (0, 2), (2, 0), (2, 2)],
        5: [(0, 0), (0, 2), (1, 1), (2, 0), (2, 2)], 6: [(0, 0), (1, 0), (2, 0), (0, 2), (1, 2), (2, 2)]}
PIPS[7] = PIPS[6] + [(1, 1)]
PIPS[8] = [(r, c) for r in range(3) for c in range(3) if (r, c) != (1, 1)]
PIPS[9] = [(r, c) for r in range(3) for c in range(3)]

ICON = {                                      # 端子の絵板 5×5 (メカの記号。 字は刻まない)
    "atk": ["....#", "...#.", "#.#..", ".#...", "#.#.."],
    "blk": ["#####", "#####", "#####", ".###.", "..#.."],
    "chg": ["..##.", ".##..", "#####", "..##.", ".##.."],
}


def icon(x, y, key, col):
    for j, row in enumerate(ICON[key]):
        for i, v in enumerate(row):
            if v == "#": put(x + i, y + j, col)


def outlined(pts, col):
    """液晶の中の記号: 白い塗り + 8 近傍の黒い縁 (Slay the Spire の予告のように背景から浮かせる)。"""
    s = set(pts)
    for (x, y) in s:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if (x + dx, y + dy) not in s: put(x + dx, y + dy, LCD_INK)
    for (x, y) in s: put(x, y, col)


def glyph_pts(x, y, s):
    pts = []
    for n, ch in enumerate(s):
        for j, row in enumerate(GLYPH[ch]):
            for i, v in enumerate(row):
                if v == "#": pts.append((x + n * 4 + i, y + j))
    return pts


# ════════════════════════════════════════════════════════════
#  場面の値 (試作用の 1 場面: 4 層「蛇鱗の戦士」戦・T7)
#    敵攻撃 = round(1.2 × (基礎 11 + 出目 5)) = 19  (T5〜 の段階 1)
#    配線: 出目 5 3 6 7 4 → 攻撃 7+6 = 13 / ブロック 3+4 = 7 / 充電 5
#    被ダメ予測 = 19 − 7 = 12 (HP 68 → 56) / 与ダメ予測 = 13 + 武器 6 − 硬鱗 2 = 17 (640 → 623)
# ════════════════════════════════════════════════════════════
TURN, STAGE = 7, 1
FOE_ATK = 19
FOE_HP, FOE_MAX, FOE_PRE = 640, 918, 17
HP, HP_MAX, HP_PRE = 68, 100, 12
HOPE, HOPE_MAX = 16, 30
DICE = (5, 3, 6, 7, 4)
WIRE = ("chg", "blk", "atk", "atk", "blk")               # ダイスごとの接続先
NO_REROLL = {3}                                          # 出目パーツ T1 の面 (7) に止まった = 振り直せない
TERMS = ("atk", "blk", "chg")
TERM_SUM = {t: sum(d for d, w in zip(DICE, WIRE) if w == t) for t in TERMS}
CHG_NOW, CHG_GAIN = 4, TERM_SUM["chg"]
FACES = [(f, 0) for f in range(1, 7)] + [(7, 1), (4, 3), (9, 2), (2, 4)]   # 素の 6 面 + 出目パーツ (出目, Tier)
WEAPON = "鍛鉄の剣"
ITEMS = ["工廠の材料箱", "無限モーター", "呼雷粉", "逆さ避雷針", "過負荷チューナー"]  # 見えている 5 品 (続きは送り)

ICON_DIR = os.path.join(ROOT, "Assets", "Resources", "Icons", "Items")


def item_desc(name):
    import json
    for it in json.load(open(os.path.join(ROOT, "Assets", "Data", "InventorySystem", "items.json"), encoding="utf-8-sig"))["items"]:
        if it["id"] == name: return it.get("description", "")
    return ""


def paste_icon(name, x, y):
    img.alpha_composite(Image.open(os.path.join(ICON_DIR, name + ".png")).convert("RGBA"), (x, y))


def vfd_lines(x0, y, lines, maxw, max_lines=99):
    """表示器の文字 (文字の格子)。 幅で折り返し、 max_lines を超えたら … で切る。"""
    font = ImageFont.truetype(FONT_PATH, 12)
    for s, col in lines:
        if not s: y += 7; continue
        out = []
        while s:
            n = len(s)
            while n > 1 and font.getlength(s[:n]) > maxw: n -= 1
            out.append(s[:n]); s = (" " + s[n:]) if n < len(s) else ""
        if len(out) > max_lines:
            out = out[:max_lines]; out[-1] = out[-1][:-1] + "…"
        for ln in out:
            text(x0, y, ln, col); y += 15
    return y


# ════════════════════════════════════════════════════════════
#  部品 ── Highfleet 式 (2026-09-27 改訂)
#    液晶はほぼ全面。 計器は液晶の上に張り出す「機械」で、 輪郭は面取り (45°) ・段・張り出した耳で崩す。
#    材質を混ぜる (ガンメタルの本体 / 青鋼の小板 / カーキの筐体 / 鋼の金具)、 ケーブルで繋ぐ。
#    帯に整列させた版 (額縁) は「モニターの額縁」に見えて奥行きが消えたので撤回。
# ════════════════════════════════════════════════════════════
KH = {"D": (58, 58, 46), "M": (82, 82, 64), "L": (108, 106, 84), "H": (168, 164, 134)}           # カーキの筐体
KH_T = ((78, 78, 61), (70, 70, 55), (88, 87, 69))


def chamfer_m(x, y, w, h, c=(0, 0, 0, 0)):
    """面取りした矩形。 c = (左上, 右上, 右下, 左下) の切り落としの大きさ (45°)。"""
    tl, tr, br, bl = c
    m = set()
    for j in range(y, y + h):
        for i in range(x, x + w):
            u, v, ru, rv = i - x, j - y, x + w - 1 - i, y + h - 1 - j
            if (tl and u + v < tl) or (tr and ru + v < tr) or (br and ru + rv < br) or (bl and u + rv < bl):
                continue
            m.add((i, j))
    return m


def plate(x, y, w, h):
    """白い銘板 (字は刻まない。 黒い線 1 本だけ)。"""
    fill(x, y, w, h, LABEL[1]); hline(x, y, w, LABEL[2]); hline(x, y + h - 1, w, LABEL[0])
    for (i, j) in ((x, y), (x + w - 1, y), (x, y + h - 1), (x + w - 1, y + h - 1)): put(i, j, K)
    if h >= 4: hline(x + 2, y + h // 2, max(1, w // 2), K)


def cable(p0, p1, sag, clamps=()):
    """ケーブル (黒いゴム 5 px) を垂らす。 影 → 縁 → 芯 → 上の艶 → 留め具の順に描く。"""
    (x0, y0), (x1, y1) = p0, p1
    n = int(max(abs(x1 - x0), abs(y1 - y0)) * 3) + 1
    pts = []
    for k in range(n + 1):
        t = k / n
        pts.append((round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t + sag * 4 * t * (1 - t))))
    core, body = set(), set()
    for (x, y) in pts:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1): core.add((x + dx, y + dy))
    for (x, y) in core:
        for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)): body.add((x + dx, y + dy))
    for (x, y) in body:
        for k in (2, 3):
            if (x + 1, y + k) not in body: mul(x + 1, y + k, 0.55 if k == 2 else 0.75)
    for (x, y) in body: put(x, y, K)
    for (x, y) in core: put(x, y, BLACK[1])
    for (x, y) in core:                                          # 上の艶 (灰の 1 列)
        if (x, y - 1) not in core and (x, y - 1) in body: put(x, y, BLACK[3])
    for t in clamps:
        x, y = pts[int(t * n)]
        for dx in (0, 1, 2):
            vline(x + dx, y - 3, 7, (STEEL["L"], STEEL["M"], STEEL["D"])[dx])
        put(x, y - 3, STEEL["H"]); hline(x, y + 4, 3, K)


LCD_X, LCD_Y, LCD_W, LCD_H = 32, 20, 256, 139                 # 液晶 = day.png の大きさそのまま (引き延ばさない)


def draw_lcd(target):
    """液晶: day.png (256×139) を 1:1 で中央に置く。 外は筐体 (draw_cabinet) が覆うので何も描かない。"""
    img.paste(Image.open(os.path.join(ROOT, "Assets", "Materials", "day.png")).convert("RGBA"), (LCD_X, LCD_Y))
    ox, oy = 152, 84                                             # 敵 (仮): 槍を持つ蜥蜴の戦士
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
    if target:                                                   # 照準 (選択中 = 青緑)
        x0, y0, x1, y1 = ox - 5, oy - 6, ox + 19, oy + 21
        for (cx, cy, sx, sy) in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):
            for k in range(4):
                put(cx + sx * k, cy, C["sel"]); put(cx, cy + sy * k, C["sel"])
                put(cx + sx * k + sx, cy + sy, LCD_INK); put(cx + sx, cy + sy * k + sy, LCD_INK)


def draw_cabinet():
    """液晶の外 = 筐体の鋼板 + 液晶のベゼル (黒いベークライト)。
    鋼板は帯として計器を並べる場所ではなく「面」: 継ぎ目・リベット・通気口で質感だけ持たせ、 計器は液晶との境をまたいで張り出す。"""
    lcd = rect_m(LCD_X, LCD_Y, LCD_W, LCD_H)
    shape(rect_m(0, 0, W, H) - lcd, OLV, OLV_T, seed=1, shadow=0)
    for x in (16, 303):                                           # 継ぎ目 (縦): 暗い溝 + 右に明るい縁
        vline(x, 1, H - 2, OLV["D"]); vline(x + 1, 1, H - 2, OLV["L"])
    for y in (9, 170):                                            # 継ぎ目 (横)
        hline(1, y, W - 2, OLV["D"]); hline(1, y + 1, W - 2, OLV["L"])
    for x in (16, 303):
        for y in (9, 170): screw(x - 1, y - 1)
    for i in range(24, W - 20, 12):                               # リベットの列 (上下の縁)
        dome(i, 3); dome(i, H - 5)
    for j in range(20, H - 16, 12):                               # リベットの列 (左右の縁)
        dome(3, j); dome(W - 5, j)
    for k in range(5):                                            # 通気口 (左下・右上)
        hline(5, 150 + k * 3, 8, REC[0]); hline(5, 151 + k * 3, 8, OLV["L"])
        hline(307, 24 + k * 3, 8, REC[0]); hline(307, 25 + k * 3, 8, OLV["L"])
    bz = round_m(LCD_X - 4, LCD_Y - 4, LCD_W + 8, LCD_H + 8, 3) - lcd
    shape(bz, BLACK_F, (BLACK[1], BLACK[0], BLACK[2]), seed=2, shadow=1, glint=True)
    hline(LCD_X, LCD_Y, LCD_W, K); vline(LCD_X, LCD_Y, LCD_H, K)  # 液晶の縁の影 (上・左)
    hline(LCD_X, LCD_Y + 1, LCD_W, (0, 0, 0)); vline(LCD_X + 1, LCD_Y, LCD_H, (0, 0, 0))


def draw_status():
    """左上: ゴールド / 素材 (横 1 列)。 角に留めた板。 余白は右のゲージの引き出しに揃えて 3〜4 px。"""
    shape(chamfer_m(0, 0, 49, 16, (0, 0, 5, 0)), GUN, GUN_T, seed=11, shadow=2)
    recess(3, 3, 26, 10)
    for k, ch in enumerate("0250"): tube(4 + k * 6, 4, ch)
    recess(31, 3, 14, 10)
    for k, ch in enumerate("07"): tube(32 + k * 6, 4, ch)


def draw_log():
    """上: 記録 (閉じた引き出し。 取っ手だけ)。"""
    shape(chamfer_m(68, 0, 32, 8, (0, 0, 3, 3)), GUN, GUN_T, seed=6, shadow=2)
    grip(76, 3, 16, 3)


def draw_enemy_sign():
    """上: 敵 HP の札。 鋼の腕で上から吊る。 HP のニキシー 4 桁 + 与ダメ予測 + 目盛り + 名前の表示器。"""
    for ax in (118, 198):                                          # 吊り腕
        steel(rect_m(ax, 0, 4, 9)); screw(ax, 5, STEEL)
    shape(chamfer_m(106, 7, 107, 14, (0, 0, 4, 4)) | chamfer_m(134, 18, 52, 10, (0, 0, 3, 3)), GUN, GUN_T, seed=21, shadow=2)
    recess(109, 9, 26, 10)
    for k, ch in enumerate(f"{FOE_HP:04d}"): tube(110 + k * 6, 10, ch)
    recess(137, 9, 17, 10)                                         # 与ダメ予測 (点滅)
    for k, ch in enumerate(f"-{FOE_PRE}"): seg7(138 + k * 5, 11, ch, C["atk"])
    recess(156, 11, 53, 6)
    n = 12; lit = round(n * FOE_HP / FOE_MAX); after = round(n * (FOE_HP - FOE_PRE) / FOE_MAX)
    for s in range(n):
        x = 158 + s * 4
        if s < after: fill(x, 12, 3, 3, C["hp"]); put(x, 12, C["hp_hi"])
        elif s < lit: fill(x, 12, 3, 3, C["hp_pre"])
        else: fill(x, 12, 3, 3, REC[2])
    recess(136, 20, 48, 6, floor=REC[0])                           # 名前 (琥珀の表示器)
    text(160 * TS, 20 * TS + 3, "蛇鱗の戦士", C["vfd"], "c")


def draw_dial():
    """右上: エスカレーション計 (アナログ)。 カーキの筐体を角に据え、 左下を面取り。"""
    shape(chamfer_m(278, 0, 42, 31, (0, 0, 0, 7)), KH, KH_T, seed=71, shadow=2)
    cx, cy, R, TMAX = 299.0, 22.0, 15.0, 25
    band = [None, C["amber"], (232, 124, 52), (204, 60, 46), (122, 30, 30)]
    recess(282, 4, 35, 20)
    for j in range(6, 23):
        for i in range(283, 316):
            dx, dy = i + 0.5 - cx, j + 0.5 - cy
            d = math.hypot(dx, dy)
            if j == 22 and abs(dx) < R: put(i, j, K); continue
            if dy > 0 or d >= R: continue
            if d >= R - 1: put(i, j, K); continue
            t = (180 - math.degrees(math.atan2(-dy, dx))) / 180 * TMAX
            st = min(4, int(t // 5))
            if d >= R - 3.5 and band[st]: put(i, j, band[st])
            else: put(i, j, LABEL[2] if d < R - 5 else LABEL[1])
    for T in range(0, TMAX + 1, 5):                               # 目盛り (5 ターンごと)
        a = math.radians(180 - T / TMAX * 180)
        for r in (9.0, 9.5, 10.0, 10.5, 11.0):
            put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), K)
    a = math.radians(180 - TURN / TMAX * 180)                     # 針
    for k in range(0, 25):
        r = k * 0.5
        put(int(cx + r * math.cos(a)), int(cy - r * math.sin(a)), RED["M"])
    fill(298, 20, 2, 2, STEEL["L"]); put(298, 20, STEEL["H"])      # 軸
    put(288, 13, LABEL[2]); put(289, 12, (255, 255, 255))          # ガラスのきらめき
    for s in range(4):                                             # 到達段階のランプ
        fill(290 + s * 5, 26, 3, 2, band[s + 1] if s < STAGE else REC[2])


def gauge(gx, gw, top, n, lit, after, c, hi, pre):
    """縦の目盛り (下から点灯)。 after〜lit の間は予測で消える分 (淡い色・実機では点滅)。"""
    recess(gx, top, gw, n * 3 + 2)
    for s in range(n):
        y = top + 1 + (n - 1 - s) * 3
        if s < after: fill(gx + 1, y, gw - 2, 2, c); hline(gx + 1, y, gw - 2, hi)
        elif s < lit: fill(gx + 1, y, gw - 2, 2, pre)
        else: fill(gx + 1, y, gw - 2, 2, REC[2])


def draw_right():
    """右: ゲージの引き出し (右の縁から張り出す)。 上に HP の数値の小板が左へ段になって出る。 左の耳が握り。"""
    m = (chamfer_m(288, 44, 32, 100, (7, 0, 0, 7)) | chamfer_m(264, 40, 40, 15, (4, 0, 0, 4))
         | chamfer_m(281, 84, 9, 30, (3, 0, 0, 3)))
    shape(m, GUN, GUN_T, seed=31, shadow=2)
    recess(267, 42, 20, 10)
    for k, ch in enumerate(f"{HP:03d}"): tube(268 + k * 6, 43, ch)
    recess(288, 42, 15, 10)                                        # 被ダメ予測 (点滅)
    for k, ch in enumerate(f"-{HP_PRE}"): seg7(289 + k * 5, 44, ch, C["hp_hi"])
    grip(283, 90, 3, 18, vertical=True)
    gauge(292, 7, 58, 27, round(27 * HP / HP_MAX), round(27 * (HP - HP_PRE) / HP_MAX),
          C["hp"], C["hp_hi"], C["hp_pre"])
    gauge(300, 5, 58, 27, 0, 0, C["shield"], C["shield_hi"], C["shield_hi"])
    gauge(306, 6, 58, 27, round(27 * HOPE / HOPE_MAX), round(27 * HOPE / HOPE_MAX), C["hope"], C["hope_hi"], C["hope_hi"])
    screw(314, 60); screw(314, 132)
    for hy in (50, 136):                                           # 蝶番
        steel(rect_m(316, hy, 4, 5))


def draw_left():
    """左: 所持の引き出し。 上段 = 武器 32px の専用スロット、 下段 = 16px の所持品を 1 列で送る (幅を細らせて段にする)。"""
    m = (chamfer_m(0, 19, 44, 44, (0, 5, 0, 0)) | chamfer_m(0, 58, 33, 83, (0, 0, 5, 0))
         | chamfer_m(31, 86, 8, 30, (0, 3, 3, 0)))
    shape(m, GUN, GUN_T, seed=41, shadow=2)
    grip(33, 92, 3, 18, vertical=True)
    shape(chamfer_m(2, 21, 40, 40, (0, 3, 0, 3)), OLV, OLV_T, seed=42, shadow=1)   # 武器の座 (青鋼)
    recess(4, 23, 36, 36, floor=REC[0])
    paste_icon(WEAPON, 6, 25)
    recess(4, 63, 20, 74, floor=REC[0])
    for k, name in enumerate(ITEMS[:4]):
        paste_icon(name, 6, 65 + k * 18)
        if k: hline(6, 64 + k * 18, 16, REC[1])
    recess(25, 63, 4, 74); fill(26, 65, 2, 26, STEEL["M"]); put(26, 65, STEEL["H"])   # 送り
    for hy in (24, 130):                                           # 蝶番
        steel(rect_m(0, hy, 3, 5))


def draw_faces():
    """左下: ダイスの面 (5 個共通の面表)。 素の 6 面 = 白、 パーツ = 琥珀、 下の点 = Tier。 増えたら送る。"""
    shape(chamfer_m(0, 164, 94, 16, (0, 6, 0, 0)), OLV, OLV_T, seed=51, shadow=2)
    recess(3, 166, 87, 12)
    for n, (f, tier) in enumerate(FACES):
        cx, cy = 5 + n * 8, 168
        if tier:
            fill(cx, cy, 7, 7, C["amber"]); hline(cx, cy, 7, C["amber_hi"])
            hline(cx, cy + 6, 7, (170, 112, 40)); vline(cx + 6, cy + 1, 6, (200, 136, 50))
        else:
            fill(cx, cy, 7, 7, IVORY[2]); hline(cx, cy, 7, IVORY[3])
            hline(cx, cy + 6, 7, IVORY[0]); vline(cx + 6, cy + 1, 6, IVORY[1])
        for (r, c) in PIPS[f]: put(cx + 1 + c * 2, cy + 1 + r * 2, C["pip"])
        for t in range(tier): put(cx + 1 + t * 2, cy + 7, C["amber"])
    fill(86, 168, 3, 8, STEEL["M"]); put(86, 168, STEEL["H"])      # 送り


def draw_board():
    """下: 戦闘盤 (下から引き上げる)。 肩を面取り、 両脇にレール、 上に取っ手の台。"""
    m = (chamfer_m(102, 135, 117, 45, (6, 6, 0, 0)) | chamfer_m(138, 130, 46, 8, (3, 3, 0, 0))
         | rect_m(98, 147, 5, 33) | rect_m(218, 147, 5, 33))
    shape(m, GUN, GUN_T, seed=61, shadow=2)
    steel(rect_m(141, 131, 3, 5)); steel(rect_m(178, 131, 3, 5))
    grip(145, 132, 32, 3)
    TX = {"atk": 106, "blk": 130, "chg": 154}
    for n, t in enumerate(TERMS):                                  # 配線端子: 絵板 + 合計 2 桁
        tx = TX[t]
        shape(chamfer_m(tx, 139, 23, 11, (2, 0, 2, 0)), OLV, OLV_T, seed=50 + n)
        recess(tx + 2, 140, 7, 9); icon(tx + 3, 142, t, TERM_C[t])
        for k in range(2):
            recess(tx + 9 + k * 6, 140, 6, 9)
            seg7(tx + 10 + k * 6, 141, f"{TERM_SUM[t]:02d}"[k])
    shape(chamfer_m(178, 139, 23, 11, (2, 0, 2, 0)), OLV, OLV_T, seed=54)   # 特殊端子 (未購入 = 蓋)
    screw(188, 143)
    recess(106, 151, 95, 7)                                        # 配線 (経路 LED・見せ方は要相談)
    SOCK = (113, 129, 145, 161, 177)
    ROWS = {"atk": 152, "blk": 154, "chg": 156}
    sock_x = [sx + 7 for sx in SOCK]; term_x = {t: TX[t] + 11 for t in TERMS}
    for t in TERMS:
        xs = [sock_x[i] for i, w in enumerate(WIRE) if w == t] + [term_x[t]]
        hline(min(xs), ROWS[t], max(xs) - min(xs) + 1, TERM_C[t])
        vline(term_x[t], 151, ROWS[t] - 151 + 1, TERM_C[t])
    for i, w in enumerate(WIRE):
        vline(sock_x[i], ROWS[w], 158 - ROWS[w], TERM_C[w])
    for n, sx in enumerate(SOCK):                                  # ダイスソケット ×5
        recess(sx, 160, 14, 14)
        pips = set(PIPS[DICE[n]])
        for r in range(3):
            for c in range(3):
                lx, ly = sx + 3 + c * 3, 163 + r * 3
                if (r, c) in pips: fill(lx, ly, 2, 2, C["amber"]); put(lx, ly, C["amber_hi"])
                else: fill(lx, ly, 2, 2, REC[2])
        shape(rect_m(sx + 11, 158, 3, 3), BLACK_F, (BLACK[2], BLACK[1], BLACK[3]), shadow=1, glint=False)
        put(sx + 12, 159, C["ghost"] if n in NO_REROLL else C["hope_hi"])   # 振り直し: 黄 = 押せる
    sx = SOCK[2]                                                   # 選択中のソケット (青緑の枠)
    for p in range(sx - 1, sx + 15): put(p, 159, C["sel"]); put(p, 174, C["sel"])
    for q in range(159, 175): put(sx - 1, q, C["sel"]); put(sx + 14, q, C["sel"])
    put(sx + 12, 159, C["hope_hi"])
    recess(205, 140, 9, 32)                                        # 充電ゲージ (暗い分 = 得る予測)
    for s in range(10):
        y = 141 + (9 - s) * 3
        if s < CHG_NOW: fill(206, y, 7, 2, C["chg"]); hline(206, y, 7, C["chg_hi"])
        elif s < min(10, CHG_NOW + CHG_GAIN): fill(206, y, 7, 2, C["chg_pre"])
        else: fill(206, y, 7, 2, REC[2])


FIGHT = {
    "F": ["#####", "#....", "####.", "#....", "#....", "#....", "#...."],
    "I": ["###", ".#.", ".#.", ".#.", ".#.", ".#.", "###"],
    "G": [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
    "H": ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "T": ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
    "!": ["#", "#", "#", "#", "#", ".", "#"],
}


def draw_fight(lit=True):
    """右下: 決定ボタン。 カーキの筐体 (左上を面取り) に横長で赤く光る「FIGHT!」。"""
    shape(chamfer_m(250, 152, 70, 28, (6, 0, 0, 0)), KH, KH_T, seed=81, shadow=2)
    for j in range(171, 176):                                      # 黄黒の警告縞
        for i in range(254, 316):
            put(i, j, HAZ[0] if ((i + j) // 2) % 2 == 0 else HAZ[1])
    hline(254, 170, 62, K); hline(254, 176, 62, K)
    recess(254, 155, 62, 14, floor=(90, 22, 20) if lit else REC[1])   # 縁の赤み = 光の漏れ (色で描く)
    fill(256, 157, 58, 10, (228, 58, 46) if lit else RED["D"])
    hline(256, 157, 58, (255, 132, 110) if lit else RED["M"])
    hline(256, 166, 58, (170, 38, 30) if lit else (70, 18, 16))
    x = 256 + (58 - 29) // 2
    for ch in "FIGHT!":
        rows = FIGHT[ch]
        for j, row in enumerate(rows):
            for i, v in enumerate(row):
                if v == "#": put(x + i, 158 + j, (255, 236, 220) if lit else RED["M"])
        x += len(rows[0]) + 1


def draw_cables():
    """ケーブル: 機械どうしを繋いで、 液晶の上に「置かれた物」の感じを出す。"""
    cable((212, 14), (279, 18), 9, clamps=(0.5,))                  # 敵の札 → 計器
    cable((221, 166), (251, 170), 3)                               # 戦闘盤 → 決定ボタン
    cable((36, 126), (99, 160), 10, clamps=(0.45,))                # 所持 → 戦闘盤


def draw_enemy_detail():
    """敵の詳細: 敵をクリック → 照準 → 右のゲージの引き出しの下から滑り出る (琥珀の表示器)。 高さは中身に合わせる。"""
    shape(chamfer_m(184, 50, 104, 58, (6, 0, 0, 6)) | chamfer_m(178, 66, 8, 26, (3, 0, 0, 3)), GUN, GUN_T, seed=91, shadow=2)
    grip(180, 70, 3, 18, vertical=True)
    recess(188, 53, 97, 52, floor=REC[0])
    fill(281, 55, 2, 48, REC[1]); fill(281, 55, 2, 20, STEEL["M"])  # 送り
    vfd_lines(188 * TS + 6, 53 * TS + 6, [
        ("蛇鱗の戦士", C["vfd_hi"]),
        (f"HP {FOE_HP}/{FOE_MAX}", C["vfd"]),
        ("攻撃 11 + 出目 1〜7", C["vfd"]),
        (f"T{TURN}  段階1 ×1.2  次の段階まで3T", C["vfd"]),
        ("", None),
        ("硬鱗", C["vfd_hi"]),
        (" 受けるダメージを-2（最低0）", C["vfd_dim"]),
        ("尾撃", C["vfd_hi"]),
        (" ロール敗北時、相手に1の固定ダメージ", C["vfd_dim"]),
    ], maxw=90 * TS - 12)


def draw_item_names():
    """所持品の名前と効果: 左の引き出しの耳を引く → その下から滑り出る。 行はアイコンの列と揃える。"""
    shape(chamfer_m(22, 60, 124, 81, (0, 6, 6, 0)) | chamfer_m(144, 86, 8, 30, (0, 3, 3, 0)), GUN, GUN_T, seed=95, shadow=2)
    grip(146, 92, 3, 18, vertical=True)
    recess(38, 63, 104, 74, floor=REC[0])
    for k, name in enumerate(ITEMS[:4]):
        if k: hline(39, 63 + k * 18, 102, REC[1])
        vfd_lines(38 * TS + 6, (65 + k * 18) * TS, [(name, C["vfd_hi"]), (item_desc(name), C["vfd_dim"])],
                  maxw=102 * TS - 12, max_lines=2)


# ════════════════════════════════════════════════════════════
#  出力
# ════════════════════════════════════════════════════════════
STATES = {
    "combat": ("戦闘 (標準)", "cabinet-drawers.png"),
    "target": ("敵を選択", "cabinet-drawers-target.png"),
    "items": ("所持品を開く", "cabinet-drawers-items.png"),
}


def render(state):
    global img, PX
    img = Image.new("RGBA", (W, H), (0, 0, 0, 255)); PX = img.load(); TEXT.clear()
    draw_lcd(target=(state == "target"))
    draw_cabinet()
    draw_cables()
    draw_log()
    draw_enemy_sign()
    draw_dial()
    draw_board()
    draw_fight(lit=True)
    draw_faces()
    # 手で引き出したものは戦闘盤より手前。 ただし出てきた元の引き出しよりは奥 (その下から滑り出る)
    if state == "target": draw_enemy_detail()
    draw_right()
    if state == "items": draw_item_names()
    draw_left()
    draw_status()
    big = img.convert("RGB").resize((W * TS, H * TS), Image.NEAREST)
    d = ImageDraw.Draw(big); d.fontmode = "1"
    font = ImageFont.truetype(FONT_PATH, 12)
    for (x, y, s, col, align) in TEXT:
        if align == "c": x -= int(font.getlength(s)) // 2
        d.text((x, y), s, font=font, fill=col)
    return big


TABS = [
    ("所持 (左上)", "ゴールド / 素材のニキシー (横 1 列)。 角に留めた板", (0, 0, 49, 16)),
    ("所持品 (左・引き出し)", "武器 32px の専用スロット + 16px の所持品を 1 列で送る。 右の耳を引くと名前と効果が出る", (0, 19, 44, 122)),
    ("ダイスの面 (左下)", "5 個共通の面表。 素の 6 面 = 白、 出目パーツ = 琥珀、 下の点 = Tier。 増えたら送る", (0, 164, 94, 16)),
    ("記録 (上・閉)", "取っ手だけ見えている", (68, 0, 32, 8)),
    ("敵 HP (上・吊り)", "鋼の腕で吊る札。 HP のニキシー 4 桁 + 与ダメ予測 (橙・点滅) + 目盛り + 名前の表示器", (106, 0, 107, 28)),
    ("エスカレーション計 (右上)", "カーキの筐体のアナログ計。 針 = 今のターン、 色帯 = 段階 (T5/10/15/20)、 ランプ = 到達段階", (278, 0, 42, 31)),
    ("ゲージ (右・引き出し)", "自 HP のニキシー + 被ダメ予測 (点滅) + HP / シールド / 希望。 左の耳が握り", (264, 40, 56, 104)),
    ("戦闘盤 (下・引き上げ)", "端子 攻撃 / ブロック / 充電 + 特殊 (蓋)、 配線、 ソケット ×5 と振り直し (黄 = 押せる)、 充電ゲージ", (98, 130, 125, 50)),
    ("決定ボタン (右下)", "カーキの筐体に横長で赤く光る FIGHT!", (250, 152, 70, 28)),
]
TABS_BY_STATE = {
    "target": [("敵の詳細 (右の引き出しの下から)", "敵をクリック → 照準 → 滑り出る。 名前 / HP / 攻撃の内訳 / 段階 / パッシブ。 送りでスクロール", (178, 50, 110, 58))],
    "items": [("所持品の名前と効果 (左の引き出しの下から)", "左の耳を引くと、 アイコンと同じ行に名前と効果 (2 行で切る)。 列と一緒に送る", (22, 60, 130, 81))],
}

HTML = """<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><title>DICE BOUND — 引き出し式の筐体 (試作)</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
  :root { --bg:#f8f7f2; --ink:#1a1a1a; --ink-2:#4a4a4a; --ink-3:#7a7a7a; --line:#d4d1c8; --grid:#e8e5db;
          --panel:#fff; --accent:#a15a2a; --dim:#2b6cb0; }
  @media (prefers-color-scheme: dark) { :root { --bg:#14161a; --ink:#e8e6e0; --ink-2:#b5b3ad; --ink-3:#7d7a72;
          --line:#333740; --grid:#262a32; --panel:#1c1f24; --accent:#e2c58a; --dim:#7ea1d1; } }
  * { box-sizing:border-box; } html,body { margin:0; padding:0; background:var(--bg); color:var(--ink); }
  body { font-family:"SF Mono","Consolas","Hiragino Kaku Gothic ProN","Yu Gothic",monospace; font-size:13px; line-height:1.6; }
  .doc { max-width:1220px; margin:0 auto; padding:32px 24px 80px; }
  header { border-bottom:2px solid var(--ink); padding-bottom:16px; margin-bottom:28px; }
  h1 { font-size:22px; margin:0 0 4px; } header .sub { color:var(--ink-3); font-size:12px; }
  h2 { font-size:15px; margin:36px 0 12px; padding-bottom:4px; border-bottom:1px solid var(--line); color:var(--accent);
       display:flex; gap:8px; align-items:baseline; }
  h2 .num { background:var(--accent); color:var(--bg); padding:2px 8px; font-size:11px; }
  code { background:var(--panel); border:1px solid var(--line); padding:1px 5px; font-size:12px; }
  .card { background:var(--panel); border:1px solid var(--line); padding:14px; border-radius:4px; margin:12px 0; }
  .warn { background:var(--panel); border-left:3px solid var(--accent); padding:10px 14px; margin:12px 0; font-size:12px; }
  .bar { display:flex; flex-wrap:wrap; gap:16px; align-items:center; margin:12px 0 8px; }
  .bar label { display:inline-flex; gap:5px; align-items:center; color:var(--ink-2); cursor:pointer; }
  .grp { display:inline-flex; gap:6px; align-items:center; }
  button { font:inherit; background:var(--panel); color:var(--ink); border:1px solid var(--line); padding:3px 11px; border-radius:3px; cursor:pointer; }
  button[aria-pressed="true"] { background:var(--accent); color:var(--bg); border-color:var(--accent); }
  .stage { overflow:auto; border:1px solid var(--line); padding:16px; background:var(--grid); }
  .frame { position:relative; margin:0 auto; }
  .frame img { display:block; width:100%; height:100%; image-rendering:pixelated; }
  .zone { position:absolute; outline:1px solid var(--dim); outline-offset:-1px; }
  .zone b { position:absolute; top:0; left:0; background:var(--dim); color:#fff; font-size:9px; line-height:1.2; padding:0 2px; font-weight:400; white-space:nowrap; }
  .caption { text-align:center; color:var(--ink-3); font-size:11px; margin-top:8px; }
  table { border-collapse:collapse; width:100%; font-size:12px; margin:8px 0; }
  th,td { border:1px solid var(--line); padding:5px 9px; text-align:left; vertical-align:top; }
  td.n { font-variant-numeric:tabular-nums; white-space:nowrap; }
  .sw { display:inline-block; width:10px; height:10px; border:1px solid var(--line); vertical-align:-1px; margin-right:4px; }
</style></head><body><div class="doc">
<header><h1>DICE BOUND — 引き出し式の筐体 (試作)</h1>
<div class="sub">絵の格子 320×180 (液晶も筐体も共通・1080p で 1 px = 6×6 画面 px) · 文字だけ 3 倍の格子 (960×540) · 生成元 <code>Tools/cabinet_drawers.py</code></div></header>
<div class="warn"><b>試作であって仕様ではない。</b> 採用されたら <a href="cabinet-layout.html">cabinet-layout.html</a> を差し替える。
液晶は day.png (256×139) の大きさそのままで、 外は筐体の鋼板と黒いベゼル。 場面は 4 層「蛇鱗の戦士」戦の T7 (敵攻撃 19 = 1.2 × (11 + 出目 5)、 配線は攻撃 13 / ブロック 7 / 充電 5)。 敵の絵は仮の影絵。
予測の淡い色は実機では点滅。 文字は仮に MS ゴシック 12px の埋め込みビットマップ (配布できないので本番は PixelMplus12 / DotGothic16 等に替える)。</div>
<h2><span class="num">§1</span> 表示</h2>
<div class="bar"><span class="grp"><span style="color:var(--ink-3)">状態</span><span id="sb"></span></span>
<span class="grp"><span style="color:var(--ink-3)">倍率</span><span id="zb"></span></span>
<label><input type="checkbox" id="zones"> 引き出しの範囲を重ねる</label></div>
<div class="stage"><div class="frame" id="frame"><img id="pic" alt="引き出し式の筐体"><div id="ov"></div></div></div>
<div class="caption" id="cap"></div>
<h2><span class="num">§2</span> 引き出し</h2>
<div class="card"><table><tr><th>引き出し</th><th>中身・開け方</th><th>範囲 (x, y, w, h)</th></tr>__ROWS__</table></div>
<h2><span class="num">§3</span> 決まったこと (2026-09-27)</h2>
<div class="card"><table>
<tr><th>項目</th><th>決定</th></tr>
<tr><td>敵の攻撃予告</td><td>液晶の中。 敵の頭上に剣の記号と数字 (Slay the Spire 式)</td></tr>
<tr><td>エスカレーション</td><td>筐体側のアナログ計 (針 + 段階の色帯 + 到達ランプ)</td></tr>
<tr><td>収支の予測 / HP の数値 / 充電ゲージ / 振り直し / 端子の種類</td><td>入れる (上の図)</td></tr>
<tr><td>状態・メーター・役</td><td>液晶の中の数値で出す。 筐体では扱わない</td></tr>
<tr><td>敵の詳細</td><td>敵をクリック → 照準 → 右の引き出しの下から滑り出る</td></tr>
<tr><td>格子</td><td>320×180 (240×135 から変更)。 720p / 1080p / 1440p / 4K のすべてで整数倍</td></tr>
<tr><td>形</td><td>Highfleet 式: 液晶はほぼ全面、 計器は液晶の上に張り出す機械。 面取り・段・耳で輪郭を崩し、 材質を混ぜ (ガンメタル / 青鋼 / カーキ / 鋼)、 ケーブルで繋ぐ。 帯に整列させた版は額縁に見えて撤回</td></tr>
<tr><td>所持品</td><td>武器だけ 32px の専用スロット。 その下に 16px のアイテムを 1 列で並べて送る。 耳を引くと同じ行に名前と効果</td></tr>
<tr><td>パッシブ</td><td>個数は数えない。 引き出しをスクロールして見る</td></tr>
<tr><td>場面ごとの引き出し構成</td><td>作らない。 場面の違いは液晶の仕事</td></tr>
<tr><td>引き出しの開閉</td><td>演出や表示が変わるときは自動で開く。 プレイヤーもいつでも開け閉めできる。 全部開けて液晶が隠れるのはプレイヤーの選択</td></tr>
<tr><td>文字</td><td>絵の格子の例外として 3 倍細かい格子 (960×540) に置く。 ドット字。 筐体の上では表示器 (琥珀の蛍光表示) に出す</td></tr>
<tr><td>決定ボタン</td><td>横長で赤く光る FIGHT!</td></tr>
<tr><td>面の刻み</td><td>四隅の L 字 (「#」に見えた) を撤去。 面は平ら + まばらな点</td></tr>
<tr><td>要相談</td><td>配線 (どのダイスをどの端子に繋いだか) の見せ方。 試作は仮に経路を端子の色で点灯</td></tr>
</table></div>
<h2><span class="num">§4</span> 状態の色 (試案)</h2>
<div class="card"><table><tr><th>色</th><th>意味</th></tr>
<tr><td><span class="sw" style="background:#f0702c"></span>橙</td><td>攻撃端子・与ダメ予測</td></tr>
<tr><td><span class="sw" style="background:#609ecc"></span>青</td><td>ブロック端子 (シールドと同じ色)</td></tr>
<tr><td><span class="sw" style="background:#a0d646"></span>黄緑</td><td>充電端子・充電ゲージ</td></tr>
<tr><td><span class="sw" style="background:#ce343a"></span>赤</td><td>HP (自分・敵とも)</td></tr>
<tr><td><span class="sw" style="background:#e6c664"></span>黄</td><td>希望・振り直しボタンが押せる</td></tr>
<tr><td><span class="sw" style="background:#60d6c8"></span>青緑</td><td>選択中 (ソケット・照準)</td></tr>
<tr><td><span class="sw" style="background:#f6beba"></span>淡い色</td><td>予測で失う分 (実機では点滅)</td></tr>
<tr><td><span class="sw" style="background:#4e682c"></span>暗い色</td><td>予測で得る分 (充電)</td></tr>
<tr><td><span class="sw" style="background:#2c2820"></span>消灯</td><td>押せない・未到達</td></tr>
</table></div>
<h2><span class="num">§5</span> 材質で色を分ける</h2>
<div class="card">枠 = 青みの鋼板 / 引き出しの面 = 暗いガンメタル / 金具 = 磨いた鋼 / 握り・つまみ = 黒いベークライト / 計器の文字盤 = 白 /
小板 = 青鋼 / 計器・ボタンの筐体 = カーキ / ケーブル = 黒いゴム / 決定ボタン = 赤 + 黄黒の警告縞 (Highfleet に寄せた冷たい工業色。 真鍮と革は使わない)。 輪郭は矩形の重ねではなく<b>マスク</b>で任意の形に描く。</div>
<script>
const S=__STATES__; const fr=document.getElementById('frame'), ov=document.getElementById('ov'), cap=document.getElementById('cap'),
 zb=document.getElementById('zb'), sb=document.getElementById('sb'), pic=document.getElementById('pic');
let zoom=1, st=Object.keys(S)[0];
function group(el, items, cur, on){ el.innerHTML=''; items.forEach(([k,l])=>{const b=document.createElement('button'); b.textContent=l;
 b.setAttribute('aria-pressed',k===cur); b.onclick=()=>{on(k); group(el,items,k,on);}; el.appendChild(b);}); }
group(zb, [[1,'1080p の 50%'],[1.5,'75%'],[2,'100% (実寸)']], zoom, z=>{zoom=z; draw();});
group(sb, Object.entries(S).map(([k,v])=>[k,v.name]), st, k=>{st=k; draw();});
document.getElementById('zones').onchange=draw;
function draw(){ const s=S[st], k=960*zoom/s.w; pic.src=s.src; fr.style.width=(960*zoom)+'px'; fr.style.height=(540*zoom)+'px';
 cap.textContent=s.w+'×'+s.h+' (1080p で 1 px = '+(1920/s.w)+'×'+(1920/s.w)+' 画面 px) を 1080p の '+(zoom*50)+'% で表示'; ov.innerHTML='';
 if(!document.getElementById('zones').checked) return;
 for(const [n,x,y,w,h] of S[st].zones){ const e=document.createElement('div'); e.className='zone';
  e.style.cssText=`left:${x*k}px;top:${y*k}px;width:${w*k}px;height:${h*k}px`; e.innerHTML='<b>'+n+'</b>'; ov.appendChild(e);} }
draw();
</script></div></body></html>"""


if __name__ == "__main__":
    import json
    dd = os.path.join(ROOT, "docs", "design")
    states = {}
    for key, (name, fn) in STATES.items():
        big = render(key)
        path = os.path.join(dd, fn); big.save(path)
        buf = _io.BytesIO(); big.save(buf, "PNG")
        tabs = TABS + TABS_BY_STATE.get(key, [])
        states[key] = {"name": name, "w": W, "h": H, "src": "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii"),
                       "zones": [[n, *r] for n, _, r in tabs]}
        print(f"PNG : {os.path.relpath(path, ROOT)}  {big.width}×{big.height}")
    rows = "".join(f"<tr><td>{n}</td><td>{d}</td><td class='n'>{r[0]}, {r[1]}, {r[2]}, {r[3]}</td></tr>"
                   for n, d, r in TABS + sum(TABS_BY_STATE.values(), []))
    old = os.path.join(dd, "cabinet-drawers-240.png")          # 比較用 (240×135 版の最終画像)
    if os.path.exists(old):
        with open(old, "rb") as f:
            states["old240"] = {"name": "比較: 240×135 版", "w": 240, "h": 135, "zones": [],
                                "src": "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")}
    htm = os.path.join(dd, "cabinet-drawers.html")
    with open(htm, "w", encoding="utf-8") as f:
        f.write(HTML.replace("__ROWS__", rows).replace("__STATES__", json.dumps(states, ensure_ascii=False)))
    print(f"HTML: {os.path.relpath(htm, ROOT)}")
