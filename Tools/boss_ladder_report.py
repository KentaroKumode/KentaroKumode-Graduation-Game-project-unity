#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ボス攻撃の梯子スイープをアームごとに並べる (2026-09-19)。

使い方: python Tools/boss_ladder_report.py <session> [<基準session>:<arm>]
"""
import collections, glob, io, json, math, os, sys

BANDS = [(1, "〜2層"), (2, "3層道中"), (3, "3層ボス"), (4, "4層道中"), (5, "4層ボス"),
         (6, "5層道中"), (7, "5層ボス"), (8, "5/6層止まり"), (9, "6層ボス"), (10, "7層以降"), (11, "完全クリア")]


def load(session, arm):
    a = {"band": {}, "f": [0] * 3, "d": [0] * 3, "h": [0.0] * 3, "ent": [0] * 9}
    for f in glob.glob(os.path.join(session, arm + "_c*", "response.json")):
        j = json.load(io.open(f, encoding="utf-8"))
        s = int(j["seedStart"])
        for i, b in enumerate(j["bandScores"]):
            a["band"][s + i] = b
        for i in range(3):
            a["f"][i] += j.get("skillFights", [0] * 3)[i]
            a["d"][i] += j.get("skillDeaths", [0] * 3)[i]
            a["h"][i] += j.get("skillHpLostPct", [0.0] * 3)[i]
        for i in range(9):
            a["ent"][i] += j.get("bossEntryCount", [0] * 9)[i]
    return a


def mcnemar(a, b):
    ks = set(a) & set(b)
    up = sum(1 for k in ks if a[k] < 11 <= b[k]); dn = sum(1 for k in ks if b[k] < 11 <= a[k])
    n = up + dn
    p = min(1.0, 2 * sum(math.comb(n, i) for i in range(min(up, dn) + 1)) / 2 ** n) if n else 1.0
    return up, dn, p


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sess = sys.argv[1]
    arms = sorted({os.path.basename(d).rsplit("_c", 1)[0] for d in glob.glob(os.path.join(sess, "*_c*"))})
    base = None
    if len(sys.argv) > 2:
        bs, ba = sys.argv[2].rsplit(":", 1)
        base = load(bs, ba)
    order = ["heavy_only", "atk25", "atk50", "atk75", "atk100"]
    arms = [x for x in order if x in arms] + [x for x in arms if x not in order]
    print("{:<11}{:>8}{:>22}{:>12}{:>12}   ボス到達(1/3/5/6/8層)".format("アーム", "クリア", "対基準", "ボス死亡/戦", "ボス被ダメ/戦"))
    rows = {}
    for arm in arms:
        a = load(sess, arm); rows[arm] = a
        v = list(a["band"].values()); clear = sum(1 for b in v if b >= 11) / max(1, len(v))
        cmp = ""
        if base:
            up, dn, p = mcnemar(base["band"], a["band"])
            bc = sum(1 for b in base["band"].values() if b >= 11) / max(1, len(base["band"]))
            cmp = "{:+.2f}pt ({}/{})".format((clear - bc) * 100, up, dn)
        f = max(1, a["f"][2])
        print("{:<11}{:>8.2%}{:>22}{:>12.2%}{:>11.1f}%   {}".format(
            arm, clear, cmp, a["d"][2] / f, 100 * a["h"][2] / f,
            "/".join(str(a["ent"][i]) for i in (1, 3, 5, 6, 8))))
    print("\nランの終わり方 (件数)")
    print("  {:<12}".format("") + "".join("{:>9}".format(n) for _, n in BANDS))
    for arm, a in rows.items():
        c = collections.Counter(a["band"].values())
        print("  {:<12}".format(arm) + "".join("{:>9,}".format(c.get(b, 0)) for b, _ in BANDS))


if __name__ == "__main__":
    main()
