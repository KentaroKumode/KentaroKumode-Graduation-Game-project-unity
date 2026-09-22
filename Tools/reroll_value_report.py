#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""リロールの「代金」と「供給」を分解して読む (2026-09-17)。

棚は 12 枠固定 (ShopManager.Generate) なので、 **リロールは補給を増やす唯一の手段を
兼ねている**。 だから回数を削るアームは「金が浮く」と「供給が減る」が混ざり、
単独では代金の価値に答えられない。 価格だけを動かす F を足して 3 点で分解する:

    供給の価値 = F − Z    (代金を払わずに補給だけ受け取った利得)
    代金の負担 = F − B    (いま払っている分がクリア率何 pt か)
    純便益     = B − Z    (現状のリロールが差し引きで稼いでいる分)

F は **1G 固定であって無料ではない** (ShopInventory.CurrentRerollPrice の下限)。
"""
import glob, io, json, math, os, re, sys

sys.stdout.reconfigure(encoding="utf-8")
S = sys.argv[1].rstrip("/\\") if len(sys.argv) > 1 else sorted(
    glob.glob(os.path.join("AutoRunLogs", "sweep", "*_reroll_value")))[-1]

LB = ["B_base", "F_free1g", "Z_cap0", "C1_cap1", "C2_cap2", "C3_cap3"]
NAME = {"B_base": "B 現状", "F_free1g": "F 1G固定", "Z_cap0": "Z 禁止",
        "C1_cap1": "C1 上限1", "C2_cap2": "C2 上限2", "C3_cap3": "C3 上限3"}


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
    return dict(rows)


def field(lb, pat, grp=1):
    """summary の数値をラン数で加重平均する。"""
    tot = n = 0
    rx = re.compile(pat, re.M)
    for d in sorted(glob.glob(os.path.join(S, lb + "_c*"))):
        for f in glob.glob(os.path.join(d, "AutoRunLogs", "*", "summary_*.txt")):
            t = io.open(f, encoding="utf-8", errors="replace").read()
            m = rx.search(t)
            r = re.search(r"n(\d+)_buffOn", f)
            if m and r:
                tot += float(m.group(grp)) * int(r.group(1))
                n += int(r.group(1))
    return tot / n if n else float("nan")


PATS = [
    ("リロール回数",   r"平均リロール回数\s*:\s*([\d.]+)",           1, "{:>9.2f}"),
    ("リロール代 (G)", r"平均リロール回数\s*:\s*[\d.]+\s*\(平均消費\s*([\d.]+)G", 1, "{:>12.1f}"),
    ("購入数",         r"ショップ購入数\s*:\s*([\d.]+)",             1, "{:>8.2f}"),
    ("総供給 (品)",    r"経路別 \(1ラン平均 / 計 ([\d.]+) 品\)",      1, "{:>11.2f}"),
    ("ショップ比 (%)", r"ショップ\s+:\s*[\d.]+ 品\s*\(([\d.]+)%\)",   1, "{:>13.1f}"),
    ("獲得ゴールド",   r"平均総獲得ゴールド\s*:\s*([\d.]+)",         1, "{:>12.1f}"),
    ("クリア残金 (G)", r"ラン終了時の残金\s*:\s*クリア\s*([\d.]+)G",  1, "{:>13.1f}"),
    ("最終店の残金",   r"最後に店を出た時点\s*:\s*残金\s*([\d.]+)G",  1, "{:>12.1f}"),
]


def mcnemar(a, b):
    keys = sorted(set(a) & set(b))
    w = sum(1 for k in keys if a[k] < 11 <= b[k])
    l = sum(1 for k in keys if b[k] < 11 <= a[k])
    z = (w - l) / math.sqrt(w + l) if w + l else 0.0
    return w, l, z, len(keys)


def main():
    print("session: " + S + "\n")
    B = {lb: bands(lb) for lb in LB}
    rate = {lb: 100.0 * sum(1 for x in B[lb].values() if x >= 11) / max(1, len(B[lb]))
            for lb in LB}

    hdr = "{:<12}".format("") + "".join("{:>11}".format(NAME[lb]) for lb in LB)
    print(hdr)
    print("{:<12}".format("クリア率 (%)")
          + "".join("{:>11.2f}".format(rate[lb]) for lb in LB))
    print("{:<12}".format("n")
          + "".join("{:>11,}".format(len(B[lb])) for lb in LB))
    print()
    for label, pat, grp, _ in PATS:
        vals = [field(lb, pat, grp) for lb in LB]
        print("{:<12}".format(label) + "".join("{:>11.2f}".format(v) for v in vals))

    print("\n── B 現状との McNemar（同一 runIdx）──")
    for lb in LB[1:]:
        w, l, z, n = mcnemar(B["B_base"], B[lb])
        print("  {:<10} 改善{:>5} 悪化{:>5}  z={:+6.2f}  {}  (n={:,})".format(
            NAME[lb], w, l, z, "有意" if abs(z) >= 2 else "ns", n))

    print("\n── 分解（クリア率 pt）──")
    print("  供給の価値 (F − Z) : {:+.2f}".format(rate["F_free1g"] - rate["Z_cap0"]))
    print("  代金の負担 (F − B) : {:+.2f}".format(rate["F_free1g"] - rate["B_base"]))
    print("  純便益     (B − Z) : {:+.2f}".format(rate["B_base"] - rate["Z_cap0"]))

    print("\n── 上限の掃引（Z→C1→C2→C3→B）──")
    seq = ["Z_cap0", "C1_cap1", "C2_cap2", "C3_cap3", "B_base"]
    for i, lb in enumerate(seq):
        d = "" if i == 0 else "  前段差 {:+.2f}".format(rate[lb] - rate[seq[i - 1]])
        print("  {:<10} {:>6.2f}%{}".format(NAME[lb], rate[lb], d))
    best = max(seq, key=lambda x: rate[x])
    print("  → 最良: {} ({:.2f}%)  現状との差 {:+.2f}pt".format(
        NAME[best], rate[best], rate[best] - rate["B_base"]))


if __name__ == "__main__":
    main()
