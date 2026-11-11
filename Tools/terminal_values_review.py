# 端子の数値 (TerminalCards.cs) の合否ページ (HTML) を書き出す。 条件・種類・使い捨てを併記し、 変更前 → 変更後を並べる。
#   使い方: python Tools/terminal_values_review.py <変更前のコミット> <出力する .html>
#   ページは合否と直しの指示を Artifact の db (collection "values1") に保存する。
import io, re, sys, json, subprocess

ROOT = "C:/Users/kumod/My project/"
TC = "Assets/Scripts/CombatSystem/TerminalCards.cs"


class S(str):
    def __add__(self, o): return S(str(self) + (o if isinstance(o, str) else str(o)))


def cs_fmt(v, f):
    frac = f.split(".")[1] if "." in f else ""
    s = ("%." + str(len(frac)) + "f") % v
    while "#" in frac and len(s.split(".")[1]) > frac.count("0") and s.endswith("0"): s = s[:-1]
    return s


def parse(tcs):
    m = re.search(r"public static class TerminalIds\s*\{(.*?)\n    \}", tcs, re.S)
    names = dict(re.findall(r"(\w+)\s*=\s*\"([^\"]+)\"", m.group(1)))
    consts = {}
    for mm in re.finditer(r"public const (?:int|float) ([^;]+);", tcs):
        for nm, val in re.findall(r"(\w+)\s*=\s*([0-9.]+)f?", mm.group(1)): consts[nm] = float(val) if "." in val else int(val)
    condlab = dict(re.findall(r"case TerminalCond\.(\w+): return \"([^\"]+)\";", tcs))

    def desc_of(expr):
        e = re.sub(r"(\w+)\.ToString\(\"([^\"]+)\"\)", r'cs_fmt(\1,"\2")', expr)
        e = re.sub(r"\(int\)\(([^)]*)\)", r"int(round(\1))", e)
        e = re.sub(r"\"([^\"]*)\"", lambda m: "S(" + json.dumps(m.group(1), ensure_ascii=False) + ")", e)
        return str(eval(e, {"S": S, "cs_fmt": cs_fmt, "int": int, "round": round}, dict(consts)))

    build = tcs[tcs.index("// ── 基本 ──"):tcs.index("private static readonly Dictionary<string, TerminalDef> _byId")]
    out, order, cur = {}, [], "基本"
    lines = [l for l in tcs.splitlines() if "var perm = D(TerminalIds.Mains" in l] + build.splitlines()
    for line in lines:
        g = re.search(r"// ── (.+?) ──", line)
        if g: cur = g.group(1)
        mm = re.search(r"D\(TerminalIds\.(\w+),\s*([ABCS]),\s*", line)
        if not mm: continue
        k = mm.group(1)
        args, cur_a, depth, q = [], "", 0, False
        for ch in line[mm.end():]:
            if ch == '"': q = not q
            if not q:
                if ch == "(": depth += 1
                elif ch == ")":
                    if depth == 0: break
                    depth -= 1
                elif ch == "," and depth == 0: args.append(cur_a); cur_a = ""; continue
            cur_a += ch
        args.append(cur_a)
        try: d = desc_of(args[0].strip())
        except Exception as ex: d = "(読み取れず: %s)" % ex
        cm = re.search(r"TerminalCond\.(\w+)", args[1]) if len(args) > 1 else None
        cond = condlab.get(cm.group(1), cm.group(1)) if cm and cm.group(1) != "None" else ""
        grp = "レア" if "var perm" in line else cur
        out[k] = {"key": k, "name": names.get(k, k), "kind": mm.group(2), "group": grp, "desc": d, "cond": cond,
                  "once": "Once(" in line, "rare": "Rare(" in line or "var perm" in line, "perm": "var perm" in line,
                  "basic": "Basic(" in line}
        if k not in order: order.append(k)
    return out, order


def icons_of():
    ics = io.open(ROOT + "Assets/Scripts/UI/Game2D/Game2DTerminalIcons.cs", encoding="utf-8").read()
    r = {}
    for mm in re.finditer(r"TerminalIds\.(\w+)\s*,\s*new\[\]\s*\{(.*?)\}\s*\}", ics, re.S): r[mm.group(1)] = re.findall(r"\"([^\"]*)\"", mm.group(2))
    return r


old_rev, out = sys.argv[1], sys.argv[2]
new_src = io.open(ROOT + TC, encoding="utf-8").read()
old_src = subprocess.run(["git", "-C", ROOT, "show", old_rev + ":" + TC], capture_output=True).stdout.decode("utf-8")
new, order = parse(new_src); old, _ = parse(old_src)
icons = icons_of()
data = []
for k in order:
    n = new[k]
    if n["basic"]: continue
    o = old.get(k)
    n = dict(n); n["before"] = o["desc"] if o else ""; n["changed"] = bool(o) and o["desc"] != n["desc"]; n["rows"] = icons.get(k, [])
    data.append(n)

html = r"""<title>端子の数値 合否</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=DotGothic16&display=swap">
<style>
/* 暗い作業台に端子の札を並べ、 値を 1 枚ずつ合否で決める */
:root{--bg:#191c1d;--panel:#22282a;--line:#39444a;--fg:#e4ece9;--mute:#8d9ba0;--ok:#7fc48f;--ng:#e08a6a;--accent:#e6b85c;--new:#9fd0e0;
  --f-dot:"DotGothic16","MS Gothic",monospace;--f-body:system-ui,"Yu Gothic UI","Hiragino Sans",sans-serif;color-scheme:dark}
html,body{background:var(--bg);color:var(--fg)}
body{font-family:var(--f-body);padding-block:20px 60px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px))}
.wrap{max-width:1080px;margin:0 auto;display:grid;gap:16px}
h1{font-family:var(--f-dot);font-weight:400;font-size:24px;margin:0}
p{margin:0;color:var(--mute);line-height:1.7;max-width:72ch}
.bar{position:sticky;top:env(safe-area-inset-top,0px);z-index:5;background:var(--panel);border:1px solid var(--line);padding:10px 14px;display:flex;gap:14px;align-items:center;flex-wrap:wrap}
.bar b{font-family:var(--f-dot);font-weight:400;font-size:16px}
.bar .ok{color:var(--ok)} .bar .ng{color:var(--ng)}
.bar select{background:var(--bg);color:var(--fg);border:1px solid var(--line);padding:5px 10px;font:inherit;cursor:pointer}
#save{color:var(--mute);font-size:13px;margin-left:auto}
h2{font-family:var(--f-dot);font-weight:400;font-size:17px;margin:14px 0 0;color:var(--accent)}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(310px,1fr));gap:10px}
.card{background:var(--panel);border:1px solid var(--line);padding:10px;display:grid;grid-template-columns:auto 1fr;gap:10px;min-width:0}
.card[data-v=ok]{border-color:var(--ok)} .card[data-v=ng]{border-color:var(--ng)}
canvas{image-rendering:pixelated;display:block;width:56px;height:76px}
.side{display:grid;gap:6px;align-content:start;min-width:0}
.name{font-family:var(--f-dot);font-size:17px}
.tags{display:flex;gap:5px;flex-wrap:wrap}
.tag{font-size:11px;color:var(--mute);border:1px solid var(--line);padding:0 5px}
.tag.cond{color:var(--accent);border-color:var(--accent)}
.row{font-size:13px;line-height:1.55;display:grid;grid-template-columns:2.2em 1fr;gap:4px}
.row i{font-style:normal;color:var(--mute);font-size:11px;padding-top:2px}
.row.before span{color:var(--mute)}
.row.after span{color:var(--fg)}
.card[data-ch="1"] .row.after span{color:var(--new)}
.same{font-size:11px;color:var(--mute)}
.vb{display:flex;gap:6px}
.vb button{flex:1;font-family:var(--f-dot);font-size:15px;padding:6px 0;background:var(--bg);color:var(--mute);border:1px solid var(--line);cursor:pointer}
.vb button[aria-pressed=true].o{background:var(--ok);color:#10201a;border-color:var(--ok)}
.vb button[aria-pressed=true].n{background:var(--ng);color:#2a120a;border-color:var(--ng)}
textarea{width:100%;box-sizing:border-box;min-height:38px;background:var(--bg);color:var(--fg);border:1px solid var(--line);font:inherit;font-size:13px;padding:5px;resize:vertical}
button:focus-visible,textarea:focus-visible,select:focus-visible{outline:2px solid var(--accent);outline-offset:1px}
</style>
<div class="wrap">
  <header>
    <h1>端子の数値 合否</h1>
    <p>基本以外の端子 __N__ 種の数値です。「前」が今日までの値、「後」が約 2 倍にした今の値（水色は値が変わった所）。条件・種類・使い捨ても併記しています。1 枚ずつ「合」か「否」を押し、否のときは望む値を書いてください。押した内容は自動で保存され、こちらで読み取って直します。</p>
  </header>
  <div class="bar">
    <b>合 <span class="ok" id="nOk">0</span></b><b>否 <span class="ng" id="nNg">0</span></b><b>未 <span id="nNone">0</span></b>
    <label for="filt" style="color:var(--mute);font-size:13px">表示</label>
    <select id="filt"><option value="all">全部</option><option value="ch">値が変わった物だけ</option><option value="none">未判定だけ</option><option value="ng">否だけ</option></select>
    <span id="save">保存の準備中…</span>
  </div>
  <div id="list"></div>
</div>
<script>
const DATA = __DATA__;
const KIND = {A:['攻撃','#b0564a'],B:['防御','#4a78a8'],C:['充電','#b8963e'],S:['補助','#4f8a5c']};
const state = {}; let db = null;
const $ = id => document.getElementById(id);
function mix(hex, t, to){ const n=parseInt(hex.slice(1),16), r=n>>16, g=n>>8&255, b=n&255; const f=c=>Math.round(c+(to-c)*t); return 'rgb('+f(r)+','+f(g)+','+f(b)+')'; }
function drawStick(cv, rows, kind){ cv.width=28; cv.height=38; const c=cv.getContext('2d'); const col=KIND[kind][1];
  c.fillStyle='#8f989a'; c.fillRect(7,0,14,12); c.fillStyle='#5d6668'; c.fillRect(10,3,3,5); c.fillRect(15,3,3,5);
  c.fillStyle=mix(col,.45,0); c.fillRect(2,12,24,26); c.fillStyle=col; c.fillRect(3,13,22,24);
  const pal=[mix(col,.5,0),mix(col,.45,255),mix(col,.88,255)];
  for(let y=0;y<rows.length;y++)for(let x=0;x<16;x++){ const v=+rows[y][x]||0; if(!v)continue; c.fillStyle=pal[v-1]; c.fillRect(6+x,17+y,1,1);} }
function counts(){ let o=0,n=0; for(const d of DATA){ const v=(state[d.key]||{}).v; if(v==='ok')o++; else if(v==='ng')n++; }
  $('nOk').textContent=o; $('nNg').textContent=n; $('nNone').textContent=DATA.length-o-n; }
function applyFilter(){ const f=$('filt').value;
  for(const el of document.querySelectorAll('.card')){ const v=el.dataset.v||''; el.hidden = f==='none'? v!=='' : f==='ng'? v!=='ng' : f==='ch'? el.dataset.ch!=='1' : false; } }
const timers={};
function save(key){ counts(); applyFilter(); if(!db){ $('save').textContent='保存先に接続できていません (この端末の中だけに残ります)'; try{localStorage.setItem('tv',JSON.stringify(state));}catch(e){} return; }
  clearTimeout(timers[key]); $('save').textContent='保存中…';
  timers[key]=setTimeout(async()=>{ const d=DATA.find(x=>x.key===key), s=state[key]||{};
    try{ await db.doc('values1/'+key).set({name:d.name, after:d.desc, verdict:s.v||'', note:s.note||'', at:new Date().toISOString()}); $('save').textContent='保存しました'; }
    catch(e){ $('save').textContent='保存に失敗しました: '+(e&&e.message||e); } }, 500); }
function el(tag, cls, text){ const e=document.createElement(tag); if(cls)e.className=cls; if(text!=null)e.textContent=text; return e; }
function build(){ const list=$('list'); let group=null, grid=null;
  for(const d of DATA){
    if(d.group!==group){ group=d.group; list.appendChild(el('h2',null,group)); grid=el('div','grid'); list.appendChild(grid); }
    const card=el('div','card'); card.id='c_'+d.key; card.dataset.v=''; card.dataset.ch=d.changed?'1':'0';
    const cv=document.createElement('canvas'); drawStick(cv,d.rows,d.kind);
    const side=el('div','side');
    side.appendChild(el('div','name',d.name));
    const tags=el('div','tags'); tags.appendChild(el('span','tag',KIND[d.kind][0]));
    tags.appendChild(el('span','tag cond','条件 '+(d.cond||'なし')));
    if(d.once) tags.appendChild(el('span','tag','使い捨て')); if(d.perm) tags.appendChild(el('span','tag','場に残る')); if(d.rare) tags.appendChild(el('span','tag','レア'));
    side.appendChild(tags);
    if(d.changed){ const b=el('div','row before'); b.appendChild(el('i',null,'前')); b.appendChild(el('span',null,d.before)); side.appendChild(b); }
    const a=el('div','row after'); a.appendChild(el('i',null,d.changed?'後':'値')); a.appendChild(el('span',null,d.desc)); side.appendChild(a);
    if(!d.changed) side.appendChild(el('div','same','今回は変えていない'));
    const vb=el('div','vb');
    const bo=el('button','o','合'); bo.type='button'; bo.setAttribute('aria-pressed','false');
    const bn=el('button','n','否'); bn.type='button'; bn.setAttribute('aria-pressed','false');
    const ta=document.createElement('textarea'); ta.id='n_'+d.key; ta.placeholder='否のときは望む値・条件'; ta.setAttribute('aria-label',d.name+' の直しの指示');
    const set=v=>{ const s=state[d.key]||(state[d.key]={v:'',note:''}); s.v = s.v===v? '' : v; paint(d.key); save(d.key); };
    bo.onclick=()=>set('ok'); bn.onclick=()=>set('ng');
    ta.oninput=()=>{ (state[d.key]||(state[d.key]={v:'',note:''})).note=ta.value; save(d.key); };
    vb.append(bo,bn); side.append(vb,ta); card.append(cv,side); grid.appendChild(card);
  } }
function paint(key){ const s=state[key]||{}, card=$('c_'+key); if(!card)return; card.dataset.v=s.v||'';
  card.querySelector('.o').setAttribute('aria-pressed', s.v==='ok'); card.querySelector('.n').setAttribute('aria-pressed', s.v==='ng');
  const ta=$('n_'+key); if(document.activeElement!==ta) ta.value=s.note||''; }
build(); counts();
$('filt').onchange=applyFilter;
(async()=>{
  try{ db = await claude.use('db'); }catch(e){ db=null; }
  if(!db){ $('save').textContent='保存先に接続できていません (この端末の中だけに残ります)'; try{ Object.assign(state, JSON.parse(localStorage.getItem('tv')||'{}')); }catch(e){}
    for(const d of DATA) paint(d.key); counts(); return; }
  try{ const snap = await db.collection('values1').get();
    for(const doc of snap.docs){ const x=doc.data()||{}; state[doc.id]={v:x.verdict||'', note:x.note||''}; paint(doc.id); }
    counts(); $('save').textContent='読み込みました'; }
  catch(e){ $('save').textContent='読み込みに失敗しました: '+(e&&e.message||e); }
})();
</script>
"""
io.open(out, "w", encoding="utf-8").write(html.replace("__DATA__", json.dumps(data, ensure_ascii=False)).replace("__N__", str(len(data))))
print(len(data), "terminals ->", out, "| 変更あり", sum(1 for d in data if d["changed"]), "| 読み取れず", [d["key"] for d in data if "読み取れず" in d["desc"] or "読み取れず" in d["before"]])
for d in data:
    if d["changed"]: print(" ", d["name"], "|", d["cond"], "|", d["before"], "=>", d["desc"])
