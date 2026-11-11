"""端子ごとの使われ方 (CombatSystem.TerminalDiag) を、 並列ワーカーの応答から合算して表にする。

使い方: python Tools/terminal_diag_report.py AutoRunLogs/learn_parallel/<実行ディレクトリ>

列: 山札にあった戦闘 / 場に出たターン / 発動 / 発動率 (発動 ÷ 場に出た) / 撃破ボーナス / 写し取り・焼き切り・保持器の対象。
発動率が低い端子は「BOT が使っていない」候補。 基本の端子 (攻撃・防御・充電) は比べる目安として先頭に出す。
"""
import glob, io, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COLS = ["山札の戦闘", "場に出た", "発動", "撃破ボーナス", "写し取り対象", "焼き切り対象", "保持器対象"]


def order():
    src = io.open(os.path.join(ROOT, "Assets/Scripts/CombatSystem/TerminalCards.cs"), encoding="utf-8-sig").read()
    m = re.search(r"public static class TerminalIds\s*\{(.*?)\n    \}", src, re.S)
    return [jp for _, jp in re.findall(r"(\w+)\s*=\s*\"([^\"]+)\"", m.group(1))]


def main():
    run_dir = sys.argv[1]
    tot = {}
    files = glob.glob(os.path.join(run_dir, "round_*", "c*", "response.json"))
    for f in files:
        r = json.load(io.open(f, encoding="utf-8-sig"))
        names, vals = r.get("termDiagNames") or [], r.get("termDiag") or []
        W = len(COLS)
        for i, n in enumerate(names):
            row = tot.setdefault(n, [0.0] * W)
            for c in range(W): row[c] += vals[i * W + c]
    if not tot: sys.exit("termDiag が応答に無い (ワーカーが計装の前のビルド?)  files=%d" % len(files))
    print("応答 %d 個を合算" % len(files))
    print("%-8s %9s %9s %9s %6s %7s %6s %6s %6s" % ("端子", *[c[:5] for c in COLS[:3]], "発動率", *[c[:4] for c in COLS[3:]]))
    for n in order():
        if n not in tot: print("%-8s  (山札に一度も入らなかった)" % n); continue
        r = tot[n]
        rate = r[2] / r[1] if r[1] else 0.0
        print("%-8s %9d %9d %9d %5.1f%% %7d %6d %6d %6d" % (n, r[0], r[1], r[2], rate * 100, r[3], r[4], r[5], r[6]))


if __name__ == "__main__":
    main()
