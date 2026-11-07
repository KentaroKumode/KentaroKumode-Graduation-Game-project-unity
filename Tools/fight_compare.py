"""2 つの実行ディレクトリを、 戦闘 (遭遇) の種類ごとに比べる。
使い方: python -X utf8 Tools/fight_compare.py <A の実行ディレクトリ> <B の実行ディレクトリ> [--min N] [--first N]
同じシードのランだけを使い、 learn_runs.json の fights (遭遇の種類 key・開始/終了 HP・勝敗) を種類ごとに集計する。
出力: 種類ごとの 戦闘数 / 敗北率 / 平均損耗 (最大 HP 比) / ターン数 を A と B で並べ、 B が良い順に並べる。
『先読み (Super) が 1 ターン最善 (Optimal) を上回る場面』を探すための道具。"""
import glob
import json
import os
import sys
from collections import defaultdict

ROOT = "AutoRunLogs/learn_parallel"


def load(d):
    if not os.path.isdir(d):
        d = os.path.join(ROOT, d)
    rows = {}
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        j = j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
        for x in j:
            rows[x["index"]] = x
    return rows


def agg(rows, idx):
    t = defaultdict(lambda: [0, 0, 0.0, 0.0])   # n, lost, loss_sum, turns
    for i in idx:
        for f in rows[i].get("fights") or []:
            if not f.get("end") or not f.get("key") or f.get("maxHp", 0) <= 0:
                continue
            a = t[f["key"]]
            a[0] += 1
            a[1] += 0 if f.get("won") else 1
            a[2] += (f["hpBefore"] - f["hpAfter"]) / f["maxHp"]
            a[3] += f.get("turns", 0)
    return t


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    mn = int(sys.argv[sys.argv.index("--min") + 1]) if "--min" in sys.argv else 30
    first = int(sys.argv[sys.argv.index("--first") + 1]) if "--first" in sys.argv else None
    if first is not None:
        args = [a for a in args if a != str(first)]
    args = [a for a in args if a != str(mn)]
    A, B = load(args[0]), load(args[1])
    idx = sorted(set(A) & set(B))
    if first:
        idx = idx[:first]
    ta, tb = agg(A, idx), agg(B, idx)
    rows = []
    for k in set(ta) & set(tb):
        na, nb = ta[k][0], tb[k][0]
        if min(na, nb) < mn:
            continue
        la, lb = 100 * ta[k][2] / na, 100 * tb[k][2] / nb
        da, db = 100 * ta[k][1] / na, 100 * tb[k][1] / nb
        rows.append((la - lb, k, na, nb, da, db, la, lb))
    rows.sort(reverse=True)
    print(f"対にしたラン {len(idx)}  (A={os.path.basename(args[0].rstrip('/'))[-6:]} / B={os.path.basename(args[1].rstrip('/'))[-6:]})")
    print(f"{'遭遇 (層|種|名)':<32}{'戦闘数A/B':>12}{'敗北% A':>9}{'敗北% B':>9}{'損耗% A':>9}{'損耗% B':>9}{'差(A−B)':>9}")
    for d, k, na, nb, da, db, la, lb in rows:
        print(f"{k[:30]:<32}{na:>6}/{nb:<5}{da:>9.1f}{db:>9.1f}{la:>9.1f}{lb:>9.1f}{d:>9.1f}")
    print("差 (A−B) が正 = B の方が HP を失っていない (B が良い)。")


if __name__ == "__main__":
    main()
