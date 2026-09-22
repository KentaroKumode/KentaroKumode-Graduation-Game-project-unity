#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""AutoRunner のフィールドをアームごとに振るスイープ driver。

**キーの取り違えが一番高くつく。** 2026-09-13 に `useIttBeta` を運び漏らして
11.5pt ぶんの偽の結論を出し、 スイープ数本を捨てた。 だから設定は
**直近の Editor 版 job.json をそのまま雛形にして**、 アーム固有のキーだけを
上書き・追加する形にする ── 手で並べ直さない。

アームは JSON で渡す:
    [{"label": "A", "eliteDropRate": 1.0, "combatGoldScale": 4}, ...]
label 以外のキーはそのまま AutoRunner のフィールド名として運ばれる
(受け側は反射なので、 フィールドを足せば勝手に通る)。

使い方:
    python Tools/nav_elite_sweep.py --arms Tools/arms/econ_elite.json --runs 10000 --chunks 6
"""
import argparse
import glob
import io
import json
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "UltraProductionWorkerBuild", "UltraProductionWorker.exe")
DESC = os.path.join(ROOT, "UltraProductionWorkerBuild", "ultra-production-worker.json")
SEED_BASE = 60000          # AutoRunner.RankMarginSeedBase

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


def newest_editor_job():
    """直近の Editor 版 job.json を設定の雛形として拾う。"""
    cands = sorted(glob.glob(os.path.join(
        ROOT, "AutoRunLogs", "rank_margin_parallel", "*", "*_c00", "job.json")))
    if not cands:
        sys.exit("雛形にする job.json が無い。 先に Editor の並列メニューを一度回すこと")
    return cands[-1]


REPORT_EVERY_SEC = 20
STALL_SEC = 120
progress_of = {}   # job.json -> (アーム名, progress.txt, そのチャンクのラン数)


def read_completed(path, size):
    """progress.txt の completed=N。 まだ無ければ 0、 response.json があれば完了扱い。"""
    resp = os.path.join(os.path.dirname(path), "response.json")
    if os.path.exists(resp):
        return size
    try:
        for line in io.open(path, encoding="utf-8"):
            if line.startswith("completed="):
                return min(size, int(line.split("=", 1)[1]))
    except Exception:
        pass
    return 0


def main():
    sys.stdout.reconfigure(line_buffering=True)   # 背景実行でファイルへ書くときも 1 行ずつ出す
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=10000)
    ap.add_argument("--chunks", type=int, default=4)
    ap.add_argument("--parallel", type=int, default=22)
    ap.add_argument("--arms", required=True, help="アーム定義 JSON")
    args = ap.parse_args()

    arms = json.load(io.open(args.arms, encoding="utf-8"))
    if not arms:
        sys.exit("アームが空")

    # 古い出力を刈ってから始める (2026-09-22)。 スイープは 1 回で 25 チャンク × 数ファイルを作り、
    #   何も消さないので、 放っておくと git status とディスクが際限なく膨らむ。
    #   残す件数などの規則は prune_outputs.py 側に 1 か所だけ置く。
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import prune_outputs
    prune_outputs.prune(quiet=True)

    if not os.path.exists(EXE):
        sys.exit("Player が無い: " + EXE)
    fingerprint = json.load(io.open(DESC, encoding="utf-8"))["fingerprints"]["buildFingerprint"]

    tmpl = json.load(io.open(newest_editor_job(), encoding="utf-8"))
    keys = list(tmpl["configKeys"])
    vals = list(tmpl["configValues"])
    print("雛形: " + newest_editor_job())
    print("  運ぶキー {} 個: {}".format(len(keys), ", ".join(keys)))
    for k in ("useIttBeta", "useMutualAttackPipeline", "metaBuffMode", "forceNoRelic"):
        print("  {:<26} = {}".format(k, vals[keys.index(k)] if k in keys else "**欠落**"))

    tag = os.path.splitext(os.path.basename(args.arms))[0]
    session = os.path.join(ROOT, "AutoRunLogs", "sweep",
                           time.strftime("%Y%m%d_%H%M%S") + "_" + tag)
    os.makedirs(session, exist_ok=True)

    slots = []
    print("\n── アーム定義 ──")
    for arm in arms:
        label = arm["label"]
        # focusTrack / focusRank は AutoRunner のフィールドではなく job のヘッダ。
        #   **アーム定義から分離する** ── configKeys へ混ぜると「未知の設定フィールド」警告になる。
        track = arm.get("focusTrack", "")
        frank = int(arm.get("focusRank", 0))
        rspec = arm.get("rankSpec", "")
        # seedOffset: アームごとに担当するシード区間をずらす。
        #   8 通りの配分で 1/8 ずつ分担し、 合算で 60000..69999 を 1 回ずつ覆う用途。
        soff = int(arm.get("seedOffset", 0))
        # アームごとのラン数。 **変種へ分担させるときは --runs ではなくこちらが正**。
        nruns = int(arm.get("runs", args.runs))
        over = {k: v for k, v in arm.items()
                if k not in ("label", "focusTrack", "focusRank", "rankSpec",
                             "seedOffset", "runs")}
        print("  {:<14} {:<34}{}".format(label,
              rspec if rspec else ((track + " r" + str(frank)) if track else "Balanced"), "  ".join(
            "{}={}".format(k, v) for k, v in sorted(over.items()))))
        # アーム固有のキーを**雛形のコピーへ**足す。 雛形は書き換えない。
        ak, av = list(keys), list(vals)
        for k, v in over.items():
            # 文字列は**そのまま**渡す。 repr() だと引用符ごと運ばれ、 "id:atk,..." のような
            #   区切り指定の先頭と末尾の要素が読めなくなる (2026-09-19: enemyAttackSpec の
            #   boss_layer1 と boss_layer7_p4 だけ上書きが効いていなかった)。
            sv = (("True" if v else "False") if isinstance(v, bool)
                  else v if isinstance(v, str) else repr(v))
            if k in ak:
                av[ak.index(k)] = sv      # 雛形に同名があれば上書き
            else:
                ak.append(k); av.append(sv)
        per, extra, offset = nruns // args.chunks, nruns % args.chunks, 0
        for c in range(args.chunks):
            size = per + (1 if c < extra else 0)
            if size <= 0:
                continue
            d = os.path.join(session, "{}_c{:02d}".format(label, c))
            os.makedirs(d, exist_ok=True)
            ck, cv = list(ak), list(av)
            # 付与試行モード: **チャンクごとに担当区間をずらす**。
            #   runIdx = 90000 + randomGrantStartIndex + i なので、 ここを分けないと
            #   全プロセスが同じ割り当てを引き、 行数だけ増えて情報が増えない。
            if str(over.get("randomGrantTrial", "")).lower() == "true":
                if "randomGrantStartIndex" in ck:
                    cv[ck.index("randomGrantStartIndex")] = repr(soff + offset)
                else:
                    ck.append("randomGrantStartIndex"); cv.append(repr(soff + offset))
            job = {
                "label": label, "focusTrack": track, "focusRank": frank, "rankSpec": rspec,
                "runs": size, "seedStart": SEED_BASE + soff + offset,
                "configKeys": ck, "configValues": cv,
                "responsePath": os.path.join(d, "response.json"),
                "progressPath": os.path.join(d, "progress.txt"),
                "outputRoot": os.path.join(d, "AutoRunLogs"),
                "buildFingerprint": fingerprint,
            }
            jp = os.path.join(d, "job.json")
            io.open(jp, "w", encoding="utf-8").write(json.dumps(job, ensure_ascii=False, indent=4))
            slots.append((label, jp, job["responsePath"], os.path.join(d, "worker.log")))
            progress_of[jp] = (label, job["progressPath"], size)
            offset += size

    total = sum(int(a.get("runs", args.runs)) for a in arms)
    print("\n{} アーム × {:,} ラン = {:,} ラン / {} プロセス (同時 {})".format(
        len(arms), args.runs, total, len(slots), args.parallel))
    print("出力: " + session)

    pending, running, t0 = list(slots), [], time.time()
    last_report, last_total, last_change = t0, -1, t0
    while pending or running:
        while pending and len(running) < args.parallel:
            label, jp, rp, lp = pending.pop(0)
            log = io.open(lp, "w", encoding="utf-8", errors="replace")
            p = subprocess.Popen(
                [EXE, "-batchmode", "-nographics", "--rank-margin-job", jp],
                stdout=log, stderr=subprocess.STDOUT, cwd=ROOT)
            running.append((p, label, rp, log))
        time.sleep(2.0)
        still = []
        for p, label, rp, log in running:
            if p.poll() is None:
                still.append((p, label, rp, log))
            else:
                log.close()
                if p.returncode != 0:
                    print("  !! {} 異常終了 rc={}".format(label, p.returncode))
        if len(still) != len(running):
            done = len(slots) - len(pending) - len(still)
            el = time.time() - t0
            print("  {}/{} 完了  {:.0f}s  ({:.0f} ラン/秒)".format(
                done, len(slots), el, (done / max(1, len(slots))) * total / max(1e-9, el)))
        running = still

        # 途中経過: worker が書く progress.txt (completed=N) を集計する。
        #   チャンク完了の行だけだと、 最初のチャンクが終わるまで何も出ず、 止まっているのか区別できない。
        now = time.time()
        if now - last_report >= REPORT_EVERY_SEC or not (pending or running):
            last_report = now
            by_arm, done_runs = {}, 0
            for lab, pp, size in progress_of.values():
                c = read_completed(pp, size)
                done_runs += c
                a = by_arm.setdefault(lab, [0, 0]); a[0] += c; a[1] += size
            if done_runs != last_total:
                last_total, last_change = done_runs, now
            el = now - t0
            rate = done_runs / max(1e-9, el)
            eta = "{:.0f}s".format((total - done_runs) / rate) if rate > 0 else "?"
            arms_s = "  ".join("{} {:,}/{:,}".format(k, v[0], v[1]) for k, v in by_arm.items())
            print("  [{:>4.0f}s] {:,}/{:,} ラン ({:.0%})  {:.0f} ラン/秒  残り約 {}  | {}".format(
                el, done_runs, total, done_runs / max(1, total), rate, eta, arms_s))
            if now - last_change >= STALL_SEC and (pending or running):
                print("  !! {:.0f} 秒間 進捗なし ── スタックの疑い (worker.log を確認)".format(now - last_change))

    print("\n完了 {:.0f}s".format(time.time() - t0))
    io.open(os.path.join(session, "SESSION"), "w", encoding="utf-8").write(session)
    print(session)


if __name__ == "__main__":
    main()
