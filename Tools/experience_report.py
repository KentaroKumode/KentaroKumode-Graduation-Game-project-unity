"""体験の大目標 (docs/experience-goal.md) の物差しを、 並列ランナーの腕の記録から集計する (段階 1・2・2026-11-17)。

    python Tools/experience_report.py <腕の定義ファイル> <出力フォルダ>

腕の定義ファイル: 1 行 1 腕 「ラベル<TAB>AutoRunLogs/learn_parallel/<日時> のフォルダ」。 # は注釈。
ラベルの決まり (集計の役割):
  S        = 基準 (Super・難易度 0・パネル 24 点)。 アブレーションとパネルの比べる相手
  K0..K9   = 知識の梯子の段 (同じシードの対で並べる)
  R_*      = 参考の段 (Naive・Optimal の既定)。 引きの質 (結果の版) の探り役にも使う
  A_*      = Super のアブレーション
  P0 / P12 = Super のパネルの点数 0 / 12 (24 は S)

測定の条件 (2026-11-18〜): 嵐あり + 嵐を読む BOT (T2) が AutoRunner の既定 (GAME.md §13-5)。 腕の実効は「腕と実効状態」の policySig (storm= / snav=) で読む。

読むもの: 各腕の round_000/c*/learn_runs.json (ラン)・response.json (実効状態・学習の読み)・worker.log (Item not found)。
書くもの (出力フォルダ):
  summary.md                 表 (条件・n・単位・信頼区間・検定)
  ore_candidates_<腕>.csv    蹂躙の候補 1 ラン 1 行
  luck_scores.csv            引きの質 (棚の版・結果の版) 1 シード 1 行

単位の決まり: 「クリア率」は %、 差は「クリア率の差 (百分率ポイント)」、 ITT の値は「ITT の Δクリア率 (百分率ポイント・Optimal の表)」。
"""
import csv
import glob
import gzip
import io
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict

CLEAR_BAND = 11
# 深さ (Run.Depth) → 画面の層 (currentFloor)。 2026-11-17 の記録 (shownFloor) で確かめた対応。
SHOWN = {0: 0, 1: 1, 3: 2, 5: 3, 6: 4, 7: 5}
AXES = ['会心', '出血', '毒', '臨界', '充電', 'シールド', '山札', '役']   # AxisRules.Ranked の順 (2026-11-20 に 6 → 8)


def boss_label(bk):
    d = int(bk.replace('boss_d', ''))
    return f'第{SHOWN.get(d, "?")}層 (深さ {d}) のボス'


# ---------------------------------------------------------------- 読み込み
def load_arm(d):
    runs = {}
    eff, reads, notfound, fails = set(), set(), 0, 0
    for c in sorted(glob.glob(os.path.join(d, 'round_000', 'c*'))):
        p = os.path.join(c, 'learn_runs.json')
        if os.path.exists(p):
            with io.open(p, encoding='utf-8-sig') as f:
                js = json.load(f)
        elif os.path.exists(p + '.gz'):   # 空きを作るために圧縮した記録 (2026-11-17)
            with gzip.open(p + '.gz', 'rt', encoding='utf-8-sig') as f:
                js = json.load(f)
        else:
            fails += 1
            continue
        for r in js.get('runs', []):
            runs[r['index']] = r
        rp = os.path.join(c, 'response.json')
        if os.path.exists(rp):
            with io.open(rp, encoding='utf-8-sig') as f:
                resp = json.load(f)
            eff.add(resp.get('effectiveState', ''))
            reads.add(resp.get('learningRead', '') + (' (手書きの序列)' if resp.get('learningFallback') else ''))
        wl = os.path.join(c, 'worker.log')
        if os.path.exists(wl):
            with io.open(wl, encoding='utf-8', errors='replace') as f:
                notfound += sum(1 for ln in f if 'Item not found' in ln)
    cfg = ''
    cp = os.path.join(d, 'config.txt')
    if os.path.exists(cp):
        cfg = io.open(cp, encoding='utf-8-sig').read()
    return {'dir': d, 'runs': runs, 'eff': eff, 'reads': reads, 'notfound': notfound, 'missingChunks': fails, 'config': cfg}


def is_clear(r):
    return r.get('bandScore', -1) >= CLEAR_BAND


# 画面の層ごとの死亡率 (2026-11-18): (画面の層, 死んだ帯, 着いた帯の下限, §13-5 の目標 (下限, 上限)・深さで読む, 名前)
LAYERS = [
    (1, (1,), 1, (0.0, 0.01), '第 1 層 (深さ 0・1)'),
    (2, (2, 3), 2, (0.12, 0.15), '第 2 層 (深さ 3)'),
    (3, (6, 7), 6, (0.20, 0.25), '第 3 層 (深さ 5)'),
    (4, (9,), 9, (0.25, 0.30), '第 4 層 (深さ 6)'),
    (5, (10,), 10, (0.45, 0.50), '第 5 層 (深さ 7・ヴェスカ)'),
]


def death_cause(r):
    """死んだランの死因 (Captures/storm の段階 3-0 と同じ分け方)。"""
    if r.get('deathInBossFight'):
        return 'boss'
    fs = r.get('fights', [])
    if fs and not fs[-1].get('won'):
        return 'road_in' if fs[-1].get('stormIn', -1) == 1 else 'road_out'
    return 'walk'


def _won_turns(rw, k):
    return rw.get(k + '_won') == 1 and rw.get(k + '_turns', '') != ''


def ore_a(rw):
    """蹂躙の候補 A: 第 3 層 (深さ 5) のボスを 5 ターン以内。"""
    return _won_turns(rw, 'boss_d5') and rw['boss_d5_turns'] <= 5


def ore_c(rw):
    """蹂躙の判定 (候補 C・作者 2026-11-18): 第 3 層のボス ≤5 ターン・第 4 層 (深さ 6) のボス ≤6 ターン・ヴェスカ 4 段の合計 ≤20 ターンのどれか。"""
    return (ore_a(rw) or (_won_turns(rw, 'boss_d6') and rw['boss_d6_turns'] <= 6)
            or (rw.get('vesca_won') == 1 and rw.get('vesca_turns', '') != '' and rw['vesca_turns'] <= 20))


# ---------------------------------------------------------------- 統計
def wilson(k, n, z=1.96):
    if n == 0:
        return (float('nan'),) * 3
    p = k / n
    den = 1 + z * z / n
    c = (p + z * z / (2 * n)) / den
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / den
    return p, c - h, c + h


def pct(k, n):
    p, lo, hi = wilson(k, n)
    if n == 0:
        return 'n=0'
    return f'{100 * p:.1f}% [{100 * lo:.1f}, {100 * hi:.1f}] ({k}/{n})'


def binom_two_sided(b, c):
    """McNemar の正確検定 (二項・両側)。"""
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    # P(X <= k) * 2 (X ~ Bin(n, 0.5))
    logp = []
    for i in range(0, k + 1):
        logp.append(math.lgamma(n + 1) - math.lgamma(i + 1) - math.lgamma(n - i + 1) - n * math.log(2))
    m = max(logp)
    s = sum(math.exp(x - m) for x in logp)
    return min(1.0, 2 * math.exp(m) * s)


def paired(a_runs, b_runs, idx):
    """同じシードの対: b = A だけクリア / c = B だけクリア。"""
    b = c = both = 0
    for i in idx:
        ca, cb = is_clear(a_runs[i]), is_clear(b_runs[i])
        if ca and not cb:
            b += 1
        elif cb and not ca:
            c += 1
        elif ca and cb:
            both += 1
    return b, c, both


def paired_line(b, c):
    p = binom_two_sided(b, c)
    z = (c - b) / math.sqrt(b + c) if b + c > 0 else 0.0
    orr = (c + 0.5) / (b + 0.5)
    return f'不一致 {b}対{c} (z {z:+.2f}・正確 p={p:.2g}・対のオッズ比 {orr:.2f})'


# ---------------------------------------------------------------- 蹂躙の候補
def boss_rows(r):
    """ボス戦の段の記録 (Vesca は段ごと)。"""
    out = []
    for f in r.get('fights', []):
        eid = f.get('enemyId', '')
        if not eid.startswith('boss_layer'):
            continue
        out.append(f)
    return out


def ore_candidates(r):
    row = {'index': r['index'], 'clear': int(is_clear(r)), 'band': r.get('bandScore', -1),
           'class': r.get('startClass', ''), 'reached': r.get('reachedFloor', 0)}
    vesca_turns = 0
    vesca_won = False
    vesca_seen = False
    best_frac = 0.0
    best_frac_at = ''
    for f in boss_rows(r):
        eid = f['enemyId']
        mh = max(1, f.get('enemyMaxHp', 0))
        frac_req = f.get('maxTurnRequested', 0) / mh
        frac_land = f.get('maxTurnLanded', 0) / mh
        if eid.startswith('boss_layer7'):
            vesca_seen = True
            vesca_turns += f.get('turns', 0)
            if f.get('end') and f.get('won'):
                vesca_won = True
            key = 'vesca_' + eid.replace('boss_layer7', 'p1').replace('p1_', '')
            row[key + '_frac'] = round(frac_req, 4)
        else:
            if not f.get('end'):
                continue
            key = f'boss_d{f.get("floor", 0)}'
            row[key + '_id'] = eid
            row[key + '_turns'] = f.get('turns', 0) if f.get('won') else ''
            row[key + '_won'] = int(bool(f.get('won')))
            row[key + '_frac'] = round(frac_req, 4)
            row[key + '_fracLanded'] = round(frac_land, 4)
            row[key + '_taken'] = round(max(0, f.get('hpBefore', 0) - f.get('hpAfter', 0)) / max(1, f.get('maxHp', 1)), 4)
        if f.get('won') and frac_req > best_frac:
            best_frac, best_frac_at = frac_req, eid
    if vesca_seen:
        row['vesca_turns'] = vesca_turns if vesca_won else ''
        row['vesca_won'] = int(vesca_won)
    row['maxBossFrac'] = round(best_frac, 4)
    row['maxBossFracAt'] = best_frac_at
    # 後半 (深さ 5 以上) の戦闘で失った HP (最大 HP に対する割合の合計・戦闘ごとの最大)
    late = [f for f in r.get('fights', []) if f.get('floor', 0) >= 5 and f.get('end')]
    losses = [max(0, f.get('hpBefore', 0) - f.get('hpAfter', 0)) / max(1, f.get('maxHp', 1)) for f in late]
    row['late_fights'] = len(late)
    row['late_lossSum'] = round(sum(losses), 4)
    row['late_lossMax'] = round(max(losses), 4) if losses else ''
    row['late_noDmgShare'] = round(sum(1 for x in losses if x <= 0.05) / len(losses), 4) if losses else ''
    return row


def quantiles(vals, qs=(0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)):
    v = sorted(vals)
    if not v:
        return {}
    out = {}
    for q in qs:
        i = min(len(v) - 1, max(0, int(round(q * (len(v) - 1)))))
        out[q] = v[i]
    return out


# ---------------------------------------------------------------- 引きの質
def load_itt(path):
    with io.open(path, encoding='utf-8-sig') as f:
        js = json.load(f)
    return {it['id']: it['dClearPt'] for it in js.get('items', [])}, js.get('baselineClear')


def parse_shelf(s):
    depth, _, rest = s.partition('|')
    items = []
    for tok in rest.split(';'):
        if not tok:
            continue
        parts = tok.split(':')
        if len(parts) < 3:
            continue
        kind, iid, price = parts[0], ':'.join(parts[1:-1]), parts[-1]
        items.append((int(kind), iid, int(price)))
    return int(depth or 0), items


def shelf_luck(r, itt, nshelves=3, top=2):
    """最初の nshelves 回の棚: 棚ごとに ITT の値の上位 top 品の和 (負は 0)。 価格は見ない (判断に依らない値だけ)。"""
    sh = r.get('shelves', [])[:nshelves]
    total, missing, seen = 0.0, 0, 0
    for s in sh:
        _, items = parse_shelf(s)
        vals = []
        for _, iid, _ in items:
            if iid in itt:
                vals.append(itt[iid])
            else:
                missing += 1
            seen += 1
        vals.sort(reverse=True)
        total += sum(max(0.0, v) for v in vals[:top])
    return total, len(sh), missing, seen


def hash_tiebreak(i):
    return ((i * 2654435761) & 0xFFFFFFFF) / 2 ** 32


def bottom_quartile(score):
    keys = sorted(score, key=lambda i: (score[i], hash_tiebreak(i)))
    n = len(keys) // 4
    return set(keys[:n]), set(keys[-n:])


def auc(score, runs, idx):
    pos = [score[i] for i in idx if is_clear(runs[i])]
    neg = [score[i] for i in idx if not is_clear(runs[i])]
    if not pos or not neg:
        return float('nan')
    allv = sorted([(v, 1) for v in pos] + [(v, 0) for v in neg])
    # 順位和 (同点は平均順位)
    ranks = {}
    i = 0
    rank_sum = 0.0
    while i < len(allv):
        j = i
        while j < len(allv) and allv[j][0] == allv[i][0]:
            j += 1
        avg = (i + j + 1) / 2.0
        rank_sum += avg * sum(1 for k in range(i, j) if allv[k][1] == 1)
        i = j
    return (rank_sum - len(pos) * (len(pos) + 1) / 2) / (len(pos) * len(neg))


# ---------------------------------------------------------------- 蹂躙の回の中身
def boss_end_rows(r):
    return [f for f in r.get('fights', []) if f.get('enemyId', '').startswith('boss_layer') and f.get('end')
            and not f['enemyId'].startswith('boss_layer7')]


def ore_contents(runs, P):
    """候補の判定に入ったボス戦と、 同じ深さのボスに勝った残りの戦闘を比べる (勝った戦闘だけが母集団 = 生存の偏りを揃える)。"""
    P('## 蹂躙の回の中身 (腕 S・同じ深さのボスに勝った戦闘どうしで比べる)\n')
    P('lift = (判定に入った戦闘でその品を持っていた割合) ÷ (同じ深さのボスに勝った残りの戦闘で持っていた割合)。 持ち物はボス戦の開始時。'
      ' 持ち物の数が群で違うと lift は数の差を拾うので、 平均の持ち物の数を並べる。\n')
    defs = [
        ('第3層 (深さ 5) のボスを 5 ターン以内', 5, lambda f: f.get('turns', 99) <= 5),
        ('第3層 (深さ 5) のボスに 1 ターンで最大 HP の 35% 以上', 5, lambda f: f.get('maxTurnRequested', 0) >= 0.35 * max(1, f.get('enemyMaxHp', 1))),
        ('第2層 (深さ 3) のボスを 3 ターン以内', 3, lambda f: f.get('turns', 99) <= 3),
        ('第4層 (深さ 6) のボスを 8 ターン以内', 6, lambda f: f.get('turns', 99) <= 8),
    ]
    for name, depth, pred in defs:
        hit, rest = [], []
        for r in runs.values():
            for f in boss_end_rows(r):
                if not f.get('won') or f.get('floor') != depth:
                    continue
                (hit if pred(f) else rest).append((r, f))
        if not hit:
            P(f'### {name}: 該当なし\n')
            continue
        P(f'### {name}\n')
        bosses = Counter(f['enemyId'] for _, f in hit)
        P(f'該当 {len(hit)} 戦 ({", ".join(f"{k}×{v}" for k, v in bosses.most_common())}) / 比べる相手 {len(rest)} 戦。'
          f' 該当のランのクリア {pct(sum(is_clear(r) for r, _ in hit), len(hit))}・相手 {pct(sum(is_clear(r) for r, _ in rest), len(rest))}\n')
        def mean_held(g):
            return sum(len([x for x in f.get('held', []) if not x.startswith('装備:')]) for _, f in g) / max(1, len(g))
        def mean_axis(g, k):
            return sum((list(f.get('axisAtStart') or []) + [0] * len(AXES))[k] for _, f in g) / max(1, len(g))   # 6 軸の古い記録は足りない分を 0
        P(f'平均の持ち物の数: 該当 {mean_held(hit):.1f} / 相手 {mean_held(rest):.1f}\n')
        P('| 軸 (戦闘の開始時の品の数) | ' + ' | '.join(AXES) + ' |')
        P('|---|' + '---|' * len(AXES))
        P('| 該当 | ' + ' | '.join(f'{mean_axis(hit, k):.2f}' for k in range(len(AXES))) + ' |')
        P('| 相手 | ' + ' | '.join(f'{mean_axis(rest, k):.2f}' for k in range(len(AXES))) + ' |')
        P('')
        ch, cr = Counter(), Counter()
        for _, f in hit:
            ch.update(set(f.get('held', [])))
        for _, f in rest:
            cr.update(set(f.get('held', [])))
        rows = []
        for k, v in ch.items():
            if v < max(10, len(hit) // 20):
                continue
            ph, pr = v / len(hit), (cr.get(k, 0) + 0.5) / (len(rest) + 1)
            rows.append((ph / pr, k, ph, pr))
        rows.sort(reverse=True)
        P('| 品 | 該当で持っていた割合 | 相手で持っていた割合 | lift |')
        P('|---|---|---|---|')
        for lf, k, ph, pr in rows[:15]:
            P(f'| {k} | {100 * ph:.0f}% | {100 * pr:.0f}% | {lf:.2f} |')
        P('')


# ---------------------------------------------------------------- 本体
def main():
    spec, outdir = sys.argv[1], sys.argv[2]
    itt_path = sys.argv[3] if len(sys.argv) > 3 else os.path.join('AutoRunLogs', 'learning', 'buffOn_debuffOff', 'itt_clear.super.json')
    os.makedirs(outdir, exist_ok=True)
    arms = {}
    for ln in io.open(spec, encoding='utf-8-sig'):
        ln = ln.strip()
        if not ln or ln.startswith('#'):
            continue
        label, d = ln.split('\t')
        arms[label] = load_arm(d)
    out = []
    P = out.append

    # ---- 腕の条件 (実効状態)
    P('# 体験の物差し: 集計 (Tools/experience_report.py)\n')
    P('## 腕と実効状態\n')
    P('| 腕 | フォルダ | ラン数 | policySig (ランの記録・Begin の後) | パネル (metaRanks) | 学習の読み | Item not found | 欠けたチャンク |')
    P('|---|---|---|---|---|---|---|---|')
    for lab, a in arms.items():
        sig = Counter(r.get('policySig', '') for r in a['runs'].values())
        ranks = Counter(r.get('metaRanks', '') for r in a['runs'].values())
        sig_s = ' / '.join(f'{k} ×{v}' for k, v in sig.most_common(3))
        rk_s = ' / '.join(f'{k or "(0 点)"} ×{v}' for k, v in ranks.most_common(2))
        P(f'| {lab} | {os.path.basename(a["dir"])} | {len(a["runs"])} | `{sig_s}` | {rk_s} | {" ; ".join(sorted(a["reads"]))} | {a["notfound"]} | {a["missingChunks"]} |')
    P('')

    common = None
    for lab, a in arms.items():
        if lab.startswith('A_') or re.match(r'^P\d', lab):
            continue   # アブレーション・パネルはラン数が梯子と違うことがあるので、 梯子の共通のシードを狭めない (対は腕ごとに取る)
        s = set(a['runs'])
        common = s if common is None else common & s
    common = sorted(common or [])
    P(f'全腕に共通するシード: {len(common)} 本\n')

    # ---- クリア率
    P('## クリア率と蹂躙率 (全ラン・難易度 0・Wilson 95%)\n')
    P('蹂躙の判定は候補 C (作者 2026-11-18): 第 3 層 (深さ 5) のボスを 5 ターン以内・第 4 層 (深さ 6) のボスを 6 ターン以内・ヴェスカ 4 段を合計 20 ターン以内のどれか。 候補 A = 第 3 層のボスを 5 ターン以内だけ。 分母は全ラン。\n')
    P('| 腕 | クリア率 | 蹂躙 (候補 C) | うちクリア | 候補 A |')
    P('|---|---|---|---|---|')
    for lab, a in arms.items():
        rs = list(a['runs'].values())
        rows_ = [ore_candidates(r) for r in rs]
        hc = [rw for rw in rows_ if ore_c(rw)]
        ha = [rw for rw in rows_ if ore_a(rw)]
        P(f'| {lab} | {pct(sum(is_clear(r) for r in rs), len(rs))} | {pct(len(hc), len(rs))} | {pct(sum(rw["clear"] for rw in hc), len(hc))} | {pct(len(ha), len(rs))} |')
    P('')

    # ---- 層ごとの死亡率 (到達者あたり・§13-5 の目標と並べる・2026-11-18)
    P('## 層ごとの死亡率 (その層に着いたランのうち、 その層で終わった割合・§13-5)\n')
    P('目標は §13-5「到達者のうちその層で死ぬ割合」を深さで読んだ値 (深さ 1 ≤1% / 3 12〜15% / 5 20〜25% / 6 25〜30% / 7 (ヴェスカ) 45〜50%・深さ 2・4 は 2026-09-28 に削除)。'
      ' 帯 (bandScore) で数える: 第 1 層 = 帯 1・第 2 層 = 2〜3・第 3 層 = 6〜7・第 4 層 = 9・第 5 層 = 10。 帯 8 (第 3 層を倒して〈決意〉なしで終了) は死に数えず、 次の層へも着かない。\n')
    P('| 腕 | ' + ' | '.join((f'{nm} ({lo * 100:.0f}〜{hi * 100:.0f}%)' if lo else f'{nm} (≤{hi * 100:.0f}%)') for _, _, _, (lo, hi), nm in LAYERS) + ' | 帯 8 |')
    P('|---|' + '---|' * (len(LAYERS) + 1))
    for lab, a in arms.items():
        rs = [r for r in a['runs'].values() if r.get('bandScore', -1) >= 1]
        cells = []
        for _, dead_b, reach_b, (lo, hi), _ in LAYERS:
            reach = sum(1 for r in rs if r['bandScore'] >= reach_b)
            dead = sum(1 for r in rs if r['bandScore'] in dead_b)
            if not reach:
                cells.append('—')
                continue
            rate = dead / reach
            mark = '' if lo <= rate <= hi else '*'
            cells.append(f'{100 * rate:.1f}%{mark} ({dead}/{reach})')
        P(f'| {lab} | ' + ' | '.join(cells) + f' | {sum(1 for r in rs if r["bandScore"] == 8)} |')
    P('')
    P('`*` = 目標の外。 深い層ほど着いた集団が選ばれている (§13-5 の「条件付きであることの注意」)。\n')
    P('### 死んだランの死因 (層ごと・腕 S と参考の段)\n')
    P('道中の戦闘は、 その戦闘が嵐の中で始まったか (敵に〈嵐〉) で分ける。 「歩いて尽きた」= 最後の戦闘に勝った後に、 嵐の HP か物資切れで HP が尽きた。\n')
    P('| 腕 | 層 | 死 | ボス戦 | 道中の戦闘 (嵐の外) | 道中の戦闘 (嵐の中) | 歩いて尽きた |')
    P('|---|---|---|---|---|---|---|')
    for lab, a in arms.items():
        if not (lab == 'S' or lab.startswith('R_')):
            continue
        rs = [r for r in a['runs'].values() if r.get('bandScore', -1) >= 1]
        for _, dead_b, _, _, nm in LAYERS:
            c = Counter(death_cause(r) for r in rs if r['bandScore'] in dead_b)
            n = sum(c.values())
            if not n:
                continue
            P(f'| {lab} | {nm} | {n} | ' + ' | '.join(f'{c[k]} ({100 * c[k] / n:.0f}%)' for k in ('boss', 'road_out', 'road_in', 'walk')) + ' |')
    P('')

    # ---- 蹂躙の候補
    if 'S' in arms:
        for lab in [l for l in arms if l == 'S' or l.startswith('P')]:
            a = arms[lab]
            rows = [ore_candidates(r) for r in a['runs'].values()]
            keys = []
            for rw in rows:
                for k in rw:
                    if k not in keys:
                        keys.append(k)
            with io.open(os.path.join(outdir, f'ore_candidates_{lab}.csv'), 'w', encoding='utf-8-sig', newline='') as f:
                w = csv.DictWriter(f, fieldnames=keys)
                w.writeheader()
                for rw in rows:
                    w.writerow(rw)
        rows = [ore_candidates(r) for r in arms['S']['runs'].values()]
        P('## 蹂躙の候補の分布 (腕 S = Super・難易度 0・パネル 24 点)\n')
        P('ボスは深さ (Run.Depth) で書く。 ターン数は勝った戦闘だけ。 「1 ターンの最大」= 1 ターンに要求した与ダメ (はみ出しの前) ÷ ボスの最大 HP。\n')
        bkeys = sorted({k[:-6] for rw in rows for k in rw if k.startswith('boss_d') and k.endswith('_turns')})
        P('| 候補 | n (戦った / 勝った) | 10% | 25% | 50% | 75% | 90% | 95% | 99% |')
        P('|---|---|---|---|---|---|---|---|---|')
        for bk in bkeys:
            fought = [rw for rw in rows if bk + '_won' in rw]
            won = [rw for rw in fought if rw[bk + '_won'] == 1]
            ids = Counter(rw[bk + '_id'] for rw in fought)
            qt = quantiles([rw[bk + '_turns'] for rw in won])
            P(f'| {boss_label(bk)} のターン数 ({", ".join(f"{k}×{v}" for k, v in ids.most_common())}) | {len(fought)} / {len(won)} | ' + ' | '.join(str(qt.get(q, '')) for q in (0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)) + ' |')
            qf = quantiles([rw[bk + '_frac'] for rw in won])
            P(f'| {boss_label(bk)} の 1 ターンの最大 (最大 HP 比) | {len(fought)} / {len(won)} | ' + ' | '.join(f'{qf.get(q, 0):.3f}' for q in (0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)) + ' |')
            qk = quantiles([rw[bk + '_taken'] for rw in won])
            P(f'| {boss_label(bk)} で失った HP (最大 HP 比・勝った戦闘) | {len(fought)} / {len(won)} | ' + ' | '.join(f'{qk.get(q, 0):.3f}' for q in (0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)) + ' |')
        vs = [rw for rw in rows if rw.get('vesca_won') == 1]
        qv = quantiles([rw['vesca_turns'] for rw in vs])
        P(f'| ヴェスカ (4 段) のターン数の合計 | {sum(1 for rw in rows if "vesca_won" in rw)} / {len(vs)} | ' + ' | '.join(str(qv.get(q, '')) for q in (0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)) + ' |')
        lf = [rw for rw in rows if rw['late_fights'] > 0]
        ql = quantiles([rw['late_lossSum'] for rw in lf])
        P(f'| 後半 (深さ 5 以上) の戦闘で失った HP の合計 (最大 HP 比・戦闘の和) | {len(lf)} | ' + ' | '.join(f'{ql.get(q, 0):.2f}' for q in (0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)) + ' |')
        lf3 = [rw for rw in lf if rw['late_fights'] >= 3]
        qn = quantiles([rw['late_noDmgShare'] for rw in lf3])
        P(f'| 後半の戦闘のうち失った HP ≤ 5% の割合 (後半 3 戦以上のラン) | {len(lf3)} | ' + ' | '.join(f'{qn.get(q, 0):.2f}' for q in (0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 0.99)) + ' |')
        P('')

        # しきい値の候補: 絶対の基準ごとの該当率 (全ランが分母)
        P('### 絶対のしきい値ごとの該当率 (分母 = 全ラン・腕 S)\n')
        N = len(rows)
        P('| 判定 | 該当 | うちクリア |')
        P('|---|---|---|')
        for bk in bkeys:
            for t in (3, 4, 5):
                hit = [rw for rw in rows if rw.get(bk + '_won') == 1 and rw[bk + '_turns'] != '' and rw[bk + '_turns'] <= t]
                P(f'| {boss_label(bk)} を {t} ターン以内で倒した | {pct(len(hit), N)} | {pct(sum(rw["clear"] for rw in hit), len(hit))} |')
            for fr in (0.25, 0.5, 1.0):
                hit = [rw for rw in rows if rw.get(bk + '_won') == 1 and rw[bk + '_frac'] >= fr]
                P(f'| {boss_label(bk)} に 1 ターンで最大 HP の {int(fr * 100)}% 以上 | {pct(len(hit), N)} | {pct(sum(rw["clear"] for rw in hit), len(hit))} |')
        for t in (12, 16, 20):
            hit = [rw for rw in rows if rw.get('vesca_won') == 1 and rw['vesca_turns'] <= t]
            P(f'| ヴェスカ (4 段の合計) を {t} ターン以内 | {pct(len(hit), N)} | — |')
        for s in (0.1, 0.25, 0.5):
            hit = [rw for rw in rows if rw['late_fights'] >= 3 and rw['late_lossSum'] <= s]
            P(f'| 後半 3 戦以上で失った HP の合計 ≤ 最大 HP の {int(s * 100)}% | {pct(len(hit), N)} | {pct(sum(rw["clear"] for rw in hit), len(hit))} |')
        P('')

    # ---- 引きの質
    itt, base = load_itt(itt_path) if os.path.exists(itt_path) else ({}, None)
    luck = {}
    if itt and common:
        # 棚の版: 探り役の腕の棚から (棚が腕を通して同じかを先に数える)
        probe_lab = 'S' if 'S' in arms else list(arms)[0]
        P('## 引きの質の点数\n')
        P(f'ITT の表: `{itt_path}` ({len(itt)} 品・基準クリア {base})。\n')
        # 棚の一致
        P('### 棚は判断に依らないか (同じシードで、 腕どうしの n 番目の棚が文字列で同じ割合)\n')
        P('| 腕 | 1 番目 | 2 番目 | 3 番目 |')
        P('|---|---|---|---|')
        ref = arms[probe_lab]['runs']
        for lab, a in arms.items():
            if lab == probe_lab:
                continue
            cells = []
            for k in range(3):
                same = tot = 0
                for i in [j for j in common if j in a["runs"]]:
                    sa, sb = ref[i].get('shelves', []), a['runs'][i].get('shelves', [])
                    if len(sa) > k and len(sb) > k:
                        tot += 1
                        same += sa[k] == sb[k]
                cells.append(f'{100 * same / max(1, tot):.1f}% (n={tot})')
            P(f'| {lab} 対 {probe_lab} | ' + ' | '.join(cells) + ' |')
        P('')
        sc_shelf, miss, seen = {}, 0, 0
        for i in common:
            v, ns, m, s = shelf_luck(ref[i], itt, nshelves=1, top=3)
            sc_shelf[i] = v
            miss += m
            seen += s
        P(f'棚の版 L_棚 = 最初の棚 (どの腕でも同じ = 判断に依らないのは 1 番目だけ・上の表) の ITT の値の上位 3 品の和 (負は 0) を足した値。 単位は ITT の Δクリア率 (百分率ポイント)。 ITT に無い品 {miss}/{seen} 枠は 0。\n')
        # 結果の版: 参考の段 (R_*) のバンドの平均
        probes = [l for l in arms if l.startswith('R_')]
        sc_out = {}
        if probes:
            for i in common:
                sc_out[i] = sum(arms[l]['runs'][i].get('bandScore', 0) for l in probes) / len(probes) + 0.01 * sc_shelf.get(i, 0) / 100
        luck = {'棚': sc_shelf}
        if sc_out:
            luck['結果(' + '+'.join(probes) + ')'] = sc_out
        with io.open(os.path.join(outdir, 'luck_scores.csv'), 'w', encoding='utf-8-sig', newline='') as f:
            w = csv.writer(f)
            w.writerow(['index', 'L_shelf', 'L_outcome', 'class'])
            for i in common:
                w.writerow([i, round(sc_shelf[i], 3), round(sc_out.get(i, float('nan')), 3), ref[i].get('startClass', '')])

        P('### 引きの質がクリアをどれだけ言い当てるか (AUC・0.5 = 当たらない)\n')
        P('| 腕 | ' + ' | '.join(luck) + ' |')
        P('|---|' + '---|' * len(luck))
        for lab, a in arms.items():
            P(f'| {lab} | ' + ' | '.join(f'{auc(sc, a["runs"], [j for j in common if j in a["runs"]]):.3f}' for sc in luck.values()) + ' |')
        P('')

        # ---- 知識の関与
        ladder = [l for l in arms if re.match(r'^K\d', l)] + [l for l in arms if l.startswith('R_')] + (['S'] if 'S' in arms else [])
        k0 = 'K0' if 'K0' in arms else None
        for name, sc in luck.items():
            lo, hi = bottom_quartile(sc)
            P(f'## 知識の関与 (引きの質 = {name}・下位 1/4 = {len(lo)} シード・上位 1/4 = {len(hi)} シード)\n')
            P('| 段 | 全体 | 下位 1/4 | 上位 1/4 | 下位 ÷ 全体 | 下位で K0 との対 |')
            P('|---|---|---|---|---|---|')
            for lab in ladder:
                rs = arms[lab]['runs']
                allk = sum(is_clear(rs[i]) for i in common)
                lok = sum(is_clear(rs[i]) for i in lo)
                hik = sum(is_clear(rs[i]) for i in hi)
                ratio = (lok / len(lo)) / (allk / len(common)) if allk else float('nan')
                pl = ''
                if k0 and lab != k0:
                    b, c, _ = paired(arms[k0]['runs'], rs, sorted(lo))
                    pl = paired_line(b, c)
                P(f'| {lab} | {pct(allk, len(common))} | {pct(lok, len(lo))} | {pct(hik, len(hi))} | {ratio:.2f} | {pl} |')
            P('')
            # 職業の内訳 (判断に依らない引き)
            cls = Counter(arms[ladder[0]]['runs'][i].get('startClass', '') for i in lo)
            P('下位 1/4 の職業: ' + ' / '.join(f'{k} {v}' for k, v in cls.most_common()) + '\n')

        # ---- 結果の版・取り除き: 比べる腕を除いた全段のバンドの平均を引きの質にする (自分の結果で自分の層を選ばない)
        pool = list(ladder)
        if len(pool) >= 3:
            def loo(excl):
                use = [l for l in pool if l not in excl]
                return {i: sum(arms[l]['runs'][i].get('bandScore', 0) for l in use) / len(use) for i in common}, use
            P('## 知識の関与 (引きの質 = 比べる腕を除いた全段のバンドの平均・下位 1/4)\n')
            P('| 段 | 下位 1/4 のクリア率 | 全体 | 下位 ÷ 全体 | 引きの質に使った腕 |')
            P('|---|---|---|---|---|')
            for lab in pool:
                sc, use = loo({lab})
                lo, _ = bottom_quartile(sc)
                rs = arms[lab]['runs']
                allk = sum(is_clear(rs[i]) for i in common)
                lok = sum(is_clear(rs[i]) for i in lo)
                ratio = (lok / len(lo)) / (allk / len(common)) if allk else float('nan')
                P(f'| {lab} | {pct(lok, len(lo))} | {pct(allk, len(common))} | {ratio:.2f} | {len(use)} 腕 |')
            P('')
            P('| 対 (A 対 B) | 下位 1/4 の A | 下位 1/4 の B | B ÷ A | 対 |')
            P('|---|---|---|---|---|')
            pairs = [('K0', 'K6'), ('K0', 'S'), ('K6', 'S'), ('K0', 'K3'), ('K3', 'K6'), ('R_Naive', 'S'), ('R_Optimal', 'S'), ('K0', 'R_Optimal'),
                     ('R_Naive', 'K3'), ('R_Naive', 'K6'), ('R_Naive', 'K7'), ('K6', 'K7'), ('K7', 'S')]
            for a_, b_ in pairs:
                if a_ not in arms or b_ not in arms:
                    continue
                sc, _ = loo({a_, b_})
                lo, _ = bottom_quartile(sc)
                ka = sum(is_clear(arms[a_]['runs'][i]) for i in lo)
                kb = sum(is_clear(arms[b_]['runs'][i]) for i in lo)
                b, c, _ = paired(arms[a_]['runs'], arms[b_]['runs'], sorted(lo))
                rr = f'{kb / ka:.2f}' if ka else '∞'
                P(f'| {a_} 対 {b_} | {pct(ka, len(lo))} | {pct(kb, len(lo))} | {rr} | {paired_line(b, c)} |')
            P('')

    # ---- アブレーション
    if 'S' in arms:
        abl = [l for l in arms if l.startswith('A_')]
        if abl:
            P('## どの判断が効いているか (Super からの落ち幅・同じシードの対)\n')
            P('| 腕 | クリア率 | クリア率の差 (百分率ポイント) | 対 |')
            P('|---|---|---|---|')
            s = arms['S']['runs']
            for lab in abl:
                a = arms[lab]['runs']
                idx = sorted(set(a) & set(s))
                ks, ka = sum(is_clear(s[i]) for i in idx), sum(is_clear(a[i]) for i in idx)
                b, c, _ = paired(s, a, idx)
                P(f'| {lab} | {pct(ka, len(idx))} (S は {100 * ks / len(idx):.1f}%) | {100 * (ka - ks) / len(idx):+.1f} | {paired_line(b, c)} |')
            P('')
        pan = [l for l in arms if l.startswith('P')]
        if pan:
            P('## 整備パネルの伸び方 (Super・難易度 0)\n')
            P('| 点数 | クリア率 | 実際の段 (metaRanks) | S (24 点) との対 |')
            P('|---|---|---|---|')
            s = arms['S']['runs']
            for lab in sorted(pan, key=lambda l: int(l[1:])) + ['S']:
                a = arms[lab]['runs']
                idx = sorted(set(a) & set(s))
                rk = Counter(r.get('metaRanks', '') for r in a.values()).most_common(1)[0][0]
                ka = sum(is_clear(a[i]) for i in idx)
                pl = '' if lab == 'S' else paired_line(*paired(s, a, idx)[:2])
                P(f'| {lab if lab != "S" else "P24 (S)"} | {pct(ka, len(idx))} | `{rk or "(0 点)"}` | {pl} |')
            P('')

    if 'S' in arms:
        ore_contents(arms['S']['runs'], P)
    io.open(os.path.join(outdir, 'summary.md'), 'w', encoding='utf-8').write('\n'.join(out) + '\n')
    print('書いた: ' + os.path.join(outdir, 'summary.md'))


if __name__ == '__main__':
    main()
