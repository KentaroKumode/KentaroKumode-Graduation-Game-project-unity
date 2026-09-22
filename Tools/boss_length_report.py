#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ボス別の戦闘の長さをアームごとに並べる (2026-09-19)。 目標: 1 層以外のボス戦は最低 10 ターン。

使い方: python Tools/boss_length_report.py <session>
"""
import collections, glob, io, json, os, sys

ORDER = ["boss_layer1", "boss_layer2", "boss_layer3", "boss_layer4", "boss_layer5", "boss_layer5_hidden",
         "boss_layer6", "boss_layer7", "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4"]
NAME = {"boss_layer1": "1層", "boss_layer2": "3層ゴブリン王", "boss_layer3": "3層毒沼", "boss_layer4": "3層双子",
        "boss_layer5": "5層審判官", "boss_layer5_hidden": "5層裏", "boss_layer6": "6層灰燼",
        "boss_layer7": "ヴェスカp1", "boss_layer7_p2": "ヴェスカp2", "boss_layer7_p3": "ヴェスカp3", "boss_layer7_p4": "ヴェスカp4"}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sess = sys.argv[1]
    arms = sorted({os.path.basename(d).rsplit("_c", 1)[0] for d in glob.glob(os.path.join(sess, "*_c*"))},
                  key=lambda a: (len(a), a))
    for arm in arms:
        agg = collections.defaultdict(lambda: [0, 0, 0, 0]); clears = n = 0
        for f in glob.glob(os.path.join(sess, arm + "_c*", "response.json")):
            j = json.load(io.open(f, encoding="utf-8"))
            clears += j["clears"]; n += j["valid"]
            names, st = j.get("bossNames", []), j.get("bossStats", [])
            for i, nm in enumerate(names):
                for k in range(4):
                    agg[nm][k] += st[i * 4 + k]
        print("\n[{}]  クリア {:.2%}".format(arm, clears / max(1, n)))
        print("  {:<12}{:>8}{:>9}{:>12}{:>10}".format("ボス", "戦闘数", "平均T", "10T未満", "死亡率"))
        for b in ORDER:
            if b not in agg:
                continue
            f, t, s, d = agg[b]
            print("  {:<12}{:>8,}{:>9.1f}{:>11.0%}{:>10.1%}".format(NAME[b], f, t / max(1, f), s / max(1, f), d / max(1, f)))


if __name__ == "__main__":
    main()
