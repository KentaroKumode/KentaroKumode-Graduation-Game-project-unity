#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""層 × 戦闘の種類ごとの 戦闘数・死亡率・平均ターン・ターン分布・被ダメ (SkillDiag.ByFloorKind, 2026-09-20)。
使い方: python Tools/floor_kind_report.py <session>
"""
import glob, io, json, os, sys

FLOORS, KINDS, FF = 9, 3, 10
KN = ["雑魚", "エリート", "ボス"]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sess = sys.argv[1]
    a = [0] * (FLOORS * KINDS * FF); h = [0.0] * (FLOORS * KINDS)
    for f in glob.glob(os.path.join(sess.split("#")[0], (sess.split("#")[1] if "#" in sess else "*") + "_c*", "response.json")):
        j = json.load(io.open(f, encoding="utf-8"))
        for i, v in enumerate(j.get("floorKind", [])): a[i] += v
        for i, v in enumerate(j.get("floorKindHpLost", [])): h[i] += v
    print("{:<4}{:<6}{:>8}{:>8}{:>7}{:>9}   ターン分布 1 / 2 / 3 / 4 / 5 / 6-9 / 10+".format("層", "種類", "戦闘数", "死亡率", "平均T", "被ダメ%"))
    for fl in range(FLOORS):
        for k in range(KINDS):
            b = (fl * KINDS + k) * FF
            n = a[b]
            if n == 0: continue
            dist = " / ".join("{:.0%}".format(a[b + 3 + i] / n) for i in range(7))
            print("{:<4}{:<6}{:>8,}{:>8.1%}{:>7.1f}{:>8.0%}   {}".format("Λ" if fl == 0 else str(fl), KN[k], n, a[b + 1] / n, a[b + 2] / n, h[fl * KINDS + k] / n, dist))


if __name__ == "__main__":
    main()
