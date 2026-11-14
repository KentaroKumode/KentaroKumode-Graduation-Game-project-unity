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
        self.d.rectangle([(x + 1) * S, (y + 1) * S, (x + 1 + int((w - 2) * frac)) * S - 1, (y + h - 1) * S - 1], fill=COL[c])
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

IDLE, HIT = 60, 166
res = {}
res["now"] = current(IDLE, "0_now_decide", "今 (2026-11-15 の変更後)・判断の間")
current(HIT, "0_now_resolve", "今・大技の発動の間 (GUI を格納)", stow=True)
res["A"] = planA(IDLE, "A_decide", "案 A 下の帯・判断の間"); res["A2"] = planA(HIT, "A_resolve", "案 A・解決の間 (帯は下へ引っ込み、 HP の細い帯だけ)", stow=True)
res["B"] = planB(IDLE, "B_decide", "案 B 左右の袖 + 細い帯・判断の間"); res["B2"] = planB(HIT, "B_resolve", "案 B・解決の間", stow=True)
res["C"] = planC(IDLE, "C_decide", "案 C 手番で切り替え・判断の間 (舞台を上へ送り、 大きな操作盤)"); res["C2"] = planC(HIT, "C_resolve", "案 C・解決の間 (全面)", stow=True)
print({k: round(v * 100) for k, v in res.items()})
