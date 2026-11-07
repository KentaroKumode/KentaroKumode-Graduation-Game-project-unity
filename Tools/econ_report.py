"""経済の調整の物差し: クリア率と、 パッシブの取得数 (クリアしたランの分布)。
使い方: python -X utf8 Tools/econ_report.py <実行ディレクトリ> [...]
ゴール (2026-11-06): クリア 20〜30% / クリアしたランのパッシブ取得数 P90 ≤ 40。
取得数 = learn_runs.json の acquired (重複なし・全経路) のうち items.json の category が Passive のもの。"""
import glob
import json
import os
import sys

ITEMS = {e["id"]: e for e in json.load(open("Assets/Data/InventorySystem/items.json", encoding="utf-8-sig"))["items"]}


def load(d):
    if not os.path.isdir(d):
        d = os.path.join("AutoRunLogs/learn_parallel", d)
    rows = []
    for p in glob.glob(os.path.join(d, "round_000", "c*", "learn_runs.json")):
        j = json.load(open(p, encoding="utf-8"))
        rows += j if isinstance(j, list) else next(v for v in j.values() if isinstance(v, list))
    cfg = ""
    cp = os.path.join(d, "config.txt")
    if os.path.exists(cp):
        keep = ("shopItemPriceScale", "shopProgressPriceK", "shopRaritySlope", "normalDropRate", "eliteDropRate", "enemyAttackMul", "enemyHpMul", "itemPotency", "enemyLateFlatten")
        cfg = " ".join(ln.strip().replace(" ", "") for ln in open(cp, encoding="utf-8") if ln.strip().startswith(keep))
    return rows, cfg


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else 0


def main():
    print(f"{'実行':<8}{'クリア':>7}{'取得P(全)':>10}{'取得P(クリア)平均':>16}{'P90':>6}{'最大':>6}{'最終層到達 平均':>14}{'P90':>6}{'店で購入P':>10}  設定")
    for d in sys.argv[1:]:
        R, cfg = load(d)
        if not R:
            continue
        pas = lambda r: sum(1 for a in r.get("acquired") or [] if ITEMS.get(a, {}).get("category") == "Passive")
        C = [r for r in R if r["deathFloor"] == 0]
        allp = [pas(r) for r in R]
        cp = [pas(r) for r in C]
        fp = [pas(r) for r in R if r["deathFloor"] in (0, 7)]   # 最終層 (深さ 7) に到達したラン
        mean = lambda v: sum(v) / len(v) if v else 0
        print(f"{os.path.basename(d.rstrip('/'))[-6:]:<8}{100 * len(C) / len(R):6.1f}%{mean(allp):10.1f}{mean(cp):16.1f}{pct(cp, 0.9):6}{max(cp) if cp else 0:6}{mean(fp):14.1f}{pct(fp, 0.9):6}"
              f"{mean([r['shopBuyPassive'] for r in R]):10.1f}  {cfg}")


if __name__ == "__main__":
    main()
