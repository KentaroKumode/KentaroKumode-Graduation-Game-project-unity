# ボスの必殺以外の技の、 絵コンテ (案出し用)。 元の効果を未加工のまま、 そのボスの色の組で、 戦闘画面 480×270 の上に置く。
# 大きさも動きも元のまま ── どの素材を土台にするか・どこに出すかを決めるための下書き。 出力は Captures/fxlib/_moves/index.html (git 管理外)。
import io, json, os, subprocess
from PIL import Image, ImageDraw
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_moves"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
FOOT, CHEST, HEAD, BOARD = (240, 196), (240, 130), (240, 60), (240, 250)      # 敵の足もと・胸・頭上・こちらの盤


def stage():
    im = Image.new("RGB", (W, H), (25, 23, 28)); d = ImageDraw.Draw(im)
    d.rectangle([0, FLOOR, W, H], fill=(32, 29, 36)); d.line([(0, FLOOR), (W, FLOOR)], fill=(46, 42, 52))
    x0, y0 = EX - 50, FLOOR - 120
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0
        d.line([(x0, y0 + y), (x0 + 99, y0 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((74, 70, 84), (29, 27, 33))))
    d.rectangle([40, 232, 440, 262], outline=(64, 58, 76))
    for k in range(3): d.rectangle([60 + k * 34, 237, 80 + k * 34, 257], fill=(92, 86, 108), outline=(150, 142, 172))
    for k in range(4): d.rectangle([250 + k * 44, 239, 286 + k * 44, 255], outline=(96, 88, 116))
    return im


def frames(eid, ink):
    name = eid.replace("/", "_") + "_" + ink; png = os.path.join(OUT, name + "_sheet.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", eid, png, "--ink", ink, "--fps", "8"], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    return [sh.crop((i * m["w"], 0, (i + 1) * m["w"], m["h"])) for i in range(m["n"])], m["pivot"], name


# (ボス, 色の組, [(技, 何をする技か, [(元の効果, 置く場所, 一言)])])
PLAN = [
    ("審判官", "holy", [
        ("審判の炎", "毎ターンの終わりに、 軽減できないダメージ", [("holy/smite", BOARD, "案 A: 光の槌がこちらの盤へ落ちる (大技の「降りる光」の小さい版)"), ("fire/spirit-flame", BOARD, "案 B: こちらの盤の上に、 金の炎が灯って燃える"), ("holy/halo", BOARD, "案 C: こちらの盤に光の輪が掛かる")]),
    ]),
    ("シュヴァリエ", "steel", [
        ("通常攻撃", "レイピアの突き", [("melee/thrust", CHEST, "案 A: 突き (元は横向き ── 正面へ向けて作り直す)"), ("impacts/crit-hit", BOARD, "案 B: こちらの盤に、 鋭い当たりの光だけ"), ("melee/double-slash", CHEST, "案 C: 二段の斬り")]),
        ("構え", "形態の切り替え (コントラタック ⇄ オポジション)", [("impacts/parry", CHEST, "案 A: 受け流しの火花 (刃を構え直す)"), ("holy/divine-shield", FOOT, "案 B: 守りの構えの輪"), ("status/haste", FOOT, "案 C: 攻めの構えの気配")]),
    ]),
    ("灰燼の王", "ash", [
        ("通常攻撃", "火の一撃", [("melee/heavy-cleave", CHEST, "案 A: 重い薙ぎ払い"), ("fire/fireball", CHEST, "案 B: 火の玉"), ("fire/fire-breath", CHEST, "案 C: 火を吐く")]),
        ("灰の再生", "毎ターン HP が戻る (前半の段だけ)", [("holy/heal", FOOT, "案 A: 立ちのぼる回復の光 (色を残り火に)"), ("fire/spirit-flame", FOOT, "案 B: 足もとに火が灯り直す"), ("holy/resurrection", FOOT, "案 C: 灰から起き上がる")]),
    ]),
    ("ヴェスカ", "null", [
        ("遺物・攻撃", "攻撃の遺物を使う (握斧・破城槌・殺戮 など)", [("arcane/arcane-missiles", CHEST, "案 A: 光の矢"), ("arcane/star-fall", BOARD, "案 B: 星が降る"), ("shadow/dark-explosion", BOARD, "案 C: 闇の炸裂")]),
        ("遺物・防御", "防御の遺物を使う (手甲・城盾・真守 など)", [("scifi/force-field", FOOT, "案 A: 力場"), ("holy/divine-shield", FOOT, "案 B: 盾の輪"), ("arcane/rune-circle", FOOT, "案 C: 足もとの紋")]),
        ("遺物・妨害", "妨害の遺物を使う (塗り薬・麻痺毒・刻 など)", [("shadow/curse", BOARD, "案 A: こちらの盤に呪い"), ("shadow/hex-sigil", BOARD, "案 B: こちらの盤に印"), ("shadow/shadow-tendrils", BOARD, "案 C: 影の触手")]),
        ("天与の剣", "最大 HP を削る・攻撃 +20", [("arcane/arcane-beam", CHEST, "案 A: 光線"), ("melee/scythe-reap", BOARD, "案 B: 刈り取る一閃"), ("shadow/soul-drain", BOARD, "案 C: 命を吸い取る")]),
        ("天与の盾", "受けるダメージ −99%", [("holy/divine-shield", FOOT, "案 A: 盾の輪 (大きく・白く)"), ("ice/ice-crystal", FOOT, "案 B: 結晶に包まれる"), ("scifi/energy-core", CHEST, "案 C: 胸の核")]),
        ("天与の指輪", "こちらはこのターン行動できない", [("shadow/grasping-hands", BOARD, "案 A: こちらの盤を手が掴む"), ("status/petrified", BOARD, "案 B: こちらの盤が石になる"), ("arcane/rune-circle", BOARD, "案 C: こちらの盤を輪が縛る")]),
    ]),
]

base = stage(); html = []
for boss, ink, moves in PLAN:
    html.append("<h2>%s</h2>" % boss)
    for mv, what, cands in moves:
        html.append('<h3>%s <small>%s</small></h3><div class="r">' % (mv, what))
        for eid, at, note in cands:
            try: fr, pv, name = frames(eid, ink)
            except Exception as e: html.append("<figure><figcaption>%s: 書き出せず (%s)</figcaption></figure>" % (eid, type(e).__name__)); continue
            seq = []
            for f in fr[:40]:
                b = base.copy(); b.paste(f, (at[0] - pv[0], at[1] - pv[1]), f); seq.append(b)
            seq[0].save(os.path.join(OUT, name + ".gif"), save_all=True, append_images=seq[1:] + [base.copy()] * 3, duration=125, loop=0)
            html.append('<figure><img src="%s.gif"><figcaption>%s<br><code>%s</code></figcaption></figure>' % (name, note, eid))
        html.append("</div>")
io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>必殺以外の技 絵コンテ</title>
<style>body{margin:0;background:#121116;color:#e6e2ee;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1560px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:19px;margin:34px 0 0;border-top:1px solid #36323e;padding-top:14px}h3{font-size:16px;margin:16px 0 6px}small{color:#a39cb2;font-weight:400;margin-left:8px}
p{color:#a39cb2}.r{display:flex;flex-wrap:wrap;gap:12px}figure{margin:0;width:480px}img{image-rendering:pixelated;display:block;width:480px}figcaption{color:#c4bed2;font-size:13px;margin-top:4px}code{color:#8e87a0;font-size:12px}</style></head><body><main>
<h1>必殺以外の技: 絵コンテ (案出し)</h1>
<p>元の効果を未加工のまま、 そのボスの色の組で、 戦闘画面の上に置いた物。 大きさ・向き・動きは元のまま (多くは横向きの素材) ── 決めたいのは、 どの素材を土台にするかと、 どこに出すか (敵の側か、 こちらの盤か)。 1 秒 8 コマ。</p>
''' + "".join(html) + "</main></body></html>")
print("ok")
