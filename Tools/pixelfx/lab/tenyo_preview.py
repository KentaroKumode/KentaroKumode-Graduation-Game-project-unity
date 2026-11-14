# ヴェスカの大技 = 天与の剣 / 盾 / 指輪 (たたき台) の見本。 チャージ → 待機 → 発動を続けて。 480×270・1 秒 24 コマ・2 倍 (下のコマ送りは 3 コマおき)・金の色の組。 出力は Captures/fxlib/_tenyo/index.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_tenyo"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
src = io.open(os.path.join(PFX, "lab", "moves_board.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def frames(")])
base = stage(); html = []
for gid, name, note in (("sword", "天与の剣", "頭上に光の剣 → 落ちる → 白飛び → 天から金の光・左右の柱・盤を走る輪"), ("shield", "天与の盾", "足もとから環が積み上がる → 球が閉じる → 外へ衝撃の輪 → 金の球"), ("ring", "天与の指輪", "盤の上に輪が描かれ宝石が灯る → 落ちて盤を締める → 下半分が金に飛ぶ・光の格子")):
    png = os.path.join(OUT, gid + "_sheet.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", "dicebound/tenyo-heavy", png, "--ink", "holy", "--fps", "24", "--opts", json.dumps({"gift": gid})], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    fr = [sh.crop((i * W, 0, (i + 1) * W, H)) for i in range(m["n"])]
    while len(fr) > 1 and fr[-1].getchannel("A").getbbox() is None: fr.pop()
    seq = []
    for f in fr:
        b = base.copy(); b.paste(f, (0, 0), f); seq.append(b)
    seq += [base.copy()] * 9
    big = [x.resize((W * SC, H * SC), Image.NEAREST) for x in seq]
    big[0].save(os.path.join(OUT, gid + ".gif"), save_all=True, append_images=big[1:], duration=40, loop=0)
    seq = seq[::3]; cols = 8; rows = (len(seq) + cols - 1) // cols; o = Image.new("RGB", (W * cols // 2, H * rows // 2))
    for i, s in enumerate(seq): o.paste(s.resize((W // 2, H // 2), Image.NEAREST), ((i % cols) * W // 2, (i // cols) * H // 2))
    o.save(os.path.join(OUT, gid + "_frames.png"))
    html.append('<h2>%s <small>%s</small></h2><img src="%s.gif"><div class="s"><img loading="lazy" src="%s_frames.png"></div>' % (name, note, gid, gid))
io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>天与の大技 たたき台</title>
<style>body{margin:0;background:#121116;color:#e6e2ee;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:2000px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:19px;margin:34px 0 8px;border-top:1px solid #36323e;padding-top:14px}small{color:#a39cb2;font-weight:400;margin-left:8px;font-size:14px}
p{color:#a39cb2}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:6px}.s img{max-width:none}</style></head><body><main>
<h1>ヴェスカの大技 = 天与の剣 / 盾 / 指輪: たたき台</h1>
<p>チャージ (1.4 秒) → 待機 (1.5 秒のループを 1 周だけ) → 発動、 を続けて流した物。 暗転・揺れは入れていない。 1 秒 24 コマ・2 倍 (下のコマ送りは 3 コマおき)・背景と敵と下の盤は仮。 下はコマ送り (左上から右へ)。</p>
''' + "".join(html) + "</main></body></html>")
print("ok")
