#!/usr/bin/env python3
"""並列スイープ 2 回分の runs.tsv を (アーム, runIdx) で突き合わせる。

なぜ要るか
----------
並列化はアームのシード区間をプロセスへ切って配る。 **ラン i の結果が runIdx だけの
関数**でなければ、 区間の切り方を変えただけで結果が変わり、 並列版の数字は無効になる。

`digest_cmp.py` は通常バッチの runs.jsonl 用。 こちらは並列ドライバが出す
runs.tsv 用で、 **チャンク数だけを変えた 2 回**を比べるのに使う
(例: chunks=1 と chunks=4)。 分割の仕方が結果に漏れていれば必ずずれる。

使い方
------
    python Tools/tsv_digest_cmp.py <A>/runs.tsv <B>/runs.tsv
"""
import io
import sys

# Windows の既定は cp932 で、 ✔/✘ も日本語も落ちる。 出力を UTF-8 に張り替える。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def load(path):
    """(arm, runIdx) -> (bandScore, digest)"""
    out = {}
    with io.open(path, encoding="utf-8") as f:
        header = f.readline()
        if "digest" not in header:
            raise SystemExit("ヘッダに digest がない: " + path)
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 4:
                continue
            arm, run_idx, band, digest = parts[0], parts[1], parts[2], parts[3]
            out[(arm, int(run_idx))] = (band, digest)
    return out


def main(pa, pb):
    a, b = load(pa), load(pb)
    common = sorted(set(a) & set(b))
    if not common:
        print("共通の (アーム, runIdx) がありません。 同じアーム構成の 2 回を渡すこと。")
        return 1

    mismatch = [k for k in common if a[k][1] != b[k][1]]
    band_mismatch = [k for k in common if a[k][0] != b[k][0]]

    print(f"A: {pa}  ({len(a):,} 行)")
    print(f"B: {pb}  ({len(b):,} 行)")
    print(f"A のみ / B のみ : {len(set(a) - set(b)):,} / {len(set(b) - set(a)):,}")
    print(f"共通            : {len(common):,}")
    print(f"digest 不一致   : {len(mismatch):,}  ({100.0 * len(mismatch) / len(common):.2f}%)")
    print(f"bandScore 不一致: {len(band_mismatch):,}")
    print()

    if not mismatch:
        print("✔ 全一致。 チャンクの切り方は結果に漏れていない。")
        print("  → プロセスへ何分割しても逐次版と同じ数字が出る。")
        return 0

    arms = {}
    for arm, _ in mismatch:
        arms[arm] = arms.get(arm, 0) + 1
    print("✘ 不一致あり。 **分割の仕方が結果に漏れている。**")
    for arm, n in sorted(arms.items(), key=lambda kv: -kv[1]):
        print(f"    {arm}: {n:,} 件")
    print()
    print("  ずれた行 (先頭 20):")
    for k in mismatch[:20]:
        arm, run_idx = k
        print(f"    {arm:<14} run#{run_idx}  band {a[k][0]:>3} vs {b[k][0]:>3}"
              + ("   ← band も違う" if a[k][0] != b[k][0] else ""))
    print()
    print("  ラン跨ぎの状態を持つ static を疑うこと")
    print("  (前例: CombatManager._combatSeq / 2026-08-10)。")
    return 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
