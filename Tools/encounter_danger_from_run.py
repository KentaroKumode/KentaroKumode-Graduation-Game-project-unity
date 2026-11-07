"""戦闘名ごとの危険度 (encounter_danger.json・Super の航行が読む) を、 実行ディレクトリの記録から作り直す。
A/B の実行は学習に合流しないので、 敵を変えた後はこれで作り直す。
使い方: python -X utf8 Tools/encounter_danger_from_run.py <実行ディレクトリ>
書き先: AutoRunLogs/learning/buffOn_debuffOff/encounter_danger.json"""
import glob
import json
import os
import sys
from collections import defaultdict


def main():
    d = sys.argv[1]
    if not os.path.isdir(d):
        d = os.path.join("AutoRunLogs/learn_parallel", d)
    R = []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        R += j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
    E = defaultdict(lambda: dict(n=0, sum=0.0, sumSq=0.0, lost=0))
    for r in R:
        for f in r.get("fights") or []:
            if not f.get("end") or not f.get("key") or f["maxHp"] <= 0:
                continue
            loss = max(0.0, min(1.0, (f["hpBefore"] - f["hpAfter"]) / f["maxHp"]))
            e = E[f["key"]]
            e["n"] += 1; e["sum"] += loss; e["sumSq"] += loss * loss; e["lost"] += 0 if f["won"] else 1
    out = {"entries": [dict(key=k, **v) for k, v in sorted(E.items())]}
    json.dump(out, open("AutoRunLogs/learning/buffOn_debuffOff/encounter_danger.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"{len(E)} 戦闘名")
    for k, v in sorted(E.items(), key=lambda kv: -kv[1]["lost"] / max(1, kv[1]["n"]))[:12]:
        if v["n"] >= 40:
            print(f"  {k}  n={v['n']}  敗北 {100 * v['lost'] / v['n']:.1f}%  HP減 {100 * v['sum'] / v['n']:.1f}%")


if __name__ == "__main__":
    main()
