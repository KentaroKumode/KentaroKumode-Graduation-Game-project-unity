"""PixelLab の UI 生成 (generate-ui-v2) を呼んで、 PNG を保存する。

  python Tools/pixellab_ui.py "説明文" out_dir --size 320x128 [--concept 参照.png] [--palette-from 色を取る.png --max-colors 24] [--seed N] [--n 1]

トークンは環境変数 PIXELLAB_SECRET (Windows ではユーザー環境変数のレジストリからも読む)。 チャットやファイルに書かない。
1 回ごとに、 呼ぶ前と後の残り生成枠を記録する (out_dir/log.jsonl)。 枠の消費が見えるようにするため。
"""
import argparse, base64, io, json, os, sys, time, urllib.request
from PIL import Image

API = "https://api.pixellab.ai/v2"


def token():
    t = os.environ.get("PIXELLAB_SECRET")
    if not t and sys.platform == "win32":
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k: t = winreg.QueryValueEx(k, "PIXELLAB_SECRET")[0]
        except OSError: pass
    if not t: sys.exit("PIXELLAB_SECRET がありません")
    return t


def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method, headers={"Authorization": "Bearer " + token(), "Content-Type": "application/json"},
                                 data=json.dumps(body).encode() if body is not None else None)
    try:
        with urllib.request.urlopen(req, timeout=120) as r: return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        sys.exit("HTTP %d %s" % (e.code, e.read().decode()[:600]))


def b64png(path):
    im = Image.open(path).convert("RGBA"); buf = io.BytesIO(); im.save(buf, "PNG")
    return base64.b64encode(buf.getvalue()).decode(), im.size


def palette_of(path, n):
    im = Image.open(path).convert("RGBA"); cols = {}
    for p in im.getdata():
        if p[3] > 0: cols[p[:3]] = cols.get(p[:3], 0) + 1
    top = sorted(cols.items(), key=lambda kv: -kv[1])[:n]
    return ["#%02x%02x%02x" % c for c, _ in top]


def balance():
    b = call("GET", "/balance"); s = b.get("subscription") or {}
    return {"generations": s.get("generations"), "usd": (b.get("credits") or {}).get("usd")}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("description"); ap.add_argument("out")
    ap.add_argument("--size", default="256x256"); ap.add_argument("--concept"); ap.add_argument("--palette-from")
    ap.add_argument("--max-colors", type=int, default=0); ap.add_argument("--seed", type=int); ap.add_argument("--color", default=None)
    ap.add_argument("--keep-bg", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    w, h = map(int, a.size.lower().split("x"))
    body = {"description": a.description, "image_size": {"width": w, "height": h}, "no_background": not a.keep_bg}
    if a.seed is not None: body["seed"] = a.seed
    if a.color: body["color_palette"] = a.color
    if a.concept:
        b, (cw, ch) = b64png(a.concept)
        body["concept_image"] = {"image": {"type": "base64", "base64": b, "format": "png"}, "size": {"width": cw, "height": ch}}
    if a.palette_from:
        tp = {"colors": palette_of(a.palette_from, 64)}
        if a.max_colors: tp["max_colors"] = a.max_colors
        body["target_palette"] = tp
    before = balance()
    job = call("POST", "/generate-ui-v2", body)
    jid = job.get("background_job_id") or job.get("id")
    print("job", jid, flush=True)
    for _ in range(90):
        time.sleep(6)
        st = call("GET", "/background-jobs/%s" % jid)
        if st.get("status") in ("completed", "failed", "error"): break
    if st.get("status") != "completed": sys.exit("失敗: %s" % json.dumps(st)[:800])
    res = st.get("last_response") or {}
    imgs = res.get("images") or ([res.get("image")] if res.get("image") else [])
    stamp = time.strftime("%Y%m%d_%H%M%S"); saved = []
    for i, im in enumerate(imgs):
        data = im.get("base64") if isinstance(im, dict) else im
        if not data: continue
        if "," in data[:40]: data = data.split(",", 1)[1]
        p = os.path.join(a.out, "%s_%d.png" % (stamp, i)); open(p, "wb").write(base64.b64decode(data)); saved.append(p)
    after = balance()
    rec = {"t": stamp, "desc": a.description, "size": [w, h], "seed": a.seed, "concept": a.concept, "palette_from": a.palette_from,
           "max_colors": a.max_colors, "saved": saved, "before": before, "after": after}
    open(os.path.join(a.out, "log.jsonl"), "a", encoding="utf-8").write(json.dumps(rec, ensure_ascii=False) + "\n")
    print(json.dumps({"saved": saved, "before": before, "after": after}, ensure_ascii=False))


if __name__ == "__main__":
    main()
