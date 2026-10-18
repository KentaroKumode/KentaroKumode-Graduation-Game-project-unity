"""GUI の見た目を、 PNG で保存する (進捗報告のビフォー/アフター用)。

  python Tools/snap_gui.py docs/progress/gui_snapshot_2026-10-15

保存するもの:
  gui_layer_*.png     試作 (map-prototype.html) の「外枠 GUI の層」 480×270・透明。 コンポーネントの PNG を、 実際の位置・状態で重ねた絵そのもの (画素は表示と同一)
  display_*.png       液晶の合成 (影・格子・湾曲) のあと、 画面に出ている見た目 (960×540)
  counter_*.png       アナログカウンター (gui-counter.html) の窓だけ (実寸と 10 倍)
  gui_before_sheet.png  上の gui_layer_* を横に並べた結合画像 (地の色は見やすさ用)
  state.json          撮った時の状態 (物資・HP・GOLD・倍率・ git のコミット)

使うもの: Python の websocket-client と Pillow、 Chrome。 Unity は要らない。
Chrome は自分で起動した 1 つだけを止める (ほかのウィンドウには触らない)。
"""
import base64, http.server, json, os, socketserver, subprocess, sys, threading, time, urllib.request

try:
    import websocket
except ImportError:
    sys.exit("websocket-client が必要です: pip install websocket-client")
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESIGN = os.path.join(ROOT, "docs", "design")
CHROME = os.environ.get("CHROME", r"C:\Program Files\Google\Chrome\Application\chrome.exe")
PORT_DEBUG = 9337
GROUND = (24, 28, 36)          # 結合画像の地の色 (透明の部分を見やすくするだけ・ GUI には含まれない)


def serve():
    class H(http.server.SimpleHTTPRequestHandler):
        def __init__(self, *a, **k): super().__init__(*a, directory=DESIGN, **k)
        def log_message(self, *a): pass
    srv = socketserver.TCPServer(("127.0.0.1", 0), H)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv


def main(out):
    os.makedirs(out, exist_ok=True)
    srv = serve(); base = "http://127.0.0.1:%d/" % srv.server_address[1]
    prof = os.path.join(os.environ.get("TEMP", "."), "snap_gui_profile")
    p = subprocess.Popen([CHROME, "--headless=new", "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
                          "--remote-debugging-port=%d" % PORT_DEBUG, "--remote-allow-origins=*", "--user-data-dir=" + prof,
                          "--window-size=1100,700", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(60):
            try: tabs = json.load(urllib.request.urlopen("http://127.0.0.1:%d/json" % PORT_DEBUG)); break
            except Exception: time.sleep(0.2)
        ws = websocket.create_connection([t for t in tabs if t["type"] == "page"][0]["webSocketDebuggerUrl"], timeout=60)
        n = [0]
        def call(m, params=None):
            n[0] += 1; ws.send(json.dumps({"id": n[0], "method": m, "params": params or {}}))
            while True:
                r = json.loads(ws.recv())
                if r.get("id") == n[0]: return r
        def ev(js): return call("Runtime.evaluate", {"expression": js, "returnByValue": True, "awaitPromise": True}).get("result", {}).get("result", {}).get("value")
        def save(name, url): open(os.path.join(out, name), "wb").write(base64.b64decode(url.split(",")[1]))
        def shot(name, sel="cv"):
            r = ev("(()=>{const r=%s.getBoundingClientRect();return [r.x,r.y,r.width,r.height]})()" % sel)
            d = call("Page.captureScreenshot", {"format": "png", "clip": {"x": r[0], "y": r[1], "width": r[2], "height": r[3], "scale": 1}})
            open(os.path.join(out, name), "wb").write(base64.b64decode(d["result"]["data"]))
        call("Runtime.enable"); call("Page.enable")

        # ---- 試作の GUI ----
        call("Page.navigate", {"url": base + "map-prototype.html"}); time.sleep(4)
        info = ev("[LW,LH,LK,!!PANEL]"); assert info[0] == 480 and info[3], "液晶モードの 480×270 で開けていません: %s" % info
        ev("S.fight=false; if (!GOLDPOT.isClosed()) GOLDPOT.toggle(); draw();"); time.sleep(3.5)       # 物資のバーは出る・ GOLD 入れは閉じる
        save("gui_layer_nav_pot_closed.png", ev('offG.toDataURL("image/png")')); shot("display_nav_pot_closed.png")
        ev("GOLDPOT.toggle(); draw();"); time.sleep(3.5)                                               # GOLD 入れを開く
        save("gui_layer_nav_pot_open.png", ev('offG.toDataURL("image/png")')); shot("display_nav_pot_open.png")
        ev("GOLDPOT.toggle(); draw();"); time.sleep(2.0)
        ev("S.fight=true; draw();"); time.sleep(3.0)                                                   # 戦闘 (物資のバーは引っ込む)
        save("gui_layer_battle.png", ev('offG.toDataURL("image/png")')); shot("display_battle.png")
        state = json.loads(ev('JSON.stringify({sup:S.sup,supMax:P.supMax,hp:S.hp,hpMax:S.hpMax,gold:S.gold,zoom:ZOOM,lcdScale:LK,seed:document.getElementById("seed").value,timeMode:S.timeMode})'))

        # ---- アナログカウンター (窓だけ) ----
        call("Page.navigate", {"url": base + "gui-counter.html"}); time.sleep(2.5)
        save("counter_native.png", ev('cv.toDataURL("image/png")'))
        state["counter"] = ev("[cv.width, cv.height, Math.round(v)]")
    finally:
        p.kill(); srv.shutdown()

    # 結合画像 (実寸 1 倍・地の色は見やすさ用) と、 カウンターの 10 倍
    names = ["nav_pot_closed", "nav_pot_open", "battle"]
    sheet = Image.new("RGB", (480 * 3 + 8 * 2, 270), (60, 64, 74))
    for i, nm in enumerate(names):
        g = Image.open(os.path.join(out, "gui_layer_%s.png" % nm)).convert("RGBA")
        bg = Image.new("RGBA", g.size, GROUND + (255,)); bg.alpha_composite(g); sheet.paste(bg.convert("RGB"), (i * 488, 0))
    sheet.save(os.path.join(out, "gui_before_sheet.png"))
    c = Image.open(os.path.join(out, "counter_native.png")).convert("RGBA")
    c.resize((c.width * 10, c.height * 10), Image.NEAREST).save(os.path.join(out, "counter_x10.png"))
    try: state["git_head"] = subprocess.check_output(["git", "-C", ROOT, "rev-parse", "--short", "HEAD"], text=True).strip()
    except Exception: pass
    json.dump(state, open(os.path.join(out, "state.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("保存:", out); print(state)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "docs", "progress", "gui_snapshot"))
