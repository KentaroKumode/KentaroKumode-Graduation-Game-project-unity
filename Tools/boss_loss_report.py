"""ボスの戦闘ごとの記録から、 予想損失 (GAME.md §13-1 の MUST の物差し) を腕 (実行ディレクトリ) ごとに出す。

使い方:
    python -X utf8 Tools/boss_loss_report.py <実行ディレクトリ> [<実行ディレクトリ> ...] [--boss boss_layer6] [--all]

  実行ディレクトリ = AutoRunLogs/learn_parallel/<日時> (名前だけでもよい)。 round_000/c*/learn_runs.json の fights を読む。
  --boss ID  : そのボスだけ (複数回指定可)。 既定はボス全部 (isBoss の行)
  --all      : ボス以外も、 深さ × 種別 (通常 / エリート) で並べる (同じ層の敵と比べる用)

数え方 (GAME.md §13-1 に合わせる):
  - fights の end が立った行だけ (2 体戦・形態の交代の途中の行は数えない)。 ヴェスカは段ごとに 1 行
  - 1 戦の損失 = (hpBefore − hpAfter) ÷ maxHp (最大 HP 比・戦闘中の回復を差し引いた純減)
  - 1 ターン当たりの損失 = Σ損失 ÷ Σturns (全戦闘)
  - 撃破までの平均ターン = 勝った戦闘 (won) の turns の平均
  - 予想損失 = 1 ターン当たりの損失 × 撃破までの平均ターン
  - 負けた戦闘は損失が残り HP で頭打ちになるので、 敗北の数と率を横に並べる
  - ダウン = その戦闘でボスがダウンしたターンの数 (downs・2026-11-14 から記録。 それより前の実行は「-」)

出力の列: 戦闘 / 勝率 / ターン 平均・中央 (勝った戦闘) / 損失 平均・中央 (最大 HP 比 %) / 1T 損失 % / 予想損失 % / 負け / ダウン 平均
"""
import glob
import json
import os
import statistics
import sys
from collections import defaultdict

ROOT = "AutoRunLogs/learn_parallel"


def run_dir(d):
    if os.path.isdir(d):
        return d
    alt = os.path.join(ROOT, d)
    if os.path.isdir(alt):
        return alt
    sys.exit(f"実行ディレクトリが無い: {d}")


def load_fights(d):
    """learn_runs.json の fights を (ラン番号, 戦闘) で返す。 同じラン番号が 2 回出たら後を捨てる (ワーカーの二重処理)。"""
    seen = set()
    out = []
    files = sorted(glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")))
    if not files:
        sys.exit(f"learn_runs.json が無い: {d}/round_000/c*/")
    for p in files:
        with open(p, encoding="utf-8") as fh:
            j = json.load(fh)
        rows = j if isinstance(j, list) else (j.get("runs") or next((v for v in j.values() if isinstance(v, list)), []))
        for r in rows:
            i = r.get("index")
            if i in seen:
                continue
            seen.add(i)
            for f in r.get("fights") or []:
                out.append(f)
    return out, len(seen)


def group_key(f, all_kinds):
    if f.get("isBoss"):
        return f.get("enemyId") or "?"
    if not all_kinds:
        return None
    parts = (f.get("key") or "").split("|")   # 鍵 = 層|種別 (B/E/N)|戦闘名 (EncounterDangerStats.KeyOf)。 追跡者の戦闘は鍵なし
    kind = {"E": "エリート", "N": "通常"}.get(parts[1] if len(parts) > 1 else "", "鍵なし")
    return f"深さ {f.get('floor', 0)} {kind}"


def summarize(fights, all_kinds, only):
    g = defaultdict(list)
    for f in fights:
        if not f.get("end") or f.get("maxHp", 0) <= 0:
            continue
        k = group_key(f, all_kinds)
        if k is None or (only and k not in only):
            continue
        g[k].append(f)
    res = {}
    for k, fs in g.items():
        n = len(fs)
        won = [f for f in fs if f.get("won")]
        losses = [100.0 * (f["hpBefore"] - f["hpAfter"]) / f["maxHp"] for f in fs]
        turns_all = sum(f.get("turns", 0) for f in fs)
        tw = [f.get("turns", 0) for f in won]
        per_turn = sum(losses) / turns_all if turns_all > 0 else 0.0
        mean_turns = statistics.mean(tw) if tw else 0.0
        has_downs = any("downs" in f for f in fs)
        res[k] = dict(
            n=n, win=100.0 * len(won) / n, t_mean=mean_turns, t_med=statistics.median(tw) if tw else 0.0,
            l_mean=statistics.mean(losses), l_med=statistics.median(losses), per_turn=per_turn,
            expected=per_turn * mean_turns, lost=n - len(won),
            downs=(statistics.mean(f.get("downs", 0) for f in fs) if has_downs else None),
            floor=fs[0].get("floor", 0), shown=fs[0].get("shownFloor", 0),
        )
    return res


def main():
    argv = sys.argv[1:]
    only, all_kinds, dirs = set(), False, []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--boss":
            only.add(argv[i + 1]); i += 2; continue
        if a == "--all":
            all_kinds = True; i += 1; continue
        dirs.append(a); i += 1
    if not dirs:
        sys.exit(__doc__)
    hdr = f"{'':24} {'戦闘':>6} {'勝率%':>6} {'T平均':>6} {'T中央':>5} {'損失%平均':>8} {'中央':>6} {'1T損失%':>7} {'予想損失%':>8} {'負け':>5} {'ダウン':>6}"
    for d in dirs:
        rd = run_dir(d)
        fights, runs = load_fights(rd)
        res = summarize(fights, all_kinds, only)
        cfg = os.path.join(rd, "config.txt")
        print(f"== {rd}  (ラン {runs}) ==")
        print(hdr)
        for k in sorted(res, key=lambda k: (res[k]["floor"], k)):
            s = res[k]
            dn = f"{s['downs']:.2f}" if s["downs"] is not None else "-"
            print(f"{k:24} {s['n']:>6} {s['win']:>6.1f} {s['t_mean']:>6.1f} {s['t_med']:>5.0f} {s['l_mean']:>8.1f} {s['l_med']:>6.1f}"
                  f" {s['per_turn']:>7.2f} {s['expected']:>8.1f} {s['lost']:>5} {dn:>6}")
        if not os.path.exists(cfg):
            print("(config.txt 無し)")
        print()


if __name__ == "__main__":
    main()
