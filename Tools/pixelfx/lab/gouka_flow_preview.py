# 業火の一連の流れ: チャージ → 待機 (プレイヤーの手番) → 放つ直前の発光 → 着弾 + 画面の火。 480×270 の舞台・1 秒 8 コマ・2 倍。
import io, json, os, subprocess
from PIL import Image, ImageDraw, ImageFont
PFX = r"C:/Users/kumod/wt/hb/Tools/pixelfx"
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_gouka"
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
FONT = ImageFont.truetype(r"C:/Windows/Fonts/meiryo.ttc", 18)
LEAD = 1.4; O = dict(lead=LEAD, rush=1.2, hold=1.0, cool=1.6); SLOW = 6
SHAKE = [(0, 0), (0, 4), (-3, -2), (2, 2), (-2, -1), (1, 1), (0, -1)]


def stage():
    im = Image.new("RGB", (W, H), (26, 20, 16)); d = ImageDraw.Draw(im)
    d.rectangle([0, FLOOR, W, H], fill=(34, 27, 21)); d.line([(0, FLOOR), (W, FLOOR)], fill=(48, 38, 30))
    x0, y0 = EX - 50, FLOOR - 120
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0
        d.line([(x0, y0 + y), (x0 + 99, y0 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((70, 62, 58), (30, 26, 26))))
    d.rectangle([40, 232, 440, 262], outline=(70, 58, 46))
    return im


def frames(name, eid, opts, fps=8):
    png = os.path.join(OUT, name + "_sheet.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", "ash", "--fps", str(fps), "--opts", json.dumps(opts)], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    return [sh.crop((i * m["w"], 0, (i + 1) * m["w"], m["h"])) for i in range(m["n"])], m


def dark(im, a): return im if a <= 0.01 else im.point(lambda v: int(v * (1 - 0.5 * a)))
def red(im, a):
    if a <= 0.01: return im
    r, g, b = im.split()
    return Image.merge("RGB", (r.point(lambda v: min(255, int(v + 26 * a))), g.point(lambda v: int(v * (1 - 0.45 * a))), b.point(lambda v: int(v * (1 - 0.6 * a)))))
def sm(a, b, x): x = max(0.0, min(1.0, (x - a) / (b - a))); return x * x * (3 - 2 * x)
def empty(f): return f.getchannel("A").getbbox() is None


chg, _ = frames("flow_charge", "dicebound/gouka-charge", {"ash": 3})
idle, _ = frames("flow_idle", "dicebound/gouka-charged", {})
rel, _ = frames("flow_release", "dicebound/gouka-release", {"lead": LEAD})
scr, _ = frames("flow_screen", "dicebound/gouka-screen", dict(ash=3, depth=0.85, bias=0.2, mode="out", **O))
imp, mi = frames("flow_impact24", "dicebound/gouka-impact", {"ash": 3, "ring": "fire-ring", "flame": 1}, fps=24)
while len(scr) > 1 and empty(scr[-1]): scr.pop()
px, py = EX - mi["pivot"][0], FLOOR - mi["pivot"][1]
idx, k = [], 0
while k < len(imp):
    idx.append(k); n = len(idx); k += 1 if n <= SLOW else 2 if n <= SLOW + 2 else 3

base = stage(); seq = []          # (絵, 見出し)
for _ in range(4): seq.append((base.copy(), "王のチャージのターン"))
for i, f in enumerate(chg):
    b = dark(base.copy(), min(1.0, i / 6.0)); b.paste(f, (0, 0), f); seq.append((b, "王のチャージのターン: チャージ"))
for r in range(3):
    for f in idle:
        b = dark(base.copy(), 1); b.paste(f, (0, 0), f); seq.append((b, "プレイヤーの手番: 溜まった状態の待機 (ループ)"))
off = int(round(LEAD * 8))
for i in range(max(len(idx) + off, len(scr), len(rel))):
    t = i / 8.0; j = i - off; e1 = LEAD + O["rush"] + O["hold"]
    b = dark(base.copy(), 1 - sm(LEAD, LEAD + 0.4, t))
    if 0 <= j < len(idx): b.paste(imp[idx[j]], (px, py), imp[idx[j]])
    if i < len(scr): b.paste(scr[i], (0, 0), scr[i])
    b = red(b, (0.45 * sm(0, LEAD, t) if t < LEAD else 0.55 + 0.45 * (1 - sm(LEAD, LEAD + 0.5, t))) * (1 - sm(e1, e1 + O["cool"], t)))
    if i < len(rel): b.paste(rel[i], (0, 0), rel[i])          # 発光は赤いフィルターの上 (白く飛ぶ)
    if 2 <= j < 2 + len(SHAKE):
        dx, dy = SHAKE[j - 2]; c = Image.new("RGB", (W, H), (26, 20, 16)); c.paste(b, (dx, dy)); b = c
    seq.append((b, "王の大技のターン: 放つ直前の発光" if j < 0 else "王の大技のターン: 着弾"))
for _ in range(4): seq.append((base.copy(), ""))


def framed(im, cap):
    o = Image.new("RGB", (W * SC, H * SC + 30), (20, 17, 14)); o.paste(im.resize((W * SC, H * SC), Image.NEAREST), (0, 0))
    ImageDraw.Draw(o).text((8, H * SC + 3), cap, font=FONT, fill=(233, 225, 210)); return o


big = [framed(im, cap) for im, cap in seq]
big[0].save(os.path.join(OUT, "flow.gif"), save_all=True, append_images=big[1:], duration=125, loop=0)
h0 = 4 + len(chg) + 3 * len(idle)           # 大技のターンの最初のコマ
picks = [4 + len(chg) - 3, 4 + len(chg) - 1, 4 + len(chg), 4 + len(chg) + 2, h0 + 9, h0 + off - 1, h0 + off, h0 + off + 1, h0 + off + 3, h0 + off + 8, h0 + off + 16, h0 + off + 28]
o = Image.new("RGB", (W * 4, H * 3), (0, 0, 0))
for q, i in enumerate(picks):
    if i < len(seq): o.paste(seq[i][0], ((q % 4) * W, (q // 4) * H))
o.save(os.path.join(OUT, "flow_contact.png"))
io.open(os.path.join(OUT, "flow.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>業火 一連の流れ</title>
<style>body{margin:0;background:#14110e;color:#e9e1d2;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1960px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:17px;margin:26px 0 6px}p{color:#a59c8b;margin:4px 0 0}a{color:#e0a04a}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:8px}.s img{max-width:none}</style></head><body><main>
<p><a href="screen.html">← テンポの見比べ</a></p><h1>灰燼の王〈業火〉: 一連の流れ (試作)</h1>
<p>チャージ → 溜まった状態の待機 (プレイヤーの手番の間ループ・ここでは 3 周) → 放つ直前の発光 (1.4 秒) → 着弾と画面の火。 1 秒 8 コマ・2 倍に拡大・背景と敵と下の枠は仮。 下の字は今どの場面か。</p>
<h2>通し</h2><img src="flow.gif"><h2>コマの抜き出し</h2><div class="s"><img src="flow_contact.png"></div></main></body></html>''')
print(len(chg), len(idle), len(rel), len(scr), len(idx), len(seq))
