"""戦闘演出のたたき台用: エネミー画像と背景を PixelLab で生成して保存する。

  python Tools/pixellab_battle.py enemies  [--only slime,goblin]   # docs/design/battle-assets/enemy_*.png (128x128・背景なし)
  python Tools/pixellab_battle.py backdrop                         # docs/design/battle-assets/backdrop.png (480x270)

トークンは環境変数 PIXELLAB_SECRET (pixellab_ui.py と同じ)。 チャットやファイルに書かない。
呼ぶ前と後の残り生成枠を out/log.jsonl に記録する。
"""
import argparse, base64, io, json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixellab_ui as px   # call / balance / token を使い回す

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "design", "battle-assets")

STYLE = "dark fantasy dungeon, limited muted palette, clean pixel art, strong silhouette, 2D game enemy sprite"
ENEMIES = {
    "slime":    "a gooey green slime monster with a glossy highlight and a menacing grin",
    "goblin":   "a snarling goblin warrior holding a rusty cleaver, ragged leather armor",
    "skeleton": "a skeleton soldier with a cracked round shield and a chipped short sword",
    "wolf":     "a huge scarred dire wolf, bristling grey fur, glowing yellow eyes, fangs bared",
    "orc":      "a brutish orc brute with a spiked club and iron shoulder plates",
}
BACKDROP = ("a dark stone dungeon hall seen from the front for a side-view turn-based battle, empty floor in the lower third, "
            "arched stone wall with torches casting warm orange light, deep blue-black shadows, no characters, no text, "
            "wide flat floor for two fighters to stand on, atmospheric")


def wait(job_id):
    for _ in range(100):
        time.sleep(6)
        st = px.call("GET", "/background-jobs/%s" % job_id)
        if st.get("status") in ("completed", "failed", "error"): return st
    sys.exit("時間切れ: %s" % job_id)


def images_of(st):
    res = st.get("last_response") or {}
    imgs = res.get("images") or ([res.get("image")] if res.get("image") else [])
    out = []
    for im in imgs:
        data = im.get("base64") if isinstance(im, dict) else im
        if not data: continue
        if "," in data[:40]: data = data.split(",", 1)[1]
        out.append(base64.b64decode(data))
    return out


def log(rec):
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "log.jsonl"), "a", encoding="utf-8") as f: f.write(json.dumps(rec, ensure_ascii=False) + "\n")


def gen_enemy(name, desc, seed):
    body = {"description": "%s, %s, facing left, full body" % (desc, STYLE), "image_size": {"width": 128, "height": 128},
            "no_background": True, "view": "side", "direction": "west", "outline": "single color black outline",
            "shading": "medium shading", "detail": "medium detail", "seed": seed}
    before = px.balance()
    job = px.call("POST", "/create-image-pixflux-background", body)
    jid = job.get("background_job_id") or job.get("id")
    st = wait(jid)
    if st.get("status") != "completed": sys.exit("失敗 %s: %s" % (name, json.dumps(st)[:600]))
    imgs = images_of(st)
    if not imgs: sys.exit("画像が返らなかった %s: %s" % (name, json.dumps(st)[:600]))
    path = os.path.join(OUT, "enemy_%s.png" % name); open(path, "wb").write(imgs[0])
    after = px.balance(); log({"kind": "enemy", "name": name, "seed": seed, "saved": path, "before": before, "after": after})
    print(name, "->", os.path.relpath(path), after, flush=True)


def gen_backdrop(seed):
    body = {"description": "%s, %s" % (BACKDROP, STYLE), "image_size": {"width": 480, "height": 270}, "no_background": False, "seed": seed}
    before = px.balance()
    job = px.call("POST", "/generate-image-v2", body)
    jid = job.get("background_job_id") or job.get("id")
    st = wait(jid)
    if st.get("status") != "completed": sys.exit("失敗 backdrop: %s" % json.dumps(st)[:600])
    imgs = images_of(st)
    if not imgs: sys.exit("画像が返らなかった backdrop: %s" % json.dumps(st)[:600])
    path = os.path.join(OUT, "backdrop.png"); open(path, "wb").write(imgs[0])
    after = px.balance(); log({"kind": "backdrop", "seed": seed, "saved": path, "before": before, "after": after})
    print("backdrop ->", os.path.relpath(path), after, flush=True)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("what", choices=["enemies", "backdrop"]); ap.add_argument("--only", default=""); ap.add_argument("--seed", type=int, default=20261020)
    a = ap.parse_args(); os.makedirs(OUT, exist_ok=True)
    if a.what == "backdrop": gen_backdrop(a.seed); return
    only = [x for x in a.only.split(",") if x] or list(ENEMIES)
    for i, n in enumerate(only): gen_enemy(n, ENEMIES[n], a.seed + i)


if __name__ == "__main__":
    main()
