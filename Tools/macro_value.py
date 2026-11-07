"""マクロの価値 V (状態 → このランのクリア確率) を実行の記録から測る。
使い方: python -X utf8 Tools/macro_value.py <実行ディレクトリ> [...]
learn_runs.json の stateSnaps = 10 列ずつ: 種別 (0 層に入った / 1 ボスに入る) / 深さ / HP / 最大 HP / パッシブ数 / 所持金 / 回復薬 / 護符 / 出目パーツ / 物資。

見るもの: 同じ深さで HP・品の数を分けた時のクリア率の差が、 判断を変えるほど大きいか。
注意: 観測なので交絡がある (品が多いランは元々強い)。 品の価値はここからは取らない (ITT を使う)。 HP は交絡が比較的小さい。"""
import glob
import json
import math
import os
import sys
from collections import defaultdict

COLS = 10


def load(d):
    if not os.path.isdir(d):
        d = os.path.join("AutoRunLogs/learn_parallel", d)
    R = []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        R += j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
    return R


def fit(rows):
    """ロジスティック回帰 (HP 割合・パッシブ数・回復薬・護符・金)。 素朴な勾配法で十分。"""
    names = ["HP割合×10", "パッシブ", "回復薬", "護符", "金/10"]
    mean = [sum(r[0][i] for r in rows) / len(rows) for i in range(5)]
    sd = [max(1e-6, math.sqrt(sum((r[0][i] - mean[i]) ** 2 for r in rows) / len(rows))) for i in range(5)]
    w = [0.0] * 5; b = 0.0
    for _ in range(400):
        gw = [0.0] * 5; gb = 0.0
        for x, y in rows:
            z = [(x[i] - mean[i]) / sd[i] for i in range(5)]
            p = 1 / (1 + math.exp(-max(-30, min(30, b + sum(a * c for a, c in zip(w, z))))))
            for i in range(5):
                gw[i] += (y - p) * z[i]
            gb += y - p
        n = len(rows)
        w = [a + 1.0 * g / n for a, g in zip(w, gw)]; b += 1.0 * gb / n
    return names, [w[i] / sd[i] for i in range(5)]


def main():
    for d in sys.argv[1:]:
        R = load(d)
        print(f"== {d}  n={len(R)}  クリア {100 * sum(1 for r in R if r['deathFloor'] == 0) / max(1, len(R)):.1f}%")
        for kind, label in ((1, "ボスに入る時"), (0, "層に入る時")):
            by = defaultdict(list)
            for r in R:
                s = r.get("stateSnaps") or []
                y = 1 if r["deathFloor"] == 0 else 0
                seen = set()
                for i in range(0, len(s) - COLS + 1, COLS):
                    k, dep, hp, mh, pas, coins, heal, df, parts, prov = s[i:i + COLS]
                    if k != kind or dep in seen or dep < 1:
                        continue
                    seen.add(dep)
                    by[dep].append(((10.0 * hp / max(1, mh), pas, heal, df, coins / 10.0), y, hp / max(1, mh), pas))
            print(f"\n[{label}] 深さごとのクリア率 (HP 割合の帯 / パッシブ数の帯)")
            for dep in sorted(by):
                rows = by[dep]; n = len(rows)
                c = sum(r[1] for r in rows) / n
                band = defaultdict(list)
                for r in rows:
                    band[min(3, int(r[2] * 4))].append(r[1])
                hb = " ".join(f"{['〜25', '〜50', '〜75', '75〜'][b]}%:{100 * sum(v) / len(v):4.1f}({len(v)})" for b, v in sorted(band.items()))
                ps = sorted(r[3] for r in rows)
                q1, q3 = ps[n // 4], ps[n * 3 // 4]
                lo = [r[1] for r in rows if r[3] <= q1]; hi = [r[1] for r in rows if r[3] >= q3]
                print(f"  深さ{dep} n={n:>5} クリア {100 * c:4.1f}% | HP {hb} | 品 ≤{q1}: {100 * sum(lo) / max(1, len(lo)):4.1f}% ≥{q3}: {100 * sum(hi) / max(1, len(hi)):4.1f}%")
                if n >= 300 and 0 < sum(r[1] for r in rows) < n:
                    names, w = fit([(r[0], r[1]) for r in rows])
                    print("           対数オッズ/単位: " + " ".join(f"{a} {b:+.3f}" for a, b in zip(names, w)))


if __name__ == "__main__":
    main()
