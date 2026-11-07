"""店ごとのパッシブの提示と購入 (AutoRunner.shopPassiveAudit) を集計する。
使い方: python -X utf8 Tools/shop_passive_audit.py <実行ディレクトリ>
各チャンクの AutoRunLogs/shop_passive_audit.tsv を読む。 1 行 = 1 店:
  層 / 店を出た時の所持金 / 買った (B S G L) / リロールで捨てた (B S G L) / 買えたのに残した (B S G L) / 金が足りず残した (B S G L)
提示 = 買った + 捨てた + 残した (棚に並んだパッシブの枠の延べ数)。"""
import glob
import os
import sys
from collections import defaultdict

RAR = ["BRONZE", "SILVER", "GOLD", "LEGENDARY"]


def main():
    d = sys.argv[1]
    if not os.path.isdir(d):
        d = os.path.join("AutoRunLogs/learn_parallel", d)
    rows = []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "AutoRunLogs", "shop_passive_audit.tsv")):
        for ln in open(p, encoding="utf-8"):
            f = ln.split()
            if len(f) == 18:
                rows.append([int(x) for x in f])
    if not rows:
        print("記録が無い")
        return
    by = defaultdict(lambda: [0] * 16)
    shops = defaultdict(int)
    coins = defaultdict(int)
    for r in rows:
        L = r[0]
        shops[L] += 1
        coins[L] += r[1]
        for i in range(16):
            by[L][i] += r[2 + i]
        for i in range(16):
            by["全"][i] += r[2 + i]
        shops["全"] += 1
        coins["全"] += r[1]

    def show(L):
        a = by[L]
        b, dsc, la, lp = a[0:4], a[4:8], a[8:12], a[12:16]
        shown = [b[i] + dsc[i] + la[i] + lp[i] for i in range(4)]
        n = shops[L]
        print(f"\n== 層 {L} (店 {n} 軒・店を出た時の所持金 平均 {coins[L] / n:.0f}G)")
        print(f"{'レア度':<10}{'提示/店':>8}{'購入/店':>8}{'購入率':>8}{'捨てた':>8}{'買えたのに残した':>18}{'金が足りず残した':>18}")
        for i in range(4):
            if shown[i] == 0:
                continue
            print(f"{RAR[i]:<10}{shown[i] / n:8.2f}{b[i] / n:8.2f}{100 * b[i] / shown[i]:7.1f}%{100 * dsc[i] / shown[i]:7.1f}%{100 * la[i] / shown[i]:17.1f}%{100 * lp[i] / shown[i]:17.1f}%")
        S, B = sum(shown), sum(b)
        print(f"{'合計':<10}{S / n:8.2f}{B / n:8.2f}{100 * B / max(1, S):7.1f}%{100 * sum(dsc) / max(1, S):7.1f}%{100 * sum(la) / max(1, S):17.1f}%{100 * sum(lp) / max(1, S):17.1f}%")

    for L in sorted(k for k in by if k != "全"):
        show(L)
    show("全")


if __name__ == "__main__":
    main()
