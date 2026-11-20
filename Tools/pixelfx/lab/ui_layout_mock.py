# 戦闘の画面の再構成: 配置の案を 480×270 の図にする (3 倍で描く)。 出力は Captures/ui_layout/ (git 管理外)。
import os
from PIL import Image, ImageDraw, ImageFont
OUT = r"C:/Users/kumod/My project/Captures/ui_layout"; os.makedirs(OUT, exist_ok=True)
W, H, S = 480, 270, 3
F10 = ImageFont.truetype(r"C:/Windows/Fonts/meiryob.ttc", 27)   # 液晶の 10 ドットの字に当たる大きさ
F8 = ImageFont.truetype(r"C:/Windows/Fonts/meiryob.ttc", 21)    # 8 ドット (数字と短い語)
F14 = ImageFont.truetype(r"C:/Windows/Fonts/meiryob.ttc", 39)   # 14 ドット (大きな数字)
TEN = r"C:/Users/kumod/My project/Captures/fxlib/_tenyo/sword.gif"

def lcd(frame):
    im = Image.open(TEN); im.seek(frame)
    return im.convert("RGB").resize((W, H), Image.NEAREST).resize((W * S, H * S), Image.NEAREST)

COL = {"panel": (24, 30, 38, 235), "soft": (24, 30, 38, 170), "hp": (150, 44, 40, 255), "sh": (70, 120, 190, 255), "gold": (210, 170, 70, 255),
       "atk": (190, 80, 60, 255), "def": (70, 110, 180, 255), "chg": (200, 170, 60, 255), "line": (120, 130, 140, 255), "txt": (232, 226, 210, 255),
       "dim": (150, 150, 150, 255), "die": (228, 222, 204, 255), "ok": (190, 90, 40, 255), "warn": (220, 90, 70, 255)}

class Mock:
    def __init__(self, frame):
        self.im = lcd(frame).convert("RGBA"); self.ov = Image.new("RGBA", self.im.size, (0, 0, 0, 0)); self.d = ImageDraw.Draw(self.ov); self.mask = Image.new("L", (W, H), 0); self.md = ImageDraw.Draw(self.mask)
    def box(self, x, y, w, h, fill="panel", outline="line", count=True):
        self.d.rectangle([x * S, y * S, (x + w) * S - 1, (y + h) * S - 1], fill=COL[fill] if isinstance(fill, str) else fill, outline=COL[outline] if outline else None, width=S)
        if count: self.md.rectangle([x, y, x + w - 1, y + h - 1], fill=255)
    def text(self, x, y, s, c="txt", f=F10, anchor="la"):
        self.d.text((x * S, y * S - 4), s, font=f, fill=COL[c], anchor=anchor, stroke_width=3, stroke_fill=(10, 12, 16, 255))
    def bar(self, x, y, w, h, frac, c="hp", marks=()):
        self.d.rectangle([x * S, y * S, (x + w) * S - 1, (y + h) * S - 1], fill=(14, 16, 20, 255), outline=COL["line"], width=S)
        if frac > 0: self.d.rectangle([(x + 1) * S, (y + 1) * S, (x + 1 + int((w - 2) * frac)) * S - 1, (y + h - 1) * S - 1], fill=COL[c])
        for m in marks: self.d.rectangle([(x + int(w * m)) * S, (y + 1) * S, (x + int(w * m) + 1) * S - 1, (y + h - 1) * S - 1], fill=COL["gold"])
    def die(self, x, y, n, sz=22):
        self.d.rectangle([x * S, y * S, (x + sz) * S - 1, (y + sz) * S - 1], fill=COL["die"], outline=(40, 40, 40, 255), width=S)
        self.d.text(((x + sz / 2) * S, (y + sz / 2) * S), str(n), font=F14 if sz >= 20 else F10, fill=(30, 30, 30, 255), anchor="mm")
    def card(self, x, y, w, h, kind, val=""):
        c = {"攻": "atk", "防": "def", "充": "chg"}[kind]
        self.d.rectangle([x * S, y * S, (x + w) * S - 1, (y + h) * S - 1], fill=COL[c], outline=(20, 20, 20, 255), width=S)
        self.d.text(((x + w / 2) * S, (y + h / 2 - 5) * S), kind, font=F10, fill=(250, 244, 230, 255), anchor="mm", stroke_width=2, stroke_fill=(20, 20, 20, 255))
        if val: self.d.text(((x + w / 2) * S, (y + h - 9) * S), val, font=F8, fill=(250, 244, 230, 255), anchor="mm", stroke_width=2, stroke_fill=(20, 20, 20, 255))
    def pips(self, x, y, n, on, c="chg", sz=6, gap=2):
        for i in range(n): self.d.rectangle([(x + i * (sz + gap)) * S, y * S, (x + i * (sz + gap) + sz) * S - 1, (y + sz) * S - 1], fill=COL[c] if i < on else (40, 44, 50, 255), outline=(20, 20, 20, 255), width=2)
    def area(self): return sum(1 for v in self.mask.tobytes() if v) / (W * H)
    def save(self, name, title):
        im = Image.alpha_composite(self.im, self.ov).convert("RGB"); d = ImageDraw.Draw(im)
        a = self.area(); cap = "%s   ── 常設の表示が覆う面積 %d%% / 敵と演出に空く面積 %d%%" % (title, round(a * 100), round((1 - a) * 100))
        o = Image.new("RGB", (im.size[0], im.size[1] + 54), (18, 17, 22)); o.paste(im, (0, 54)); ImageDraw.Draw(o).text((14, 8), cap, font=ImageFont.truetype(r"C:/Windows/Fonts/meiryob.ttc", 28), fill=(230, 226, 238))
        o.save(os.path.join(OUT, name + ".png")); return a

def enemy_tag(m, x, y):   # 敵の予告: 次の攻撃の値だけ (出目は値にもう入っている・防御は敵の詳細の窓で見る ── 2026-11-15 作者)。 毒などは印で
    m.box(x, y, 46, 26, "soft", "line", count=True)
    m.text(x + 4, y + 1, "次", "dim", F8); m.text(x + 42, y + 6, "28", "warn", F14, "ra")
    m.pips(x + 2, y + 29, 2, 2, "chg", 5, 2)   # 状態の印 (毒 2 など)

def enemy_hp(m):   # 敵の HP: 足もとのすぐ下に棒、 数字は棒の右 (下の帯に掛からないよう 1 行で)
    m.bar(180, 198, 100, 6, 0.62, "hp", (0.33, 0.66)); m.text(283, 196, "884", "txt", F8)

def clock(m, x, y):       # 激化の時計 (細い 1 行)。 ターンの数は出さない: 効くのは「次の段まであと何ターンか」だけ
    x += 26; m.box(x, y, 80, 12, "soft", None)
    m.text(x + 3, y + 1, "激化", "dim", F8); m.pips(x + 29, y + 3, 5, 1, "warn", 6, 2); m.text(x + 72, y + 1, "2", "txt", F8)

def preview(m, x1, x2, y):   # 収支の見込み: 与える / 受ける
    m.box(x1, y, 76, 22, "soft", None); m.text(x1 + 4, y + 1, "与える", "dim", F8); m.text(x1 + 72, y + 3, "34", "gold", F14, "ra")
    m.box(x2, y, 76, 22, "soft", None); m.text(x2 + 4, y + 1, "受ける", "dim", F8); m.text(x2 + 72, y + 3, "12", "warn", F14, "ra")

# ---------------------------------------------------------------- 今 (2026-11-15 の変更後)
def current(frame, name, title, stow=False):
    m = Mock(frame)
    if not stow:
        m.box(2, 2, 56, 17); m.text(8, 5, "持ち物", "txt", F8)
        m.box(2, 20, 108, 98)
        for i, n in enumerate((2, 5, 4)): m.die(8, 26 + i * 30, n); m.box(36, 26 + i * 30, 40, 22, (40, 46, 54, 255)); m.text(82, 32 + i * 30, "□", "dim", F8)
        m.box(2, 122, 108, 46); m.text(8, 126, "会心 15% 2.0倍", "gold", F8); m.text(8, 139, "攻撃 4", "atk", F8); m.text(8, 152, "防御 3 貫通 2", "def", F8)
        m.box(2, 170, 142, 40); m.text(8, 174, "充電", "gold", F8); m.pips(36, 176, 10, 3, "chg", 8, 2); m.text(8, 190, "激化 あと2", "txt", F8); m.pips(80, 192, 5, 1, "warn", 8, 2)
        m.box(2, 214, 108, 52); m.text(8, 220, "振り直す-3 おまかせ", "txt", F8); m.text(8, 244, "道具 逃げる 決める", "txt", F8)
        m.box(370, 60, 108, 120); m.die(378, 68, 4, 18); m.text(378, 92, "攻撃 28", "warn", F10); m.text(378, 108, "出目 4", "txt", F8); m.text(378, 121, "防御 3", "txt", F8); m.text(378, 150, "ターン3 段階0", "dim", F8); m.text(378, 163, "目盛りまで 220", "dim", F8)
        m.box(318, 184, 160, 80); m.text(324, 188, "山12 捨3", "dim", F8)
        for i, k in enumerate("攻防充攻防"): m.card(324 + i * 30, 206, 26, 30, k)
        m.box(110, 213, 208, 55, "soft"); m.bar(150, 222, 160, 9, 0.7, "sh"); m.bar(150, 240, 160, 9, 0.8, "hp"); m.text(116, 236, "HP", "txt", F10)
    enemy_hp(m)
    return m.save(name, title)

# ---------------------------------------------------------------- 案 A: 下の帯
def planA(frame, name, title, stow=False):
    m = Mock(frame)
    clock(m, 174, 2); m.box(2, 2, 22, 12, "soft", None); m.text(5, 3, "袋", "txt", F8)
    enemy_tag(m, 300, 84); enemy_hp(m)
    if not stow:
        preview(m, 96, 308, 172)
        m.box(0, 206, 480, 64)
        # 帯の上の 1 行: 自分の HP とシールド・充電・自分の数字
        m.text(5, 208, "HP", "txt", F8); m.bar(22, 209, 150, 7, 0.8, "hp"); m.bar(22 + 120, 209, 30, 7, 1, "sh"); m.text(176, 208, "82/100 +14", "txt", F8)
        m.text(258, 208, "充電", "gold", F8); m.pips(286, 210, 10, 3, "chg", 6, 2)
        m.text(476, 208, "会心15% 攻4 防3 貫2", "dim", F8, "ra")
        # 出目と差し込み口 (横に並べる): 出目の下に、 挿した端子が入る
        for i, n in enumerate((2, 5, 4)):
            x = 6 + i * 48; m.die(x + 12, 220, n, 20); m.box(x, 242, 44, 24, (40, 46, 54, 255), "line", count=False)
        m.card(8, 244, 40, 20, "攻", "")
        # 手札
        for i, (k, v) in enumerate((("防", "×1.0"), ("充", "+3"), ("攻", "×1.2"), ("防", "+4"), ("攻", "×1.0"))): m.card(158 + i * 40, 220, 36, 46, k, v)
        m.text(360, 256, "山12", "dim", F8)
        # 操作
        m.box(392, 220, 84, 24, COL["ok"], "line", count=False); m.text(434, 226, "決める", "txt", F10, "ma")
        for i, t in enumerate(("振直", "任せ", "道具", "逃")): m.box(392 + i * 21, 248, 20, 18, (40, 46, 54, 255), "line", count=False); m.text(402 + i * 21, 252, t[:1], "txt", F8, "ma")
    else:
        m.box(0, 258, 480, 12, "soft", None); m.text(5, 259, "HP", "txt", F8); m.bar(22, 261, 150, 6, 0.68, "hp"); m.text(176, 259, "68/100", "txt", F8)
    return m.save(name, title)

# ---------------------------------------------------------------- 案 B: 左右の袖 + 細い帯
def planB(frame, name, title, stow=False):
    m = Mock(frame)
    clock(m, 174, 2); enemy_hp(m)
    if not stow:
        # 左の袖: 出目と差し込み口 (縦)
        m.box(0, 16, 62, 190)
        for i, n in enumerate((2, 5, 4)):
            y = 22 + i * 60; m.die(20, y, n); m.box(8, y + 26, 46, 26, (40, 46, 54, 255), "line", count=False)
        m.card(10, 50, 42, 22, "攻"); m.text(4, 3, "袋", "txt", F8)
        # 右の袖: 敵の予告と収支
        m.box(418, 16, 62, 190); m.text(424, 20, "次の攻撃", "dim", F8); m.text(424, 32, "28", "warn", F14); m.text(424, 52, "出目 4", "txt", F8); m.text(424, 64, "防御 3", "txt", F8); m.text(424, 76, "毒 2", "dim", F8)
        m.text(424, 104, "与える", "dim", F8); m.text(474, 116, "34", "gold", F14, "ra"); m.text(424, 140, "受ける", "dim", F8); m.text(474, 152, "12", "warn", F14, "ra"); m.text(424, 182, "山12 捨3", "dim", F8)
        # 下の帯: 手札と操作
        m.box(0, 208, 480, 62)
        m.text(5, 211, "HP", "txt", F8); m.bar(22, 212, 110, 7, 0.8, "hp"); m.text(5, 224, "盾 +14", "def", F8); m.text(5, 238, "充電", "gold", F8); m.pips(5, 250, 10, 3, "chg", 6, 2); m.text(136, 211, "82", "txt", F8)
        for i, (k, v) in enumerate((("防", "×1.0"), ("充", "+3"), ("攻", "×1.2"), ("防", "+4"), ("攻", "×1.0"))): m.card(150 + i * 42, 214, 38, 50, k, v)
        m.box(372, 214, 104, 28, COL["ok"], "line", count=False); m.text(424, 221, "決める", "txt", F10, "ma")
        for i, t in enumerate(("振直", "任せ", "道具", "逃")): m.box(372 + i * 26, 246, 24, 18, (40, 46, 54, 255), "line", count=False); m.text(384 + i * 26, 250, t[:2], "txt", F8, "ma")
    else:
        m.box(0, 258, 480, 12, "soft", None); m.text(5, 259, "HP", "txt", F8); m.bar(22, 261, 150, 6, 0.68, "hp"); m.text(176, 259, "68/100", "txt", F8)
    return m.save(name, title)

# ---------------------------------------------------------------- 案 C: 手番で切り替える (判断の間は大きな操作盤・解決の間は全面)
def planC(frame, name, title, stow=False):
    m = Mock(frame)
    if not stow:
        # 舞台を 34 ドット上へ送る (敵が上へ寄り、 操作盤に隠れない)
        base = m.im.copy(); m.im.paste((12, 12, 16, 255), (0, 0, W * S, H * S)); m.im.paste(base.crop((0, 34 * S, W * S, H * S)), (0, 0))
        clock(m, 174, 2); m.box(2, 2, 22, 12, "soft", None); m.text(5, 3, "袋", "txt", F8)
        enemy_tag(m, 300, 50); m.bar(190, 165, 100, 6, 0.62, "hp", (0.33, 0.66)); m.text(240, 172, "884/1425", "txt", F8, "ma")
        m.box(0, 150, 480, 120)
        preview(m, 8, 396, 154)
        m.text(96, 157, "HP", "txt", F8); m.bar(114, 158, 150, 7, 0.8, "hp"); m.bar(114 + 120, 158, 30, 7, 1, "sh"); m.text(268, 157, "82/100 +14", "txt", F8); m.text(96, 168, "充電", "gold", F8); m.pips(124, 170, 10, 3, "chg", 6, 2); m.text(388, 168, "会心15% 攻4 防3 貫2", "dim", F8, "ra")
        for i, n in enumerate((2, 5, 4)):
            x = 10 + i * 58; m.die(x + 15, 184, n, 24); m.box(x, 212, 54, 34, (40, 46, 54, 255), "line", count=False); m.text(x + 27, 250, ("攻撃 +2", "空き", "空き")[i], "dim" if i else "atk", F8, "ma")
        m.card(12, 214, 50, 30, "攻", "×1.0")
        for i, (k, v) in enumerate((("防", "×1.0"), ("充", "+3"), ("攻", "×1.2"), ("防", "+4"), ("攻", "×1.0"))): m.card(190 + i * 38, 184, 34, 62, k, v)
        m.text(190, 250, "山12 捨3", "dim", F8)
        m.box(388, 184, 88, 34, COL["ok"], "line", count=False); m.text(432, 194, "決める", "txt", F10, "ma")
        for i, t in enumerate(("振り直す -3", "おまかせ")): m.box(388, 222 + i * 14, 88, 12, (40, 46, 54, 255), "line", count=False); m.text(432, 223 + i * 14, t, "txt", F8, "ma")
        m.text(432, 252, "道具   逃げる", "dim", F8, "ma")
    else:
        clock(m, 174, 2); enemy_tag(m, 300, 84); enemy_hp(m)
        m.box(0, 258, 480, 12, "soft", None); m.text(5, 259, "HP", "txt", F8); m.bar(22, 261, 150, 6, 0.68, "hp"); m.text(176, 259, "68/100", "txt", F8)
    return m.save(name, title)

# ---------------------------------------------------------------- 案 D: 回路 (今の部品を並べ直すのではなく、 1 ターンのやり取りそのものを画面にする)
#   攻撃の線 (左): 出目 → 攻撃の端子 → 敵。 守りの線 (右): 敵の次の攻撃 → 守りの端子 ← 出目。 見込みは、 数字の枠ではなく、 HP の棒の上の「削れる分」で見せる
def wire(m, pts, c, w=2, dash=False):
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        if dash:
            n = max(1, int(max(abs(x1 - x0), abs(y1 - y0)) / 6))
            for i in range(0, n, 2): m.d.line([((x0 + (x1 - x0) * i / n) * S, (y0 + (y1 - y0) * i / n) * S), ((x0 + (x1 - x0) * (i + 1) / n) * S, (y0 + (y1 - y0) * (i + 1) / n) * S)], fill=COL[c], width=w * S)
        else: m.d.line([(x0 * S, y0 * S), (x1 * S, y1 * S)], fill=COL[c], width=w * S)
def node(m, x, y, kind, label, val=None, on=True):   # 端子: 場に開いている口。 出目が入ると、 効き目の数字が出る
    c = {"攻": "atk", "防": "def", "充": "chg"}[kind]; w, h = 40, 18
    fill = COL[c] if on else (COL[c][0] // 3, COL[c][1] // 3, COL[c][2] // 3, 255)
    m.d.rectangle([(x - w // 2) * S, (y - h // 2) * S, (x + w // 2) * S - 1, (y + h // 2) * S - 1], fill=fill, outline=(16, 16, 20, 255), width=S); m.md.rectangle([x - w // 2, y - h // 2, x + w // 2 - 1, y + h // 2 - 1], fill=255)
    m.text(x - 17, y - 6, label, "txt" if on else "dim", F8)
    if val: m.text(x + 18, y - 6, val, "txt", F8, "ra")

def planD(frame, name, title, stow=False):
    m = Mock(frame)
    # 激化の時計 (上の縁・灯りだけ) と、 隅の小さな入口
    m.pips(214, 4, 5, 1, "warn", 6, 3); m.text(262, 1, "2", "dim", F8); m.text(4, 2, "袋 道具 逃げる", "dim", F8)
    # 敵の HP: 足もとの棒。 今の手で削れる分を、 棒の上で明るく見せる (数字の枠は作らない)
    m.bar(180, 198, 120, 7, 0.62, "hp", (0.33, 0.66)); m.d.rectangle([(180 + 1 + int(118 * 0.50)) * S, 199 * S, (180 + 1 + int(118 * 0.62)) * S - 1, 204 * S - 1], fill=(250, 220, 120, 255))
    if not stow:
        # ---- 攻撃の線 (左): 出目 5 → 攻撃 ×1.2 → 敵。 出目 2 → 攻撃 ×1.0 → 敵
        wire(m, [(206, 236), (150, 236), (150, 170), (118, 170)], "atk"); wire(m, [(78, 170), (60, 170), (60, 128), (176, 128)], "atk")
        wire(m, [(240, 228), (240, 214), (140, 214), (140, 196), (118, 196)], "atk"); wire(m, [(78, 196), (52, 196), (52, 120), (176, 120)], "atk")
        node(m, 98, 170, "攻", "×1.2", "6"); node(m, 98, 196, "攻", "×1.0", "2")
        m.text(124, 106, "34", "gold", F14, "ma"); m.text(150, 110, "▶", "atk", F8)
        m.text(100, 150, "会心 15%", "dim", F8, "ma")
        # ---- 守りの線 (右): 敵の次の攻撃 28 が降りて来る。 出目 4 → 守り +4 で受ける
        m.text(356, 96, "28", "warn", F14, "ma"); wire(m, [(304, 124), (428, 124), (428, 160)], "warn", 2, True); m.text(326, 110, "◀", "warn", F8)
        wire(m, [(274, 236), (330, 236), (330, 170), (362, 170)], "def")
        node(m, 382, 170, "防", "+4", "8"); node(m, 382, 196, "防", "×1.0", None, on=False)
        wire(m, [(402, 170), (428, 170)], "def"); m.d.rectangle([(420) * S, 160 * S, (436) * S - 1, 180 * S - 1], outline=COL["def"], width=S)
        wire(m, [(428, 180), (428, 222), (340, 222), (340, 258)], "warn", 2, True); m.text(446, 196, "20", "warn", F14, "ma")
        # ---- 充電の口 (左下): 振り直しの通貨なので、 振り直すのそばに
        node(m, 40, 244, "充", "+3", None, on=False); m.pips(66, 241, 10, 3, "chg", 5, 2); m.text(66, 250, "振り直す 3", "txt", F8)
        # ---- 手もと: 出目だけを大きく、 真ん中の下に
        for i, n in enumerate((5, 2, 4)): m.die(206 + i * 34 - 12, 228, n, 26)
        m.md.rectangle([190, 226, 290, 256], fill=255)
        # 自分の HP: 画面の下の縁。 この手で受ける分を、 棒の上で赤く欠いて見せる
        m.bar(150, 260, 180, 8, 0.8, "hp"); m.d.rectangle([(151 + int(178 * 0.6)) * S, 261 * S, (151 + int(178 * 0.8)) * S - 1, 267 * S - 1], fill=(250, 150, 130, 255)); m.text(336, 258, "82", "txt", F8)
        m.md.rectangle([150, 258, 350, 269], fill=255)
        # 決める
        m.box(398, 240, 76, 24, COL["ok"], "line"); m.text(436, 246, "決める", "txt", F10, "ma")
    else:
        m.bar(150, 260, 180, 8, 0.6, "hp"); m.text(336, 258, "62", "txt", F8); m.md.rectangle([150, 258, 350, 269], fill=255)
    return m.save(name, title)

# ================================================================ パターン集 (2026-11-15・考え方の違う物をいくつも)
def top(m):   # 共通: 激化の灯り (上の縁) と、 隅の入口
    m.pips(214, 4, 5, 1, "warn", 6, 3); m.text(4, 2, "袋", "dim", F8)
def ehp(m, x=180, y=198, w=120, cut=(0.50, 0.62)):   # 敵の HP と、 この手で削れる分
    m.bar(x, y, w, 7, cut[1], "hp", (0.33, 0.66)); m.d.rectangle([(x + 1 + int((w - 2) * cut[0])) * S, (y + 1) * S, (x + 1 + int((w - 2) * cut[1])) * S - 1, (y + 6) * S - 1], fill=(250, 220, 120, 255)); m.md.rectangle([x, y, x + w, y + 7], fill=255)
def myhp(m, x, y, w, frac=0.8, lose=0.2):
    m.bar(x, y, w, 8, frac, "hp"); m.d.rectangle([(x + 1 + int((w - 2) * (frac - lose))) * S, (y + 1) * S, (x + 1 + int((w - 2) * frac)) * S - 1, (y + 7) * S - 1], fill=(250, 150, 130, 255)); m.md.rectangle([x, y, x + w, y + 8], fill=255)
def ok(m, x, y, w=70, h=22):
    m.box(x, y, w, h, COL["ok"], "line"); m.text(x + w // 2, y + (h - 12) // 2, "決める", "txt", F10, "ma")
def chip(m, x, y, kind, label, on=True, w=30, h=14):
    c = {"攻": "atk", "防": "def", "充": "chg"}[kind]; f = COL[c] if on else (COL[c][0] // 3, COL[c][1] // 3, COL[c][2] // 3, 255)
    m.d.rectangle([x * S, y * S, (x + w) * S - 1, (y + h) * S - 1], fill=f, outline=(16, 16, 20, 255), width=S); m.md.rectangle([x, y, x + w - 1, y + h - 1], fill=255); m.text(x + w // 2, y + 1, label, "txt" if on else "dim", F8, "ma")

# ---- P2 棒で比べる: 数字を読ませず、 同じ物差しの棒で「与える」「受ける」を見せる
def planE(frame, name, title):
    m = Mock(frame); top(m); ehp(m)
    X, Wd = 96, 288
    m.text(X - 4, 208, "攻", "atk", F10, "ra"); m.bar(X, 208, Wd, 10, 0.0, "atk"); m.d.rectangle([(X + 1) * S, 209 * S, (X + 1 + 96) * S - 1, 217 * S - 1], fill=COL["gold"]); m.text(X + 100, 206, "34", "gold", F10)
    chip(m, X + 150, 206, "攻", "×1.2"); chip(m, X + 184, 206, "攻", "×1.0", False)
    m.text(X - 4, 224, "敵", "warn", F10, "ra"); m.bar(X, 224, Wd, 10, 0.0, "hp"); m.d.rectangle([(X + 1) * S, 225 * S, (X + 1 + 80) * S - 1, 233 * S - 1], fill=COL["warn"]); m.d.rectangle([(X + 1) * S, 225 * S, (X + 1 + 23) * S - 1, 233 * S - 1], fill=COL["def"]); m.text(X + 84, 222, "28", "warn", F10); m.text(X + 26, 236, "8 で受ける", "def", F8)
    chip(m, X + 150, 222, "防", "+4"); chip(m, X + 184, 222, "防", "×1.0", False); chip(m, X + 222, 214, "充", "+3", False)
    m.md.rectangle([X - 20, 204, X + Wd, 236], fill=255)
    for i, n in enumerate((5, 2, 4)): m.die(180 + i * 30, 242, n, 22)
    m.md.rectangle([178, 240, 268, 266], fill=255); m.text(170, 248, "振直3", "dim", F8, "ra"); myhp(m, 20, 258, 130); ok(m, 404, 242)
    return m.save(name, title)

# ---- P3 盤上の陣: 出目そのものを場に置く。 前の列 = 攻め (敵の足もと)・後ろの列 = 守り (手前)
def planF(frame, name, title):
    m = Mock(frame); top(m); ehp(m, 180, 190)
    def tile(x, y, kind, label, die=None):
        c = {"攻": "atk", "防": "def", "充": "chg"}[kind]; m.d.rectangle([x * S, y * S, (x + 36) * S - 1, (y + 24) * S - 1], fill=(COL[c][0] // 2, COL[c][1] // 2, COL[c][2] // 2, 255), outline=COL[c], width=S); m.md.rectangle([x, y, x + 36, y + 24], fill=255)
        m.text(x + 18, y + 13, label, "txt", F8, "ma")
        if die: m.die(x + 8, y - 12, die, 20)
    m.text(150, 208, "攻め", "atk", F8, "ra"); tile(156, 204, "攻", "×1.2", 5); tile(196, 204, "攻", "×1.0", 2); tile(236, 204, "攻", "×1.0"); m.text(300, 204, "▲ 34", "gold", F14)
    m.text(150, 242, "守り", "def", F8, "ra"); tile(156, 240, "防", "+4", 4); tile(196, 240, "防", "×1.0"); m.text(300, 234, "▼ 28", "warn", F14); m.text(356, 240, "→ 20", "warn", F10)
    tile(60, 240, "充", "+3"); m.pips(60, 230, 10, 3, "chg", 3, 1)
    myhp(m, 156, 266, 170, 0.8, 0.2); ok(m, 404, 240)
    return m.save(name, title)

# ---- P4 計器: 敵を囲む弧。 左の弧 = こちらの攻め・右の弧 = 敵の攻撃と、 受ける分
def planG(frame, name, title):
    m = Mock(frame); top(m)
    cx, cy, R = 240, 140, 104
    def arc(a0, a1, c, w=4, r=R): m.d.arc([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], a0, a1, fill=COL[c] if isinstance(c, str) else c, width=w * S)
    arc(110, 250, (50, 54, 60, 255), 5); arc(110, 182, "gold", 5); m.text(cx - R - 6, cy - 4, "34", "gold", F14, "ra")
    arc(290, 70, (50, 54, 60, 255), 5); arc(290, 12, "warn", 5); arc(290, 316, "def", 5); m.text(cx + R + 6, cy - 4, "28", "warn", F14); m.text(cx + R + 6, cy + 14, "受 20", "warn", F8)
    arc(70, 110, (40, 16, 16, 255), 6); arc(70, 95, "hp", 6); arc(95, 100, "gold", 6)
    for i, (kind, lab, on) in enumerate((("攻", "×1.2", True), ("攻", "×1.0", True))): chip(m, 118, 196 + i * 16, kind, lab, on)
    for i, (kind, lab, on) in enumerate((("防", "+4", True), ("防", "×1.0", False))): chip(m, 332, 196 + i * 16, kind, lab, on)
    chip(m, 60, 250, "充", "+3", False); m.pips(94, 255, 10, 3, "chg", 4, 1)
    for i, n in enumerate((5, 2, 4)): m.die(204 + i * 26, 238, n, 22)
    m.md.rectangle([202, 236, 282, 262], fill=255); myhp(m, 180, 262, 120); ok(m, 404, 244)
    return m.save(name, title)

# ---- P5 式: 1 ターンを 1 行の式として読む。 出目と端子が式の項で、 項を入れ替えると答えが変わる
def planH(frame, name, title):
    m = Mock(frame); top(m); ehp(m)
    y = 238; m.box(0, y - 6, 480, 38, "soft", None)
    x = 10; m.die(x, y, 5, 22); chip(m, x + 24, y + 4, "攻", "×1.2"); m.text(x + 58, y + 4, "+", "txt", F10); m.die(x + 70, y, 2, 22); chip(m, x + 94, y + 4, "攻", "×1.0"); m.text(x + 128, y + 4, "=", "txt", F10); m.text(x + 140, y + 1, "34", "gold", F14); m.text(x + 168, y + 4, "▶敵", "atk", F8)
    x = 250; m.text(x, y + 1, "28", "warn", F14); m.text(x + 28, y + 4, "−", "txt", F10); m.die(x + 40, y, 4, 22); chip(m, x + 64, y + 4, "防", "+4"); m.text(x + 98, y + 4, "=", "txt", F10); m.text(x + 110, y + 1, "20", "warn", F14); m.text(x + 138, y + 4, "▶自分", "warn", F8)
    m.text(10, 220, "空き:", "dim", F8); chip(m, 40, 218, "防", "×1.0", False); chip(m, 74, 218, "充", "+3", False); m.pips(112, 222, 10, 3, "chg", 4, 1); m.text(160, 220, "振り直す 3", "dim", F8)
    myhp(m, 250, 222, 150); ok(m, 408, 216, 66, 18)
    return m.save(name, title)

# ---- P6 机と舞台: 左が自分の机 (出目と口を、 手で触る物として大きく)・右が舞台
def planI(frame, name, title):
    m = Mock(frame); base = m.im.copy(); m.im.paste((10, 10, 14, 255), (0, 0, W * S, H * S)); m.im.paste(base.crop((0, 0, (W - 78) * S, H * S)), (78 * S, 0))
    m.pips(292, 4, 5, 1, "warn", 6, 3); ehp(m, 258, 198)
    m.box(0, 0, 156, 270); m.text(8, 6, "袋", "dim", F8)
    for i, n in enumerate((5, 2, 4)): m.die(14 + i * 46, 22, n, 34)
    m.text(78, 62, "振り直す 3", "dim", F8, "ma"); m.pips(40, 76, 10, 3, "chg", 6, 2)
    m.text(8, 96, "攻め", "atk", F10); chip(m, 8, 112, "攻", "×1.2 ←5", True, 66, 22); chip(m, 80, 112, "攻", "×1.0 ←2", True, 66, 22); m.text(150, 96, "34", "gold", F14, "ra")
    m.text(8, 146, "守り", "def", F10); chip(m, 8, 162, "防", "+4 ←4", True, 66, 22); chip(m, 80, 162, "防", "×1.0", False, 66, 22); m.text(150, 146, "8", "def", F14, "ra")
    chip(m, 8, 196, "充", "+3", False, 66, 20)
    m.text(8, 228, "HP", "txt", F8); myhp(m, 26, 229, 122); ok(m, 8, 242, 140, 22)
    m.box(352, 96, 44, 26, "soft", "line"); m.text(356, 97, "次", "dim", F8); m.text(392, 102, "28", "warn", F14, "ra"); m.text(374, 126, "受 20", "warn", F8, "ma")
    return m.save(name, title)

# ---- P7 触るまで出ない: 常設は出目と HP だけ。 出目に触ると、 そのまわりに端子が開く。 割り振った出目は、 攻めなら敵の側へ上がり、 守りなら手前に残る
def planJ(frame, name, title):
    m = Mock(frame); top(m); ehp(m)
    m.text(334, 100, "28", "warn", F14); m.text(334, 120, "受 20", "warn", F8)
    m.die(150, 176, 5, 24); m.d.rectangle([148 * S, 174 * S, 176 * S, 202 * S], outline=COL["atk"], width=2 * S); m.text(162, 204, "×1.2", "atk", F8, "ma"); wire(m, [(176, 180), (200, 160)], "atk"); m.text(150, 158, "34", "gold", F14, "ma"); m.md.rectangle([146, 172, 178, 214], fill=255)
    m.die(300, 232, 4, 24); m.d.rectangle([298 * S, 230 * S, 326 * S, 258 * S], outline=COL["def"], width=2 * S); m.text(312, 260, "+4", "def", F8, "ma"); m.md.rectangle([296, 228, 328, 268], fill=255)
    m.die(228, 232, 2, 24); m.d.rectangle([226 * S, 230 * S, 254 * S, 258 * S], outline=COL["txt"], width=2 * S); m.md.rectangle([226, 230, 254, 258], fill=255)
    for (dx, dy, kind, lab, on) in ((-52, -22, "攻", "×1.0", True), (-18, -34, "防", "×1.0", True), (18, -34, "充", "+3", True)): chip(m, 240 + dx - 15, 232 + dy, kind, lab, on)
    m.text(240, 262, "▲ 触った出目のまわりに開く", "dim", F8, "ma")
    myhp(m, 20, 258, 130); ok(m, 404, 244)
    return m.save(name, title)

IDLE, HIT = 60, 166
planE(IDLE, "E_bars", "P2 棒で比べる ── 数字ではなく、 同じ物差しの長さで見せる")
planF(IDLE, "F_ranks", "P3 盤上の陣 ── 出目を場に置く。 前の列が攻め・後ろの列が守り")
planG(IDLE, "G_gauge", "P4 計器 ── 敵を囲む弧。 左がこちらの攻め・右が敵の攻撃")
planH(IDLE, "H_formula", "P5 式 ── 1 ターンを 1 行の式として読む")
planI(IDLE, "I_desk", "P6 机と舞台 ── 左が自分の机・右が舞台")
planJ(IDLE, "J_touch", "P7 触るまで出ない ── 出目に触ると、 そのまわりに端子が開く")
res = {}
res["D"] = planD(IDLE, "D_decide", "案 D 回路・判断の間 (出目を挿した線が、 そのまま見込みになる)"); res["D2"] = planD(HIT, "D_resolve", "案 D・解決の間", stow=True)
res["now"] = current(IDLE, "0_now_decide", "今 (2026-11-15 の変更後)・判断の間")
current(HIT, "0_now_resolve", "今・大技の発動の間 (GUI を格納)", stow=True)
res["A"] = planA(IDLE, "A_decide", "案 A 下の帯・判断の間"); res["A2"] = planA(HIT, "A_resolve", "案 A・解決の間 (帯は下へ引っ込み、 HP の細い帯だけ)", stow=True)
res["B"] = planB(IDLE, "B_decide", "案 B 左右の袖 + 細い帯・判断の間"); res["B2"] = planB(HIT, "B_resolve", "案 B・解決の間", stow=True)
res["C"] = planC(IDLE, "C_decide", "案 C 手番で切り替え・判断の間 (舞台を上へ送り、 大きな操作盤)"); res["C2"] = planC(HIT, "C_resolve", "案 C・解決の間 (全面)", stow=True)
print({k: round(v * 100) for k, v in res.items()})
