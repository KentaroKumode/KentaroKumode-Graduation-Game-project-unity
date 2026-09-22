#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""ランダム付与 ITT を **離散時間生存モデル**で当て、Δクリア率 (pt) を出す。

C# 側 (Assets/Scripts/AutoTest/GrantItt.cs の RecomputeSurvival) と同じモデルの
独立実装。IRLS は発散しうるので、2 つの実装が同じ数字を出すことを確認するために使う。

  logit h_k = α_k + Σ βᵢ·zᵢ_k        h_k = P(段 k で止まる | 段 k に到達)
  P(クリア)  = Π_k (1 − h_k)

  zᵢ_k = 「段 k を踏む時点でその品を持っていたか」
       = 付与層 gᵢ ≤ FloorOfLevel[k]

**なぜ二値 1[band==11] のロジスティックではないのか。**
3 層で死んだランも 1〜3 段のハザードの推定に寄与する。全ランが全段に効くので、
クリアを直接の目的にしたまま情報を捨てずに済む。付与層が入るので、
「5 層で配られた品が 1〜4 層のハザードに影響しない」ことも自動的に扱える。

行フォーマット (AutoRunner.RecordGrantRow):
    band | gold@floor | id@floor,id@floor,... | 与ダメ,被ダメ,戦闘,勝,回復,盾
4 列目は**処置後**に決まる量なので読まない (合流点)。

使い方:
    python Tools/itt_survival.py AutoRunLogs/grant_trial/grant_runs.csv
    python Tools/itt_survival.py <csv> --tail 25000     # 末尾だけ (バランス版が揃う範囲)
"""
from __future__ import print_function
import io
import sys
import numpy as np

# band レベル k を踏む階層。R1a〜R1d(=band 1) は 1〜2 層をまとめている。
FLOOR_OF_LEVEL = [0, 2, 3, 3, 4, 4, 5, 5, 5, 6, 7, 7]
MAX_LEVEL = 11
GRANT_FLOORS = [1, 2, 3, 4, 5]
RIDGE = 1.0
MIN_TREATED = 300


def read_rows(path, tail=0, head=0):
    rows = []
    with io.open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            parts = line.split("|")
            if len(parts) < 3:
                continue
            try:
                band = int(parts[0])
            except ValueError:
                continue
            if band < 1 or band > MAX_LEVEL:
                continue
            gold, gfl = 0, 0
            if "@" in parts[1]:
                a, b = parts[1].split("@", 1)
                gold, gfl = int(a), int(b)
            ids, fls = [], []
            if parts[2]:
                for tok in parts[2].split(","):
                    if "@" not in tok:
                        continue
                    i, fl = tok.rsplit("@", 1)
                    ids.append(i)
                    fls.append(max(1, int(fl)))
            rows.append((band, ids, fls, gold, gfl))
    if head:
        rows = rows[:head]
    if tail:
        rows = rows[-tail:]
    return rows


def build(rows):
    """(段観測の設計行列, 応答) を組む。行 = (ラン, 段) の at-risk 観測。"""
    counts = {}
    golds = set()
    for band, ids, fls, gold, gfl in rows:
        for i in ids:
            counts[i] = counts.get(i, 0) + 1
        if gold > 0:
            golds.add(gold)
    cols = sorted(counts)
    idx = {c: j for j, c in enumerate(cols)}
    gcols = sorted(golds)
    gidx = {g: len(cols) + j for j, g in enumerate(gcols)}
    n_stage = MAX_LEVEL - 1                      # 段 k = 2..11
    sbase = len(cols) + len(gcols)
    D = sbase + n_stage

    xs, ys = [], []
    for band, ids, fls, gold, gfl in rows:
        last = min(band + 1, MAX_LEVEL)
        for k in range(2, last + 1):
            fk = FLOOR_OF_LEVEL[k]
            act = [sbase + (k - 2)]
            for i, fl in zip(ids, fls):
                if fl <= fk and i in idx:
                    act.append(idx[i])
            if gold > 0 and gfl <= fk:
                act.append(gidx[gold])
            xs.append(act)
            ys.append(1.0 if band == k - 1 else 0.0)
    return cols, gcols, sbase, D, xs, np.asarray(ys), counts


def irls(D, xs, y, sbase, n_iter=30):
    beta = np.zeros(D)
    beta[sbase:] = -2.0
    for it in range(n_iter):
        H = np.zeros((D, D))
        g = np.zeros(D)
        for act, yi in zip(xs, y):
            eta = beta[act].sum()
            mu = 1.0 / (1.0 + np.exp(-eta))
            w = max(1e-6, mu * (1 - mu))
            r = yi - mu
            for a in act:
                g[a] += r
                for b in act:
                    H[a, b] += w
        for i in range(sbase):
            H[i, i] += RIDGE
        step = np.linalg.solve(H, g)
        step = np.clip(step, -1.0, 1.0)
        beta += step
        if np.abs(step).max() < 1e-6:
            break
    for i in range(sbase):
        H[i, i] += RIDGE
    cov = np.linalg.inv(H)
    return beta, cov, it + 1


def clear_prob(alpha, b, grant_floor):
    p = 1.0
    for k in range(2, MAX_LEVEL + 1):
        eta = alpha[k]
        if grant_floor > 0 and grant_floor <= FLOOR_OF_LEVEL[k]:
            eta += b
        p *= 1.0 - 1.0 / (1.0 + np.exp(-eta))
    return p


def main():
    path = sys.argv[1]
    tail = head = 0
    for i, a in enumerate(sys.argv):
        if a == "--tail":
            tail = int(sys.argv[i + 1])
        if a == "--head":
            head = int(sys.argv[i + 1])

    rows = read_rows(path, tail=tail, head=head)
    cols, gcols, sbase, D, xs, y, counts = build(rows)
    print("ラン %d / 段観測 %d / 品 %d / 金 %s / 母数 %d"
          % (len(rows), len(xs), len(cols), gcols, D))

    beta, cov, iters = irls(D, xs, y, sbase)
    alpha = [0.0] * (MAX_LEVEL + 1)
    for k in range(2, MAX_LEVEL + 1):
        alpha[k] = beta[sbase + (k - 2)]
    p0 = clear_prob(alpha, 0.0, 0)
    print("IRLS %d 反復 / 基準クリア率 %.2f%%" % (iters, p0 * 100))
    print("段別ハザード: " + " ".join(
        "k%d=%.3f" % (k, 1 / (1 + np.exp(-alpha[k]))) for k in range(2, MAX_LEVEL + 1)))
    for j, gv in enumerate(gcols):
        bg = beta[len(cols) + j]
        print("  [金 %dG] Δクリア %.3fpt (層3で受領)" % (gv, (clear_prob(alpha, bg, 3) - p0) * 100))

    out = []
    h = 1e-3
    for j, c in enumerate(cols):
        b = beta[j]
        per = [(clear_prob(alpha, b, f) - p0) * 100 for f in GRANT_FLOORS]
        mean = float(np.mean(per))
        up = np.mean([clear_prob(alpha, b + h, f) for f in GRANT_FLOORS])
        dn = np.mean([clear_prob(alpha, b - h, f) for f in GRANT_FLOORS])
        deriv = (up - dn) / (2 * h) * 100
        se = abs(deriv) * float(np.sqrt(max(0, cov[j, j])))
        out.append((mean, se, counts[c], c, per))
    out.sort(reverse=True)

    print("\n%-28s %8s %7s %7s | %s" % ("品", "Δクリア", "SE", "t", "層1..5"))
    for mean, se, nt, c, per in out:
        flag = "" if nt >= MIN_TREATED else "  ※標本不足"
        print("%-28s %8.3f %7.3f %7.2f | %s%s"
              % (c, mean, se, mean / se if se > 0 else 0,
                 " ".join("%6.2f" % v for v in per), flag))

    arr = np.array([o[0] for o in out if o[2] >= MIN_TREATED])
    if len(arr):
        qs = np.percentile(arr, [95, 85, 55, 30, 50])
        print("\n分位 (Δクリア pt): 上位5%%=%.3f 15%%=%.3f 45%%=%.3f 70%%=%.3f 中央=%.3f min=%.3f max=%.3f"
              % (qs[0], qs[1], qs[2], qs[3], qs[4], arr.min(), arr.max()))
        sig = sum(1 for m, s, n_, c, p in out if n_ >= MIN_TREATED and s > 0 and abs(m) >= 2 * s)
        print("2σ を超えた品: %d / %d  (うち正 %d)"
              % (sig, len(arr),
                 sum(1 for m, s, n_, c, p in out if n_ >= MIN_TREATED and s > 0 and m >= 2 * s)))


if __name__ == "__main__":
    main()
