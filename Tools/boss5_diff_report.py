#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""5 層ボス (大技サイクル) で Super と Optimal の配線がどう違うかを、 大技ターン / 溜めターン別に見る (2026-09-19)。

使い方: python Tools/boss5_diff_report.py <session> [enemyId=boss_layer5] [period=3]
"""
import csv, glob, io, json, os, sys
from collections import defaultdict


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sess = sys.argv[1]
    enemy = sys.argv[2] if len(sys.argv) > 2 else "boss_layer5"
    period = int(sys.argv[3]) if len(sys.argv) > 3 else 3

    # ラン番号 → 終わり方 (band)。 CSV の run は _cur.index で、 response の seedStart+i と 1 ずれうるので両方引く
    band = {}
    for f in glob.glob(os.path.join(sess, "*_c*", "response.json")):
        j = json.load(io.open(f, encoding="utf-8"))
        for i, b in enumerate(j["bandScores"]):
            band[int(j["seedStart"]) + i] = b

    rows = []
    for f in glob.glob(os.path.join(sess, "*_c*", "wiring_diff.csv")):
        for r in csv.DictReader(io.open(f, encoding="utf-8")):
            if r["enemy"] != enemy:
                continue
            r = {k: (int(v) if k != "enemy" else v) for k, v in r.items()}
            b = band.get(r["run"] + 1, band.get(r["run"]))
            r["died5"] = b == 7
            rows.append(r)
    print("{} の配線判断 {:,} 件 (うち 5 層ボスで死んだランの判断 {:,})".format(
        enemy, len(rows), sum(r["died5"] for r in rows)))

    def show(title, keyf):
        g = defaultdict(list)
        for r in rows:
            g[keyf(r)].append(r)
        print("\n" + title)
        print("  {:<30}{:>8}{:>10}{:>10}{:>10}{:>10}{:>10}".format(
            "区分", "判断数", "敵攻撃", "自HP", "S ブロック", "O ブロック", "S 攻撃-O"))
        for k in sorted(g):
            v = g[k]; n = len(v)
            if n < 30:
                continue
            m = lambda f: sum(f(x) for x in v) / n
            print("  {:<30}{:>8,}{:>10.1f}{:>10.1f}{:>10.1f}{:>10.1f}{:>+10.1f}".format(
                str(k), n, m(lambda x: x["enemyAtk"]), m(lambda x: x["pHp"]),
                m(lambda x: x["sBlk"]), m(lambda x: x["oBlk"]), m(lambda x: x["sAtk"] - x["oAtk"])))

    phase = lambda r: "大技" if r["turn"] % period == 0 else "溜め(大技まで{})".format(period - r["turn"] % period)
    show("[ターンの種類]", phase)
    show("[大技ターン] 大技が自HP 以上か", lambda r: (phase(r), "大技≥HP" if r["enemyAtk"] >= r["pHp"] else "大技<HP")
         if phase(r) == "大技" else ("溜め", "-"))
    show("[5 層ボスで死んだランか × ターンの種類]", lambda r: ("死" if r["died5"] else "生", phase(r)))


if __name__ == "__main__":
    main()
