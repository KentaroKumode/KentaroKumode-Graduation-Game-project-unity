"""外部の書き手 (Gemini) が書き直したフレーバーの合否ページ (HTML) を書き出す。

使い方: python Tools/flavor_review.py <書き直しの JSON があるフォルダ> <出力する .html>
  - フォルダの *.json (各要素 {"id","flavorText","note","rename"}) を全部読み、items.json の今の文と並べる。
  - ページは合否と直しの指示を Artifact の db (collection "flavor1") に保存する。
  - 書き直しの無い品は出さない (rename / note だけの提案は出す)。
"""
import glob, io, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    src, out = sys.argv[1], sys.argv[2]
    d = json.load(io.open(os.path.join(ROOT, "Assets/Data/InventorySystem/items.json"), encoding="utf-8-sig"))
    items = {i["id"]: i for i in (d["items"] if isinstance(d, dict) and "items" in d else d)}
    order = list(items)
    props = {}
    for f in sorted(glob.glob(os.path.join(src, "*.json"))):
        try: arr = json.load(io.open(f, encoding="utf-8"))
        except Exception: continue
        if not isinstance(arr, list): continue
        for p in arr:
            if isinstance(p, dict) and p.get("id") in items: props[p["id"]] = p
    rows = []
    for k in order:
        if k not in props: continue
        i, p = items[k], props[k]
        rows.append({"id": k, "rarity": i.get("rarity", ""), "cat": i.get("category", ""), "axis": i.get("axis", ""),
                     "family": i.get("family", ""), "tier": i.get("tier", ""), "desc": i.get("description", ""),
                     "before": i.get("flavorText", ""), "after": p.get("flavorText") or "", "note": p.get("note") or "", "rename": p.get("rename") or ""})
    html = TEMPLATE.replace("__DATA__", json.dumps(rows, ensure_ascii=False)).replace("__N__", str(len(rows)))
    io.open(out, "w", encoding="utf-8").write(html)
    print(len(rows), "品 ->", out, "| 名前の案", sum(1 for r in rows if r["rename"]))


TEMPLATE = r"""<title>フレーバー書き直し合否</title>
<style>
:root{--bg:#f4f2ee;--card:#fffdf9;--ink:#24211d;--mute:#6f685e;--line:#ddd6cb;--old:#8a8378;--ok:#2f7a4f;--ng:#b0463c;--accent:#7a5c2e;--chip:#ece6db}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#1b1a18;--card:#252320;--ink:#ece6dc;--mute:#a69e92;--line:#3a3631;--old:#958d81;--ok:#6dbb8a;--ng:#e0857a;--accent:#d2ad6e;--chip:#312e29}}
:root[data-theme="dark"]{--bg:#1b1a18;--card:#252320;--ink:#ece6dc;--mute:#a69e92;--line:#3a3631;--old:#958d81;--ok:#6dbb8a;--ng:#e0857a;--accent:#d2ad6e;--chip:#312e29}
body{background:var(--bg);color:var(--ink);font-family:system-ui,"Yu Gothic UI","Hiragino Sans",sans-serif;padding-block:20px 60px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px))}
.wrap{max-width:980px;margin:0 auto;display:grid;gap:14px}
h1{font-size:22px;margin:0}
p.lead{color:var(--mute);line-height:1.7;margin:4px 0 0}
.bar{position:sticky;top:env(safe-area-inset-top,0px);z-index:2;background:var(--bg);display:flex;flex-wrap:wrap;gap:12px;align-items:center;padding:8px 0;border-bottom:1px solid var(--line)}
.bar b{font-weight:600}.ok{color:var(--ok)}.ng{color:var(--ng)}#save{color:var(--mute);font-size:13px;margin-left:auto}
.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:14px 16px;display:grid;gap:8px}
.card[data-v="ok"]{border-color:var(--ok)}.card[data-v="ng"]{border-color:var(--ng)}
.head{display:flex;flex-wrap:wrap;gap:8px;align-items:baseline}
.name{font-size:17px;font-weight:600}
.tag{background:var(--chip);color:var(--mute);border-radius:4px;padding:1px 7px;font-size:12px}
.desc{color:var(--mute);font-size:13px}
.txt{line-height:1.8;margin:0}
.old{color:var(--old);text-decoration:line-through;text-decoration-color:color-mix(in srgb,var(--old) 50%,transparent)}
.lbl{font-size:12px;color:var(--mute);letter-spacing:.04em}
.prop{font-size:13px;color:var(--accent);line-height:1.6}
.row{display:grid;grid-template-columns:1fr 1fr;gap:12px}
@media (max-width:640px){.row{grid-template-columns:1fr}}
.vb{display:flex;gap:8px;align-items:center;flex-wrap:wrap}
button{font:inherit;border:1px solid var(--line);background:var(--card);color:var(--ink);border-radius:6px;padding:4px 16px;cursor:pointer}
button.o[aria-pressed="true"]{background:var(--ok);border-color:var(--ok);color:#fff}
button.n[aria-pressed="true"]{background:var(--ng);border-color:var(--ng);color:#fff}
button.r[aria-pressed="true"]{background:var(--accent);border-color:var(--accent);color:#fff}
textarea{font:inherit;width:100%;box-sizing:border-box;min-height:44px;border:1px solid var(--line);border-radius:6px;background:var(--bg);color:var(--ink);padding:6px 8px}
select{font:inherit}
:focus-visible{outline:2px solid var(--accent);outline-offset:1px}
</style>
<div class="wrap">
  <header>
    <h1>フレーバー書き直し合否</h1>
    <p class="lead">Gemini が書き直した __N__ 品のフレーバーです。左（取り消し線）が今の文、右が書き直し。名前の案や注記がある品は、その下に出ます。
    1 品ずつ「合」（書き直しを採る）か「否」（今の文のまま）を押し、直してほしい点があれば書いてください。名前の案を採る時は「名前も採る」を押してください。押した内容は自動で保存されます。</p>
  </header>
  <div class="bar">
    <b>合 <span class="ok" id="nOk">0</span></b><b>否 <span class="ng" id="nNg">0</span></b><b>未 <span id="nNone">0</span></b>
    <label for="filt" style="color:var(--mute);font-size:13px">表示</label>
    <select id="filt"><option value="all">全部</option><option value="none">未判定だけ</option><option value="rename">名前の案がある物</option><option value="ng">否だけ</option></select>
    <span id="save">保存の準備中…</span>
  </div>
  <div id="list"></div>
</div>
<script>
const DATA=__DATA__;
const state={};let db=null;const $=id=>document.getElementById(id);
function counts(){let o=0,n=0;for(const d of DATA){const v=(state[d.id]||{}).v;if(v==='ok')o++;else if(v==='ng')n++;}$('nOk').textContent=o;$('nNg').textContent=n;$('nNone').textContent=DATA.length-o-n;}
function applyFilter(){const f=$('filt').value;for(const el of document.querySelectorAll('.card')){const v=el.dataset.v||'';el.hidden=f==='none'?v!=='':f==='ng'?v!=='ng':f==='rename'?el.dataset.r!=='1':false;}}
const timers={};
function key(id){let h=0;for(const c of id)h=(h*31+c.codePointAt(0))>>>0;return 'i'+h.toString(36);}
function save(id){counts();applyFilter();if(!db){$('save').textContent='保存先に接続できていません';return;}clearTimeout(timers[id]);$('save').textContent='保存中…';
 timers[id]=setTimeout(async()=>{const s=state[id]||{};try{await db.doc('flavor1/'+key(id)).set({id,verdict:s.v||'',note:s.note||'',rename:!!s.rn,at:new Date().toISOString()});$('save').textContent='保存しました';}catch(e){$('save').textContent='保存に失敗しました: '+(e&&e.message||e);}},500);}
function el(t,c,txt){const e=document.createElement(t);if(c)e.className=c;if(txt!=null)e.textContent=txt;return e;}
function build(){const list=$('list');for(const d of DATA){const card=el('div','card');card.id='c_'+key(d.id);card.dataset.v='';card.dataset.r=d.rename?'1':'';
 const head=el('div','head');head.append(el('span','name',d.id));for(const t of [d.rarity,d.cat,d.axis&&('軸 '+d.axis),d.family&&(d.family+(d.tier?' '+d.tier:''))])if(t)head.append(el('span','tag',t));
 const desc=el('div','desc',d.desc);
 const row=el('div','row');const L=el('div');L.append(el('div','lbl','今の文'),el('p','txt old',d.before||'（なし）'));const R=el('div');R.append(el('div','lbl','書き直し'),el('p','txt',d.after||'（文は変えず、提案だけ）'));row.append(L,R);
 card.append(head,desc,row);
 if(d.note)card.append(el('div','prop','注: '+d.note));
 if(d.rename)card.append(el('div','prop','名前の案: '+d.rename));
 const vb=el('div','vb');const bo=el('button','o','合');bo.type='button';const bn=el('button','n','否');bn.type='button';vb.append(bo,bn);
 let br=null;if(d.rename){br=el('button','r','名前も採る');br.type='button';vb.append(br);}
 const ta=el('textarea');ta.id='n_'+key(d.id);ta.placeholder='直してほしい点';ta.setAttribute('aria-label',d.id+' の直しの指示');
 const st=()=>state[d.id]||(state[d.id]={v:'',note:'',rn:false});
 const set=v=>{const s=st();s.v=s.v===v?'':v;paint(d.id);save(d.id);};bo.onclick=()=>set('ok');bn.onclick=()=>set('ng');
 if(br)br.onclick=()=>{const s=st();s.rn=!s.rn;paint(d.id);save(d.id);};
 ta.oninput=()=>{st().note=ta.value;save(d.id);};card.append(vb,ta);list.append(card);}}
function paint(id){const s=state[id]||{},c=$('c_'+key(id));if(!c)return;c.dataset.v=s.v||'';c.querySelector('.o').setAttribute('aria-pressed',s.v==='ok');c.querySelector('.n').setAttribute('aria-pressed',s.v==='ng');
 const r=c.querySelector('.r');if(r)r.setAttribute('aria-pressed',!!s.rn);const ta=$('n_'+key(id));if(document.activeElement!==ta)ta.value=s.note||'';}
build();counts();$('filt').onchange=applyFilter;
(async()=>{try{db=await claude.use('db');}catch(e){db=null;}
 if(!db){$('save').textContent='保存先に接続できていません';return;}
 try{const snap=await db.collection('flavor1').get();for(const doc of snap.docs){const x=doc.data()||{};if(!x.id)continue;state[x.id]={v:x.verdict||'',note:x.note||'',rn:!!x.rename};paint(x.id);}counts();$('save').textContent='読み込みました';}
 catch(e){$('save').textContent='読み込みに失敗しました: '+(e&&e.message||e);}})();
</script>
"""

if __name__ == "__main__":
    main()
