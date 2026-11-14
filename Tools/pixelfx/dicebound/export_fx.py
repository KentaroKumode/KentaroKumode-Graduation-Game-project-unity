"""ボスの大技・通常の技・武器の攻撃の絵 (2026-11-14 に作者が決めた物) を、 見本と同じ道具で書き出して Assets/Resources/Game2DFx/ へ置く。

  python Tools/pixelfx/dicebound/export_fx.py            # 全部
  python Tools/pixelfx/dicebound/export_fx.py gouka      # 名前の頭で絞る

絵を作るのは lab/lcd_frames.mjs (見本の *_preview.py が呼んでいる物そのもの・同じ引数)。 ここは出力を詰め直すだけで、 画素は 1 つも変えない:
  ・コマごとに、 中身のある範囲 (不透明な画素の外接矩形) だけを切り出して 1 枚の絵に並べ、 切り出した位置を json に持つ
  ・1 回きりの効果は、 後ろの空のコマを捨てる (ループは捨てない・コマの数が周期なので)
  ・書き出した後に、 並べた絵からコマを組み立て直して lcd_frames.mjs の出力と画素で突き合わせる (1 つでも違えば止まる)
出力: <名前>.png.bytes (中身は PNG) と <名前>.json ({ frames: [{ frame, spriteSourceSize }], meta: { pixelfx: { id, fps, frames, frameSize, pivot, loop } } })。
読む側は Assets/Scripts/UI/Game2D/Battle/FxSheet.cs。 ヴェスカの破片の形 (lab/shard_dump.mjs) と帯の表 (library/dicebound/vesca-tear.json) も同じ所へ写す。
"""
import io, json, os, shutil, subprocess, sys, tempfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PFX = os.path.dirname(HERE)
PROJ = os.path.abspath(os.path.join(PFX, "..", ".."))
DEST = os.path.join(PROJ, "Assets", "Resources", "Game2DFx")

# 烈炎の数 (ash) は戦闘で変わるので、 数ごとに焼く。 ASH_MAX を超えた分は ASH_MAX の絵を使う (ゲーム側で頭打ち)
ASH_MAX = 8
GOUKA_O = dict(lead=1.4, rush=1.2, hold=1.0, cool=1.6)          # gouka_flow_preview.py の O
VESCA_PHASES = [0, 0.75, 1.5, 2.25]                             # 待機 (24 コマ・3 秒) の 0 / 6 / 12 / 18 コマ目

# (ゲームでの名前, 効果の id, 色の組, fps, 設定, ループか)
PLAN = []
def add(name, fid, ink, opts=None, fps=8, loop=False): PLAN.append((name, fid, ink, fps, opts or {}, loop))

# ── #350 灰燼の王〈業火〉 (gouka_flow_preview.py) ──
for a in range(ASH_MAX + 1):
    add("gouka-charge-a%d" % a, "dicebound/gouka-charge", "ash", {"ash": a})
    add("gouka-screen-a%d" % a, "dicebound/gouka-screen", "ash", dict(ash=a, depth=0.85, bias=0.2, mode="out", **GOUKA_O))
    add("gouka-impact-a%d" % a, "dicebound/gouka-impact", "ash", {"ash": a, "ring": "fire-ring", "flame": 1}, fps=24)
add("gouka-charged", "dicebound/gouka-charged", "ash", {}, loop=True)
add("gouka-release", "dicebound/gouka-release", "ash", {"lead": 1.4})
# ── #351 審判官 (judge_flow_preview.py・judge_phase_preview.py) ──
add("judge-charge", "dicebound/judge-charge", "holy", {"scale": 1.4})
add("judge-charged", "dicebound/judge-charged", "holy", {"scale": 1.4}, loop=True)
for k in range(4): add("judge-release-p%d" % k, "dicebound/judge-release", "holy", {"scale": 1.4, "lead": 1.4, "phase": k})
add("judge-verdict", "dicebound/judge-verdict", "holy", {})
# ── #352 シュヴァリエ (chevalier_flow_preview.py) ──
add("chevalier-charge", "dicebound/chevalier-charge", "steel", {})
add("chevalier-charged", "dicebound/chevalier-charged", "steel", {}, loop=True)
add("chevalier-slash", "dicebound/chevalier-slash", "steel", {"lead": 0.75})
# ── #353 ヴェスカ (vesca_flow_preview.py・issue のコメント) ──
add("vesca-charge", "dicebound/vesca-charge", "null", {})
add("vesca-charge-broken", "dicebound/vesca-charge", "null", {"broken": True})
add("vesca-charged", "dicebound/vesca-charged", "null", {}, loop=True)
add("vesca-charged-broken", "dicebound/vesca-charged", "null", {"broken": True}, loop=True)
for k, p in enumerate(VESCA_PHASES):
    add("vesca-stop-p%d" % k, "dicebound/vesca-stop", "null", {"phase": p})
    add("vesca-lost-p%d" % k, "dicebound/vesca-lost", "null", {"phase": p})
add("vesca-shatter", "dicebound/vesca-shatter", "null", {})
add("vesca-glitch", "dicebound/vesca-glitch", "null", {})
add("vesca-off", "dicebound/vesca-off", "null", {})
# ── #354 ボスの通常の技 (moves_preview.py) ──
add("move-relic-strike", "dicebound/vesca-slash", "null", {})
add("move-relic-guard", "dicebound/moves", "null", {"move": "relic-guard"})
add("move-relic-hex", "dicebound/vesca-hex", "null", {})
add("move-tenyo-sword", "dicebound/tenyo-sword", "holy", {})
add("move-tenyo-shield", "dicebound/moves", "holy", {"move": "tenyo-shield"})
add("move-tenyo-ring", "dicebound/moves", "holy", {"move": "tenyo-ring"})
add("move-king-attack", "dicebound/moves", "ash", {"move": "king-attack"})
add("move-king-regen", "dicebound/moves", "ash", {"move": "king-regen"})
add("move-judge-flame", "dicebound/moves", "holy", {"move": "judge-flame"})
add("move-chev-thrust", "dicebound/moves", "steel", {"move": "chev-thrust"})
add("move-chev-stance", "dicebound/moves", "steel", {"move": "chev-stance"})
# ── #355 プレイヤーの武器 (weapons_preview.py: 通常 = steel・会心 = holy) ──
for fam in ("sword", "axe", "dagger", "shield"):
    add("weapon-%s-normal" % fam, "dicebound/weapons", "steel", {"weapon": fam + "-normal"})
    add("weapon-%s-crit" % fam, "dicebound/weapons", "holy", {"weapon": fam + "-crit"})

PACK_W = 2048


def render(fid, ink, fps, opts, tmp):
    """見本と同じ呼び方で lab/lcd_frames.mjs を走らせ、 コマ (RGBA) の並びと json を返す。"""
    png = os.path.join(tmp, "strip.png")
    subprocess.run(["node", "lab/lcd_frames.mjs", fid, png, "--ink", ink, "--fps", str(fps), "--opts", json.dumps(opts)], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
    m = json.load(io.open(png[:-4] + ".json", encoding="utf-8")); sh = Image.open(png).convert("RGBA")
    return [sh.crop((i * m["w"], 0, (i + 1) * m["w"], m["h"])) for i in range(m["n"])], m


def pack(frames):
    """コマを中身のある範囲で切り出し、 左から右・上から下へ並べる。 戻り値 = (絵, [(frame, src)])。"""
    cuts, x, y, rowh, used_w = [], 0, 0, 0, 1
    for f in frames:
        bb = f.getchannel("A").getbbox()
        if bb is None: cuts.append((None, None, None)); continue
        w, h = bb[2] - bb[0], bb[3] - bb[1]
        if x + w > PACK_W: x, y, rowh = 0, y + rowh, 0
        cuts.append((f.crop(bb), (x, y, w, h), (bb[0], bb[1], w, h)))
        x += w; rowh = max(rowh, h); used_w = max(used_w, x)
    sheet = Image.new("RGBA", (used_w, max(1, y + rowh)), (0, 0, 0, 0))
    out = []
    for img, fr, src in cuts:
        if img is not None: sheet.paste(img, (fr[0], fr[1]))
        out.append((fr, src))
    return sheet, out


def rebuild(sheet, rects, w, h):
    for fr, src in rects:
        g = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        if fr is not None: g.paste(sheet.crop((fr[0], fr[1], fr[0] + fr[2], fr[1] + fr[3])), (src[0], src[1]))
        yield g


def main(argv):
    os.makedirs(DEST, exist_ok=True)
    only = argv[1:] if len(argv) > 1 else None
    total = 0
    with tempfile.TemporaryDirectory() as tmp:
        for name, fid, ink, fps, opts, loop in PLAN:
            if only and not any(name.startswith(o) for o in only): continue
            frames, m = render(fid, ink, fps, opts, tmp)
            if not loop:
                while len(frames) > 1 and frames[-1].getchannel("A").getbbox() is None: frames.pop()
            sheet, rects = pack(frames)
            # 突き合わせ: 並べ直した絵から組み立てたコマ = lcd_frames.mjs のコマ (画素で 1 つも違わない)
            for i, (a, b) in enumerate(zip(rebuild(sheet, rects, m["w"], m["h"]), frames)):
                if a.tobytes() != b.tobytes(): raise SystemExit("%s: コマ %d が組み立て直しで一致しない" % (name, i))
            js = {"frames": [{"frame": {"x": fr[0], "y": fr[1], "w": fr[2], "h": fr[3]} if fr else {"x": 0, "y": 0, "w": 0, "h": 0},
                              "spriteSourceSize": {"x": src[0], "y": src[1], "w": src[2], "h": src[3]} if src else {"x": 0, "y": 0, "w": 0, "h": 0}} for fr, src in rects],
                  "meta": {"app": "pixelfx lab/lcd_frames.mjs + export_fx.py", "pixelfx": {"id": fid, "ink": ink, "opts": json.dumps(opts), "fps": fps, "frames": len(frames),
                                                                                       "frameSize": [m["w"], m["h"]], "pivot": m["pivot"], "loop": loop}}}
            bio = io.BytesIO(); sheet.save(bio, "PNG", optimize=True); data = bio.getvalue()
            io.open(os.path.join(DEST, name + ".png.bytes"), "wb").write(data)
            io.open(os.path.join(DEST, name + ".json"), "w", encoding="utf-8").write(json.dumps(js, separators=(",", ":"), ensure_ascii=False))
            total += len(data)
            print("%-26s %3d コマ (%d) %4dx%-4d %7.1f KB" % (name, len(frames), m["n"], sheet.width, sheet.height, len(data) / 1024.0))
        if not only or any("vesca".startswith(o) or o.startswith("vesca") for o in only):
            shards = os.path.join(tmp, "shards.json")
            subprocess.run(["node", "lab/shard_dump.mjs", shards], cwd=PFX, check=True, stdout=subprocess.DEVNULL)
            shutil.copyfile(shards, os.path.join(DEST, "vesca-shards.json"))
            shutil.copyfile(os.path.join(PFX, "library", "dicebound", "vesca-tear.json"), os.path.join(DEST, "vesca-tear.json"))
            print("vesca-shards.json / vesca-tear.json")
    print("合計 %.1f MB" % (total / 1048576.0))


if __name__ == "__main__":
    sys.exit(main(sys.argv))
