"""効果音の試聴ページ (Tools/sfx_player.html) を作る。 音は HTML の中に埋め込むので、 ファイル 1 つで開ける。
音を作り直したら `python Tools/sfx_player_build.py` で作り直す。
"""
import base64, json, os

ROOT = os.path.dirname(os.path.abspath(__file__))
SFX = os.path.join(ROOT, '..', 'Assets', 'Resources', 'Game2DSfx')
CAND = os.path.join(ROOT, 'sfx_ref', 'candidates')

GROUPS = [
    ('【画面の中・8bit】戦闘の演出', [('slash_swing', '風切り (振りかぶり)'), ('hit_slash', 'ヒット (通常)'), ('hit_crit', 'ヒット (会心)'), ('hit_enemy', '被弾 (敵の攻撃)'), ('block', '防御'),
             ('hit_axe', 'ヒット 斧 (通常)'), ('crit_axe', 'ヒット 斧 (会心)'), ('charge_shield', '盾 チャージ (通常)'), ('charge_shield_crit', '盾 チャージ (会心)'), ('hit_shield', '盾 解放 (通常)'), ('crit_shield', '盾 解放 (会心)'),
             ('battle_start', '戦闘開始'), ('enemy_appear', '敵の登場'), ('victory', '勝利'), ('defeat', '敗北')]),
    ('【画面の中・8bit】航行・地図上のできごと', [('step', '足音'), ('node_hover', 'ノード ホバー'), ('recon', '偵察'), ('flare_launch', '照明 打ち上げ'), ('flare_burst', '照明 開く'), ('lure_set', '囮罠 設置'),
             ('lure_hit', '囮罠 作動'), ('chase_riser', '追跡者 補足 (上昇)'), ('chase_hit', '追跡者 補足 (衝撃)'), ('alert', '警戒'), ('lost', '見失い'), ('caught', '捕まる'),
             ('familiar', '使い魔'), ('hurt', '被ダメージ'), ('floor_up', '階層移動')]),
    ('【画面の中・8bit】天候・地形', [('wx_fog', '霧'), ('wx_sand', '砂嵐'), ('wx_snow', '雪'), ('wx_hail', '雹'), ('crumble', '道が崩れる'), ('sink', '点が沈む')]),
    ('【画面の外・筐体の部品】ゴールドポット・パネル', [('nav_press', '航行ボタン (機械式スイッチ)'),
             ('count_tick', '数字ドラム'), ('gui_in', 'GUI 入る (金属の板が滑る)'), ('gui_out', 'GUI 引っ込む'), ('radar_1', 'レーダー 弱 (ソナー)'), ('radar_2', 'レーダー 中'), ('radar_3', 'レーダー 強')]),
    ('【画面の外・筐体の部品】戦闘パネル・メニュー', [('decide', '決定 (GO! / ROLL)'), ('dice_1', 'ダイス 1'), ('dice_2', 'ダイス 2'), ('dice_3', 'ダイス 3'), ('wire', '配線'),
           ('ui_hover', 'ホバー'), ('ui_click', 'クリック'), ('ui_confirm', '確定'), ('ui_open', '開く'), ('ui_close', '閉じる'), ('ui_tab', 'タブ')]),
]
REFS = [('REF_original_with_bgm', '参考: FE 聖魔の光石 (10.9 秒〜・BGM 入り)'), ('V1_synth_noise', '候補 V1 (雑音の合成)'), ('V2_envcopy_percussive', '候補 V2 (分布コピー・粒立ち)'),
        ('V3_envcopy_wide', '候補 V3 (分布コピー・広め)'), ('V4_envcopy_soft', '候補 V4 (分布コピー・柔らかめ)')]
COMBOS = [('剣 通常', [('slash_swing', 0), ('hit_slash', 0.38)]), ('剣 会心 (斬撃痕 → 会心)', [('slash_swing', 0), ('hit_slash', 0.3), ('hit_slash', 0.4), ('hit_slash', 0.5), ('hit_crit', 0.7)]),
          ('短剣 通常', [('slash_swing', 0), ('hit_slash', 0.25), ('hit_slash', 0.4)]), ('短剣 会心 (痕 ×4 → 会心)', [('slash_swing', 0), ('hit_slash', 0.25), ('hit_slash', 0.33), ('hit_slash', 0.41), ('hit_slash', 0.49), ('hit_crit', 0.65)]),
          ('斧 通常', [('slash_swing', 0), ('hit_axe', 0.45)]), ('斧 会心', [('slash_swing', 0), ('crit_axe', 0.8)]),
          ('盾 通常 (チャージ → 解放)', [('charge_shield', 0.18), ('hit_shield', 0.64)]), ('盾 会心 (チャージ → 解放)', [('charge_shield_crit', 0.18), ('crit_shield', 1.04)]),
          ('敵の攻撃', [('slash_swing', 0), ('hit_enemy', 0.35)]), ('防御成功', [('slash_swing', 0), ('block', 0.35)]),
          ('戦闘の始まり', [('battle_start', 0), ('enemy_appear', 0.35), ('decide', 1.5)]),
          ('ターンの流れ', [('decide', 0), ('dice_1', 0.1), ('dice_2', 0.25), ('dice_3', 0.4), ('wire', 0.9), ('wire', 1.1), ('decide', 1.5), ('slash_swing', 1.7), ('hit_slash', 2.08)])]


def b64(path):
    with open(path, 'rb') as f: return base64.b64encode(f.read()).decode()


data = {}
for _, items in GROUPS:
    for k, _l in items:
        p = os.path.join(SFX, k + '.wav')
        if os.path.exists(p): data[k] = b64(p)
for k, _l in REFS:
    p = os.path.join(CAND, k + '.wav')
    if os.path.exists(p): data[k] = b64(p)

# 実在の録音・生成した音 (Tools/sfx_real/<サブフォルダ>/*.mp3|wav): サブフォルダごとに 1 つの区分で並べる
APPROVED_LABELS = {'wx_fog': '天候 霧 (8kHz・8bit)', 'wx_sand': '天候 砂嵐 (8kHz・8bit)', 'wx_snow': '天候 雪 (8kHz・8bit)', 'wx_hail': '天候 ひょう (8kHz・8bit・降り続ける)', 'radar_1': 'レーダー Lv1 遠い (間隔 1000ms)', 'radar_2': 'レーダー Lv2 かなり近い (間隔 700ms)', 'radar_3': 'レーダー Lv3 察知された (間隔 450ms)', 'gui_in': 'GUI 出る (カセットデッキ重ね)', 'gui_out': 'GUI 引っ込む (一斉にしまう 0.45 秒)', 'count_tick': '数字ドラム 1 つ進む (単発・連打で音程ばらつき)', 'nav_press': 'パネルのボタン (罠・照明・偵察)  カチッ + コトッ', 'pot_open': 'GOLD 入れ物 開 (カチッ + ガシャッ)', 'pot_close': 'GOLD 入れ物 閉 (衝突音 ↗ + レジのベル)',
                   'coin_few_1': 'コイン 数枚 (1)  GOLD +1〜5', 'coin_few_2': 'コイン 数枚 (2)  ※ 1・2 をランダムに',
                   'coin_pour_1': 'コインの雨 1 秒  GOLD +6〜20', 'coin_pour_2': 'コインの雨 2 秒  GOLD +21〜30', 'coin_pour_3': 'コインの雨 3 秒  GOLD +31〜'}
APPROVED_GROUP = []
_ap = os.path.join(ROOT, 'sfx_real', 'approved')
if os.path.isdir(_ap):
    for f in sorted(os.listdir(_ap)):
        if f.lower().endswith('.wav'):
            n = os.path.splitext(f)[0]; data[n] = b64(os.path.join(_ap, f)); APPROVED_GROUP.append((n, APPROVED_LABELS.get(n, n)))
REAL_GROUPS = []
real_root = os.path.join(ROOT, 'sfx_real')
if os.path.isdir(real_root):
    for sub in sorted(os.listdir(real_root)):
        d = os.path.join(real_root, sub)
        if not os.path.isdir(d) or sub == 'approved': continue      # approved = 採用済み (ゲームの音として、 上の区分に入っている)
        items = []
        for f in sorted(os.listdir(d)):
            if f.lower().endswith(('.mp3', '.wav', '.ogg')):
                key = 'real_%s_%s' % (sub, os.path.splitext(f)[0]); data[key] = b64(os.path.join(d, f)); items.append((key, os.path.splitext(f)[0]))
        if items: REAL_GROUPS.append(('【実在の録音・生成】' + sub, items))

html = '''<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>DICE BOUND 効果音</title>
<style>
:root{--bg:#14161a;--panel:#1d2127;--line:#2c333b;--ink:#dcd6c4;--dim:#8d8a7c;--amber:#e0a030;--red:#e0603c;--blue:#4a86d8}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.5 "Segoe UI","Yu Gothic UI",sans-serif;padding:16px}
header{display:flex;flex-wrap:wrap;gap:12px 24px;align-items:center;margin-bottom:16px;position:sticky;top:0;background:var(--bg);padding:8px 0;z-index:2;border-bottom:1px solid var(--line)}
h1{font-size:18px;margin:0;letter-spacing:.04em}h2{font-size:13px;color:var(--amber);margin:24px 0 8px;letter-spacing:.12em}
.vol{display:flex;align-items:center;gap:8px;color:var(--dim)}input[type=range]{width:160px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:8px}
button{all:unset;cursor:pointer;background:var(--panel);border:1px solid var(--line);padding:8px 10px;display:flex;flex-direction:column;gap:2px}
button:hover{border-color:var(--amber)}button:active,button.on{background:#2a2f37;border-color:var(--amber)}button:focus-visible{outline:2px solid var(--blue)}
button b{font-weight:600}button span{color:var(--dim);font-size:12px;font-family:Consolas,monospace}
.combo button{border-color:#4a3a1c}.ref button{border-color:#3a4a6a}
.note{color:var(--dim);font-size:12px}
</style></head><body>
<header><h1>DICE BOUND 効果音</h1>
<label class="vol">音量 <input id="vol" type="range" min="0" max="1" step="0.01" value="0.8"><output id="vo">80%</output></label>
<span class="note">クリックで再生 / 数字キー 1〜9 でまとめ再生</span></header>
<div id="app"></div>
<script>
const DATA = ''' + json.dumps(data) + ''';
const GROUPS = ''' + json.dumps([[g, [[k, l] for k, l in items if k in data]] for g, items in ([('★ 採用済み (実在の録音)  ※ ゲームに反映済みの音', APPROVED_GROUP)] if APPROVED_GROUP else []) + GROUPS + REAL_GROUPS], ensure_ascii=False) + ''';
const REFS = ''' + json.dumps([[k, l] for k, l in REFS if k in data], ensure_ascii=False) + ''';
const COMBOS = ''' + json.dumps(COMBOS, ensure_ascii=False) + ''';
const ctx = new (window.AudioContext || window.webkitAudioContext)();
const gain = ctx.createGain(); gain.gain.value = 0.8; gain.connect(ctx.destination);
const buf = {};
function b2ab(s){const b=atob(s),u=new Uint8Array(b.length);for(let i=0;i<b.length;i++)u[i]=b.charCodeAt(i);return u.buffer;}
const ready = Promise.all(Object.keys(DATA).map(async k => { buf[k] = await ctx.decodeAudioData(b2ab(DATA[k])); }));
function play(k, delay = 0, btn) {
  if (ctx.state === 'suspended') ctx.resume();
  const b = buf[k]; if (!b) return;
  const s = ctx.createBufferSource(); s.buffer = b; s.connect(gain); s.start(ctx.currentTime + delay);
  if (btn) { btn.classList.add('on'); setTimeout(() => btn.classList.remove('on'), Math.max(120, b.duration * 1000)); }
}
function section(title, items, cls, fn) {
  const h = document.createElement('h2'); h.textContent = title; app.appendChild(h);
  const g = document.createElement('div'); g.className = 'grid ' + (cls || ''); app.appendChild(g);
  for (const [k, label] of items) {
    const b = document.createElement('button'); b.innerHTML = '<b></b><span></span>'; b.firstChild.textContent = label; b.lastChild.textContent = k;
    b.onclick = () => fn(k, b); g.appendChild(b);
  }
}
const app = document.getElementById('app');
ready.then(() => {
  section('流れで聴く (組み合わせ)', COMBOS.map(([n, seq], i) => [i, n]), 'combo', (i, b) => { for (const [k, t] of COMBOS[i][1]) play(k, t); b.classList.add('on'); setTimeout(() => b.classList.remove('on'), 600); });
  for (const [g, items] of GROUPS) section(g, items, '', (k, b) => play(k, 0, b));
  section('参考 (FE 聖魔の光石) と候補', REFS, 'ref', (k, b) => play(k, 0, b));
  const keys = ['通常攻撃', '会心攻撃', '敵の攻撃', '防御成功', '戦闘の始まり', 'ターンの流れ'];
  document.addEventListener('keydown', e => { const i = '123456789'.indexOf(e.key); if (i >= 0 && !e.repeat) for (const [k, t] of COMBOS[i][1]) play(k, t); });
});
const vol = document.getElementById('vol'), vo = document.getElementById('vo');
vol.oninput = () => { gain.gain.value = +vol.value; vo.textContent = Math.round(vol.value * 100) + '%'; };
</script></body></html>'''
out = os.path.join(ROOT, 'sfx_player.html')
with open(out, 'w', encoding='utf-8') as f: f.write(html)
print(len(data), 'sounds ->', os.path.normpath(out), '%.1f MB' % (os.path.getsize(out) / 1e6))
