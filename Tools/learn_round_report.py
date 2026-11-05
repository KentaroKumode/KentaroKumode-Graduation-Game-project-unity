"""学習 1 ラウンドの要約: 層ごとの死亡率・戦闘数・戦闘の重さ / 金 / 剣の舞。
2026-11-05 以前のワーカーの記録は 1 戦 2 件ずつ入っていた (戦闘終了処理の二重登録・修正済み)。 新しい記録は 1 件ずつ。"""
import glob, json, sys, statistics as st
from collections import Counter, defaultdict

root = sys.argv[1]
DL = {1: 1, 3: 2, 5: 3, 6: 4, 7: 5, 8: 5}
runs, resp = [], []
for f in glob.glob(root + "/round_000/c*/learn_runs.json"):
    runs += json.load(open(f, encoding="utf-8"))["runs"]
for f in glob.glob(root + "/round_000/c*/response.json"):
    resp.append(json.load(open(f, encoding="utf-8")))
N = len(runs)

# 死んだ層: bandScore と到達深さから (11 = クリア)
reach, dead = Counter(), Counter()
for r in runs:
    top = DL.get(r["reachedFloor"], 5)
    for L in range(1, top + 1): reach[L] += 1
    if r["bandScore"] < 11: dead[top] += 1
print(f"runs={N}  クリア {100*sum(r['bandScore']>=11 for r in runs)/N:.1f}%")
print("層  到達   死亡   死亡率")
for L in range(1, 6):
    print(f"{L}  {reach[L]:>5}  {dead[L]:>5}  {100*dead[L]/max(1,reach[L]):6.2f}%")

g = defaultdict(lambda: {"n": 0.0, "turns": [], "loss": []})
for r in runs:
    for c in r["fights"]:
        if not c["end"]: continue
        L = DL.get(c["floor"], c["floor"])
        kind = "boss" if c["isBoss"] else ("elite" if c["enemyId"].endswith("_elite") else "normal")
        x = g[(L, kind)]; x["n"] += 1; x["turns"].append(c["turns"])
        x["loss"].append(100.0 * max(0, c["hpBefore"] - c["hpAfter"]) / max(1, c["maxHp"]))
print("\n層 種別     戦闘/ラン  ターン中央  被ダメ%HP平均")
for k in sorted(g):
    x = g[k]
    print(f"{k[0]}  {k[1]:<7}  {x['n']/N:7.2f}   {st.median(x['turns']):5.0f}      {st.mean(x['loss']):6.1f}")

gold = sum(r.get("goldGained", 0) for r in resp); nn = sum(r.get("runs", 0) for r in resp)
tr = sum(r.get("swordDanceTransforms", 0) for r in resp)
acqR = [sum(r.get("acquiredByRarity", [0]*4)[i] for r in resp) for i in range(4)]
print(f"\n獲得ゴールド/ラン {gold/max(1,nn):.0f}  剣の舞成立 {100*tr/max(1,nn):.1f}%  パッシブ取得/ラン {sum(acqR)/max(1,nn):.1f} (LEG {acqR[3]/max(1,nn):.1f})")
