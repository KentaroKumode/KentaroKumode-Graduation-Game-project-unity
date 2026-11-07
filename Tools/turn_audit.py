"""配線の判断の記録 (AutoRunner.turnAudit → turn_audit.tsv) を層ごとに集計する。
使い方: python -X utf8 Tools/turn_audit.py <実行ディレクトリ> [...]
見るもの (2026-11-06): 後半の被弾がブロックの幅 (3 個の出目の合計) の中にあるか、 ブロックに挿しているか。
  被弾/3個 = 予告の敵攻撃 ÷ 出目の合計 (全部ブロックに挿したら止まる量) の中央値
  受け切れる = 予告 ≤ 出目の合計 のターンの率
  ブロック率 = ブロックに 1 本以上挿したターンの率 / 平均の本数
加えて learn_runs.json から 層ごとの死亡率 (その層に着いたランのうち、 そこで死んだ率) を出す。"""
import glob
import json
import os
import statistics
import sys
from collections import defaultdict

DEPTH = {0: 0, 1: 1, 2: 3, 3: 5, 4: 6, 5: 7}
KIND = {0: "雑魚", 1: "精鋭", 2: "ボス"}


def resolve(d):
    return d if os.path.isdir(d) else os.path.join("AutoRunLogs/learn_parallel", d)


def turns(d):
    g = defaultdict(list)
    for p in glob.glob(os.path.join(d, "round_000", "c*", "AutoRunLogs", "turn_audit.tsv")):
        for ln in open(p, encoding="utf-8"):
            f = ln.split("\t")
            if len(f) != 13:
                continue
            fl, kind, turn, heavy, atk, hp, mhp, sh, s, a, b, c, nb = map(int, f)
            g[(fl, kind if not heavy else 3)].append((atk, s, a, b, c, nb, hp, mhp))
    return g


def deaths(d):
    R = []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        R += j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
    return R


def main():
    for d in map(resolve, sys.argv[1:]):
        print(f"== {os.path.basename(d.rstrip('/'))}")
        g = turns(d)
        print(f"{'深さ':>4} {'種別':<6}{'ターン':>8}{'予告 中央':>9}{'出目計':>7}{'被弾/3個':>9}{'受け切れる':>10}{'ブロック率':>10}{'本数':>6}{'攻撃に':>7}")
        for (fl, k) in sorted(g):
            v = g[(fl, k)]
            if len(v) < 50:
                continue
            ratio = statistics.median(x[0] / max(1, x[1]) for x in v)
            print(f"{DEPTH.get(fl, fl):>4} {KIND.get(k, '大技'):<6}{len(v):>8}{statistics.median(x[0] for x in v):>9.0f}{statistics.mean(x[1] for x in v):>7.1f}"
                  f"{ratio:>9.2f}{sum(1 for x in v if x[0] <= x[1]) / len(v):>10.0%}{sum(1 for x in v if x[5] > 0) / len(v):>10.0%}"
                  f"{statistics.mean(x[5] for x in v):>6.2f}{statistics.mean(x[2] for x in v):>7.1f}")
        R = deaths(d)
        # 「受け切るか、 喰らって火力に回すか」が成り立つ条件 (2026-11-06):
        #   開始時 HP = 戦闘開始時の HP ÷ 最大 HP (いつも満タンなら喰らう代償が無い)。 fights の floor は深さ (ターン記録は ordinal)
        #   (出目の比重は 出目 ÷ (武器 + 出目 + 攻撃+N) で測る。 与ダメで割ると倍率が分母に入って不当に小さく出る)
        if R:
            hp0 = defaultdict(list)
            for r in R:
                for f in r.get("fights") or []:
                    if f.get("turns", 0) > 0 and f.get("enemyMaxHp", 0) > 1:
                        hp0[(f["floor"], 2 if f.get("isBoss") else 0)].append(f["hpBefore"] / max(1, f["maxHp"]))
            print(f"{'深さ':>4} {'種別':<6}{'開始時HP 平均':>12}{'50%未満':>9}")
            for (fl, kind) in sorted(hp0):
                v = hp0[(fl, kind)]
                if len(v) >= 30:
                    print(f"{fl:>4} {KIND[kind]:<6}{statistics.mean(v):>12.0%}{sum(1 for x in v if x < 0.5) / len(v):>9.0%}")
        # 与ダメの内訳 (fights[].dmgParts = [武器, 攻撃の出目, 攻撃+N, 主攻撃, 固定, 追撃])
        #   出目の比重 = 出目 ÷ (武器 + 出目 + 攻撃+N): 倍率はこの合計に掛かるので、 1 個を攻撃へ回したときの主攻撃の伸び率に等しい
        #   主攻撃 / 固定 / その他 (毒・出血など = 与ダメ − 主攻撃 − 固定 − 追撃) の与ダメに占める割合
        #   7 列目 (2026-11-06〜) = 攻撃+N のうち出目から来た分 (出目への上乗せ率・切り返し)。 出目の比重に含める
        P = defaultdict(lambda: [0] * 8)
        for r in R:
            for f in r.get("fights") or []:
                p = list(f.get("dmgParts") or [])
                if len(p) == 6:
                    p.append(0)
                if len(p) == 7 and f.get("enemyMaxHp", 0) > 1:
                    a = P[(f["floor"], 2 if f.get("isBoss") else 0)]
                    for i in range(7):
                        a[i] += p[i]
                    a[7] += f["dealt"]
        if P:
            print(f"{'深さ':>4} {'種別':<6}{'出目の比重':>10}{'武器':>6}{'攻撃+N':>8}{'主攻撃':>8}{'固定':>6}{'追撃':>6}{'その他':>8}")
            for (fl, kind) in sorted(P):
                w, dd, bo, mn, fx, pu, dsc, tot = P[(fl, kind)]
                base = max(1, w + dd + bo); tot = max(1, tot)
                print(f"{fl:>4} {KIND[kind]:<6}{(dd + dsc) / base:>10.0%}{w / base:>6.0%}{(bo - dsc) / base:>8.0%}{mn / tot:>8.0%}{fx / tot:>6.0%}{pu / tot:>6.0%}{max(0, tot - mn - fx - pu) / tot:>8.0%}")
        if R:
            n = len(R)
            clear = sum(1 for r in R if r["deathFloor"] == 0)
            reach = defaultdict(int); die = defaultdict(int)
            for r in R:
                df = r["deathFloor"]
                for dep in (1, 3, 5, 6, 7):
                    if df == 0 or df >= dep:
                        reach[dep] += 1
                if df:
                    die[df] += 1
            print(f"クリア {100 * clear / n:.1f}% (n={n})  層の死亡率: " + " / ".join(f"{dep}層 {100 * die[dep] / max(1, reach[dep]):.1f}%" for dep in (1, 3, 5, 6, 7)))


if __name__ == "__main__":
    main()
