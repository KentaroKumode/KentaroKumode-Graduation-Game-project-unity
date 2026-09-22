#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""並列ワーカーが吐いた付与ログを 1 本へまとめる (2026-09-17)。

ITT は **割り当て行を全部 1 つの回帰へ入れて初めて** 意味を持つ。 プロセスごとの
CSV を別々に推定すると、 1 品あたりの処置ラン数が 1/24 になって全部が標本不足になる。

**設計が違うログを混ぜないこと。** 付与プールの語彙も 1 品あたりの付与確率も
アームごとに違う。 このスクリプトは 1 セッション分だけを集める。

使い方:
    python Tools/itt_merge.py                      # 直近の itt_allkinds セッション
    python Tools/itt_merge.py <sessionディレクトリ>
"""
import glob
import io
import os
import shutil
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


def main():
    if len(sys.argv) > 1:
        session = sys.argv[1].rstrip("/\\")
    else:
        # **末尾を固定しない。** `*_itt_allkinds` は `..._itt_allkinds4` にマッチせず、
        #   2026-09-17 に**古いセッションを黙って再マージした**。 前方一致にする。
        cands = sorted(glob.glob(os.path.join(ROOT, "AutoRunLogs", "sweep", "*_itt_allkinds*")))
        if not cands:
            sys.exit("itt_allkinds* のセッションが無い")
        session = cands[-1]
        print("(自動選択) 候補 {} 件 → 最新を採用".format(len(cands)))
    print("session: " + session)

    parts = sorted(glob.glob(os.path.join(
        session, "*", "AutoRunLogs", "grant_trial", "allkinds_grant_runs.csv")))
    if not parts:
        sys.exit("付与ログが 1 つも無い: " + session)

    rows, seen = [], 0
    for p in parts:
        n = 0
        for line in io.open(p, encoding="utf-8", errors="replace"):
            line = line.rstrip("\n")
            if line:
                rows.append(line)
                n += 1
        seen += 1
        print("  {:>6,} 行  {}".format(n, os.path.relpath(p, session)))
    print("\nチャンク {} / 合計 {:,} 行".format(seen, len(rows)))

    out_dir = os.path.join(ROOT, "AutoRunLogs", "grant_trial")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "allkinds_grant_runs.csv")
    if os.path.exists(out):
        bak = out + "." + time.strftime("%Y%m%d_%H%M%S") + ".bak"
        shutil.move(out, bak)
        print("既存を退避: " + os.path.basename(bak))
    io.open(out, "w", encoding="utf-8").write("\n".join(rows) + "\n")
    print("書き出し: " + out)

    # 学習ディレクトリの既存 ITT を退避する。 **上書きすると戻せない** ──
    #   新しい fit が悪かったときに旧序列へ戻す道を残す。
    root = os.path.join(ROOT, "AutoRunLogs", "learning", "buffOn_debuffOff")
    stamp = time.strftime("%Y%m%d_%H%M%S")
    for name in ("itt_fit.txt", "itt_clear.txt", "itt_clear.json", "itt_effects.json"):
        p = os.path.join(root, name)
        if os.path.exists(p):
            shutil.copy2(p, p + "." + stamp + ".preallkinds")
            print("退避: {}.{}.preallkinds".format(name, stamp))
    print("\n次: Editor で GrantItt.Recompute → RecomputeSurvival を回す")


if __name__ == "__main__":
    main()
