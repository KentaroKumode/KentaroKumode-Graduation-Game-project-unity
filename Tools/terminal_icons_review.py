# 端子のアイコン (Game2DTerminalIcons.cs) の合否ページ (HTML) を書き出す。
#   使い方: python Tools/terminal_icons_review.py <出力する .html>
#   ページは合否と直しの指示を Artifact の db (collection "reviews3" (第 4 稿・17×17・2 色)) に保存する。
import io, re, sys, json

ROOT = "C:/Users/kumod/My project/"
ics = io.open(ROOT + "Assets/Scripts/UI/Game2D/Game2DTerminalIcons.cs", encoding="utf-8").read()
tcs = io.open(ROOT + "Assets/Scripts/CombatSystem/TerminalCards.cs", encoding="utf-8").read()

m = re.search(r"public static class TerminalIds\s*\{(.*?)\n    \}", tcs, re.S)
names = dict(re.findall(r"(\w+)\s*=\s*\"([^\"]+)\"", m.group(1)))
icons = {}
for mm in re.finditer(r"TerminalIds\.(\w+)\s*,\s*new\[\]\s*\{(.*?)\}\s*\}", ics, re.S):
    icons[mm.group(1)] = re.findall(r"\"([^\"]*)\"", mm.group(2))

# 種類 (攻撃/防御/充電/補助) と区分を TerminalCards.Build から読む
kind = {}
for mm in re.finditer(r"D\(TerminalIds\.(\w+),\s*([ABCS])\b", tcs):
    kind[mm.group(1)] = mm.group(2)
kind["Mains"] = "A"
order = [k for k in re.findall(r"TerminalIds\.(\w+)", tcs[tcs.index("private static List<TerminalDef> Build()"):tcs.index("private static readonly Dictionary<string, TerminalDef> _byId")]) if k in icons]
seen = set(); keys = [k for k in order if not (k in seen or seen.add(k))]
if "Mains" in icons and "Mains" not in keys: keys.append("Mains")
groups = {}
build = tcs[tcs.index("// ── 基本 ──"):tcs.index("private static readonly Dictionary<string, TerminalDef> _byId")]
cur = "基本"
for line in build.splitlines():
    g = re.search(r"// ── (.+?) ──", line)
    if g: cur = g.group(1)
    for k in re.findall(r"TerminalIds\.(\w+)", line): groups.setdefault(k, cur)
    if "Rare(perm)" in line: groups["Mains"] = "レア"

# 効果の文 (TerminalCards.Build の説明の式を、 定数を読んでそのまま評価する) と条件の札
consts = {}
for mm in re.finditer(r"public const (?:int|float) ([^;]+);", tcs):
    for nm, val in re.findall(r"(\w+)\s*=\s*([0-9.]+)f?", mm.group(1)): consts[nm] = float(val) if "." in val else int(val)
condlab = dict(re.findall(r"case TerminalCond\.(\w+): return \"([^\"]+)\";", tcs))


class S(str):
    def __add__(self, o): return S(str(self) + (o if isinstance(o, str) else str(o)))


def cs_fmt(v, f):
    # C# の書式 "0.0" / "0.0#" (# の桁は 0 なら出さない)
    frac = f.split(".")[1] if "." in f else ""
    s = ("%." + str(len(frac)) + "f") % v
    while "#" in frac and len(s.split(".")[1]) > frac.count("0") and s.endswith("0"): s = s[:-1]
    return s


def desc_of(expr):
    e = re.sub(r"(\w+)\.ToString\(\"([^\"]+)\"\)", r'cs_fmt(\1,"\2")', expr)
    e = re.sub(r"\(int\)\(([^)]*)\)", r"int(round(\1))", e)
    e = re.sub(r"\"([^\"]*)\"", lambda m: "S(" + json.dumps(m.group(1), ensure_ascii=False) + ")", e)
    return str(eval(e, {"S": S, "cs_fmt": cs_fmt, "int": int, "round": round}, dict(consts)))


desc, cond, once = {}, {}, set()
for line in build.splitlines() + [l for l in tcs.splitlines() if "var perm = D(TerminalIds.Mains" in l]:
    mm = re.search(r"D\(TerminalIds\.(\w+),\s*[ABCS],\s*", line)
    if not mm: continue
    k = mm.group(1)
    # D( の引数を、 括弧の深さと引用符を見ながら切り出す (式の中に括弧やカンマがあるため)
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
    try: desc[k] = desc_of(args[0].strip())
    except Exception as ex: desc[k] = "(読み取れず: %s)" % ex
    cm = re.search(r"TerminalCond\.(\w+)", args[1]) if len(args) > 1 else None
    if cm and cm.group(1) != "None": cond[k] = condlab.get(cm.group(1), cm.group(1))
    if "Once(" in line: once.add(k)
data = [{"key": k, "name": names.get(k, k), "kind": kind.get(k, "S"), "group": groups.get(k, ""), "rows": icons[k],
         "desc": desc.get(k, ""), "cond": cond.get(k, ""), "once": k in once} for k in keys]

html = r"""<title>端子アイコン合否</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=DotGothic16&display=swap">
<style>
/* 暗い作業台の上に USB メモリを並べ、 1 本ずつ合否を付ける */
:root{--bg:#191c1d;--panel:#22282a;--line:#39444a;--fg:#e4ece9;--mute:#8d9ba0;--ok:#7fc48f;--ng:#e08a6a;--accent:#e6b85c;
  --f-dot:"DotGothic16","MS Gothic",monospace;--f-body:system-ui,"Yu Gothic UI","Hiragino Sans",sans-serif;color-scheme:dark}
html,body{background:var(--bg);color:var(--fg)}
body{font-family:var(--f-body);padding-block:20px 60px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px))}
.wrap{max-width:1080px;margin:0 auto;display:grid;gap:16px}
h1{font-family:var(--f-dot);font-weight:400;font-size:24px;margin:0}
p{margin:0;color:var(--mute);line-height:1.7;max-width:70ch}
.bar{position:sticky;top:env(safe-area-inset-top,0px);z-index:5;background:var(--panel);border:1px solid var(--line);padding:10px 14px;display:flex;gap:14px;align-items:center;flex-wrap:wrap}
.bar b{font-family:var(--f-dot);font-weight:400;font-size:16px}
.bar .ok{color:var(--ok)} .bar .ng{color:var(--ng)}
.bar select,.bar button{background:var(--bg);color:var(--fg);border:1px solid var(--line);padding:5px 10px;font:inherit;cursor:pointer}
#save{color:var(--mute);font-size:13px;margin-left:auto}
h2{font-family:var(--f-dot);font-weight:400;font-size:17px;margin:14px 0 0;color:var(--accent)}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:10px}
.card{background:var(--panel);border:1px solid var(--line);padding:10px;display:grid;grid-template-columns:auto 1fr;gap:10px;min-width:0}
.card[data-v=ok]{border-color:var(--ok)} .card[data-v=ng]{border-color:var(--ng)}
.pics{display:grid;gap:6px;justify-items:center}
canvas{image-rendering:pixelated;display:block}
.big{width:136px;height:136px;background:#3a4a52}
.side{display:grid;gap:6px;align-content:start;min-width:0}
.name{font-family:var(--f-dot);font-size:17px}
.name small{color:var(--mute);font-size:12px;margin-left:6px}
.ef{font-size:13px;line-height:1.55;color:var(--fg)}
.ef .tag{display:inline-block;font-size:11px;color:var(--accent);border:1px solid var(--line);padding:0 5px;margin-right:5px}
.vb{display:flex;gap:6px}
.vb button{flex:1;font-family:var(--f-dot);font-size:15px;padding:6px 0;background:var(--bg);color:var(--mute);border:1px solid var(--line);cursor:pointer}
.vb button[aria-pressed=true].o{background:var(--ok);color:#10201a;border-color:var(--ok)}
.vb button[aria-pressed=true].n{background:var(--ng);color:#2a120a;border-color:var(--ng)}
textarea{width:100%;box-sizing:border-box;min-height:44px;background:var(--bg);color:var(--fg);border:1px solid var(--line);font:inherit;font-size:13px;padding:5px;resize:vertical}
button:focus-visible,textarea:focus-visible,select:focus-visible{outline:2px solid var(--accent);outline-offset:1px}
</style>
<div class="wrap">
  <header>
    <h1>端子アイコン合否（第4稿・新しい様式）</h1>
    <p>端子 60 種を新しい様式で描き直しました。17×17・明るい主役と、その後ろの暗めの飾りの 2 色だけです（主役の中の線は抜き）。物の絵に限らず、意味を表す記号にした物もあります。左が拡大、その右が USB メモリに載せた実寸の 3 倍です。1 つずつ「合」か「否」を押し、直してほしい点があれば書いてください。押した内容は自動で保存され、こちらで読み取って描き直します。</p>
  </header>
  <div class="bar">
    <b>合 <span class="ok" id="nOk">0</span></b><b>否 <span class="ng" id="nNg">0</span></b><b>未 <span id="nNone">0</span></b>
    <label for="filt" style="color:var(--mute);font-size:13px">表示</label>
    <select id="filt"><option value="all">全部</option><option value="none">未判定だけ</option><option value="ng">否だけ</option></select>
    <span id="save">保存の準備中…</span>
  </div>
  <div id="list"></div>
</div>
<script>
const DATA = __DATA__;
const KIND = {A:['攻撃','#b0564a'],B:['防御','#4a78a8'],C:['充電','#b8963e'],S:['補助','#4f8a5c']};
const state = {};   // key -> {v:'ok'|'ng'|'', note:''}
let db = null;
const $ = id => document.getElementById(id);
function mix(hex, t, to){ const n=parseInt(hex.slice(1),16), r=n>>16, g=n>>8&255, b=n&255;
  const f=c=>Math.round(c+(to-c)*t); return 'rgb('+f(r)+','+f(g)+','+f(b)+')'; }
function drawIcon(cv, rows, pal){ cv.width=17; cv.height=17; const c=cv.getContext('2d');
  for(let y=0;y<17;y++)for(let x=0;x<17;x++){ const v=+rows[y][x]||0; if(!v)continue; c.fillStyle=pal[v-1]; c.fillRect(x,y,1,1);} }
function drawStick(cv, rows, kind){ cv.width=29; cv.height=39; const c=cv.getContext('2d'); const col=KIND[kind][1];
  c.fillStyle='#8f989a'; c.fillRect(8,0,13,12); c.fillStyle='#5d6668'; c.fillRect(10,3,3,5); c.fillRect(16,3,3,5);
  c.fillStyle=mix(col,.45,0); c.fillRect(2,12,25,27); c.fillStyle=col; c.fillRect(3,13,23,25);
  const pal=[col,mix(col,.35,235),mix(col,.75,235)];   // ゲームと同じ混ぜ方: 後ろの飾り 35% / 主役 75%
  for(let y=0;y<17;y++)for(let x=0;x<17;x++){ const v=+rows[y][x]||0; if(!v)continue; c.fillStyle=pal[v-1]; c.fillRect(6+x,17+y,1,1);} }
function counts(){ let o=0,n=0; for(const d of DATA){ const v=(state[d.key]||{}).v; if(v==='ok')o++; else if(v==='ng')n++; }
  $('nOk').textContent=o; $('nNg').textContent=n; $('nNone').textContent=DATA.length-o-n; }
function applyFilter(){ const f=$('filt').value;
  for(const el of document.querySelectorAll('.card')){ const v=el.dataset.v||''; el.hidden = f==='none'? v!=='' : f==='ng'? v!=='ng' : false; } }
const timers={};
function save(key){ counts(); applyFilter(); if(!db){ $('save').textContent='保存先に接続できていません (この端末の中だけに残ります)'; try{localStorage.setItem('rv3',JSON.stringify(state));}catch(e){} return; }
  clearTimeout(timers[key]); $('save').textContent='保存中…';
  timers[key]=setTimeout(async()=>{ const d=DATA.find(x=>x.key===key), s=state[key]||{};
    try{ await db.doc('reviews3/'+key).set({name:d.name, verdict:s.v||'', note:s.note||'', at:new Date().toISOString()}); $('save').textContent='保存しました'; }
    catch(e){ $('save').textContent='保存に失敗しました: '+(e&&e.message||e); } }, 500); }
function build(){ const list=$('list'); let group=null, grid=null;
  for(const d of DATA){
    if(d.group!==group){ group=d.group; const h=document.createElement('h2'); h.textContent=group; list.appendChild(h); grid=document.createElement('div'); grid.className='grid'; list.appendChild(grid); }
    const card=document.createElement('div'); card.className='card'; card.id='c_'+d.key; card.dataset.v='';
    const pics=document.createElement('div'); pics.className='pics';
    const big=document.createElement('canvas'); big.className='big'; drawIcon(big,d.rows,['#3a4a52','#778489','#bdc7c7']);
    const st=document.createElement('canvas'); st.style.width='87px'; st.style.height='117px'; drawStick(st,d.rows,d.kind);
    pics.append(big,st);
    const side=document.createElement('div'); side.className='side';
    const nm=document.createElement('div'); nm.className='name'; nm.textContent=d.name; const sm=document.createElement('small'); sm.textContent=KIND[d.kind][0]; nm.appendChild(sm);
    const vb=document.createElement('div'); vb.className='vb';
    const bo=document.createElement('button'); bo.type='button'; bo.className='o'; bo.textContent='合'; bo.setAttribute('aria-pressed','false');
    const bn=document.createElement('button'); bn.type='button'; bn.className='n'; bn.textContent='否'; bn.setAttribute('aria-pressed','false');
    const ta=document.createElement('textarea'); ta.id='n_'+d.key; ta.placeholder='直してほしい点'; ta.setAttribute('aria-label',d.name+' の直しの指示');
    const set=v=>{ const s=state[d.key]||(state[d.key]={v:'',note:''}); s.v = s.v===v? '' : v; paint(d.key); save(d.key); };
    bo.onclick=()=>set('ok'); bn.onclick=()=>set('ng');
    ta.oninput=()=>{ (state[d.key]||(state[d.key]={v:'',note:''})).note=ta.value; save(d.key); };
    const ef=document.createElement('div'); ef.className='ef';
    if(d.cond){ const t=document.createElement('span'); t.className='tag'; t.textContent='条件 '+d.cond; ef.appendChild(t); }
    if(d.once){ const t=document.createElement('span'); t.className='tag'; t.textContent='使い捨て'; ef.appendChild(t); }
    ef.appendChild(document.createTextNode(d.desc||''));
    vb.append(bo,bn); side.append(nm,ef,vb,ta); card.append(pics,side); grid.appendChild(card);
  } }
function paint(key){ const s=state[key]||{}, card=$('c_'+key); if(!card)return; card.dataset.v=s.v||'';
  card.querySelector('.o').setAttribute('aria-pressed', s.v==='ok'); card.querySelector('.n').setAttribute('aria-pressed', s.v==='ng');
  const ta=$('n_'+key); if(document.activeElement!==ta) ta.value=s.note||''; }
build(); counts();
$('filt').onchange=applyFilter;
(async()=>{
  try{ db = await claude.use('db'); }catch(e){ db=null; }
  if(!db){ $('save').textContent='保存先に接続できていません (この端末の中だけに残ります)'; try{ Object.assign(state, JSON.parse(localStorage.getItem('rv3')||'{}')); }catch(e){}
    for(const d of DATA) paint(d.key); counts(); return; }
  try{ const snap = await db.collection('reviews3').get();
    for(const doc of snap.docs){ const x=doc.data()||{}; state[doc.id]={v:x.verdict||'', note:x.note||''}; paint(doc.id); }
    counts(); $('save').textContent='読み込みました'; }
  catch(e){ $('save').textContent='読み込みに失敗しました: '+(e&&e.message||e); }
})();
</script>
"""
out = sys.argv[1]
io.open(out, "w", encoding="utf-8").write(html.replace("__DATA__", json.dumps(data, ensure_ascii=False)))
print(len(data), "icons ->", out, "| missing kind:", [d["key"] for d in data if d["key"] not in kind], "| 効果なし:", [d["key"] for d in data if not d["desc"] or "読み取れず" in d["desc"]])
for d in data: print(" ", d["name"], "|", d["cond"], "|", "使い捨て" if d["once"] else "", "|", d["desc"])
