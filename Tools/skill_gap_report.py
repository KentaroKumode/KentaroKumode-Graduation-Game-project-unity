#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""配線方策 (Naive / Optimal / Super) のアームを、 戦闘の種類別に並べる (2026-09-19)。

worker の response.json の skillFights / skillDeaths / skillTurns / skillHpLostPct
(CombatSystem.SkillDiag) を合算し、 雑魚・エリート・ボスごとに
  1 戦あたり被ダメ (最大HP比)・死亡率・平均ターン
を出す。 クリア率と、 同一シードのペア比較 (McNemar) も併記する。

使い方:
    python Tools/skill_gap_report.py <session>:<arm> [<session>:<arm> ...]
    先頭のアームが比較の基準。
"""
import glob, io, json, math, os, sys

KINDS = ["雑魚", "エリート", "ボス"]


def load(session, arm):
    agg = {"f": [0] * 3, "d": [0] * 3, "t": [0] * 3, "h": [0.0] * 3, "band": {}}
    for d in sorted(glob.glob(os.path.join(session, arm + "_c*"))):
        j = json.load(io.open(os.path.join(d, "response.json"), encoding="utf-8"))
        for i in range(3):
            agg["f"][i] += j.get("skillFights", [0] * 3)[i]
            agg["d"][i] += j.get("skillDeaths", [0] * 3)[i]
            agg["t"][i] += j.get("skillTurns", [0] * 3)[i]
            agg["h"][i] += j.get("skillHpLostPct", [0.0] * 3)[i]
        s = int(j["seedStart"])
        for k, b in enumerate(j["bandScores"]):
            agg["band"][s + k] = b
    return agg


def mcnemar(a, b):
    ks = sorted(set(a) & set(b))
    up = sum(1 for k in ks if a[k] < 11 <= b[k])
    dn = sum(1 for k in ks if b[k] < 11 <= a[k])
    n = up + dn
    p = min(1.0, 2 * sum(math.comb(n, i) for i in range(min(up, dn) + 1)) / 2 ** n) if n else 1.0
    return len(ks), up, dn, p


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    arms = []
    for spec in sys.argv[1:]:
        sess, arm = spec.rsplit(":", 1)
        arms.append((arm, load(sess, arm)))
    base_name, base = arms[0]

    print("{:<10}{:>8}{:>10}".format("アーム", "クリア", "対基準"))
    for name, a in arms:
        vals = list(a["band"].values())
        clear = sum(1 for b in vals if b >= 11) / max(1, len(vals))
        if a is base:
            print("{:<10}{:>8.2%}{:>10}".format(name, clear, "—"))
        else:
            n, up, dn, p = mcnemar(base["band"], a["band"])
            bclear = sum(1 for b in base["band"].values() if b >= 11) / max(1, len(base["band"]))
            print("{:<10}{:>8.2%}{:>+9.2f}pt  (改善 {} / 悪化 {}, p={:.3g}, n={})".format(
                name, clear, (clear - bclear) * 100, up, dn, p, n))

    for i, k in enumerate(KINDS):
        print("\n[{}]".format(k))
        print("  {:<10}{:>9}{:>14}{:>10}{:>10}".format("アーム", "戦闘数", "被ダメ/戦(%HP)", "死亡率", "平均T"))
        for name, a in arms:
            f = max(1, a["f"][i])
            print("  {:<10}{:>9,}{:>14.1f}{:>9.2%}{:>10.2f}".format(
                name, a["f"][i], 100 * a["h"][i] / f, a["d"][i] / f, a["t"][i] / f))


if __name__ == "__main__":
    main()
