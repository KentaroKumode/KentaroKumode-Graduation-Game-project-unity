# 戦闘の画面の案「場」: 表示のための領域を取らない。 敵だけが場面に居て、 表示は敵・床・手前に散らす (板も枠も帯も無い・狙いを結ぶ線も無い・自機の絵も無い)。
# 480×270 の実寸・ゲームのドット字。 出力は Captures/ui_layout/scene/ (git 管理外)。
import os, random
from PIL import Image, ImageDraw, ImageFont
ROOT = r"C:/Users/kumod/My project"
OUT = ROOT + "/Captures/ui_layout/scene"; os.makedirs(OUT, exist_ok=True)
W, H, S = 480, 270, 3
FONT = ROOT + "/Assets/Fonts/x8y12pxDenkiChip.ttf"
F12 = ImageFont.truetype(FONT, 12); F24 = ImageFont.truetype(FONT, 24)
BONE, GOLD, GOLD2, RED, RED2, BLUE, BLUE2, DIM, DARK, CHG, GRN = (242, 236, 216), (244, 200, 92), (255, 232, 160), (228, 88, 64), (116, 38, 34), (110, 170, 236), (40, 72, 124), (150, 140, 128), (10, 8, 10), (232, 190, 80), (104, 170, 108)
BACK = Image.open(ROOT + "/docs/design/battle-assets/backdrop.png").convert("RGB")
FOE = Image.open(ROOT + "/docs/design/battle-assets/enemy_orc.png").convert("RGBA")
FX, FY = 240 - 64, 84            # 敵の左上 (足もとは y=204 あたり)

def text(d, x, y, s, c, f=F12, anchor="la"):
    d.fontmode = "1"
    for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1), (1, 1), (-1, 1), (1, -1), (-1, -1)): d.text((x + dx, y + dy), s, font=f, fill=DARK, anchor=anchor)
    d.text((x, y), s, font=f, fill=c, anchor=anchor)

PIPS = {1: [(1, 1)], 2: [(0, 0), (2, 2)], 3: [(0, 0), (1, 1), (2, 2)], 4: [(0, 0), (2, 0), (0, 2), (2, 2)], 5: [(0, 0), (2, 0), (1, 1), (0, 2), (2, 2)], 6: [(0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2)]}
def die(d, x, y, n, sz=18, ring=None, shadow=False):
    x, y = int(round(x)), int(round(y))
    if shadow: d.ellipse([x - 2, y + sz - 4, x + sz + 1, y + sz + 3], fill=DARK)
    d.rectangle([x - 1, y - 1, x + sz, y + sz], fill=DARK)
    d.rectangle([x, y, x + sz - 1, y + sz - 1], fill=BONE); d.rectangle([x, y + sz - 2, x + sz - 1, y + sz - 1], fill=(170, 162, 146))
    g = sz // 4
    for (i, j) in PIPS[n]: cx, cy = x + g + i * (sz - 2 * g - 2) // 2, y + g - 1 + j * (sz - 2 * g - 2) // 2; d.rectangle([cx, cy, cx + 2, cy + 2], fill=DARK)
    if ring: d.rectangle([x - 2, y - 2, x + sz + 1, y + sz + 1], outline=ring)

def socket(d, x, y, c, label, side, lit=False):
    """口: 枠ではなく、 四隅の鉤だけ。 中は後ろが見える"""
    c2 = c if lit else tuple(int(v * 0.8) for v in c); n = 15
    for (cx, cy, sx, sy) in ((x, y, 1, 1), (x + n, y, -1, 1), (x, y + n, 1, -1), (x + n, y + n, -1, -1)):
        d.line([(cx, cy), (cx + 4 * sx, cy)], fill=DARK, width=3); d.line([(cx, cy), (cx, cy + 4 * sy)], fill=DARK, width=3)
    for (cx, cy, sx, sy) in ((x, y, 1, 1), (x + n, y, -1, 1), (x, y + n, 1, -1), (x + n, y + n, -1, -1)):
        d.line([(cx, cy), (cx + 4 * sx, cy)], fill=c2); d.line([(cx, cy), (cx, cy + 4 * sy)], fill=c2)
    if side == "l": text(d, x - 4, y + 2, label, c2, F12, "ra")
    elif side == "r": text(d, x + n + 5, y + 2, label, c2, F12, "la")
    else: text(d, x + 8, y - 13, label, c2, F12, "ma")

def sword(d, x, y, c):
    for i in range(7): d.point((x + i, y + 6 - i), fill=c); d.point((x + i + 1, y + 6 - i), fill=c)
    d.line([(x - 1, y + 4), (x + 3, y + 8)], fill=c); d.point((x - 1, y + 8), fill=c)

A_SOCK = [(150, 118, "×4", "l"), (140, 142, "×4", "l"), (150, 166, "+6", "l")]      # 攻めの口: 敵の脇に浮く
D_SOCK = [(160, 247, "×3", "t"), (340, 247, "+5", "t")]                              # 守りの口: 自分の HP の両脇
ORB = (38, 244)                                                                     # 充電: 左下の玉
FLOOR = [(210, 226), (238, 233), (266, 226)]                                         # 出目は床に転がっている
EB = (190, 290, 211); MB = (190, 290, 255)                                           # HP の棒 (敵の足もと / 手前)
EMAX, MMAX = 150, 100

def bar(d, b, frac, c, hi):
    x0, x1, y = b; d.rectangle([x0 - 1, y - 1, x1 + 1, y + 5], fill=DARK); d.rectangle([x0, y, x1, y + 4], fill=(38, 30, 30))
    e = x0 + int((x1 - x0) * frac)
    if e > x0: d.rectangle([x0, y, e, y + 4], fill=c); d.line([(x0, y), (e, y)], fill=hi)
    return e

def render(st):
    im = BACK.copy()
    foe = FOE
    if st.get("foe_hit", 0): foe = Image.blend(FOE, Image.new("RGBA", FOE.size, (255, 255, 255, 255)), st["foe_hit"]); foe.putalpha(FOE.getchannel("A"))
    im.paste(foe, (FX + st.get("foe_dx", 0), FY), foe); d = ImageDraw.Draw(im)
    # 持ち物: 左上に小さな印だけ (帯なし)
    for i, c in enumerate(((190, 120, 70), (120, 160, 200), (200, 180, 90), (150, 110, 170))): d.rectangle([5 + i * 13, 5, 14 + i * 13, 14], fill=DARK); d.rectangle([6 + i * 13, 6, 13 + i * 13, 13], fill=c); d.rectangle([8 + i * 13, 8, 11 + i * 13, 11], fill=tuple(min(255, v + 50) for v in c))
    # 敵の次の手: 頭の上
    if st.get("intent", True):
        sword(d, 222, 64, DARK); sword(d, 221, 63, RED); text(d, 234, 60, str(st.get("atk", 26)), RED)
        for i in range(3): d.rectangle([256 + i * 6, 65, 259 + i * 6, 68], fill=DARK); d.rectangle([257 + i * 6, 66, 258 + i * 6, 67], fill=RED if i < 1 else (80, 60, 56))   # 激化
    # 敵の HP: 足もと
    ehp = st.get("ehp", 120); e = bar(d, EB, ehp / EMAX, RED, (248, 140, 110))
    m = EB[0] + int(100 * 75 / EMAX); d.line([(m, EB[2] - 3), (m, EB[2] + 6)], fill=DARK, width=3); d.line([(m, EB[2] - 2), (m, EB[2] + 5)], fill=GOLD2)          # ダウンの目盛り
    x = e
    for i, v in enumerate(st.get("cut", [])):
        wd = int(100 * v / EMAX); d.rectangle([x - wd + 1, EB[2], x, EB[2] + 4], fill=GOLD if i % 2 == 0 else GOLD2); x -= wd
    text(d, EB[1] + 6, EB[2] - 4, str(ehp), BONE)
    if st.get("dmg"): text(d, EB[1] + 30, EB[2] - 4, "-%d" % st["dmg"], GOLD2)
    # 自分の HP: 手前
    mhp = st.get("mhp", 82); e = bar(d, MB, mhp / MMAX, GRN, (160, 214, 160))
    bite, cover = st.get("bite", 0), st.get("cover", 0)
    if bite:
        wd = int(100 * bite / MMAX); d.rectangle([e - wd + 1, MB[2], e, MB[2] + 4], fill=RED)
        if cover: cw = int(100 * min(cover, bite) / MMAX); d.rectangle([e - cw + 1, MB[2] - 2, e, MB[2] + 6], fill=BLUE); d.rectangle([e - cw + 1, MB[2] - 2, e, MB[2] - 2], fill=BONE)
        text(d, MB[1] + 30, MB[2] - 4, "-%d" % max(0, bite - cover), RED if bite > cover else BLUE)
    text(d, MB[1] + 6, MB[2] - 4, str(mhp), BONE)
    # 口
    if st.get("socks", True):
        used = {w for (_, _, _, w) in st["dice"] if w}
        for i, (x, y, lab, sd) in enumerate(A_SOCK): socket(d, x, y, RED, lab, sd, ("A%d" % i) in used)
        for i, (x, y, lab, sd) in enumerate(D_SOCK): socket(d, x, y, BLUE, lab, sd, ("D%d" % i) in used)
        # 充電の玉
        ox, oy = ORB; d.ellipse([ox - 11, oy - 11, ox + 11, oy + 11], fill=DARK); d.ellipse([ox - 9, oy - 9, ox + 9, oy + 9], fill=(92, 70, 24)); d.pieslice([ox - 9, oy - 9, ox + 9, oy + 9], -90, -90 + 108, fill=CHG); d.ellipse([ox - 5, oy - 5, ox + 5, oy + 5], fill=DARK); text(d, ox + 1, oy - 6, "3", GOLD2, F12, "ma")
        # 決める
        rdy = st.get("ready", False); text(d, 440, 240, "決める", GOLD2 if rdy else DIM, F12, "ma")
        c = GOLD2 if rdy else DIM; d.line([(418, 256), (462, 256)], fill=DARK, width=3); d.line([(419, 256), (461, 256)], fill=c)
        if rdy: d.polygon([(440, 228), (444, 232), (440, 236), (436, 232)], fill=GOLD2, outline=DARK)
    for (n, x, y, where) in st["dice"]:
        ring = RED if where and where[0] == "A" else BLUE if where and where[0] == "D" else None
        die(d, x + (1 if where else 0), y + (1 if where else 0), n, 14 if where else 18, ring, shadow=not where)
    for (x, y, c) in st.get("shards", []): d.rectangle([int(x) - 1, int(y) - 1, int(x) + 3, int(y) + 3], fill=DARK); d.rectangle([int(x), int(y), int(x) + 2, int(y) + 2], fill=c)
    for (x, y, s_, c, big) in st.get("pops", []): text(d, int(x), int(y), s_, c, F24 if big else F12, "ma")
    if st.get("flash", 0) > 0: im = Image.blend(im, Image.new("RGB", (W, H), st.get("flash_c", (255, 250, 236))), st["flash"]); d = ImageDraw.Draw(im)
    if st.get("cursor"):
        cx, cy = st["cursor"]; d.polygon([(cx, cy), (cx, cy + 9), (cx + 3, cy + 7), (cx + 6, cy + 11), (cx + 8, cy + 10), (cx + 5, cy + 6), (cx + 9, cy + 6)], fill=BONE, outline=DARK)
    return im

def lerp(a, b, u): return a + (b - a) * u
def ease(u): u = max(0.0, min(1.0, u)); return 1 - (1 - u) ** 3
def spos(where): x, y, _, _ = (A_SOCK if where[0] == "A" else D_SOCK)[int(where[1])]; return (x, y)
frames = []
def hold(st, n): frames.extend([render(st)] * n)
def move(st, idx, where, after, n=7):
    nn, x0, y0, _ = st["dice"][idx]; x1, y1 = spos(where)
    for k in range(n):
        u = ease((k + 1) / n); s2 = dict(st); s2["dice"] = list(st["dice"]); s2["dice"][idx] = (nn, lerp(x0, x1, u), lerp(y0, y1, u), None); s2["cursor"] = (lerp(x0, x1, u) + 10, lerp(y0, y1, u) + 8); frames.append(render(s2))
    st["dice"][idx] = (nn, x1, y1, where); after(st); hold(st, 6)

random.seed(5)
st = {"dice": [], "intent": False, "ehp": 120, "mhp": 82}
hold(st, 4)
st["intent"] = True
for k in range(6): s2 = dict(st); s2["bite"] = int(26 * ease((k + 1) / 6)); frames.append(render(s2))     # 敵の手が出る → 自分の HP に食われる分が出る
st["bite"] = 26
for k in range(6):                                                                                        # 出目が床へ転がり込む
    s2 = dict(st); u = ease((k + 1) / 6); s2["dice"] = [(random.randint(1, 6) if k < 5 else n, lerp(x + 40 * (i - 1), x, u), lerp(280, y, u) - 14 * (1 - u) * u * 4, None) for i, (n, (x, y)) in enumerate(zip((5, 2, 4), FLOOR))]; frames.append(render(s2))
st["dice"] = [(n, x, y, None) for n, (x, y) in zip((5, 2, 4), FLOOR)]; hold(st, 8)
move(st, 0, "A0", lambda s: s.update(cut=[20], dmg=20))
move(st, 1, "A1", lambda s: s.update(cut=[20, 8], dmg=28))
move(st, 2, "D0", lambda s: s.update(cover=12, ready=True))
hold(st, 8); still = render(st)
# 決める: 攻めの出目が敵へ飛び、 削れる分が棒から落ちる
for k in range(4):
    s2 = dict(st); u = (k + 1) / 4; s2["socks"] = False; s2["dice"] = [(n, lerp(x, 232, u), lerp(y, 140, u), w) if w and w[0] == "A" else (n, x, y, w) for (n, x, y, w) in st["dice"]]; frames.append(render(s2))
sh = [[250 - random.uniform(0, 19), EB[2] + random.uniform(0, 4), random.uniform(-1.4, 1.4), random.uniform(-2.8, -0.6), GOLD if random.random() < 0.6 else GOLD2] for _ in range(22)]
st2 = dict(st); st2.update(socks=False, cut=[], dmg=0, ehp=92, dice=[t for t in st["dice"] if t[3][0] == "D"])
for k in range(13):
    s2 = dict(st2); s2["flash"] = 0.6 if k == 0 else 0.2 if k == 1 else 0; s2["foe_hit"] = 0.8 if k < 2 else 0; s2["foe_dx"] = (4, -3, 2, -1, 0)[min(k, 4)]
    for q in sh: q[0] += q[2]; q[1] += q[3]; q[3] += 0.45
    s2["shards"] = [(q[0], q[1], q[4]) for q in sh if q[1] < 268]; s2["pops"] = [(240, 104 - k * 1.3, "-28", GOLD2, True)] if k < 11 else []; frames.append(render(s2))
# 敵の一撃: 守りの出目が受け、 覆えなかった分が砕ける
e = MB[0] + 82
sh = [[e - 12 - random.uniform(0, 14), MB[2] + random.uniform(0, 4), random.uniform(-1.6, 1.2), random.uniform(-3.0, -0.8), RED if random.random() < 0.7 else RED2] for _ in range(18)]
for k in range(3): s2 = dict(st2); s2["foe_dx"] = 0; frames.append(render(dict(s2, pops=[(240, 150 + k * 30, "▼", RED, True)])))
st3 = dict(st2); st3.update(bite=0, cover=0, mhp=68, intent=False)
for k in range(12):
    s2 = dict(st3); s2["flash"] = 0.4 if k == 0 else 0; s2["flash_c"] = (200, 40, 30)
    for q in sh: q[0] += q[2]; q[1] += q[3]; q[3] += 0.5
    s2["shards"] = [(q[0], q[1], q[4]) for q in sh if q[1] < 270] + ([(e - 12 + i * 5, MB[2] - 4 - k * 2, BLUE) for i in range(3)] if k < 5 else [])
    s2["pops"] = [(332, 238 - k, "-14", RED, False)] if k < 10 else []; s2["dice"] = st2["dice"] if k < 3 else []; frames.append(render(s2))
st3["dice"] = []; hold(st3, 10)

big = [f.resize((W * S, H * S), Image.NEAREST) for f in frames]
big[0].save(OUT + "/turn.gif", save_all=True, append_images=big[1:], duration=70, loop=0)
still.resize((W * S, H * S), Image.NEAREST).save(OUT + "/still.png")
print("frames", len(frames))
