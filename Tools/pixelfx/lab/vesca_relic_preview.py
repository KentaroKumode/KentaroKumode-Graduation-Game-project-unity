# ヴェスカの遺物 (汎用 3 つ) の見本。 480×270 の舞台・1 秒 8 コマ・2 倍・紫の色の組。 出力は Captures/fxlib/_vesca/relic.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_vesca"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
src = io.open(os.path.join(PFX, "lab", "moves_board.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def frames(")])      # 舞台を借りる
base = stage(); cards = []
for kind, title in (("strike", "遺物・攻撃: 胸の前から、 こちらの盤へ突き刺す光の槍 + 当たり"), ("guard", "遺物・防御: ヴェスカを包む光の球 (Divine shield)"), ("hex", "遺物・妨害: こちらの盤に、 呪いの印が描かれて燃え崩れる (Hex sigil)")):
    png = os.path.join(OUT, "relic_%s_sheet.png" % kind)
    subprocess.run(["node", "lab/lcd_frames.mjs", "dicebound/vesca-relic", png, "--ink", "null", "--fps", "8", "--opts", json.dumps({"kind": kind})], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA"); seq = [base.copy()] * 2
    for i in range(m["n"]):
        f = sh.crop((i * W, 0, (i + 1) * W, H)); b = base.copy(); b.paste(f, (0, 0), f); seq.append(b)
    seq += [base.copy()] * 3
    big = [x.resize((W * SC, H * SC), Image.NEAREST) for x in seq]
    big[0].save(os.path.join(OUT, "relic_%s.gif" % kind), save_all=True, append_images=big[1:], duration=125, loop=0)
    o = Image.new("RGB", (W * 5, H), (0, 0, 0))
    for q, i in enumerate([2, 3, 4, 6, 9]): o.paste(seq[min(i, len(seq) - 1)], (q * W, 0))
    o.save(os.path.join(OUT, "relic_%s_contact.png" % kind))
    cards.append('<h2>%s</h2><img src="relic_%s.gif"><div class="s"><img loading="lazy" src="relic_%s_contact.png"></div>' % (title, kind, kind))
io.open(os.path.join(OUT, "relic.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>ヴェスカ 遺物</title>
<style>body{margin:0;background:#100e14;color:#e4def0;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:2420px;margin:0 auto;padding:16px 16px 80px}h1{font-size:20px;margin:0}h2{font-size:17px;margin:30px 0 6px}p{color:#a39ab4}a{color:#b89cf8}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:6px}.s img{max-width:none}</style></head><body><main>
<p><a href="flow.html">← 大技</a></p><h1>ヴェスカ: 遺物を使う時の汎用 3 つ (試作)</h1><p>毎ターン、 抽選された遺物の種類で出し分ける。 1 秒 8 コマ・2 倍・背景と敵と下の盤は仮。</p>''' + "".join(cards) + "</main></body></html>")
print("ok")
