#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Λ層の深さ別 A/B (2026-09-20)。 クリア率・Λ戦闘数・Λ死亡・6層以降の成績を並べる。

Λ は 5 層のままなので、 層別集計では **層 0 = Λ** に分けてある (SkillDiag.ByFloorKind)。
使い方: python Tools/lambda_depth_report.py <session>
"""
import collections, glob, io, json, os, sys


def arms(sess):
    return sorted({os.path.basename(d).rsplit("_c", 1)[0] for d in glob.glob(os.path.join(sess, "*_c*"))},
                  key=lambda a: (len(a), a))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sess = sys.argv[1]
    print("{:<10}{:>9}{:>10}{:>9}{:>9}{:>10}{:>10}{:>9}".format(
        "アーム", "クリア率", "Λ戦/ラン", "Λ死亡率", "被ダメ/戦", "獲得G/ラン", "消耗品/ラン", "6層到達"))
    for a in arms(sess):
        c = v = gold = cons = 0
        fk = [0] * (9 * 3 * 10); hp = [0.0] * 27; band = collections.Counter()
        for f in glob.glob(os.path.join(sess, a + "_c*", "response.json")):
            j = json.load(io.open(f, encoding="utf-8"))
            c += j["clears"]; v += j["valid"]; gold += j.get("goldGained", 0); cons += j.get("consumablesAcquired", 0)
            for i, x in enumerate(j.get("floorKind", [])): fk[i] += x
            for i, x in enumerate(j.get("floorKindHpLost", [])): hp[i] += x
            for b in j["bandScores"]:
                if b >= 0: band[b] += 1
        lam = [fk[k * 10:(k + 1) * 10] for k in range(3)]
        fights = sum(r[0] for r in lam); deaths = sum(r[1] for r in lam)
        reach6 = sum(n for b, n in band.items() if b >= 9)
        print("{:<10}{:>9.2%}{:>10.1f}{:>9.1%}{:>9.0%}{:>10.0f}{:>10.1f}{:>9.1%}".format(
            a, c / max(1, v), fights / max(1, v), deaths / max(1, fights),
            sum(hp[0:3]) / max(1, fights), gold / max(1, v), cons / max(1, v), reach6 / max(1, v)))


if __name__ == "__main__":
    main()
