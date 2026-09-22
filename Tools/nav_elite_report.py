#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""nav_elite_sweep.py の結果を読む。

**主指標はクリア率ではなくエリート踏破数**。 1-1 で直したのは「BOT が
エリートを選べない」という航行判断の欠陥なので、 まず手が変わったことを
確かめる ── クリア率だけ見ると、 手が変わっていないのに別の理由で動いた場合を
取り違える。

比較は **McNemar のペア比較**（同一 runIdx）。 平均の差では判断しない。
"""
import glob
import io
import json
import math
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


def load_arm(session, label):
    """アームの全チャンクを seed 順に結合し、 ラン単位の (クリア, digest) を返す。

    **クリア判定は bandScore == 11**（最大バンド＝ Null Point 突破）。
    response の `clears` は集計値なのでペア比較に使えない。
    """
    MAX_BAND = 11
    rows = []
    for d in sorted(glob.glob(os.path.join(session, label + "_c*"))):
        rp = os.path.join(d, "response.json")
        if not os.path.exists(rp):
            continue
        j = json.load(io.open(rp, encoding="utf-8"))
        if not j.get("valid", True) or not j.get("completedNormally", True):
            print("  !! 不完全なチャンク: " + rp)
        bs = j.get("bandScores") or []
        dg = j.get("digests") or []
        seed = int(j.get("seedStart", 0))
        for i, b in enumerate(bs):
            rows.append((seed + i, b >= MAX_BAND, dg[i] if i < len(dg) else ""))
    rows.sort(key=lambda r: r[0])
    return [r[1] for r in rows], [r[2] for r in rows], len(rows)


def tile_avg(session, label, tile="EliteBattle"):
    """summary の「タイル踏破分布」から 1 ランあたりの平均を、ラン数で加重平均する。"""
    tot, n = 0.0, 0
    pat = re.compile(re.escape(tile) + r"\s*:\s*平均\s*([0-9.]+)/ラン\s*\(総(\d+)")
    for d in sorted(glob.glob(os.path.join(session, label + "_c*"))):
        for f in glob.glob(os.path.join(d, "AutoRunLogs", "*", "summary_*.txt")):
            s = io.open(f, encoding="utf-8", errors="replace").read()
            m = pat.search(s)
            r = re.search(r"n(\d+)_buffOn", f)
            if m and r:
                tot += float(m.group(1)) * int(r.group(1))
                n += int(r.group(1))
    return (tot / n if n else float("nan")), n


def mcnemar(a, b):
    """a→b で改善した件数 / 悪化した件数 と z。 同一 runIdx のペア前提。"""
    m = min(len(a), len(b))
    win = sum(1 for i in range(m) if not a[i] and b[i])
    lose = sum(1 for i in range(m) if a[i] and not b[i])
    n = win + lose
    z = (win - lose) / math.sqrt(n) if n else 0.0
    return win, lose, z, m


def main():
    session = sys.argv[1] if len(sys.argv) > 1 else sorted(
        glob.glob(os.path.join("AutoRunLogs", "nav_elite", "*")))[-1]
    print("session: " + session + "\n")

    labels = [os.path.basename(d).rsplit("_c", 1)[0]
              for d in sorted(glob.glob(os.path.join(session, "*_c00")))]
    labels = sorted(set(labels))

    data, digs = {}, {}
    print("{:<12}{:>9}{:>11}{:>16}".format("アーム", "ラン", "クリア率", "エリート踏破/ラン"))
    for lb in labels:
        clears, digests, runs = load_arm(session, lb)
        et, en = tile_avg(session, lb)
        data[lb] = clears
        digs[lb] = digests
        rate = 100.0 * sum(1 for c in clears if c) / len(clears) if clears else float("nan")
        print("{:<12}{:>9,}{:>10.2f}%{:>16.2f}".format(lb, len(clears), rate, et))

    base = labels[0]
    print("\n── {} との McNemar ペア比較（同一 runIdx）──".format(base))
    print("{:<12}{:>8}{:>8}{:>9}{:>10}".format("アーム", "改善", "悪化", "z", "判定"))
    for lb in labels[1:]:
        w, l, z, m = mcnemar(data[base], data[lb])
        mark = "有意" if abs(z) >= 2.0 else "ns"
        print("{:<12}{:>8,}{:>8,}{:>9.2f}{:>10}".format(lb, w, l, z, mark))

    # digest が動いていなければ「アームが効いていない」。 数字の前にこれを見る。
    print("\n── digest 一致率（{} 比）── 100% なら手が一切変わっていない".format(base))
    for lb in labels[1:]:
        a, b = digs[base], digs[lb]
        m = min(len(a), len(b))
        same = sum(1 for i in range(m) if a[i] == b[i])
        print("  {:<12}{:>8,}/{:<8,} {:>6.1f}%".format(lb, same, m, 100.0 * same / max(1, m)))


if __name__ == "__main__":
    main()
