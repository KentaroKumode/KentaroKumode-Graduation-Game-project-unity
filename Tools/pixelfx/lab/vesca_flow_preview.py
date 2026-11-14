# ヴェスカの大技の一連の流れ (2 本): 1〜3 段目 = 時が止まり、 凍った画面が砕ける / 4 段目 = 時が壊れ、 画面が乱れて信号が切れる。
# チャージと待機は共通。 480×270 の舞台・1 秒 8 コマ・2 倍・紫の色の組。 出力は Captures/fxlib/_vesca/flow.html (git 管理外)。
import io, json, os, subprocess
import numpy as np
import random
from PIL import Image, ImageChops, ImageDraw, ImageFont
PFX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = r"C:/Users/kumod/My project/Captures/fxlib/_vesca"; os.makedirs(OUT, exist_ok=True)
W, H, EX, FLOOR, SC = 480, 270, 240, 196, 2
FONT = ImageFont.truetype(r"C:/Windows/Fonts/meiryo.ttc", 18)
DARK, LEAD = 0.55, 1.0
GLITCH = 0.9                     # 4 段目: 画面が乱れてから、 信号が切れる (vesca-off) へ替えるまでの秒
HOLD_AT, HOLD_N = 3, 8           # 4 段目: 乱れの 3 コマ目 (黒い帯が入り始め、 敵も盤もまだ見えている所) で絵を止め、 8 コマ (1 秒) そのまま。 帯だけがゆっくりずれ続ける
SHAKE = [(0, 4), (-3, -2), (3, 2), (-2, -1), (1, 1), (0, -1)]


def stage():
    im = Image.new("RGB", (W, H), (25, 22, 30)); d = ImageDraw.Draw(im)
    d.rectangle([0, FLOOR, W, H], fill=(32, 28, 38)); d.line([(0, FLOOR), (W, FLOOR)], fill=(46, 40, 56))
    x0, y0 = EX - 50, FLOOR - 120
    for y in range(120):
        k = int(y / 119.0 * 5) / 5.0
        d.line([(x0, y0 + y), (x0 + 99, y0 + y)], fill=tuple(int(a + (b - a) * k) for a, b in zip((74, 68, 86), (29, 26, 34))))
    d.rectangle([40, 232, 440, 262], outline=(64, 56, 78))
    # こちら側 (プレイヤーの盤) の仮の部品: 出目 3 つ・端子の枠・左上の体力の帯 (4 段目の乱れが、 ここにも及ぶのを見るため)
    for k in range(3): d.rectangle([60 + k * 34, 237, 60 + k * 34 + 20, 257], fill=(92, 84, 110), outline=(150, 140, 176))
    for k in range(4): d.rectangle([250 + k * 44, 239, 250 + k * 44 + 36, 255], outline=(96, 86, 118))
    d.rectangle([12, 10, 132, 18], outline=(96, 86, 118)); d.rectangle([14, 12, 96, 16], fill=(150, 140, 176)); return im


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


def tear_bands(plan, seed=7):
    """4 段目: 画面そのものの乱れ (効果を重ねるのではなく、 出来上がった画面を帯ごとに横へずらす ── 敵の側もプレイヤーの盤も一緒に)。
    コマごとの帯の一覧を作る (ゲーム側も同じ表を使う: library/dicebound/vesca-tear.json)。
    plan = コマごとの強さ (0〜1)。 "hold" のコマは、 直前に決めた帯の組をそのまま使い、 1 コマごとに少しずつ横へずらす (止まった画面が、 ゆっくりずれていく)。"""
    R = random.Random(seed); out = []; held = None
    def pick(k):
        bands = []
        for _ in range(int(k * 9 + (1 if k > 0.05 else 0))):
            h = R.choice([2, 3, 4, 6, 9, 14, 22, 30]); y = R.randrange(0, H - h); dx = int(R.choice([-1, 1]) * R.uniform(3, 96) * k)
            bands.append({"y": y, "h": h, "dx": dx, "kind": R.choices(["shift", "smear", "ghost"], [6, 2, 2])[0], "ca": min(9, 2 + abs(dx) // 10)})   # ca = 色収差 (赤と青を左右へ離す px)
        return bands
    for k in plan:
        if k == "hold":
            if held is None:
                held = [dict(b, v=R.choice([-1, 1]) * R.choice([1, 2, 2, 3, 4, 5])) for b in pick(1.0) + pick(0.6)]   # 帯を多めに。 v = 1 コマごとのずれ (px)
                for b in held: b["kind"] = "shift" if b["kind"] == "smear" else b["kind"]                                # 引き伸ばしは止まって見えるので使わない
            else:
                for b in held: b["dx"] += b["v"]
            out.append([{q: b[q] for q in ("y", "h", "dx", "kind", "ca")} for b in held])
        else: held = None; out.append(pick(k))
    return out


def torn(im, bands):
    """shift = 帯を横へずらす (はみ出た分は反対側から出る)・smear = 帯の左端の 1 列を横へ引き伸ばす・ghost = ずらした帯を、 元の上に明るい方だけ重ねる (二重像)。
    どの帯にも色収差を掛ける: 赤を左へ・青を右へ ca ドット離す (縁に赤紫と青緑のにじみが出る)。 (2026-11-14 作者の指定)"""
    out = im.copy()
    for b in bands:
        box = (0, b["y"], W, b["y"] + b["h"]); strip = im.crop(box)
        if b["kind"] == "shift": strip = ImageChops.offset(strip, b["dx"], 0)
        elif b["kind"] == "smear":
            x0 = abs(b["dx"]) * 3 % W; strip.paste(strip.crop((x0, 0, x0 + 1, b["h"])).resize((W - x0, b["h"])), (x0, 0))
        else: strip = ImageChops.lighter(strip, ImageChops.offset(strip, b["dx"], 0))
        r, g, bl = strip.split(); ca = b.get("ca", 0)
        out.paste(Image.merge("RGB", (ImageChops.offset(r, -ca, 0), g, ImageChops.offset(bl, ca, 0))), box)
    return out


def frozen_tint(im):
    """凍った画面: 色を抜いて、 紫がかった灰色に (時が止まった)。 明るさは少し上げる (暗いままだと、 砕けても見えない)。"""
    g = im.convert("L").point(lambda v: min(255, int(v * 1.5 + 14)))
    return Image.merge("RGB", (g.point(lambda v: int(v * 0.86)), g.point(lambda v: int(v * 0.8)), g))


def shattered(frozen, live, geom):
    """凍った画面の写しを、 破片の形に貼って動かす (ゲーム側がやる処理の見本)。 geom = その時の破片 (奥から順)。 破片ごとに 1 つのアフィン変換。"""
    out = live.copy()
    for sh in geom:
        src, dst = np.array(sh["src"], float), np.array(sh["dst"], float)
        x, y = dst[:, 0], dst[:, 1]
        if abs(np.dot(x, np.roll(y, 1)) - np.dot(y, np.roll(x, 1))) < 6: continue          # ほぼ真横を向いた破片は見えない
        A = np.c_[dst, np.ones(len(dst))]
        cx, cy = np.linalg.lstsq(A, src[:, 0], rcond=None)[0], np.linalg.lstsq(A, src[:, 1], rcond=None)[0]   # 今の位置 → 元の位置
        piece = frozen.transform((W, H), Image.AFFINE, (cx[0], cx[1], cx[2], cy[0], cy[1], cy[2]), Image.NEAREST, fillcolor=frozen.getpixel((2, 2)))   # 画面の外へはみ出た破片の部分は、 縁の色で埋める
        m = Image.new("L", (W, H), 0); ImageDraw.Draw(m).polygon([tuple(q) for q in dst], fill=255)
        out.paste(piece, (0, 0), m)
    return out


chg = frames("flow_charge", "dicebound/vesca-charge", {}); idle = frames("flow_idle", "dicebound/vesca-charged", {})
stop = frames("flow_stop", "dicebound/vesca-stop", {}); sha = frames("flow_shatter", "dicebound/vesca-shatter", {})
lost = frames("flow_lost", "dicebound/vesca-lost", {}); gli = frames("flow_glitch", "dicebound/vesca-glitch", {}); off = frames("flow_off", "dicebound/vesca-off", {})
subprocess.run(["node", "lab/shard_dump.mjs", os.path.join(OUT, "shards.json")], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
GEOM = json.load(io.open(os.path.join(OUT, "shards.json"), encoding="utf-8"))["frames"]
while len(sha) > 1 and empty(sha[-1]): sha.pop()
while len(off) > 1 and empty(off[-1]): off.pop()
base = stage()
chg4 = frames("flow_charge4", "dicebound/vesca-charge", {"broken": True}); idle4 = frames("flow_idle4", "dicebound/vesca-charged", {"broken": True})
def head_of(c, d):
    h = [(base.copy(), "ヴェスカのチャージのターン")] * 3
    for i, f in enumerate(c):
        b = dark(base.copy(), DARK * min(1.0, i / 6.0)); b.paste(f, (0, 0), f); h.append((b, "ヴェスカのチャージのターン: チャージ"))
    for f in d:
        b = dark(base.copy(), DARK); b.paste(f, (0, 0), f); h.append((b, "プレイヤーの手番: 待機 (ループ)"))
    return h
head = head_of(chg, idle); head4 = head_of(chg4, idle4)
# 4 段目は、 チャージと待機の間も、 画面そのものがときどき乱れる (弱く・短く)。 チャージ = 後半へ向けて少しずつ増える・待機 = 6 コマに 1 回、 1〜2 コマだけ
TEAR_CHARGE = [0.0] * 6 + [0.08, 0, 0.1, 0, 0, 0.14, 0.1, 0, 0.18, 0, 0.2, 0.25, 0.4, 0.3, 0.12, 0, 0.1]
TEAR_IDLE = [0.22 if i % 6 == 2 else 0.12 if i % 6 == 3 else 0.0 for i in range(len(idle4))]
BANDS_CHARGE = tear_bands(TEAR_CHARGE, seed=11); BANDS_IDLE = tear_bands(TEAR_IDLE, seed=12)
for i, bd in enumerate(BANDS_CHARGE + BANDS_IDLE):
    if bd and 3 + i < len(head4): head4[3 + i] = (torn(head4[3 + i][0], bd), head4[3 + i][1])
cards = []
n1 = int(LEAD * 8)
# 4 段目の乱れの強さ (大技のターンの頭からのコマごと): 溜め 8 コマ・乱れ 7 コマ・信号が切れて戻るまで (黒い間は見えない)・戻った直後の 5 コマ
# 溜め 8 コマ・乱れ 4 コマ・止まって 8 コマ (帯だけずれ続ける)・乱れの残り・信号が切れて戻るまで (黒い間は見えない)・戻った直後
TEAR = [0.1, 0.15, 0.25, 0.35, 0.45, 0.55, 0.65, 0.75] + [1.0] * HOLD_AT + ["hold"] * (HOLD_N + 1) + [1.0] * (int(GLITCH * 8) - HOLD_AT - 1) + [0.0] * 16 + [0.7, 0.6, 0.5, 0.4, 0.3, 0.22, 0.14, 0.08, 0.04]
BANDS = tear_bands(TEAR)
# 帯の表: charge = チャージの 23 コマ・idle = 待機の 24 コマ (ループ)・frames = 大技のターンの頭から
io.open(os.path.join(OUT, "tear.json"), "w", encoding="utf-8").write(json.dumps({"fps": 8, "w": W, "h": H, "charge": BANDS_CHARGE, "idle": BANDS_IDLE, "frames": BANDS}))
for key, title, lead, parts in (
        ("p123", "1〜3 段目: 時が止まり、 凍った画面が砕ける", stop, [(sha, "ヴェスカの大技 (1〜3 段目): 画面が凍る → ひび → 砕ける", 4)]),
        ("p4", "4 段目 (天与) だけ: 時が壊れ、 画面が乱れて、 信号が切れる", lost, [(gli[:HOLD_AT], "ヴェスカの大技 (4 段目): 画面が乱れる", -1), ([gli[HOLD_AT]] * (HOLD_N + 1), "ヴェスカの大技 (4 段目): 乱れたまま止まる (ゆっくりずれていく)", -1), (gli[HOLD_AT + 1:int(GLITCH * 8)], "ヴェスカの大技 (4 段目): 画面が乱れる", -1), (off, "ヴェスカの大技 (4 段目): 信号が切れる → 戻る", -1)])):
    seq = list(head4 if key == "p4" else head); h0 = len(seq)
    for i in range(n1):
        b = dark(base.copy(), DARK); f = lead[min(i, len(lead) - 1)]; b.paste(f, (0, 0), f)
        seq.append((b, "ヴェスカの大技のターン: " + ("針が遅くなって止まる" if key == "p123" else "針が暴れ、 文字盤がずれる")))
    for fr, cap, sh0 in parts:
        frozen = frozen_tint(seq[-1][0]) if key == "p123" else None      # 止まった瞬間の画面の写し
        for j, f in enumerate(fr):
            if frozen is None: b = base.copy()
            else: b = frozen.copy() if GEOM[j] is None else shattered(frozen, base, GEOM[j])   # 割れる前は 1 枚・割れたら破片
            b.paste(f, (0, 0), f); seq.append((shaken(b, j - sh0) if sh0 >= 0 else b, cap))
    if key == "p4":                                         # 画面そのものを乱す (効果を重ね終えた後の画面に掛ける)
        seq += [(base.copy(), "ヴェスカの大技 (4 段目): 戻った後も、 しばらく乱れが残る")] * 4
        for i in range(h0, len(seq)):
            if i - h0 < len(BANDS) and BANDS[i - h0]: seq[i] = (torn(seq[i][0], BANDS[i - h0]), seq[i][1])
    seq += [(base.copy(), "")] * 4
    big = [framed(a, c) for a, c in seq]
    big[0].save(os.path.join(OUT, key + ".gif"), save_all=True, append_images=big[1:], duration=125, loop=0)
    m = len(seq) - h0 - (8 if key == "p4" else 4); picks = [h0 + int(m * q / 12) for q in range(12)]
    o = Image.new("RGB", (W * 4, H * 3), (0, 0, 0))
    for q, i in enumerate(picks): o.paste(seq[i][0], ((q % 4) * W, (q // 4) * H))
    o.save(os.path.join(OUT, key + "_contact.png"))
    cards.append('<h2>%s</h2><img src="%s.gif"><p>大技のターンのコマの抜き出し</p><div class="s"><img loading="lazy" src="%s_contact.png"></div>' % (title, key, key))
o = Image.new("RGB", (W * 4, H * 2), (0, 0, 0))
for r, hd in enumerate((head, head4)):
    for q, i in enumerate([3 + 10, 3 + 14, 3 + len(chg) + 1, 3 + len(chg) + 13]): o.paste(hd[i][0], (q * W, r * H))
o.save(os.path.join(OUT, "charge_contact.png"))
io.open(os.path.join(OUT, "flow.html"), "w", encoding="utf-8").write('''<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>ヴェスカ 一連の流れ</title>
<style>body{margin:0;background:#100e14;color:#e4def0;font:14px/1.6 "Yu Gothic UI",sans-serif}main{max-width:1960px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0}h2{font-size:17px;margin:30px 0 6px}p{color:#a39ab4;margin:6px 0 0}img{image-rendering:pixelated;display:block;max-width:100%}.s{overflow-x:auto;margin-top:6px}.s img{max-width:none}</style></head><body><main>
<h1>ヴェスカの大技: 一連の流れ (試作・2 本)</h1>
<p>チャージと待機 (虚の玉の上に時計の文字盤・長針は逆回り、 短針は順回り) は共通。 大技は、 1〜3 段目で 1 つ、 4 段目だけの物が 1 つ。 1 秒 8 コマ・2 倍・背景と敵と下の枠は仮。</p>
<h2>チャージ完了の前後と、 待機 (上 = 1〜3 段目・下 = 4 段目の壊れかけ)</h2><div class="s"><img src="charge_contact.png"></div>''' + "".join(cards) + "</main></body></html>")
print(len(chg), len(idle), len(stop), len(sha), len(lost), len(gli), len(off))
