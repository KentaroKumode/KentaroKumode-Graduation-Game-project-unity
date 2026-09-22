#!/usr/bin/env python3
"""2 バッチの deterministicDigest を index で突き合わせ、ラン跨ぎの状態依存を検出する。

なぜ要るか
----------
並列化はランをプロセスへ分割する。 **ラン i の結果が runIdx だけの関数**でなければ、
担当シードの並びが変わるだけで結果が変わり、 決定論が壊れる。

2026-08-10 に `CombatManager._combatSeq` が**ラン跨ぎで累積**していた前例がある
(ラン i がラン 1〜i−1 に依存していた)。 同種の static が他に無い保証はないので、
**並列化に着手する前にこれで確かめる**。

バッチ長を変えて同じ配分を走らせ、共通 index の digest を比べる。
累積状態があれば後続ランの履歴が変わるので必ずずれる。

使い方
------
    python Tools/digest_cmp.py AutoRunLogs/batch_n1000/runs.jsonl AutoRunLogs/batch_n2000/runs.jsonl
"""
import io
import json
import sys


def load(path):
    """index -> (digest, bandScore)。 digest が無い行は捨てる。"""
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
            if "index" not in d:
                continue
            dig = d.get("deterministicDigest") or ""
            if not dig:
                continue
            out[d["index"]] = (dig, d.get("bandScore"))
    return out


def main(pa, pb):
    a, b = load(pa), load(pb)
    common = sorted(set(a) & set(b))
    if not common:
        print("共通 index がありません。 同じ配分・同じシード列の 2 バッチを渡してください。")
        return 1

    mismatch = [i for i in common if a[i][0] != b[i][0]]
    band_mismatch = [i for i in common if a[i][1] != b[i][1]]

    print(f"A: {pa}  ({len(a):,} 本)")
    print(f"B: {pb}  ({len(b):,} 本)")
    print(f"共通 index      : {len(common):,}")
    print(f"digest 不一致   : {len(mismatch):,}  ({100.0*len(mismatch)/len(common):.2f}%)")
    print(f"bandScore 不一致: {len(band_mismatch):,}")
    print()

    if not mismatch:
        print("✔ 全一致。 ラン i の結果は runIdx だけで決まっている。")
        print("  → シードをプロセスへ分割しても結果は変わらない。 並列化して安全。")
        return 0

    first = mismatch[0]
    print("✘ 不一致あり。 **ラン跨ぎの状態依存が残っている。**")
    print(f"  最初にずれた index: {first}"
          f"  (band {a[first][1]} vs {b[first][1]})")
    print(f"  ずれ始めた位置より前は {sum(1 for i in common if i < first):,} 本一致")
    print()
    print("  並列化すると担当シードの並びが変わるので、 この依存があると結果が変わる。")
    print("  先に原因の static を潰すこと (前例: CombatManager._combatSeq / 2026-08-10)。")
    return 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
