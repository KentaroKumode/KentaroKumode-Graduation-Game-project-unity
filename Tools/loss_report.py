"""どこで・なぜ負けているかを切り分ける (learn_runs.json / response.json)。
使い方: python -X utf8 Tools/loss_report.py <実行ディレクトリ> [...]

負けた戦闘 (ランの最後の戦闘) を次の順で 1 つに分類する:
  持ち込み  開始 HP < 40%      その戦闘より前の削れ・回復不足
  瞬殺      開始 HP ≥ 60% かつ 4 ターン以内    防御不足 / 大技・固定ダメ
  接戦      敵の残り HP < 25%    もう少しで勝てた
  火力不足  敵の残り HP ≥ 50%    削り切れない (DPS 不足)
  消耗      それ以外              じわじわ負けた
分類は戦闘の数字からの推定で、 死因そのものの記録ではない。"""
import glob
import json
import os
import statistics as st
import sys
from collections import Counter, defaultdict

DEPTHS = (1, 3, 5, 6, 7)
ITEMS = {e["id"]: e for e in json.load(open("Assets/Data/InventorySystem/items.json", encoding="utf-8-sig"))["items"]}
HEALS = {k for k, e in ITEMS.items() if e.get("category") == "Consumable" and "回復" in e.get("description", "")}


def load(d):
    if not os.path.isdir(d):
        d = os.path.join("AutoRunLogs/learn_parallel", d)
    R, resp = [], []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        R += j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
    for p in glob.glob(os.path.join(d, "round_000", "c*", "response.json")):
        resp.append(json.load(open(p, encoding="utf-8")))
    return R, resp


def kind(f):
    if f.get("isBoss"):
        return "ボス"
    k = (f.get("key") or "").split("|")
    return {"E": "精鋭", "N": "通常"}.get(k[1] if len(k) > 1 else "", "他")


def classify(f):
    m = max(1, f["maxHp"]); start = f["hpBefore"] / m; left = 1 - min(1, f["dealt"] / max(1, f["enemyMaxHp"]))
    if start < 0.4:
        return "持ち込み"
    if start >= 0.6 and f["turns"] <= 4:
        return "瞬殺"
    if left < 0.25:
        return "接戦"
    if left >= 0.5:
        return "火力不足"
    return "消耗"


def mean(v):
    v = list(v)
    return sum(v) / len(v) if v else 0


def med(v):
    v = list(v)
    return st.median(v) if v else 0


def report(d):
    R, resp = load(d)
    n = len(R); C = [r for r in R if r["deathFloor"] == 0]
    pas = lambda r: sum(1 for a in r.get("acquired") or [] if ITEMS.get(a, {}).get("category") == "Passive")
    print(f"\n===== {d}  クリア {100 * len(C) / n:.1f}%  (n={n})")

    # 1. 層の通過
    print("\n[1] 層ごと: 到達 / 死亡率 / ボス到達時の所持 (HP%・パッシブ・消耗品)")
    be = defaultdict(lambda: [0, 0, 0, 0])
    for j in resp:
        for dep in DEPTHS:
            be[dep][0] += j["bossEntryCount"][dep]; be[dep][1] += j["bossEntryHpPct"][dep]
            be[dep][2] += j["bossEntryPassives"][dep]; be[dep][3] += j["bossEntryConsumables"][dep]
    for dep in DEPTHS:
        reach = sum(1 for r in R if r["deathFloor"] == 0 or r["deathFloor"] >= dep)
        die = sum(1 for r in R if r["deathFloor"] == dep)
        c, h, p, k = be[dep]; c = max(1, c)
        print(f"  深さ{dep}: 到達 {100 * reach / n:5.1f}%  死亡率 {100 * die / max(1, reach):5.1f}%  ボス前 HP {h / c:4.0f}%  パッシブ {p / c:5.1f}  消耗品 {k / c:4.1f}")

    # 2. 負け方
    print("\n[2] 負けた戦闘の分類 (深さ × 種別 と 負け方)")
    L = [(r["deathFloor"], r["fights"][-1], r) for r in R if r["deathFloor"] and r.get("fights")]
    cl = Counter(); cell = Counter()
    for dep, f, r in L:
        c = classify(f); cl[c] += 1; cell[(dep, kind(f), c)] += 1
    names = ["持ち込み", "瞬殺", "接戦", "火力不足", "消耗"]
    print(f"  全体: " + " / ".join(f"{c} {100 * cl[c] / max(1, len(L)):.0f}%" for c in names) + f"  (死亡 {len(L)})")
    print(f"  {'深さ':>4} {'種別':<4}{'死亡':>6}" + "".join(f"{c:>8}" for c in names) + f"{'開始HP':>8}{'T':>5}{'敵残':>6}{'被/T%':>7}")
    groups = defaultdict(list)
    for dep, f, r in L:
        groups[(dep, kind(f))].append(f)
    for (dep, k), fs in sorted(groups.items()):
        print(f"  {dep:>4} {k:<4}{len(fs):>6}" + "".join(f"{100 * cell[(dep, k, c)] / len(fs):>7.0f}%" for c in names)
              + f"{100 * mean(f['hpBefore'] / max(1, f['maxHp']) for f in fs):>7.0f}%{med(f['turns'] for f in fs):>5.0f}"
              + f"{100 * mean(1 - min(1, f['dealt'] / max(1, f['enemyMaxHp'])) for f in fs):>5.0f}%"
              + f"{100 * mean(f['taken'] / max(1, f['turns']) / max(1, f['maxHp']) for f in fs):>6.1f}%")

    # 3. 死因の敵
    print("\n[3] 死因の敵 (上位)")
    e = defaultdict(list)
    for dep, f, r in L:
        e[(dep, f["enemyId"])].append(f)
    wins = defaultdict(list)
    for r in R:
        for f in r.get("fights") or []:
            if f.get("won"):
                wins[(f["floor"], f["enemyId"])].append(f)
    for (dep, eid), fs in sorted(e.items(), key=lambda x: -len(x[1]))[:14]:
        w = wins[(dep, eid)]; tot = len(w) + len(fs)
        cc = Counter(classify(f) for f in fs).most_common(1)[0][0]
        print(f"  深さ{dep} {eid:<26} 死亡 {len(fs):>4} (全体の {100 * len(fs) / max(1, len(L)):4.1f}%)  遭遇あたり {100 * len(fs) / max(1, tot):5.1f}%  多い負け方 {cc:<5}"
              f" 開始HP {100 * mean(f['hpBefore'] / max(1, f['maxHp']) for f in fs):3.0f}%  T {med(f['turns'] for f in fs):3.0f}  勝ち戦 T {med(f['turns'] for f in w):3.0f}・HP減 {100 * mean((f['hpBefore'] - f['hpAfter']) / max(1, f['maxHp']) for f in w):3.0f}%")

    # 4. 勝ち戦のコスト
    print("\n[4] 勝った戦闘のコスト (深さ × 種別): T 中央 / HP 減 / 与ダメ/T / 回数/ラン(クリア)")
    W = defaultdict(list)
    for r in R:
        for f in r.get("fights") or []:
            if f.get("won") and f.get("enemyMaxHp", 0) > 1:
                W[(f["floor"], kind(f))].append(f)
    cw = Counter()
    for r in C:
        for f in r.get("fights") or []:
            cw[(f["floor"], kind(f))] += 1
    for (dep, k), fs in sorted(W.items()):
        if len(fs) < 50 or dep == 0:
            continue
        print(f"  深さ{dep} {k:<4} T {med(f['turns'] for f in fs):4.0f}  HP減 {100 * mean((f['hpBefore'] - f['hpAfter']) / max(1, f['maxHp']) for f in fs):5.1f}%  与ダメ/T {med(f['dealt'] / max(1, f['turns']) for f in fs):6.0f}  敵HP {med(f['enemyMaxHp'] for f in fs):6.0f}  {cw[(dep, k)] / max(1, len(C)):5.1f}回")

    # 5. 資源
    print("\n[5] 資源 (死亡ラン vs クリア)")
    for label, S in (("クリア", C),) + tuple((f"深さ{dep}死", [r for r in R if r["deathFloor"] == dep]) for dep in DEPTHS):
        if not S:
            continue
        print(f"  {label:<7} n={len(S):>4} パッシブ {mean(pas(r) for r in S):5.1f}  金 {mean(r['goldGained'] for r in S):5.0f}  残金 {mean(r['finalCoins'] for r in S):4.0f}  総回復 {mean(r['totalHealed'] for r in S):5.0f}  総被ダメ {mean(r['totalDamageTaken'] for r in S):5.0f}"
              f"  総シールド {mean(r['totalShieldGained'] for r in S):5.0f}  回復薬の種類 {mean(sum(1 for a in r.get('acquired') or [] if a in HEALS) for r in S):3.1f}  購入 {mean(r['shopPurchases'] for r in S):4.0f}(パッシブ {mean(r['shopBuyPassive'] for r in S):4.0f})")


for d in sys.argv[1:]:
    report(d)
