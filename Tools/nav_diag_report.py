"""追跡者に捕まった後の結果 (GameLoop.NavDiag) を、 並列ワーカーの応答から合算して表にする (#35)。

使い方: python Tools/nav_diag_report.py AutoRunLogs/learn_parallel/<実行ディレクトリ>

目標 (GAME.md §23-21): 捕まった後の死亡 15〜25%・生き延びた時の残り HP 30〜40%。
"""
import glob, io, json, os, sys

COLS = ["捕まった", "勝った", "逃げた", "救済", "死んだ", "HP前", "HP後", "深さ"]
NAMES = {"summoner": "召喚者", "hound": "猟犬使い", "sprinter": "疾走者", "brute": "重装", "all": "全部"}


def main():
    run = sys.argv[1]
    tot, nresp = {}, 0
    for f in glob.glob(os.path.join(run, "round_*", "c*", "response.json")):
        r = json.load(io.open(f, encoding="utf-8-sig")); nresp += 1
        names, vals = r.get("navDiagNames") or [], r.get("navDiag") or []
        W = len(COLS)
        for i, n in enumerate(names):
            row = tot.setdefault(n, [0.0] * W)
            for c in range(W): row[c] += vals[i * W + c]
    if not tot: sys.exit("navDiag が応答に無い (応答 %d 個)" % nresp)
    runs = None
    try:
        for l in io.open(os.path.join(run, "rounds.txt"), encoding="utf-8"):
            if "有効" in l: runs = int(l.split("有効")[1].split("/")[0].strip().replace(",", ""))
    except Exception: pass
    print("応答 %d 個%s" % (nresp, "・%d ラン" % runs if runs else ""))
    print("%-8s %7s %6s %7s %7s %7s %7s %8s %8s %6s" % ("型", "捕まった", "/ラン", "勝った", "逃げた", "救済", "死亡率", "HP前%", "生還HP%", "深さ"))
    for k in ["summoner", "hound", "sprinter", "brute", "all"] + [k for k in tot if k not in NAMES]:
        if k not in tot: continue
        r = tot[k]; c = r[0] or 1; alive = (r[1] + r[2] + r[3]) or 1
        print("%-8s %7d %6.2f %6.1f%% %6.1f%% %6.1f%% %6.1f%% %7.1f %8.1f %6.2f" % (
            NAMES.get(k, k), r[0], r[0] / runs if runs else 0, 100 * r[1] / c, 100 * r[2] / c, 100 * r[3] / c, 100 * r[4] / c,
            r[5] / c, r[6] / alive, r[7] / c))
    print("目標: 死亡率 15〜25% / 生還時の残り HP 30〜40%")


if __name__ == "__main__":
    main()
