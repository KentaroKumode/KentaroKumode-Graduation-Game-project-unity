# コマの速さの見比べ: 全部 8 コマ / 形 8 コマ + 粒 24 コマ / 全部 24 コマ。 出力は Captures/fxlib/_fps/index.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_fps"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
src = io.open(os.path.join(PFX, "lab", "moves_board.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def frames(")])
base = stage(); html = []
MODES = (("a8", "全部 8 コマ (今)", ["--fps", "8"], 125), ("mix", "形 8 コマ + 粒 24 コマ", ["--fps", "8", "--pfps", "24"], 40), ("a24", "全部 24 コマ", ["--fps", "24"], 40))
for eid, key, val, ink, name in (("dicebound/weapons", "weapon", "sword-normal", "steel", "剣・通常"), ("dicebound/weapons", "weapon", "dagger-normal", "steel", "短剣・通常"), ("dicebound/weapons", "weapon", "axe-crit", "holy", "斧・会心"), ("dicebound/moves", "move", "king-attack", "ash", "灰燼の王・通常攻撃")):
    html.append('<h2>%s</h2><div class="r">' % name)
    for mid, label, fl, ms in MODES:
        png = os.path.join(OUT, "%s_%s.png" % (val, mid))
        subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", ink, *fl, "--opts", json.dumps({key: val})], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
        m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
        fr = [sh.crop((i * W, 0, (i + 1) * W, H)) for i in range(m["n"])]
        while len(fr) > 1 and fr[-1].getchannel("A").getbbox() is None: fr.pop()
        seq = []
        for f in fr:
            b = base.copy(); b.paste(f, (0, 0), f); seq.append(b.resize((W * SC, H * SC), Image.NEAREST))
        seq += [base.resize((W * SC, H * SC), Image.NEAREST)] * (400 // ms)
        seq[0].save(os.path.join(OUT, "%s_%s.gif" % (val, mid)), save_all=True, append_images=seq[1:], duration=ms, loop=0)
        os.remove(png); html.append('<figure><img src="%s_%s.gif"><figcaption>%s (%d コマ)</figcaption></figure>' % (val, mid, label, len(fr)))
    html.append("</div>")
io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>コマの速さの見比べ</title>
<style>body{margin:0;background:#121116;color:#e6e2ee;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:3000px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:18px;margin:30px 0 8px;border-top:1px solid #36323e;padding-top:12px}p{color:#a39cb2}
.r{display:flex;flex-wrap:wrap;gap:12px}figure{margin:0}img{image-rendering:pixelated;display:block;width:720px;max-width:100%}figcaption{color:#c4bed2;margin-top:4px}</style></head><body><main>
<h1>コマの速さの見比べ</h1>
<p>左: 全部 1 秒 8 コマ (今のゲーム)。 中: 斬撃や光などの形は 8 コマのまま、 粒 (火花・欠片・星) だけ 24 コマ。 右: 全部 24 コマ。 GIF の都合で、 24 コマは 1 コマ 40 ミリ秒 (実際は約 42)。</p>
''' + "".join(html) + "</main></body></html>")
print("ok")
