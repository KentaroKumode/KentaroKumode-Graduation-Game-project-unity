# 審判官の大技の一連の流れ: チャージ → 待機 (プレイヤーの手番) → 光の帯が増えて日輪が縮み、 白く飛ぶ → 画面の大半に祝福の光。
# 480×270 の舞台・1 秒 8 コマ・2 倍・金の色の組。 背景と敵と下の枠は仮。 出力は Captures/fxlib/_judge/flow.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw, ImageFont
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_judge"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
FONT = ImageFont.truetype(r"C:/Windows/Fonts/meiryo.ttc", 18)
SCALE, LEAD = 1.4, 1.4
SHAKE = [(0, 0), (0, 4), (-3, -2), (2, 2), (-2, -1), (1, 1), (0, -1)]


def stage():
    im = Image.new("RGB", (W, H), (26, 22, 20)); d = ImageDraw.Draw(im)
    d.rectangle([0, FLOOR, W, H], fill=(34, 29, 25)); d.line([(0, FLOOR), (W, FLOOR)], fill=(48, 42, 34))
    x0, y0 = EX - 50, FLOOR - 120
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0
        d.line([(x0, y0 + y), (x0 + 99, y0 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((70, 64, 60), (30, 27, 26))))
    d.rectangle([40, 232, 440, 262], outline=(70, 60, 46)); return im


def frames(name, eid, opts):
    png = os.path.join(OUT, name + "_sheet.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", "holy", "--fps", "8", "--opts", json.dumps(opts)], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    return [sh.crop((i * m["w"], 0, (i + 1) * m["w"], m["h"])) for i in range(m["n"])]


def dark(im, a): return im if a <= 0.01 else im.point(lambda v: int(v * (1 - 0.5 * a)))
def sm(a, b, x): x = max(0.0, min(1.0, (x - a) / (b - a))); return x * x * (3 - 2 * x)
def framed(im, cap):
    o = Image.new("RGB", (W * SC, H * SC + 30), (20, 18, 15)); o.paste(im.resize((W * SC, H * SC), Image.NEAREST), (0, 0))
    ImageDraw.Draw(o).text((8, H * SC + 3), cap, font=FONT, fill=(233, 225, 210)); return o


chg = frames("flow_charge", "dicebound/judge-charge", {"scale": SCALE})
idle = frames("flow_idle", "dicebound/judge-charged", {"scale": SCALE})
rel = frames("flow_release", "dicebound/judge-release", {"scale": SCALE, "lead": LEAD})
ver = frames("flow_verdict", "dicebound/judge-verdict", {})
base = stage(); seq = [(base.copy(), "審判官のチャージのターン")] * 3
for i, f in enumerate(chg):
    b = dark(base.copy(), min(1.0, i / 6.0)); b.paste(f, (0, 0), f); seq.append((b, "審判官のチャージのターン: チャージ"))
for f in idle:
    b = dark(base.copy(), 1); b.paste(f, (0, 0), f); seq.append((b, "プレイヤーの手番: 待機 (ループ)"))
h0 = len(seq); off = int(round(LEAD * 8))            # off = 祝福の光を始めるコマ (白く飛ぶコマの 1 つ前)
for i in range(max(len(rel), off + len(ver))):
    t = i / 8.0; j = i - off
    b = dark(base.copy(), 1 - sm(LEAD, LEAD + 0.4, t))
    if 0 <= j < len(ver): b.paste(ver[j], (0, 0), ver[j])
    if i < len(rel): b.paste(rel[i], (0, 0), rel[i])
    if 2 <= j < 2 + len(SHAKE):
        dx, dy = SHAKE[j - 2]; c = Image.new("RGB", (W, H), (26, 22, 20)); c.paste(b, (dx, dy)); b = c
    seq.append((b, "審判官の大技のターン: 帯が増え、 日輪が縮む" if j < 0 else "審判官の大技のターン: 光が降りる"))
seq += [(base.copy(), "")] * 4
big = [framed(a, c) for a, c in seq]
big[0].save(os.path.join(OUT, "flow.gif"), save_all=True, append_images=big[1:], duration=125, loop=0)
picks = [h0 + 1, h0 + 5, h0 + 8, h0 + 10, h0 + 11, h0 + 12, h0 + 13, h0 + 14, h0 + 17, h0 + 22, h0 + 30, h0 + 35]
o = Image.new("RGB", (W * 4, H * 3), (0, 0, 0))
for q, i in enumerate(picks):
    if i < len(seq): o.paste(seq[i][0], ((q % 4) * W, (q // 4) * H))
o.save(os.path.join(OUT, "flow_contact.png"))
io.open(os.path.join(OUT, "flow.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>審判官 一連の流れ</title>
<style>body{margin:0;background:#14120f;color:#e9e1d2;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1960px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:17px;margin:26px 0 6px}p{color:#a59c8b;margin:4px 0 0}a{color:#e0b84a}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:8px}.s img{max-width:none}</style></head><body><main>
<p><a href="charge.html">← チャージと待機</a></p><h1>審判官の大技: 一連の流れ (試作)</h1>
<p>チャージ → 待機 (1 周) → 光の帯が増えて濃くなる → 日輪が縮む → 白く飛ぶ → 画面の大半に光が降りる。 1 秒 8 コマ・2 倍・背景と敵と下の枠は仮。 下の字は今どの場面か。</p>
<h2>通し</h2><img src="flow.gif"><h2>大技のターンのコマの抜き出し</h2><div class="s"><img src="flow_contact.png"></div></main></body></html>''')
print(len(chg), len(idle), len(rel), len(ver), len(seq))
