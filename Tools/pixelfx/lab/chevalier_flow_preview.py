# シュヴァリエの大技の一連の流れ: チャージ → 完了で画面をかなり暗く落とす → 待機 (鋭いきらめきだけ) → きらめきが伸びて消える → 刃が画面を切り裂く。
# 暗転は、 刃の縁の後ろから晴れる (闇ごと切られる)。 480×270 の舞台・1 秒 8 コマ・2 倍・鋼の色の組。 出力は Captures/fxlib/_chevalier/flow.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw, ImageFont
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_chevalier"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
FONT = ImageFont.truetype(r"C:/Windows/Fonts/meiryo.ttc", 18)
DARK = 0.85                      # 待機の暗さ (0 = そのまま・1 = 真っ黒)
LEAD, SWEEP, SL = 0.75, 0.45, 0.55
E0, E1 = -(H / 2) * SL - 4, W + (H / 2) * SL + 24
SHAKE = [(4, 0), (-3, 2), (2, -2), (-2, 1), (1, -1), (0, 1)]


def stage():
    im = Image.new("RGB", (W, H), (24, 24, 30)); d = ImageDraw.Draw(im)
    d.rectangle([0, FLOOR, W, H], fill=(31, 31, 38)); d.line([(0, FLOOR), (W, FLOOR)], fill=(44, 44, 54))
    x0, y0 = EX - 50, FLOOR - 120
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0
        d.line([(x0, y0 + y), (x0 + 99, y0 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((72, 72, 84), (28, 28, 34))))
    d.rectangle([40, 232, 440, 262], outline=(62, 62, 76)); return im


def frames(name, eid, opts):
    png = os.path.join(OUT, name + "_sheet.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", "steel", "--fps", "8", "--opts", json.dumps(opts)], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    return [sh.crop((i * m["w"], 0, (i + 1) * m["w"], m["h"])) for i in range(m["n"])]


def dark(im, a): return im if a <= 0.01 else im.point(lambda v: int(v * (1 - a)))
def framed(im, cap):
    o = Image.new("RGB", (W * SC, H * SC + 30), (18, 18, 22)); o.paste(im.resize((W * SC, H * SC), Image.NEAREST), (0, 0))
    ImageDraw.Draw(o).text((8, H * SC + 3), cap, font=FONT, fill=(225, 228, 236)); return o
def cut_mask(t):
    """刃の縁の後ろ (もう切られた側) が 255 のマスク。 縁の式は chevalier-slash.js と同じ。"""
    u = max(0.0, min(1.0, (t - LEAD) / SWEEP)); e = E0 + (E1 - E0) * (1 - (1 - u) ** 3)
    m = Image.new("L", (W, H), 0); d = ImageDraw.Draw(m)
    if t >= LEAD: d.polygon([(-10, 0), (e + (H / 2) * SL, 0), (e - (H / 2) * SL, H), (-10, H)], fill=255)
    return m


chg = frames("flow_charge", "dicebound/chevalier-charge", {}); idle = frames("flow_idle", "dicebound/chevalier-charged", {}); sla = frames("flow_slash", "dicebound/chevalier-slash", {"lead": LEAD, "sweep": SWEEP})
base = stage(); seq = [(base.copy(), "シュヴァリエのチャージのターン")] * 3
for i, f in enumerate(chg):
    a = 0.3 * min(1.0, i / 6.0) + (DARK - 0.3) * max(0.0, min(1.0, (i - 13) / 3.0))      # 完了 (13 コマ目) から 3 コマで、 かなり暗く
    b = dark(base.copy(), a); b.paste(f, (0, 0), f); seq.append((b, "シュヴァリエのチャージのターン: チャージ → 完了で暗転"))
for r in range(2):
    for f in idle:
        b = dark(base.copy(), DARK); b.paste(f, (0, 0), f); seq.append((b, "プレイヤーの手番: 待機 (ループ)"))
h0 = len(seq); cut0 = int(LEAD * 8)
for i, f in enumerate(sla):
    t = i / 8.0
    b = Image.composite(base, dark(base.copy(), DARK if t < LEAD else DARK), cut_mask(t))     # 縁の後ろは晴れる
    b.paste(f, (0, 0), f)
    j = i - cut0 - 1
    if 0 <= j < len(SHAKE):
        dx, dy = SHAKE[j]; c = Image.new("RGB", (W, H), (24, 24, 30)); c.paste(b, (dx, dy)); b = c
    seq.append((b, "シュヴァリエの大技のターン: きらめきが伸びて消える" if t < LEAD else "シュヴァリエの大技のターン: 切り裂く"))
seq += [(base.copy(), "")] * 4
big = [framed(a, c) for a, c in seq]
big[0].save(os.path.join(OUT, "flow.gif"), save_all=True, append_images=big[1:], duration=125, loop=0)
picks = [3 + 12, 3 + 14, 3 + 16, 3 + 22, h0 + 2, h0 + 4, h0 + 5, h0 + 6, h0 + 7, h0 + 8, h0 + 9, h0 + 11]
o = Image.new("RGB", (W * 4, H * 3), (0, 0, 0))
for q, i in enumerate(picks):
    if i < len(seq): o.paste(seq[i][0], ((q % 4) * W, (q // 4) * H))
o.save(os.path.join(OUT, "flow_contact.png"))
io.open(os.path.join(OUT, "flow.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>シュヴァリエ 一連の流れ</title>
<style>body{margin:0;background:#101014;color:#e1e4ec;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1960px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:17px;margin:26px 0 6px}p{color:#9aa0b0;margin:4px 0 0}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:8px}.s img{max-width:none}</style></head><body><main>
<h1>シュヴァリエの大技: 一連の流れ (試作)</h1>
<p>チャージ → 完了で画面をかなり暗く落とす (中心だけ鋭く光る) → 待機 (2 周) → きらめきが伸びて消え、 暗い一拍 → 刃が画面全体を切り裂く (暗転は刃の後ろから晴れる)。 1 秒 8 コマ・2 倍・背景と敵と下の枠は仮。</p>
<h2>通し</h2><img src="flow.gif"><h2>コマの抜き出し (上段 = チャージ完了の前後・中下段 = 大技)</h2><div class="s"><img src="flow_contact.png"></div></main></body></html>''')
print(len(chg), len(idle), len(sla), len(seq))
