#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""経済 A × エリート強化 E の 2×2 要因計画を読む。

**主指標はクリア率ではなくショップ経由の割合**。 供給を減らして金で補償するので
正味の戦力はおおむね中立になる ── クリア率が動かないことを「効かなかった」と
読まないための順序。
"""
import glob, io, json, math, os, re, sys

sys.stdout.reconfigure(encoding="utf-8")
S = sys.argv[1].rstrip("/\\") if len(sys.argv) > 1 else sorted(
    glob.glob(os.path.join("AutoRunLogs", "sweep", "*_econ_elite")))[-1]
LB = ["1_現状", "2_経済A", "3_強化E", "4_A+E"]


def bands(lb):
    rows = []
    for d in sorted(glob.glob(os.path.join(S, lb + "_c*"))):
        p = os.path.join(d, "response.json")
        if not os.path.exists(p):
            print("  !! 欠落: " + p)
            continue
        j = json.load(io.open(p, encoding="utf-8"))
        s = int(j["seedStart"])
        for i, b in enumerate(j["bandScores"]):
            rows.append((s + i, b))
    rows.sort()
    return [b for _, b in rows]


def field(lb, pat):
    """summary から 1 つの数値をラン数で加重平均する。"""
    tot = n = 0
    rx = re.compile(pat, re.M)
    for d in sorted(glob.glob(os.path.join(S, lb + "_c*"))):
        for f in glob.glob(os.path.join(d, "AutoRunLogs", "*", "summary_*.txt")):
            t = io.open(f, encoding="utf-8", errors="replace").read()
            m = rx.search(t)
            r = re.search(r"n(\d+)_buffOn", f)
            if m and r:
                tot += float(m.group(1)) * int(r.group(1))
                n += int(r.group(1))
    return tot / n if n else float("nan")


def main():
    print("session: " + S + "\n")
    B = {lb: bands(lb) for lb in LB}
    rate = lambda lb: 100.0 * sum(1 for x in B[lb] if x >= 11) / len(B[lb])

    print("{:<9}{:>9}{:>11}{:>9}{:>12}{:>10}".format(
        "アーム", "クリア率", "ショップ比", "総供給", "エリート踏破", "ゴールド"))
    for lb in LB:
        print("{:<9}{:>8.2f}%{:>10.1f}%{:>9.2f}{:>12.2f}{:>10.1f}".format(
            lb, rate(lb),
            field(lb, r"ショップ\s*:\s*[\d.]+ 品\s*\(([\d.]+)%\)"),
            field(lb, r"経路別 \(1ラン平均 / 計 ([\d.]+) 品\)"),
            field(lb, r"^\s{2}EliteBattle\s*:\s*平均\s*([0-9.]+)/ラン"),
            field(lb, r"平均総獲得ゴールド\s*:\s*([\d.]+)")))

    print("\n── 現状との McNemar（同一 runIdx）──")
    for lb in LB[1:]:
        a, c = B[LB[0]], B[lb]
        m = min(len(a), len(c))
        w = sum(1 for i in range(m) if a[i] < 11 <= c[i])
        l = sum(1 for i in range(m) if c[i] < 11 <= a[i])
        z = (w - l) / math.sqrt(w + l) if w + l else 0.0
        print("  {:<9} 改善{:>5} 悪化{:>5}  z={:+.2f}  {}".format(
            lb, w, l, z, "有意" if abs(z) >= 2 else "ns"))

    print("\n── 2×2 の分解（クリア率）──")
    print("  経済A の主効果 : {:+.2f}pt".format(
        ((rate("2_経済A") - rate("1_現状")) + (rate("4_A+E") - rate("3_強化E"))) / 2))
    print("  強化E の主効果 : {:+.2f}pt".format(
        ((rate("3_強化E") - rate("1_現状")) + (rate("4_A+E") - rate("2_経済A"))) / 2))
    print("  交互作用       : {:+.2f}pt".format(
        (rate("4_A+E") - rate("3_強化E") - rate("2_経済A") + rate("1_現状")) / 2))


if __name__ == "__main__":
    main()
