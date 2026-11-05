"""SuperV3 の A/B を、 同じシードの対にして比べる。
使い方: python -X utf8 Tools/v3_ab.py <基準の実行ディレクトリ> <腕の実行ディレクトリ> [<腕> ...] [--first N]
  実行ディレクトリ = AutoRunLogs/learn_parallel/<時刻> (パスでも末尾の名前だけでもよい)
出力: 腕ごとに クリア数 / 基準との対の差 (McNemar) / 完全一致したランの割合 / 層別の死亡率 / 被ダメ・与ダメ。
基準アームを 2 回回して『完全一致 100%』を確かめるのが、 SuperV3 の全 OFF = Optimal の検算になる。"""
import glob
import json
import math
import os
import statistics as st
import sys
from collections import Counter

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


def cleared(x):
    return x["deathFloor"] == 0


def layer_rates(rows, idx):
    c = Counter(rows[i]["deathFloor"] for i in idx)
    reach = len(idx)
    out = []
    for f in (1, 3, 5, 6, 7):
        d = c.get(f, 0)
        out.append(f"{100 * d / max(1, reach):4.1f}%")
        reach -= d
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    first = None
    if "--first" in sys.argv:
        first = int(sys.argv[sys.argv.index("--first") + 1])
        args = [a for a in args if a != str(first)]
    if len(args) < 2:
        print(__doc__)
        return
    base = load(args[0])
    arms = [(a, load(a)) for a in args[1:]]
    idx = sorted(set(base).intersection(*[set(r) for _, r in arms]))
    if first:
        idx = idx[:first]
    n = len(idx)
    print(f"対にしたラン数 {n}")
    hdr = f"{'':<22}{'クリア':>8}{'率':>7}{'対の差':>12}{'z':>7}{'完全一致':>9}   層1 層2 層3 層4 層5(死亡率) | 被ダメ/ラン 与ダメ/戦闘"
    print(hdr)

    def row(name, rows, ref):
        c = sum(cleared(rows[i]) for i in idx)
        if ref is None:
            diff, z, same = "-", "-", "-"
        else:
            a = sum(1 for i in idx if cleared(rows[i]) and not cleared(ref[i]))
            b = sum(1 for i in idx if cleared(ref[i]) and not cleared(rows[i]))
            diff = f"+{a}/-{b}"
            z = f"{(a - b) / math.sqrt(max(1, a + b)):.2f}"
            s = sum(1 for i in idx if all(rows[i][k] == ref[i][k] for k in ("totalDamageDealt", "totalCombats", "deathFloor", "totalDamageTaken")))
            same = f"{100 * s / n:.1f}%"
        dmg = st.mean(rows[i]["totalDamageDealt"] for i in idx) / max(1.0, st.mean(rows[i]["totalCombats"] for i in idx))
        tk = st.mean(rows[i]["totalDamageTaken"] for i in idx)
        print(f"{name:<22}{c:>8}{100 * c / n:>6.1f}%{diff:>12}{z:>7}{same:>9}   {' '.join(layer_rates(rows, idx))} | {tk:>7.0f} {dmg:>7.0f}")

    row("基準 " + os.path.basename(args[0].rstrip('/\\'))[-6:], base, None)
    for name, rows in arms:
        row("腕 " + os.path.basename(name.rstrip('/\\'))[-6:], rows, base)
    print("z>1.96 で有意 (対の検定)。 完全一致が 100% でない腕は、 基準と違う判断をしている。")


if __name__ == "__main__":
    main()
