# ボスの必殺以外の技 (たたき台 11 個) の見本。 480×270 の舞台・1 秒 8 コマ・2 倍・ボスごとの色の組。 出力は Captures/fxlib/_moves/draft.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_moves"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
src = io.open(os.path.join(PFX, "lab", "moves_board.py"), encoding="utf-8").read()
exec(src[src.index("def stage():"):src.index("def frames(")])      # 舞台を借りる
# (技の名前, 見出し, 色の組, 効果, 設定)
def M(mv, title): return (mv, title, None, "dicebound/moves", {"move": mv})
PLAN = [("ヴェスカ", "null", [M("relic-strike", "遺物・攻撃: 盤を斜めに断つ斬撃"), M("relic-guard", "遺物・防御: 光の球に包まれる (合格・変更なし)"),
                               ("relic-hex", "遺物・妨害: ヴェスカの正面に、 大きな印が描かれて燃え崩れる (最初の Hex sigil を大きくした物)", None, "dicebound/vesca-hex", {}),
                               ("tenyo-sword", "天与の剣: 金の斬撃が 3 つ (×と、 横一文字)・光の柱", "holy", "dicebound/moves", {"move": "tenyo-sword"}),
                               ("tenyo-shield", "天与の盾: 金の球・回る光の柱と輪", "holy", "dicebound/moves", {"move": "tenyo-shield"}),
                               ("tenyo-ring", "天与の指輪: 厚い金の輪が降りて盤を締める・宝石・光の壁・鎖", "holy", "dicebound/moves", {"move": "tenyo-ring"})]),
        ("灰燼の王", "ash", [M("king-attack", "通常攻撃: 火の三日月が盤を薙ぐ・炎が暴れる・火の塊が爆ぜる"), M("king-regen", "灰の再生: 体の内側から光が漏れ出る")]),
        ("審判官", "holy", [M("judge-flame", "審判の炎: 天から太い光が落ちる・着弾は閃光と輪と光の棘とひび")]),
        ("シュヴァリエ", "steel", [M("chev-thrust", "通常攻撃: きらめき → 一直線の突き (×印を外した)"), M("chev-stance", "構え (満点・変更なし)")])]
base = stage(); html = []; thumbs = []
for boss, ink, moves in PLAN:
    html.append("<h2>%s</h2>" % boss)
    for mv, title, ink2, eid, opts in moves:
        png = os.path.join(OUT, "draft_%s_sheet.png" % mv)
        subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", ink2 or ink, "--fps", "8", "--opts", json.dumps(opts)], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
        m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA"); seq = [base.copy()] * 2
        fr = [sh.crop((i * W, 0, (i + 1) * W, H)) for i in range(m["n"])]
        while len(fr) > 1 and fr[-1].getchannel("A").getbbox() is None: fr.pop()      # 後ろの空のコマは捨てる
        for f in fr:
            b = base.copy(); b.paste(f, (0, 0), f); seq.append(b)
        seq += [base.copy()] * 3
        big = [x.resize((W * SC, H * SC), Image.NEAREST) for x in seq]
        big[0].save(os.path.join(OUT, "draft_%s.gif" % mv), save_all=True, append_images=big[1:], duration=125, loop=0)
        o = Image.new("RGB", (W * 5, H), (0, 0, 0))
        n = len(seq) - 5
        for q, i in enumerate([2, 3, 2 + max(2, n // 4), 2 + max(3, n // 2), 2 + max(4, n * 3 // 4)]): o.paste(seq[min(i, len(seq) - 1)], (q * W, 0))
        o.save(os.path.join(OUT, "draft_%s_contact.png" % mv)); thumbs.append(o)
        html.append('<h3>%s <code>%s</code></h3><img src="draft_%s.gif"><div class="s"><img loading="lazy" src="draft_%s_contact.png"></div>' % (title, mv, mv, mv))
allc = Image.new("RGB", (W * 5 // 2, H * len(thumbs) // 2))
for k, o in enumerate(thumbs): allc.paste(o.resize((W * 5 // 2, H // 2), Image.NEAREST), (0, k * H // 2))
allc.save(os.path.join(OUT, "draft_all.png"))
io.open(os.path.join(OUT, "draft.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>必殺以外の技 たたき台</title>
<style>body{margin:0;background:#121116;color:#e6e2ee;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:2420px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:19px;margin:34px 0 0;border-top:1px solid #36323e;padding-top:14px}h3{font-size:16px;margin:20px 0 6px}code{color:#8e87a0;font-size:12px;margin-left:8px}
p{color:#a39cb2}a{color:#b89cf8}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:6px}.s img{max-width:none}</style></head><body><main>
<p><a href="index.html">← 素材の候補 (絵コンテ)</a></p><h1>必殺以外の技: たたき台 (11 個・第 2 版)</h1>
<p>形は、 この画面の大きさで描いた物 (元の素材を置いただけではない)。 粒は、 技ごとに複数の種類を組み合わせてある。 どれも 1.25 秒以内・暗転や白飛びは使わない。 1 秒 8 コマ・2 倍・背景と敵と下の盤は仮。</p>
''' + "".join(html) + "</main></body></html>")
print("ok", len(thumbs))
