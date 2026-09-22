#!/usr/bin/env python3
"""Paired comparison of two AutoRun batches, run index by run index.

**Why paired and not two averages.** Two 100-run batches differ by luck of the draw as
much as by policy; at a 13% clear rate the 95% interval on a single batch is roughly
±7 points, which swallows any effect worth shipping. When both batches run the same
seeds, each run index is the *same world* played by two policies, and the only thing
left to count is where they disagreed. That is McNemar's test: the runs both policies
cleared and the runs neither cleared carry no information about which is better, so
they are discarded — the evidence lives entirely in the discordant pairs.

Usage:
    python Tools/paircmp_runs.py <baseline_runs.jsonl> <variant_runs.jsonl> [--key reached7F]

Keys: reached7F (full clear, the default), reached6F, reachedFloor>=N
"""
import argparse
import json
import math
import sys
from collections import OrderedDict


def load(path):
    """index -> row. Refuses duplicates: a repeated index means the two files are not
    the same 1:1 set of worlds and every pairing below would be silently wrong."""
    rows = OrderedDict()
    with open(path, "r", encoding="utf-8") as handle:
        for line_no, line in enumerate(handle, 1):
            line = line.strip()
            if not line:
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError as exc:
                sys.exit(f"{path}:{line_no}: 読めない行: {exc}")
            index = row.get("index")
            if index is None:
                sys.exit(f"{path}:{line_no}: index が無い")
            if index in rows:
                sys.exit(f"{path}: index {index} が重複している ── ペアが組めない")
            rows[index] = row
    return rows


def outcome(row, key):
    if key == "reached7F":
        # outcome の enum は GameOver/NormalClear/FullClear/Deadlock/Crash。
        # 完全クリアだけを成功とする (handoff の 0/1 報酬と同じ定義)。
        return 1 if row.get("outcome") == 2 else 0
    if key == "reached6F":
        return 1 if row.get("reached6F") else 0
    if key.startswith("reachedFloor>="):
        floor = int(key.split(">=", 1)[1])
        return 1 if int(row.get("reachedFloor", 0)) >= floor else 0
    sys.exit(f"未知のキー: {key}")


def mcnemar(b, c):
    """Exact two-sided binomial test on the discordant pairs.

    The chi-square form is the usual shortcut but it is unreliable exactly where we
    live — b + c in the low tens. The exact test costs nothing here."""
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    tail = sum(math.comb(n, i) for i in range(0, k + 1)) / (2.0 ** n)
    return min(1.0, 2.0 * tail)


def wilson(successes, total):
    if total == 0:
        return (0.0, 0.0)
    z = 1.96
    p = successes / total
    denominator = 1 + z * z / total
    centre = p + z * z / (2 * total)
    margin = z * math.sqrt(p * (1 - p) / total + z * z / (4 * total * total))
    return (max(0.0, (centre - margin) / denominator),
            min(1.0, (centre + margin) / denominator))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline")
    parser.add_argument("variant")
    parser.add_argument("--key", default="reached7F")
    parser.add_argument("--show-flips", type=int, default=10,
                        help="食い違ったランの index を何件まで並べるか")
    args = parser.parse_args()

    base = load(args.baseline)
    var = load(args.variant)

    shared = [i for i in base if i in var]
    if not shared:
        sys.exit("共通の index が 1 つも無い ── 同じシードで走っていない")

    only_base = len(base) - len(shared)
    only_var = len(var) - len(shared)

    both = neither = 0
    base_only = []   # baseline が成功、variant が失敗
    var_only = []    # variant が成功、baseline が失敗
    for index in shared:
        b = outcome(base[index], args.key)
        v = outcome(var[index], args.key)
        if b and v:
            both += 1
        elif b and not v:
            base_only.append(index)
        elif v and not b:
            var_only.append(index)
        else:
            neither += 1

    b_count = len(base_only)
    c_count = len(var_only)
    base_total = both + b_count
    var_total = both + c_count
    n = len(shared)

    print(f"指標          : {args.key}")
    print(f"ペア数        : {n}"
          + (f"  (対照のみ {only_base} / 変種のみ {only_var} は除外)"
             if only_base or only_var else ""))
    lo_b, hi_b = wilson(base_total, n)
    lo_v, hi_v = wilson(var_total, n)
    print(f"対照 (baseline): {base_total}/{n} = {100.0*base_total/n:5.1f}%"
          f"  [{100*lo_b:.1f}, {100*hi_b:.1f}]")
    print(f"変種 (variant) : {var_total}/{n} = {100.0*var_total/n:5.1f}%"
          f"  [{100*lo_v:.1f}, {100*hi_v:.1f}]")
    print()
    print("=== 一致・不一致の内訳 ===")
    print(f"  両方 成功        : {both}")
    print(f"  両方 失敗        : {neither}")
    print(f"  対照のみ成功 (b) : {b_count}   ← 変種が壊した")
    print(f"  変種のみ成功 (c) : {c_count}   ← 変種が救った")
    print()

    discordant = b_count + c_count
    if discordant == 0:
        print("**食い違ったランが 1 件も無い。** 2 つの方策は、この指標では区別できない。")
        print("平均が動いて見えてもそれは同じ結果の言い換えでしかない。")
        return

    p = mcnemar(b_count, c_count)
    print(f"McNemar 正確二項検定 (不一致 {discordant} 件): p = {p:.4f}")
    if p < 0.05:
        winner = "変種" if c_count > b_count else "対照"
        print(f"  → **{winner} が有意に良い** (5% 水準)")
    else:
        print("  → **有意差なし。** 差があるとは言えない")
        # 「あと何件ずれれば有意だったか」を実際に解く。
        # 以前ここは「片側が 0 対 N 近くまで偏る必要がある」と印字していたが、
        # **それは不一致が数件のときしか正しくない。** 不一致が 100 件を超えると
        # 必要な偏りは比例して緩むので、 あの文言は「絶望的に遠い」という誤った
        # 印象を与える (n=1000 の測定で実際に読み違えかけた)。
        lopsided = max(b_count, c_count)
        need = None
        for k in range(lopsided, discordant + 1):
            if mcnemar(discordant - k, k) < 0.05:
                need = k
                break
        if need is not None:
            print(f"     有意 (p<0.05) には {discordant} 件中 {need} 件以上の偏りが要る"
                  f" (いまは {lopsided} 件)")
        else:
            print(f"     不一致 {discordant} 件では、 どう偏っても 5% 水準に届かない")

    if args.show_flips > 0:
        if b_count:
            print(f"\n  変種が壊した index: "
                  + ", ".join(str(i) for i in base_only[:args.show_flips])
                  + (" ..." if b_count > args.show_flips else ""))
        if c_count:
            print(f"  変種が救った index: "
                  + ", ".join(str(i) for i in var_only[:args.show_flips])
                  + (" ..." if c_count > args.show_flips else ""))


if __name__ == "__main__":
    main()
