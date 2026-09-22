#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""先読み (Super) と 1 ターン最善 (Optimal) の配線の食い違いを局面別に集計する (2026-09-19)。

入力: worker が書く wiring_diff.csv (AutoRunner.logWiringDiff)。 各行は Super の配線判断 1 回で、
同じ局面で Optimal ならどう配線したかを並べてある。

使い方: python Tools/wiring_diff_report.py <session>
"""
import csv, glob, io, os, sys
from collections import defaultdict

KIND = {0: "雑魚", 1: "エリート", 2: "ボス"}


def rows(session):
    for f in glob.glob(os.path.join(session, "*_c*", "wiring_diff.csv")):
        with io.open(f, encoding="utf-8") as fh:
            for r in csv.DictReader(fh):
                yield {k: (int(v) if k != "enemy" else v) for k, v in r.items()}


def table(title, groups):
    print("\n" + title)
    print("  {:<24}{:>9}{:>9}{:>10}{:>10}{:>10}".format("区分", "判断数", "違う率", "Δブロック", "Δ攻撃", "Δ充電"))
    for key in sorted(groups, key=lambda k: str(k)):
        g = groups[key]
        n = len(g)
        if n < 200:
            continue
        diff = sum(1 for r in g if (r["sAtk"], r["sBlk"], r["sChg"]) != (r["oAtk"], r["oBlk"], r["oChg"]))
        db = sum(r["sBlk"] - r["oBlk"] for r in g) / n
        da = sum(r["sAtk"] - r["oAtk"] for r in g) / n
        dc = sum(r["sChg"] - r["oChg"] for r in g) / n
        print("  {:<24}{:>9,}{:>9.1%}{:>+10.2f}{:>+10.2f}{:>+10.2f}".format(str(key), n, diff / n, db, da, dc))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    data = list(rows(sys.argv[1]))
    print("判断 {:,} 件".format(len(data)))

    table("[戦闘の種類]", _group(data, lambda r: KIND[r["kind"]]))
    el = [r for r in data if r["kind"] == 1]

    def cover(r):
        # 出目を全部ブロックに回したら敵の攻撃を受け切れるか / シールドで足りるか
        if r["shield"] >= r["enemyAtk"]:
            return "a.シールドで受け切れる"
        if r["diceSum"] + r["shield"] >= r["enemyAtk"]:
            return "b.全ブロックなら受け切れる"
        return "c.受け切れない"
    table("[エリート] 敵の攻撃を受け切れるか", _group(el, cover))
    table("[エリート] 自HP 割合", _group(el, lambda r: "{:>3}%〜".format(20 * min(4, r["pHp"] * 5 // max(1, r["pMax"])))))
    table("[エリート] 敵HP 割合", _group(el, lambda r: "{:>3}%〜".format(20 * min(4, r["eHp"] * 5 // max(1, r["eMax"])))))
    table("[エリート] 次のエスカレーションまで", _group(el, lambda r: "最終段" if r["nextThr"] < 0
                                                  else "{}T".format(min(3, r["nextThr"] - r["turn"]))))
    table("[エリート] ターン", _group(el, lambda r: "T{}".format(min(6, r["turn"]))))
    table("[エリート] 充電", _group(el, lambda r: "{}".format(min(6, r["charge"] // 2 * 2))))
    table("[エリート] 被弾が自HP の何割か (敵攻撃 / 自HP)", _group(el, lambda r: "{:.1f}〜".format(
        min(1.0, (r["enemyAtk"] / max(1, r["pHp"])) // 0.2 * 0.2))))


def _group(data, keyf):
    g = defaultdict(list)
    for r in data:
        g[keyf(r)].append(r)
    return g


if __name__ == "__main__":
    main()
