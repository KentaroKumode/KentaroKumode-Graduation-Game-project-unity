# シュヴァリエ: 切り口の面の描き方の見比べ。 割れ始めの 2 コマを、 切り口のまわりだけ 5 倍に拡大して並べる。 出力は Captures/fxlib/_chevalier/face.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_chevalier"
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
src = io.open(os.path.join(PFX, "lab", "chevalier_flow_preview.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def framed(")])
base = stage(); cards = []
for key, title in (("bands", "今のまま: 1 ドットずつ等間隔に冷める段"), ("hard", "A: 白い 1 本だけ (段なし)"), ("bloom", "B: 白い 1 本 + 黒の上ににじむ光 (網点)"), ("streak", "C: 所々だけ深く焼けた筋 (浅い所は白い 1 本)")):
    fr = frames("face_" + key, "dicebound/chevalier-slash", {"face": key}); row = Image.new("RGB", (3 * 800 + 20, 450), (16, 16, 20))
    for q, i in enumerate((8, 9, 10)):
        b = base.copy(); b.paste(fr[i], (0, 0), fr[i]); cx, cy = (250, 118) if i == 8 else (200, 150) if i == 9 else (150, 180)
        row.paste(b.crop((cx - 80, cy - 45, cx + 80, cy + 45)).resize((800, 450), Image.NEAREST), (q * 810, 0))
    row.save(os.path.join(OUT, "face_%s.png" % key))
    seq = []
    for i in range(5, len(fr)):
        b = base.copy() if i >= 6 else Image.new("RGB", (W, H), (2, 3, 8)); b.paste(fr[i], (0, 0), fr[i]); seq.append(b.resize((W * 2, H * 2), Image.NEAREST))
    seq[0].save(os.path.join(OUT, "face_%s.gif" % key), save_all=True, append_images=seq[1:] + [seq[-1]] * 3, duration=125, loop=0)
    cards.append('<h2>%s</h2><div class="s"><img src="face_%s.png"></div><img src="face_%s.gif">' % (title, key, key))
io.open(os.path.join(OUT, "face.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>シュヴァリエ 切り口</title>
<style>body{margin:0;background:#101014;color:#e1e4ec;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:2500px;margin:0 auto;padding:16px 16px 80px}h1{font-size:20px;margin:0}h2{font-size:17px;margin:30px 0 6px}p{color:#9aa0b0}a{color:#8ab8f0}img{image-rendering:pixelated;display:block;max-width:100%;margin-top:6px}.s{overflow-x:auto}.s img{max-width:none}</style></head><body><main>
<p><a href="flow.html">← 一連の流れ</a></p><h1>シュヴァリエ: 切り口の面の描き方 (見比べ)</h1><p>上の 3 枚 = 割れ始めの 3 コマを、 切り口のまわりだけ 5 倍に拡大。 下 = 一閃から割れるまでの GIF (2 倍)。</p>''' + "".join(cards) + "</main></body></html>")
