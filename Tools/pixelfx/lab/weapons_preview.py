# プレイヤーの武器の攻撃 (たたき台 12 個) の見本。 480×270 の舞台・1 秒 8 コマ・2 倍。 出力は Captures/fxlib/_weapons/index.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_weapons"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
src = io.open(os.path.join(PFX, "lab", "moves_board.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def frames(")])      # 舞台を借りる
# (武器, 色の組, [(id, 見出し, 絵の一言)])
PLAN = [("剣", "steel", [("sword-normal", "通常", "一太刀 (Sword slash + 流れの筋を少し)"), ("sword-crit", "会心", "ぐるりと一周 + 交差する二太刀 (Spin slash + Double slash)・金")]),
        ("斧", "ash", [("axe-normal", "通常", "重い振り下ろし (Heavy cleave)"), ("axe-crit", "会心", "廻天 + 振り下ろし (Spin slash + Heavy cleave)・血の色")]),
        ("短剣", "poison", [("dagger-normal", "通常", "小さな連撃 (Dagger flurry)・毒の緑"), ("dagger-crit", "会心", "刈り取る三日月 + 連撃 (Scythe reap + Dagger flurry)・夜の紫")]),
        ("盾", "steel", [("shield-normal", "通常", "盾で殴る (Blunt hit)"), ("shield-crit", "会心", "受けた重さを返す (Parry + Blunt hit)・金")])]
INK = {"sword-crit": "holy", "axe-crit": "blood", "dagger-crit": "null", "shield-crit": "holy"}
base = stage(); html = []; thumbs = []
for fam, ink, moves in PLAN:
    html.append("<h2>%s</h2>" % fam)
    for wid, name, note in moves:
        png = os.path.join(OUT, wid + "_sheet.png")
        subprocess.run(["node", "lab/lcd_frames.mjs", "dicebound/weapons", png, "--ink", INK.get(wid, ink), "--fps", "8", "--opts", json.dumps({"weapon": wid})], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
        m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
        fr = [sh.crop((i * W, 0, (i + 1) * W, H)) for i in range(m["n"])]
        while len(fr) > 1 and fr[-1].getchannel("A").getbbox() is None: fr.pop()
        seq = [base.copy()] * 2
        for f in fr:
            b = base.copy(); b.paste(f, (0, 0), f); seq.append(b)
        seq += [base.copy()] * 3
        big = [x.resize((W * SC, H * SC), Image.NEAREST) for x in seq]
        big[0].save(os.path.join(OUT, wid + ".gif"), save_all=True, append_images=big[1:], duration=125, loop=0)
        n = len(seq) - 5; o = Image.new("RGB", (W * 5, H), (0, 0, 0))
        for q, i in enumerate([3, 2 + max(2, n // 4), 2 + max(3, n // 2), 2 + max(4, n * 2 // 3), 2 + max(5, n * 5 // 6)]): o.paste(seq[min(i, len(seq) - 1)], (q * W, 0))
        o.save(os.path.join(OUT, wid + "_contact.png")); thumbs.append(o)
        html.append('<h3>%s <small>%s</small></h3><img src="%s.gif"><div class="s"><img loading="lazy" src="%s_contact.png"></div>' % (name, note, wid, wid))
allc = Image.new("RGB", (W * 5 // 2, H * len(thumbs) // 2))
for k, o in enumerate(thumbs): allc.paste(o.resize((W * 5 // 2, H // 2), Image.NEAREST), (0, k * H // 2))
allc.save(os.path.join(OUT, "all.png"))
io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>武器の攻撃 たたき台</title>
<style>body{margin:0;background:#121116;color:#e6e2ee;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:2420px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:19px;margin:34px 0 0;border-top:1px solid #36323e;padding-top:14px}h3{font-size:16px;margin:20px 0 6px}small{color:#a39cb2;font-weight:400;margin-left:8px}
p{color:#a39cb2}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:6px}.s img{max-width:none}</style></head><body><main>
<h1>プレイヤーの武器の攻撃: たたき台 (家系ごとに通常と会心・8 個)</h1>
<p>元の効果を、 式を書き直さずに、 この画面の大きさで流した物 (大きさ・置く場所・色・速さだけ変えた)。 プレイヤーの攻撃なので、 絵は敵の体の上に出る。 1 秒 8 コマ・2 倍・背景と敵と下の盤は仮。</p>
''' + "".join(html) + "</main></body></html>")
print("ok", len(thumbs))
