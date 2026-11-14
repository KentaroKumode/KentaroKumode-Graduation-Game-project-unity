# ヴェスカの大技の一連の流れ (2 本): 1〜3 段目 = 時が止まり、 凍った画面が砕ける / 4 段目 = 時が壊れ、 画面が乱れて信号が切れる。
# チャージと待機は共通。 480×270 の舞台・1 秒 8 コマ・2 倍・紫の色の組。 出力は Captures/fxlib/_vesca/flow.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw, ImageFont
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_vesca"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
FONT = ImageFont.truetype(r"C:/Windows/Fonts/meiryo.ttc", 18)
DARK, LEAD = 0.55, 1.0
GLITCH = 0.9                     # 4 段目: 画面が乱れてから、 信号が切れる (vesca-off) へ替えるまでの秒
SHAKE = [(0, 4), (-3, -2), (3, 2), (-2, -1), (1, 1), (0, -1)]


def stage():
    im = Image.new("RGB", (W, H), (25, 22, 30)); d = ImageDraw.Draw(im)
    d.rectangle([0, FLOOR, W, H], fill=(32, 28, 38)); d.line([(0, FLOOR), (W, FLOOR)], fill=(46, 40, 56))
    x0, y0 = EX - 50, FLOOR - 120
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0
        d.line([(x0, y0 + y), (x0 + 99, y0 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((74, 68, 86), (29, 26, 34))))
    d.rectangle([40, 232, 440, 262], outline=(64, 56, 78)); return im


def frames(name, eid, opts):
    png = os.path.join(OUT, name + "_sheet.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", "null", "--fps", "8", "--opts", json.dumps(opts)], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    # 画面全体でない物 (玉のまわりだけ) は、 画面の大きさの透明な板に、 原点を玉の中心 (240,130) に合わせて置く
    out = []
    for i in range(m["n"]):
        f = sh.crop((i * m["w"], 0, (i + 1) * m["w"], m["h"]))
        if m["w"] != W: g = Image.new("RGBA", (W, H), (0, 0, 0, 0)); g.paste(f, (240 - m["pivot"][0], 130 - m["pivot"][1])); f = g
        out.append(f)
    return out


def dark(im, a): return im if a <= 0.01 else im.point(lambda v: int(v * (1 - a)))
def empty(f): return f.getchannel("A").getbbox() is None
def framed(im, cap):
    o = Image.new("RGB", (W * SC, H * SC + 30), (18, 16, 22)); o.paste(im.resize((W * SC, H * SC), Image.NEAREST), (0, 0))
    ImageDraw.Draw(o).text((8, H * SC + 3), cap, font=FONT, fill=(228, 222, 240)); return o
def shaken(b, j):
    if not (0 <= j < len(SHAKE)): return b
    c = Image.new("RGB", (W, H), (5, 4, 10)); c.paste(b, SHAKE[j]); return c


chg = frames("flow_charge", "dicebound/vesca-charge", {}); idle = frames("flow_idle", "dicebound/vesca-charged", {})
stop = frames("flow_stop", "dicebound/vesca-stop", {}); sha = frames("flow_shatter", "dicebound/vesca-shatter", {})
lost = frames("flow_lost", "dicebound/vesca-lost", {}); gli = frames("flow_glitch", "dicebound/vesca-glitch", {}); off = frames("flow_off", "dicebound/vesca-off", {})
while len(sha) > 1 and empty(sha[-1]): sha.pop()
while len(off) > 1 and empty(off[-1]): off.pop()
base = stage(); head = [(base.copy(), "ヴェスカのチャージのターン")] * 3
for i, f in enumerate(chg):
    b = dark(base.copy(), DARK * min(1.0, i / 6.0)); b.paste(f, (0, 0), f); head.append((b, "ヴェスカのチャージのターン: チャージ"))
for r in range(1):
    for f in idle:
        b = dark(base.copy(), DARK); b.paste(f, (0, 0), f); head.append((b, "プレイヤーの手番: 待機 (ループ)"))
cards = []
n1 = int(LEAD * 8)
for key, title, lead, parts in (
        ("p123", "1〜3 段目: 時が止まり、 凍った画面が砕ける", stop, [(sha, "ヴェスカの大技 (1〜3 段目): 画面が凍る → ひび → 砕ける", 3)]),
        ("p4", "4 段目 (天与) だけ: 時が壊れ、 画面が乱れて、 信号が切れる", lost, [(gli[:int(GLITCH * 8)], "ヴェスカの大技 (4 段目): 画面が乱れる", 0), (off, "ヴェスカの大技 (4 段目): 信号が切れる → 戻る", -1)])):
    seq = list(head); h0 = len(seq)
    for i in range(n1):
        b = dark(base.copy(), DARK); f = lead[min(i, len(lead) - 1)]; b.paste(f, (0, 0), f)
        seq.append((b, "ヴェスカの大技のターン: " + ("針が遅くなって止まる" if key == "p123" else "針が暴れ、 文字盤がずれる")))
    for fr, cap, sh0 in parts:
        for j, f in enumerate(fr):
            b = base.copy(); b.paste(f, (0, 0), f); seq.append((shaken(b, j - sh0) if sh0 >= 0 else b, cap))
    seq += [(base.copy(), "")] * 4
    big = [framed(a, c) for a, c in seq]
    big[0].save(os.path.join(OUT, key + ".gif"), save_all=True, append_images=big[1:], duration=125, loop=0)
    m = len(seq) - h0 - 4; picks = [h0 + int(m * q / 12) for q in range(12)]
    o = Image.new("RGB", (W * 4, H * 3), (0, 0, 0))
    for q, i in enumerate(picks): o.paste(seq[i][0], ((q % 4) * W, (q // 4) * H))
    o.save(os.path.join(OUT, key + "_contact.png"))
    cards.append('<h2>%s</h2><img src="%s.gif"><p>大技のターンのコマの抜き出し</p><div class="s"><img loading="lazy" src="%s_contact.png"></div>' % (title, key, key))
o = Image.new("RGB", (W * 4, H), (0, 0, 0))
for q, i in enumerate([3 + 12, 3 + 14, 3 + len(chg) + 1, 3 + len(chg) + 13]): o.paste(head[i][0], (q * W, 0))
o.save(os.path.join(OUT, "charge_contact.png"))
io.open(os.path.join(OUT, "flow.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>ヴェスカ 一連の流れ</title>
<style>body{margin:0;background:#100e14;color:#e4def0;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1960px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:17px;margin:30px 0 6px}p{color:#a39ab4;margin:6px 0 0}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:6px}.s img{max-width:none}</style></head><body><main>
<h1>ヴェスカの大技: 一連の流れ (試作・2 本)</h1>
<p>チャージと待機 (虚の玉の上に時計の文字盤・長針は逆回り、 短針は順回り) は共通。 大技は、 1〜3 段目で 1 つ、 4 段目だけの物が 1 つ。 1 秒 8 コマ・2 倍・背景と敵と下の枠は仮。</p>
<h2>チャージ完了の前後と、 待機</h2><div class="s"><img src="charge_contact.png"></div>''' + "".join(cards) + "</main></body></html>")
print(len(chg), len(idle), len(stop), len(sha), len(lost), len(gli), len(off))
