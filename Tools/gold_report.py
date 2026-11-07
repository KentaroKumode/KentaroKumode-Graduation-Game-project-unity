"""金の入手の内訳 (learn_runs.json の goldKeys / goldVals = 「深さ|経路」別) を、 クリアしたランと全ランで出す。
使い方: python -X utf8 Tools/gold_report.py <実行ディレクトリ> [...]"""
import glob
import json
import os
import sys
from collections import Counter


def load(d):
    if not os.path.isdir(d):
        d = os.path.join("AutoRunLogs/learn_parallel", d)
    R = []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        R += j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
    return R


def table(S, label):
    src, dep, cell = Counter(), Counter(), Counter()
    for r in S:
        for k, v in zip(r.get("goldKeys") or [], r.get("goldVals") or []):
            d, s = k.split("|", 1)
            src[s] += v; dep[int(d)] += v; cell[(s, int(d))] += v
    n = max(1, len(S)); tot = max(1, sum(src.values())); ds = sorted(dep)
    print(f"\n[{label}] n={len(S)}  内訳の合計 {tot / n:.0f}G/ラン  goldGained {sum(r['goldGained'] for r in S) / n:.0f}G  使用 {sum(r['coinsSpent'] for r in S) / n:.0f}G  残金 {sum(r['finalCoins'] for r in S) / n:.0f}G")
    print(f"{'経路':<22}{'計':>7}{'割合':>7}" + "".join(f"{'深' + str(d):>7}" for d in ds))
    for s, v in src.most_common():
        print(f"{s:<22}{v / n:>7.0f}{v / tot:>7.0%}" + "".join(f"{cell[(s, d)] / n:>7.0f}" for d in ds))
    print(f"{'計':<22}{tot / n:>7.0f}{1:>7.0%}" + "".join(f"{dep[d] / n:>7.0f}" for d in ds))


def main():
    for d in sys.argv[1:]:
        R = load(d)
        print(f"== {d}  クリア {100 * sum(1 for r in R if r['deathFloor'] == 0) / max(1, len(R)):.1f}%")
        table([r for r in R if r["deathFloor"] == 0], "クリアしたラン")
        table(R, "全ラン")


if __name__ == "__main__":
    main()
