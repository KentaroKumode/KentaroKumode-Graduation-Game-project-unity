#!/usr/bin/env python3
"""同一シードの 2 バッチを突き合わせ、ペア比較でラン数をどれだけ減らせるかを出す。

なぜ要るか
----------
スイープは全アームを同じシード列で走らせているのに、集計値どうしを比べている。
「このシードは当たり/外れ」という**両アームに共通のばらつき**がそのまま誤差に乗る。
ペアで見ればそれが相殺され、**片方だけクリアしたシード**だけが効く。

    必要ラン数の比 = 食い違い率 / (pA(1-pA) + pB(1-pB))

利得が 1.0 なら相関ゼロでペア比較の意味なし。小さいほど節約になる。
**推測せずに測るための道具** — 2 バッチの runs.jsonl を渡すだけ。

使い方
------
    python Tools/pair_gain.py AutoRunLogs/batch_A/runs.jsonl AutoRunLogs/batch_B/runs.jsonl
"""
import io
import json
import math
import sys


def load(path):
    """index -> クリアしたか (bandScore >= 11)。 index が無い行は捨てる。"""
    out = {}
    with io.open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                d = json.loads(line)
            except ValueError:
                continue
            if "index" not in d or "bandScore" not in d:
                continue
            # bandScore < 0 は CRASH/DEADLOCK。 ペアから外す (両アームで同じ扱いにする)
            if d["bandScore"] < 0:
                continue
            out[d["index"]] = 1 if d["bandScore"] >= 11 else 0
    return out


def main(pa, pb):
    a, b = load(pa), load(pb)
    common = sorted(set(a) & set(b))
    n = len(common)
    if n == 0:
        print("共通の index がありません。 同じシード列で走った 2 バッチを渡してください。")
        return 1

    # 2x2
    both = only_a = only_b = neither = 0
    for i in common:
        if a[i] and b[i]:
            both += 1
        elif a[i]:
            only_a += 1
        elif b[i]:
            only_b += 1
        else:
            neither += 1

    pA = (both + only_a) / n
    pB = (both + only_b) / n
    disc = (only_a + only_b) / n

    # 非ペア (独立とみなした) 差の分散 × n
    var_unpaired = pA * (1 - pA) + pB * (1 - pB)
    # ペア (McNemar) の差の分散 × n
    diff = pB - pA
    var_paired = disc - diff * diff
    gain = var_paired / var_unpaired if var_unpaired > 0 else float("nan")

    se_un = math.sqrt(var_unpaired / n) * 100
    se_pa = math.sqrt(max(var_paired, 0) / n) * 100

    print(f"共通シード      : {n:,}")
    print(f"A クリア率      : {pA*100:.2f}%   ({pa})")
    print(f"B クリア率      : {pB*100:.2f}%   ({pb})")
    print(f"差              : {diff*100:+.2f}pt")
    print()
    print("        B:クリア   B:非クリア")
    print(f"A:クリア  {both:7,}   {only_a:9,}")
    print(f"A:非ク    {only_b:7,}   {neither:9,}")
    print()
    print(f"食い違い率      : {disc*100:.2f}%   (片方だけクリア {only_a + only_b:,} 本)")
    print(f"  無相関なら    : {(pA*(1-pB) + pB*(1-pA))*100:.2f}%")
    print(f"  完全相関なら  : {abs(diff)*100:.2f}%")
    print()
    print(f"差のSE 非ペア   : ±{1.96*se_un:.3f}pt (95%CI)")
    print(f"差のSE ペア     : ±{1.96*se_pa:.3f}pt (95%CI)")
    print(f"必要ラン数の比  : {gain:.3f} 倍")
    if gain < 0.95:
        print(f"  → 同じ精度が {gain*100:.0f}% のランで出る。"
              f" 10,000ラン 相当 ≒ {10000*gain:,.0f}ラン")
    else:
        print("  → 相関がほぼ無い。 ペア比較に意味は無いので非ペアのまま払うこと。")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
