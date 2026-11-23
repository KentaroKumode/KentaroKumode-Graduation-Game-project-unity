#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""テスト・計測の出力を直近だけ残して刈る (2026-09-22)。

**なぜ要るか。** 自動周回・スイープ・並列ランナーは実行のたびに出力を作り、 何も消さない。
2026-09-22 時点で AutoRunLogs が 2.9GB、 ワーカー用ビルドが 1.7GB、 git status の未追跡が
約 2 万件あった。 git 側は .gitignore で除外したので、 ここはディスク側の上限を保つ役。

**消してよいのは「再生成できる出力」だけ。** 学習データの本体 (learning/<profile>/ 直下と
帯別ディレクトリ) とワーカー用ビルド本体には触らない ── 学習は 250,000 ラン ぶんの蓄積で、
帯別ディレクトリは高難易度帯の BOT が読む種なので、 消すと挙動が変わる。

新しさは更新時刻で判定する (ディレクトリ名の日付に頼らない)。

使い方:
    python Tools/prune_outputs.py            # 実際に消す
    python Tools/prune_outputs.py --dry-run  # 何が消えるかだけ出す
nav_elite_sweep.py と自動周回の開始時にも自動で呼ばれる。
"""
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOGS = os.path.join(ROOT, "AutoRunLogs")
WORKER = os.path.join(ROOT, "UltraProductionWorkerBuild")

# (場所, 何を数えるか, 残す件数)。 残す件数は「直近の比較に足りる」ぶん。
KEEP_RUNS = 20        # スイープ・バッチ・並列ランナーのセッション
KEEP_BACKUPS = 2      # 学習データのバックアップ (リセット前の退避)
KEEP_LOOSE = 30       # AutoRunLogs 直下のばらのレポート (itemablation_*.txt など)
# 並列の学習・測定 (learn_parallel・ItemLearningParallelRunner) は 1 つの測定が数十〜数百のフォルダになるので、
# 件数でなく日数で残す: いちばん新しいフォルダから KEEP_PARALLEL_DAYS 日以内。 2026-11-18 まで対象外で、
# 11-04〜11-13 の分が 117 GB たまってディスクの空きが 1.7 GB まで減った。 基準として残したい測定は Captures/ へ写す。
KEEP_PARALLEL_DAYS = 7
# 2026-11-23 (作者): 「3 回以上前のデータは学習・統計・全てで不要」。 1 回 = ITT の撮り直し 1 回から次の撮り直しまで
# (秘宝・仕組みを変えた較正の 1 周)。 learn_parallel の ITT のフォルダ (config.txt に randomGrantTrial = True) のうち
# 新しい方から KEEP_ROUNDS 番目より前に作られたものは、 並列・バッチ・スイープ・ITT の控え・学習のバックアップまで全部消す。
# ITT が KEEP_ROUNDS 回に満たなければ、 回での刈り込みはしない (件数・日数の上限だけ)。
KEEP_ROUNDS = 3

# ワーカー用ビルドの本体。 **これ以外の直下ディレクトリは古い実験の出力**として扱う。
WORKER_BUILD_PARTS = {"AutoRunLogs", "MonoBleedingEdge", "UltraProductionWorker_Data"}


def _size(path):
    if os.path.isfile(path):
        return os.path.getsize(path)
    total = 0
    for dp, _, fs in os.walk(path):
        for f in fs:
            try:
                total += os.path.getsize(os.path.join(dp, f))
            except OSError:
                pass
    return total


def _newest_first(paths):
    return sorted(paths, key=lambda p: os.path.getmtime(p), reverse=True)


def _born(path):
    """作った時刻 (Windows では作成時刻)。 ITT のフォルダは最後まで書き足されて更新時刻が後ろへずれるので、 回の境は作成時刻で見る。"""
    try:
        return os.path.getctime(path) if os.name == "nt" else os.path.getmtime(path)
    except OSError:
        return float("inf")


def _is_itt(path):
    cfg = os.path.join(path, "config.txt")
    try:
        with open(cfg, encoding="utf-8-sig", errors="replace") as f:
            return any(ln.split("=")[0].strip() == "randomGrantTrial" and ln.split("=", 1)[1].strip() == "True"
                       for ln in f if "=" in ln)
    except OSError:
        return False


def _round_cutoff(par_dirs):
    """新しい方から KEEP_ROUNDS 番目の ITT を作った時刻。 これより前に作った物が「3 回以上前」。 ITT が足りなければ None。"""
    itts = sorted((_born(p) for p in par_dirs if _is_itt(p)), reverse=True)
    return itts[KEEP_ROUNDS - 1] if len(itts) >= KEEP_ROUNDS else None


def _plan():
    """(消す対象, 理由) の一覧を返す。"""
    doomed = []

    def keep_newest(paths, n, why):
        for p in _newest_first(paths)[n:]:
            doomed.append((p, why))

    def subdirs(d, pred=lambda name: True):
        if not os.path.isdir(d):
            return []
        return [os.path.join(d, x) for x in os.listdir(d)
                if os.path.isdir(os.path.join(d, x)) and pred(x)]

    cutoff = _round_cutoff(subdirs(os.path.join(LOGS, "learn_parallel")))

    def before_cutoff(paths, why):
        if cutoff is None:
            return
        for p in paths:
            if _born(p) < cutoff and all(p != d for d, _ in doomed):
                doomed.append((p, why))

    # --- AutoRunLogs ---
    keep_newest(subdirs(os.path.join(LOGS, "sweep"), lambda x: not x.startswith("_")),
                KEEP_RUNS, "スイープ")
    keep_newest(subdirs(LOGS, lambda x: x.startswith("batch_")), KEEP_RUNS, "バッチ")
    keep_newest(subdirs(os.path.join(LOGS, "rank_margin_parallel")), KEEP_RUNS, "並列ランナー")
    keep_newest(subdirs(os.path.join(LOGS, "grant_trial")), KEEP_RUNS, "付与試行")
    par = subdirs(os.path.join(LOGS, "learn_parallel"))
    if par:
        newest = max(os.path.getmtime(p) for p in par)
        for p in par:
            if os.path.getmtime(p) < newest - KEEP_PARALLEL_DAYS * 86400:
                doomed.append((p, "並列の学習・測定"))
    loose = [os.path.join(LOGS, x) for x in os.listdir(LOGS)] if os.path.isdir(LOGS) else []
    keep_newest([p for p in loose if os.path.isfile(p)], KEEP_LOOSE, "ばらのレポート")

    # --- 3 回以上前 (KEEP_ROUNDS) の物は件数・日数に関係なく全部 ---
    before_cutoff(par, "3 回以上前の並列の学習・測定")
    before_cutoff(subdirs(os.path.join(LOGS, "sweep"), lambda x: not x.startswith("_")), "3 回以上前のスイープ")
    before_cutoff(subdirs(LOGS, lambda x: x.startswith("batch_")), "3 回以上前のバッチ")
    before_cutoff(subdirs(os.path.join(LOGS, "rank_margin_parallel")), "3 回以上前の並列ランナー")
    before_cutoff([p for p in loose if os.path.isfile(p)], "3 回以上前のばらのレポート")
    gt = os.path.join(LOGS, "grant_trial")
    if os.path.isdir(gt):   # ITT の控え (allkinds_grant_runs.csv.<日時>.bak)。 本体の csv は読む物なので残す
        before_cutoff([os.path.join(gt, x) for x in os.listdir(gt) if ".bak" in x], "3 回以上前の ITT の控え")
    before_cutoff(subdirs(LOGS, lambda x: x.startswith("learning_backup")), "3 回以上前の学習のバックアップ")
    before_cutoff(subdirs(os.path.join(LOGS, "learning"), lambda x: x.startswith("_backup")), "3 回以上前の学習のバックアップ")

    # --- 学習データのバックアップ (本体には触らない) ---
    for learn in (os.path.join(LOGS, "learning"), os.path.join(WORKER, "AutoRunLogs", "learning")):
        keep_newest(subdirs(learn, lambda x: x.startswith("_backup")), KEEP_BACKUPS, "学習バックアップ")

    # --- ワーカー用ビルド: 本体以外の直下ディレクトリは古い実験の出力 ---
    keep_newest(subdirs(WORKER, lambda x: x not in WORKER_BUILD_PARTS), 0, "ワーカー内の実験出力")
    wlog = os.path.join(WORKER, "BALANCE_CHANGELOG_buffOn_debuffOff.md")
    if os.path.isfile(wlog):
        doomed.append((wlog, "ワーカーが追記した changelog"))
    return doomed


def prune(dry_run=False, quiet=False):
    doomed = _plan()
    total = 0
    by_why = {}
    for p, why in doomed:
        s = _size(p)
        total += s
        n, b = by_why.get(why, (0, 0))
        by_why[why] = (n + 1, b + s)
        if not dry_run:
            if os.path.isdir(p):
                shutil.rmtree(p, ignore_errors=True)
            else:
                try:
                    os.remove(p)
                except OSError:
                    pass
    if not quiet or doomed:
        verb = "消す予定" if dry_run else "消した"
        for why, (n, b) in sorted(by_why.items(), key=lambda kv: -kv[1][1]):
            print(f"  {why:18} {n:5} 件  {b / 1e6:9.1f} MB")
        print(f"  {verb}: 計 {len(doomed)} 件 / {total / 1e9:.2f} GB")
    return len(doomed), total


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    prune(dry_run="--dry-run" in sys.argv)
