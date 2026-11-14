# 戦闘の画面の案「尺」: HP の棒そのものを盤にする。 480×270 の実寸・ゲームのドット字で描いた、 動く見本。 出力は Captures/ui_layout/tape/ (git 管理外)。
#   敵の HP = 上の尺 (敵の足もと)。 自分の HP = 下の尺 (画面の下の縁)。 出目を上へ運ぶと攻め (敵の尺に「削れる分」が付く)、 下へ運ぶと守り (自分の尺の「食われる分」を板が覆う)。
import os, math
from PIL import Image, ImageDraw, ImageFont
OUT = r"C:/Users/kumod/My project/Captures/ui_layout/tape"; os.makedirs(OUT, exist_ok=True)
W, H, S = 480, 270, 3
FONT = r"C:/Users/kumod/My project/Assets/Fonts/x8y12pxDenkiChip.ttf"
F12 = ImageFont.truetype(FONT, 12); F24 = ImageFont.truetype(FONT, 24)
BG, FLOOR, LINE = (22, 19, 20), (30, 26, 25), (44, 38, 34)
BONE, GOLD, GOLD2, RED, RED2, BLUE, BLUE2, DIM, DARK, CHG = (242, 236, 216), (244, 200, 92), (255, 232, 160), (220, 84, 62), (116, 38, 34), (96, 156, 224), (40, 72, 124), (120, 112, 104), (14, 12, 13), (214, 176, 70)

def stage():
    im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im)
    d.rectangle([0, 196, W, H], fill=FLOOR); d.line([(0, 196), (W, 196)], fill=LINE)
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0; d.line([(190, 76 + y), (289, 76 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((74, 68, 66), (34, 30, 30))))
    return im

def text(d, x, y, s, c, f=F12, anchor="la", shadow=True):
    d.fontmode = "1"
    if shadow: d.text((x + 1, y + 1), s, font=f, fill=DARK, anchor=anchor)
    d.text((x, y), s, font=f, fill=c, anchor=anchor)

PIPS = {1: [(1, 1)], 2: [(0, 0), (2, 2)], 3: [(0, 0), (1, 1), (2, 2)], 4: [(0, 0), (2, 0), (0, 2), (2, 2)], 5: [(0, 0), (2, 0), (1, 1), (0, 2), (2, 2)], 6: [(0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2)]}
def die(d, x, y, n, sz=18, ring=None, dim=False):
    x, y = int(round(x)), int(round(y)); c = (150, 144, 132) if dim else BONE
    d.rectangle([x, y, x + sz - 1, y + sz - 1], fill=c); d.rectangle([x, y + sz - 2, x + sz - 1, y + sz - 1], fill=(170, 162, 146) if not dim else (110, 104, 96))
    for px in ((x, y), (x + sz - 1, y), (x, y + sz - 1), (x + sz - 1, y + sz - 1)): d.point(px, fill=BG)
    g = sz // 4
    for (i, j) in PIPS[n]: cx, cy = x + g + i * (sz - 2 * g - 2) // 2, y + g - 1 + j * (sz - 2 * g - 2) // 2; d.rectangle([cx, cy, cx + 2, cy + 2], fill=DARK)
    if ring: d.rectangle([x - 2, y - 2, x + sz + 1, y + sz + 1], outline=ring)

def slot(d, x, y, kind, label, w=38, h=14, lit=False):
    c, c2 = (RED, RED2) if kind == "A" else (BLUE, BLUE2) if kind == "D" else (CHG, (90, 72, 26))
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=c2, outline=c if lit else tuple(int(v * 0.7) for v in c))
    text(d, x + w - 3, y + 1, label, BONE if lit else tuple(int(v * 0.75) for v in BONE), F12, "ra", False)

EX0, EX1, EY = 100, 380, 200      # 敵の尺: 左右と上端。 高さ 8
EDGE = 300                         # 敵の尺の「切り口」(残りの右端)。 残りは左へ続く
MX0, MX1, MY = 100, 380, 262      # 自分の尺
A_SLOTS = [(150, 211, "×4"), (192, 211, "×4"), (234, 211, "+6")]
D_SLOTS = [(150, 247, "×3"), (192, 247, "+5")]
C_SLOT = (44, 229, "+3")
TRAY = [(206, 228), (231, 228), (256, 228)]

def render(st):
    """st: dice=[(n, x, y, where)] where = None/'A0'/'A1'/'D0'…, cut=[幅…] 敵の尺の削れる分 (px), bite=食われる分 (px), cover=板 (px), hp_e=切り口のずれ, hp_m=自分の HP の右端, flash, shards=[...], cursor"""
    im = stage(); d = ImageDraw.Draw(im)
    # ---- 上の縁: 激化の灯り・袋
    for i in range(5): d.rectangle([222 + i * 8, 4, 227 + i * 8, 9], fill=RED if i < 1 else (50, 44, 42))
    text(d, 4, 2, "袋", DIM)
    # ---- 敵の尺
    edge = EDGE - st.get("hp_e", 0)
    d.rectangle([EX0, EY, EX1, EY + 7], fill=DARK, outline=LINE)
    d.rectangle([EX0 + 1, EY + 1, edge, EY + 6], fill=RED); d.line([(EX0 + 1, EY + 1), (edge, EY + 1)], fill=(244, 132, 104))
    for x in range(EX0 + 1, EX0 + 13, 2): d.line([(x, EY + 1), (x, EY + 6)], fill=DARK)     # 左へ続く (窓の外にまだある)
    for m in (208 - st.get("hp_e", 0) // 1,):                                                   # ダウンの目盛り
        if EX0 + 14 < m < edge: d.line([(m, EY - 2), (m, EY + 8)], fill=GOLD2); d.polygon([(m - 2, EY - 5), (m + 2, EY - 5), (m, EY - 2)], fill=GOLD2)
    d.line([(EX0, EY - 3), (EX1, EY - 3)], fill=LINE); d.line([(EX0, EY - 3), (EX0 + int(280 * st.get("frac_e", 0.62)), EY - 3)], fill=RED)   # 全体の細い線 (残りの割合)
    x = edge; tot = 0
    for i, wdt in enumerate(st.get("cut", [])):
        if wdt <= 0: continue
        d.rectangle([x - wdt + 1, EY + 1, x, EY + 6], fill=GOLD if i == 0 else GOLD2); d.line([(x - wdt + 1, EY + 1), (x - wdt + 1, EY + 6)], fill=DARK); x -= wdt; tot += wdt
    if tot:
        for xx in range(x - 14, x, 3): d.point((xx, EY + 1), fill=GOLD); d.point((xx, EY + 6), fill=GOLD)   # 会心ならここまで (点線)
        d.line([(x - 14, EY + 1), (x - 14, EY + 6)], fill=GOLD)
        text(d, (x + edge) // 2, EY - 16, str(st.get("dmg", 0)), GOLD2, F12, "ma")
    text(d, EX1 + 4, EY - 2, str(st.get("ehp", 884)), BONE)
    # ---- 自分の尺
    me = st.get("hp_m", 330)
    d.rectangle([MX0, MY, MX1, MY + 7], fill=DARK, outline=LINE)
    d.rectangle([MX0 + 1, MY + 1, me, MY + 6], fill=(92, 150, 96)); d.line([(MX0 + 1, MY + 1), (me, MY + 1)], fill=(150, 204, 150))
    bite, cover = st.get("bite", 0), st.get("cover", 0)
    if bite > 0:
        d.rectangle([me - bite + 1, MY + 1, me, MY + 6], fill=RED)
        for xx in range(me - bite + 1, me, 3): d.point((xx, MY + 2 + (xx % 2) * 3), fill=RED2)
        if cover > 0:
            cv = min(cover, bite); d.rectangle([me - cv + 1, MY - 1, me, MY + 8], fill=BLUE, outline=BONE)
        take = st.get("take", 0)
        text(d, me - bite + (bite - min(cover, bite)) // 2, MY - 14, str(take), RED if take else BLUE, F12, "ma")
    text(d, MX1 + 4, MY - 2, str(st.get("mhp", 82)), BONE)
    # ---- 口 (端子): 攻めは敵の尺の下・守りは自分の尺の上・充電は左
    used = {w for (_, _, _, w) in st["dice"] if w}
    for i, (x, y, lab) in enumerate(A_SLOTS): slot(d, x, y, "A", lab, lit=("A%d" % i) in used)
    for i, (x, y, lab) in enumerate(D_SLOTS): slot(d, x, y, "D", lab, lit=("D%d" % i) in used)
    slot(d, C_SLOT[0], C_SLOT[1], "C", C_SLOT[2], lit="C" in used)
    for i in range(10): d.rectangle([44 + i * 4, 247, 46 + i * 4, 249], fill=CHG if i < 3 else (54, 46, 30))
    text(d, 44, 253, "振り直す 3", DIM)
    # ---- 決める
    ready = st.get("ready", False)
    d.rectangle([404, 229, 462, 246], fill=(196, 96, 40) if ready else (70, 50, 38), outline=BONE if ready else DIM); text(d, 433, 232, "決める", BONE if ready else DIM, F12, "ma")
    # ---- 出目
    for (n, x, y, where) in st["dice"]:
        ring = RED if where and where[0] == "A" else BLUE if where and where[0] == "D" else None
        die(d, x, y, n, 14 if where else 18, ring)
    # ---- 砕ける破片・数字・白い光
    for (x, y, c) in st.get("shards", []): d.rectangle([int(x), int(y), int(x) + 2, int(y) + 2], fill=c)
    for (x, y, s_, c, big) in st.get("pops", []): text(d, int(x), int(y), s_, c, F24 if big else F12, "ma")
    if st.get("flash", 0) > 0:
        ov = Image.new("RGB", (W, H), (255, 250, 236)); im = Image.blend(im, ov, st["flash"]); d = ImageDraw.Draw(im)
    if st.get("cursor"):
        cx, cy = st["cursor"]; d.polygon([(cx, cy), (cx, cy + 9), (cx + 3, cy + 7), (cx + 6, cy + 11), (cx + 8, cy + 10), (cx + 5, cy + 6), (cx + 9, cy + 6)], fill=BONE, outline=DARK)
    return im

def lerp(a, b, u): return a + (b - a) * u
def ease(u): u = max(0.0, min(1.0, u)); return 1 - (1 - u) ** 3

def slot_pos(where):
    if where == "C": return (C_SLOT[0] + 2, C_SLOT[1])
    x, y, _ = (A_SLOTS if where[0] == "A" else D_SLOTS)[int(where[1])]; return (x + 2, y)

frames = []
def hold(st, n): frames.extend([render(st)] * n)
def move(st, idx, where, n=7, after=None):
    """出目 idx を口 where へ運ぶ。 after(st) で、 着いた時の見込みを更新"""
    nn, x0, y0, _ = st["dice"][idx]; x1, y1 = slot_pos(where)
    for k in range(n):
        u = ease((k + 1) / n); s2 = dict(st); s2["dice"] = list(st["dice"]); s2["dice"][idx] = (nn, lerp(x0, x1, u), lerp(y0, y1, u), None); s2["cursor"] = (lerp(x0, x1, u) + 10, lerp(y0, y1, u) + 8); frames.append(render(s2))
    st["dice"][idx] = (nn, x1, y1, where)
    if after: after(st)
    hold(st, 6)

# ---- 1 ターンの流れ
st = {"dice": [(5, TRAY[0][0], TRAY[0][1], None), (2, TRAY[1][0], TRAY[1][1], None), (4, TRAY[2][0], TRAY[2][1], None)], "cut": [], "bite": 0, "cover": 0, "take": 26, "dmg": 0}
for k in range(8):                               # 敵の次の一撃が、 自分の尺を食う (赤い分が伸びる)
    s2 = dict(st); s2["bite"] = int(73 * ease((k + 1) / 8)); frames.append(render(s2))
st["bite"] = 73; hold(st, 8)
def a0(s): s["cut"] = [40]; s["dmg"] = 20
move(st, 0, "A0", after=a0)
def a1(s): s["cut"] = [40, 16]; s["dmg"] = 28
move(st, 1, "A1", after=a1)
def d0(s): s["cover"] = 34; s["take"] = 14; s["ready"] = True
move(st, 2, "D0", after=d0)
hold(st, 8)
still = render(st)
# 決める: こちらの攻め ── 削れる分が、 尺から割れて落ちる
import random; random.seed(3)
sh = [[EDGE - random.uniform(0, 56), EY + random.uniform(0, 6), random.uniform(-1.2, 1.2), random.uniform(-2.6, -0.4), GOLD if random.random() < 0.6 else GOLD2] for _ in range(34)]
for k in range(14):
    s2 = dict(st); s2["cut"] = []; s2["dmg"] = 0; s2["hp_e"] = 56; s2["frac_e"] = 0.62 - 0.02 * min(1, k / 4); s2["ehp"] = 856; s2["flash"] = 0.7 if k == 0 else 0.25 if k == 1 else 0
    for q in sh: q[0] += q[2]; q[1] += q[3]; q[3] += 0.45
    s2["shards"] = [(q[0], q[1], q[4]) for q in sh if q[1] < 268]; s2["pops"] = [(240, 110 - k * 1.2, "-28", GOLD2, True)] if k < 12 else []
    frames.append(render(s2))
st["cut"] = []; st["dmg"] = 0; st["hp_e"] = 56; st["frac_e"] = 0.60; st["ehp"] = 856
# 敵の一撃: 板に当たって止まり、 覆えなかった分が砕ける
sh = [[330 - 34 - random.uniform(0, 39), MY + random.uniform(0, 6), random.uniform(-1.6, 1.0), random.uniform(-3.0, -0.6), RED if random.random() < 0.7 else RED2] for _ in range(26)]
for k in range(14):
    s2 = dict(st); s2["bite"] = 0; s2["cover"] = 0; s2["hp_m"] = 330 - 39; s2["mhp"] = 68; s2["flash"] = 0.35 if k == 0 else 0
    for q in sh: q[0] += q[2]; q[1] += q[3]; q[3] += 0.5
    s2["shards"] = [(q[0], q[1], q[4]) for q in sh if q[1] < 270]
    if k < 5: s2["shards"] += [(330 - 34 + i * 6, MY - 2 - k, BLUE) for i in range(6)]      # 板が弾いた火花
    s2["pops"] = [(292, 238 - k, "-14", RED, False)] if k < 12 else []
    s2["dice"] = [(n, x, y, w) for (n, x, y, w) in st["dice"]]
    frames.append(render(s2))
end = dict(st); end.update({"bite": 0, "cover": 0, "hp_m": 291, "mhp": 68, "ready": False}); hold(end, 10)

big = [f.resize((W * S, H * S), Image.NEAREST) for f in frames]
big[0].save(os.path.join(OUT, "turn.gif"), save_all=True, append_images=big[1:], duration=70, loop=0)
still.resize((W * S, H * S), Image.NEAREST).save(os.path.join(OUT, "still.png"))
still.save(os.path.join(OUT, "still_1x.png"))
# 説明つきの 1 枚
an = still.resize((W * S, H * S), Image.NEAREST).convert("RGB"); o = Image.new("RGB", (W * S, H * S + 0), (0, 0, 0)); o.paste(an); dd = ImageDraw.Draw(o); MF = ImageFont.truetype(r"C:/Windows/Fonts/meiryob.ttc", 24)
def call(x, y, tx, ty, s_, anchor="la"):
    dd.line([(x * S, y * S), (tx, ty)], fill=(255, 255, 255), width=2); bb = dd.textbbox((tx, ty), s_, font=MF, anchor=anchor); dd.rectangle([bb[0] - 6, bb[1] - 4, bb[2] + 6, bb[3] + 4], fill=(20, 20, 26), outline=(255, 255, 255)); dd.text((tx, ty), s_, font=MF, fill=(255, 255, 255), anchor=anchor)
call(272, 203, 760, 470, "この手で削れる分 (出目ごとに色が違う)")
call(238, 203, 250, 500, "会心なら、 ここまで", "la")
call(208, 198, 130, 440, "ダウンの目盛り", "la")
call(104, 203, 40, 560, "残りは窓の外へ続く", "la")
call(170, 218, 420, 660, "攻めの口 (敵の尺の下)", "ra")
call(170, 254, 420, 720, "守りの口 (自分の尺の上)", "ra")
call(313, 265, 1010, 720, "敵の一撃を、 板が覆った分", "la")
call(276, 265, 760, 760, "覆えずに食われる分", "la")
call(433, 238, 1200, 640, "全部置くと光る", "la")
call(60, 236, 60, 640, "充電の口", "la")
o.save(os.path.join(OUT, "annotated.png"))
print("frames", len(frames))
