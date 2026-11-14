# 分解の結果 (layers.mjs の出力) を見る頁を作る。
#   python lab/page.py <出力先 (layers.mjs の --out と同じ)>
# 効果ごとの頁 <id>/index.html と、 一覧 index.html。 絵が主役。 資料 (library/<id>.md) があれば、 札ごとの説明を添える。
import html, io, json, os, re, sys
OUT = sys.argv[1]
HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.join(HERE, "..", "library")
E = html.escape
CSS = '''
:root{--bg:#14110e;--panel:#1d1915;--ink:#e9e1d2;--sub:#a59c8b;--line:#38322a;--acc:#e0a04a;--px:#e0782c;--soft:#8f8a96;--lt:#ffd98a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.6 "Yu Gothic UI","Hiragino Sans","Noto Sans JP",sans-serif}
main{max-width:1500px;margin:0 auto;padding:16px 16px 80px}a{color:var(--acc)}h1{font-size:20px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 8px;border-top:1px solid var(--line);padding-top:16px}
h3{font-size:15px;margin:18px 0 4px}.sub{color:var(--sub)}code{color:var(--acc);font-size:12.5px}
.strip{overflow-x:auto;background:#0b0908;border:1px solid var(--line);border-radius:6px;padding:6px;margin:6px 0}
.strip img{display:block;image-rendering:pixelated;max-width:none}
.tl{display:grid;grid-template-columns:200px 1fr;gap:2px 10px;align-items:center;margin:8px 0;font-size:12.5px}
.tl .bar{position:relative;height:14px;background:#0b0908;border-radius:3px}
.tl .bar i{position:absolute;top:0;bottom:0;border-radius:3px;opacity:.9}.tl .bar b{position:absolute;top:-2px;bottom:-2px;width:2px;background:#fff}
.k-shape i{background:var(--px)}.k-soft i{background:var(--soft)}.k-light i{background:var(--lt)}.k-shape\\+light i{background:linear-gradient(var(--px),var(--lt))}
.axis{display:flex;justify-content:space-between;color:var(--sub);font-size:11px;margin-left:210px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:10px 12px;margin:10px 0}
.card p{margin:4px 0}.meta{color:var(--sub);font-size:12.5px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}
.grid a{display:block;background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:10px;text-decoration:none;color:var(--ink)}
.grid a:hover{border-color:var(--acc)}.grid img{max-width:100%;image-rendering:pixelated;display:block;margin-bottom:6px}
.legend span{display:inline-block;margin-right:14px}.legend i{display:inline-block;width:12px;height:12px;border-radius:3px;vertical-align:-2px;margin-right:4px}
'''


def notes(eid):
    """library/<id>.md から、 札ごとの説明 (### `札` の節) と、 冒頭の要約を拾う。"""
    p = os.path.join(LIB, eid + ".md")
    if not os.path.exists(p): return "", {}
    s = io.open(p, encoding="utf-8").read()
    head = s.split("\n## ")[0]
    head = "\n".join(l for l in head.split("\n") if l.strip() and not l.startswith("#"))
    comp = {}
    for m in re.finditer(r"^### `([^`]*)`[^\n]*\n(.*?)(?=^### |^## |\Z)", s, re.S | re.M):
        comp[m.group(1)] = m.group(2).strip()
    return head, comp


def md(s):
    s = E(s)
    s = re.sub(r"`([^`]+)`", r"<code>\1</code>", s)
    s = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", s)
    return "".join("<p>%s</p>" % l[2:] if l.startswith("- ") else "<p>%s</p>" % l for l in s.split("\n") if l.strip())


def page(eid, m):
    d = os.path.join(OUT, eid)
    dur = max(m["duration"], 0.001)
    head, comp = notes(eid)
    tl = []
    for c in m["components"]:
        l, r, pk = 100 * c["first"] / dur, 100 * (c["last"] + 1 / m["fps"]) / dur, 100 * c["peak"] / dur
        tl.append('<div><a href="#c-%s"><code>%s</code></a></div><div class="bar k-%s"><i style="left:%.2f%%;width:%.2f%%"></i><b style="left:%.2f%%"></b></div>'
                  % (E(c["file"]), E(c["tag"] or "(札なし)"), E(c["kind"]), l, max(0.6, r - l), pk))
    cards = []
    for c in m["components"]:
        n = comp.get(c["tag"], "")
        cards.append('<div class="card" id="c-%s"><h3><code>%s</code></h3><p class="meta">%s・%.2f〜%.2f 秒 (山場 %.2f 秒・%d コマ)・不透明 %d / 半透明 %d / 光 %d 画素</p>%s<div class="strip"><img loading="lazy" src="%s"></div></div>'
                     % (E(c["file"]), E(c["tag"] or "(札なし)"), E({"shape": "形", "soft": "半透明 (煙)", "light": "光だけ", "shape+light": "形 + 光"}.get(c["kind"], c["kind"])),
                        c["first"], c["last"], c["peak"], c["frames"], c["opaquePx"], c["softPx"], c["lightPx"], md(n) if n else "", E(c["file"])))
    stacks = sorted(f for f in os.listdir(d) if f.startswith("stack_"))
    st = "".join('<h3>＋ <code>%s</code></h3><div class="strip"><img loading="lazy" src="%s"></div>' % (E(re.sub(r"^stack_\d+_|\.png$", "", f)), E(f)) for f in stacks)
    rc = m["recompose"]
    body = '''<main><p><a href="../../index.html">← 一覧</a></p><h1>%s <span class="sub">%s</span></h1>
<p class="sub">%s</p><p class="meta">%d×%d・%.2f 秒・%s・書き出し %d コマ/秒 (%d コマ)・札 %d 個%s</p>%s
<h2>元のまま</h2><div class="strip"><img src="full.png"></div>
<h2>年表 (どの部品が、いつ出ているか)</h2><p class="legend sub"><span><i style="background:var(--px)"></i>形</span><span><i style="background:var(--soft)"></i>半透明</span><span><i style="background:var(--lt)"></i>光</span><span>白い線 = 山場</span></p>
<div class="tl">%s</div><div class="axis"><span>0 秒</span><span>%.2f 秒</span></div>
<h2>部品を 1 つずつ (その部品だけを描いた物)</h2>%s
<h2>部品を別々に描いて、後から重ねると同じになるか</h2>
<p>違う画素 %s%% (光っている画素 %d のうち %d)。白い所が、元と違う画素。違いが出るのは、半透明の煙が下の色に混ざる所と、部品どうしが交互に描かれて上下が入れ替わる所。</p>
<h3>重ね直した物</h3><div class="strip"><img loading="lazy" src="recomposed.png"></div><h3>元との違い</h3><div class="strip"><img loading="lazy" src="diff.png"></div>
<h2>部品を、出てきた順に足していく</h2>%s</main>''' % (
        E(m["name"]), E(eid), E(m["desc"]), m["size"][0], m["size"][1], m["duration"], "ループ" if m["loop"] else "1 回", m["fps"], m["frames"], len(m["components"]),
        "" if m.get("annotated") else "・<b>手で札を付けていない (粒の種類だけで自動に分けた)</b>", '<div class="card">%s</div>' % md(head) if head else "",
        "".join(tl), dur, "".join(cards), rc["differingPct"], rc["litPx"], rc["differingPx"], st)
    io.open(os.path.join(d, "index.html"), "w", encoding="utf-8").write('<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>%s の分解</title><style>%s</style></head><body>%s</body></html>' % (E(eid), CSS, body))


items = []
for cat in sorted(os.listdir(OUT)):
    cd = os.path.join(OUT, cat)
    if not os.path.isdir(cd): continue
    for name in sorted(os.listdir(cd)):
        mp = os.path.join(cd, name, "manifest.json")
        if not os.path.exists(mp): continue
        m = json.load(io.open(mp, encoding="utf-8"))
        page(cat + "/" + name, m)
        items.append((cat + "/" + name, m))
cards = "".join('<a href="%s/index.html"><b>%s</b> <span class="sub">%s</span><br><span class="meta">札 %d 個%s・重ね直しの違い %s%%</span></a>'
                % (E(i), E(m["name"]), E(i), len(m["components"]), "" if m.get("annotated") else " (自動)", m["recompose"]["differingPct"]) for i, m in items)
io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write('<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>効果の分解</title><style>%s</style></head><body><main><h1>効果の分解</h1><p class="sub">効果を部品 (札) に分けて、1 つずつ書き出した物。「(自動)」は、粒の種類だけで分けた物で、手で札を付けていない。</p>%s<div class="grid">%s</div></main></body></html>' % (CSS, ''.join('<p><a href="_vary/%s/index.html">部品の加工の見本: %s</a></p>' % (d, d) for d in sorted(os.listdir(os.path.join(OUT, '_vary')))) if os.path.isdir(os.path.join(OUT, '_vary')) else '', cards))
print("pages", len(items))
