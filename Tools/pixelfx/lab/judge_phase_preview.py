# 審判官: 待機のループの途中 (0 / 8 / 16 / 24 コマ目) から大技へ入っても、 絵が飛ばないことを確かめる見本。
# 版ごとに「待機の直前 8 コマ → 発光 (その位相の版) → 降りる光」の GIF と、 つなぎ目の前後 4 コマの抜き出し。 出力は Captures/fxlib/_judge/phase.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_judge"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC, SCALE, LEAD = 480, 270, 240, 196, 2, 1.4, 1.4
src = io.open(os.path.join(PFX, "lab", "judge_flow_preview.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def framed(")])      # stage / frames / dark / sm を借りる
idle = frames("ph_idle", "dicebound/judge-charged", {"scale": SCALE}); ver = frames("ph_verdict", "dicebound/judge-verdict", {})
base = stage(); cards = []
for k in range(4):
    rel = frames("ph_rel%d" % k, "dicebound/judge-release", {"scale": SCALE, "lead": LEAD, "phase": k}); seq = []
    for i in range(8):
        b = dark(base.copy(), 1); f = idle[(k * 8 - 8 + i) % 32]; b.paste(f, (0, 0), f); seq.append(b)
    off = int(round(LEAD * 8))
    for i in range(off + 8):
        b = dark(base.copy(), 1 - sm(LEAD, LEAD + 0.4, i / 8.0)); j = i - off
        if 0 <= j < len(ver): b.paste(ver[j], (0, 0), ver[j])
        if i < len(rel): b.paste(rel[i], (0, 0), rel[i])
        seq.append(b)
    big = [x.resize((W * SC, H * SC), Image.NEAREST) for x in seq]
    big[0].save(os.path.join(OUT, "phase%d.gif" % k), save_all=True, append_images=big[1:], duration=125, loop=0)
    o = Image.new("RGB", (W * 4, H), (0, 0, 0))
    for q, i in enumerate([6, 7, 8, 9]): o.paste(seq[i], (q * W, 0))
    o.save(os.path.join(OUT, "phase%d_seam.png" % k))
    cards.append('<h2>待機の %d コマ目 (%d 秒目) から入る版</h2><img src="phase%d.gif"><p>つなぎ目: 左 2 コマが待機の最後・右 2 コマが発光の最初</p><div class="s"><img src="phase%d_seam.png"></div>' % (k * 8, k, k, k))
io.open(os.path.join(OUT, "phase.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>審判官 入る位相</title>
<style>body{margin:0;background:#14120f;color:#e9e1d2;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1960px;margin:0 auto;padding:16px 16px 80px}h1{font-size:20px;margin:0}h2{font-size:17px;margin:26px 0 6px}p{color:#a59c8b;margin:4px 0}a{color:#e0b84a}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto}.s img{max-width:none}</style></head><body><main>
<p><a href="flow.html">← 一連の流れ</a></p><h1>審判官: 待機の途中から大技へ入る (4 つの版)</h1><p>待機は 32 コマで 1 周。 大技へは 0 / 8 / 16 / 24 コマ目のどれかで入る (ゲームは、 次にそのコマへ来るまで待機を回す = 最大 0.9 秒)。 版ごとに、 その位相から続く発光を焼いてある。</p>''' + "".join(cards) + "</main></body></html>")
