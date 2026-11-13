"""端子ごとの使われ方 (CombatSystem.TerminalDiag) と BOT の値付け (AutoTest.TerminalPolicyDiag) を、 並列ワーカーの応答から合算して表にする。

使い方: python Tools/terminal_diag_report.py AutoRunLogs/learn_parallel/<実行ディレクトリ> [--pairs N]

── 節 1: 使われ方 (これまでの表) ──
列: 山札にあった戦闘 / 場に出たターン / 発動 / 発動率 (発動 ÷ 場に出た) / 撃破ボーナス / 写し取り・焼き切り・呼び戻し (旧〈保持器〉・旧い手札の規則の時だけ) の対象 /
旧い条件では入らない撃破 (#117: 攻撃の端子なしのターン・ターン終了時の撃破。 旧い条件の腕でも数える = 取りこぼした回数)。
発動率が低い端子は「BOT が使っていない」候補。 基本の端子 (攻撃・防御・充電) は比べる目安として先頭に出す。
列が 7 本の古い応答 (#117 の前のワーカー) も読める (最後の列は 0)。

── 節 2〜6 (#106・2026-11-13 の計装。 古い応答には無いので、 その節は「記録なし」と出す) ──
節 2 場に出たターンの内訳: 1 枚 1 ターンごとに 1 つだけに入る。 場に出た = 止まっていた + 挿さなかった + 挿したが不発 + 発動 (発動の定義は節 1 と同じ)。
     「挿した」= 方策 / 画面が返した挿し方 (Resolve が条件・充電で直す前・〈猫の手〉込み) で出目を 1 つ以上その端子に向けた。
     引いた = ターンの始めに山から新しく引いた回数 (持ち越しで残った分・常設が場に残る分は数えない)。
節 3 報酬と店: 提示 = 戦闘後の報酬の候補に出た回数 + 店の棚に出た回数 / 取得 = 報酬で選ばれた回数 + 店で買われた回数 /
     見送り = 提示された場面で何も取られなかった回数 (店は 1 枚も買わなかった来店) / 山札率 = ランの終わりに山札に 1 枚以上あったランの割合 /
     平均枚 = その端子を持っていたランの終わりの枚数の平均 / 抜けた = 店・休憩で抜かれた・使ってランの山札から抜けた (〈応急箱〉)。
節 4 代わりに取られた端子: 同じ提示で、 その端子が取られず別の端子が取られた組の上位。 何も取らなかった提示は入らない。
節 5 BOT の点 (ChooseTerminalReward): 候補 = BOT が点を付けた回数 (店は買うたびに残りの棚へ選び直すので来店の数より多い・取り置きを割る棚は候補に入らない)。
     平均点 (取った時 / 取らなかった時)・足切り = 候補の中で最良だったが足切りに届かず見送った回数。
節 6 配線で挿さなかった理由: Optimal / Super の実際の配線の判断で、 その端子が場にあり止まっていない回数を 1 つだけに分ける:
     発動 / 不発 (c: 挿したが条件・充電で外れた) / 手なし (a: 発動させられる手が無い・対象を取る端子は対象が取れる手が無い) /
     選ばず (b: 発動させる手はあったが、 効用でもっと良い手があった)。 (b) の内訳: 同点 / 生死 (差 500 以上) / 残りの差の平均 (効用の単位 = HP 相当)。
     ミス発 = 発動のうち、 Optimal のミス (次点) で選んだ手でだけ発動した回数。 対象無 = (a) のうち対象を取らずに発動する手はあった回数。
     dw = 未来の与ダメ 1 の価値の平均 / fw = 先の HP 1 の価値の平均 / draws = この戦闘であと引く回数の見込みの平均 (写し取り・焼き切りの値付けに掛かる)。
     節 2 (本体) と節 6 (BOT) は数える所が違う: 節 6 は素朴版・人の判断を数えず、 〈金で殴る〉が後から挿し方を替えた分も反映しない。
"""
import glob, io, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COLS = ["山札の戦闘", "場に出た", "発動", "撃破ボーナス", "写し取り対象", "焼き切り対象", "呼び戻し対象(旧規則)", "旧条件で落ちる撃破"]
# TerminalDiag の列番号 (C# の定数と同じ)
ON_BOARD, FIRED = 1, 2
DRAWN, STOPPED, NOT_PLUGGED, PLUG_NOFIRE = 8, 9, 10, 11
OFFERED, TAKEN, ALL_PASSED, RUNS_IN_DECK, END_COPIES, REM_SHOP, REM_REST, LEFT_RUN = 12, 16, 20, 21, 22, 23, 24, 25
TD_WIDTH = 26
SCENES = ["通常", "エリート", "ボス", "店"]
# TerminalPolicyDiag の列番号
P_WIDTH = 23
RW, SHOP = 0, 5
HAND, NOFEAS, NOTCH, GAPSUM, TIED, FORCED, CHFIRE, PLUGNO, MISFIRE, DW, FW, DRAWS, FIRES_NOTGT = 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22


def order():
    src = io.open(os.path.join(ROOT, "Assets/Scripts/CombatSystem/TerminalCards.cs"), encoding="utf-8-sig").read()
    m = re.search(r"public static class TerminalIds\s*\{(.*?)\n    \}", src, re.S)
    return [jp for _, jp in re.findall(r"(\w+)\s*=\s*\"([^\"]+)\"", m.group(1))]


def pct(a, b):
    return "%5.1f%%" % (100.0 * a / b) if b else "     -"


def avg(a, b, w=6, p=2):
    return ("%*.*f" % (w, p, a / b)) if b else "%*s" % (w, "-")


def load(run_dir):
    files = glob.glob(os.path.join(run_dir, "round_*", "c*", "response.json"))
    tot, pol, pairs = {}, {}, {}
    scenes = [0] * (len(SCENES) * 2 + 1)
    has_new = has_pol = False
    for f in files:
        r = json.load(io.open(f, encoding="utf-8-sig"))
        names, vals = r.get("termDiagNames") or [], r.get("termDiag") or []
        if names:
            W = len(vals) // len(names)       # 応答の列の数 (古いワーカーは 7 / 8)
            if W >= TD_WIDTH: has_new = True
            for i, n in enumerate(names):
                row = tot.setdefault(n, [0.0] * TD_WIDTH)
                for c in range(min(W, TD_WIDTH)): row[c] += vals[i * W + c]
        sc = r.get("termScenes") or []
        for i in range(min(len(sc), len(scenes))): scenes[i] += sc[i]
        for k, c in zip(r.get("termPairKeys") or [], r.get("termPairCounts") or []):
            pairs[k] = pairs.get(k, 0) + c
        pn, pv = r.get("termPolicyNames") or [], r.get("termPolicy") or []
        if pn:
            has_pol = True
            W = len(pv) // len(pn)
            for i, n in enumerate(pn):
                row = pol.setdefault(n, [0.0] * P_WIDTH)
                for c in range(min(W, P_WIDTH)): row[c] += pv[i * W + c]
    return files, tot, pol, pairs, scenes, has_new, has_pol


def main():
    run_dir = sys.argv[1]
    top_pairs = 5
    if "--pairs" in sys.argv: top_pairs = int(sys.argv[sys.argv.index("--pairs") + 1])
    files, tot, pol, pairs, scenes, has_new, has_pol = load(run_dir)
    if not tot: sys.exit("termDiag が応答に無い (ワーカーが計装の前のビルド?)  files=%d" % len(files))
    ids = order()
    print("応答 %d 個を合算" % len(files))

    # ── 節 1 ──
    print("\n== 1. 使われ方 ==")
    print("%-8s %9s %9s %9s %6s %7s %6s %6s %6s %7s" % ("端子", *[c[:5] for c in COLS[:3]], "発動率", *[c[:4] for c in COLS[3:7]], "旧で落ちる"))
    for n in ids:
        if n not in tot or not tot[n][0]: print("%-8s  (山札に一度も入らなかった)" % n); continue
        r = tot[n]
        rate = r[2] / r[1] if r[1] else 0.0
        print("%-8s %9d %9d %9d %5.1f%% %7d %6d %6d %6d %7d" % (n, r[0], r[1], r[2], rate * 100, r[3], r[4], r[5], r[6], r[7]))

    if not has_new:
        print("\n(節 2〜4: 記録なし ── #106 の計装の前のワーカー)")
    else:
        # ── 節 2 ──
        print("\n== 2. 場に出たターンの内訳 (場に出た = 止まっていた + 挿さなかった + 挿したが不発 + 発動) ==")
        print("%-8s %9s %9s %8s %9s %9s %9s | %6s %6s %6s %6s" % ("端子", "引いた", "場に出た", "止まった", "挿さず", "挿し不発", "発動", "止%", "挿さず%", "不発%", "発動%"))
        for n in ids:
            if n not in tot or not tot[n][ON_BOARD]: continue
            r = tot[n]; ob = r[ON_BOARD]
            print("%-8s %9d %9d %8d %9d %9d %9d | %s %s %s %s" % (n, r[DRAWN], ob, r[STOPPED], r[NOT_PLUGGED], r[PLUG_NOFIRE], r[FIRED],
                  pct(r[STOPPED], ob), pct(r[NOT_PLUGGED], ob), pct(r[PLUG_NOFIRE], ob), pct(r[FIRED], ob)))

        # ── 節 3 ──
        runs = scenes[len(SCENES) * 2]
        print("\n== 3. 報酬と店 (終わったラン %d) ==" % runs)
        print("場面ごと: " + " / ".join("%s 提示 %d・見送り %d (%s)" % (s, scenes[i * 2], scenes[i * 2 + 1], pct(scenes[i * 2 + 1], scenes[i * 2]).strip())
                                       for i, s in enumerate(SCENES)))
        print("%-8s %7s %7s %6s %7s %6s %6s | %s | %5s %5s %5s" % ("端子", "提示", "取得", "取得率", "見送り", "山札率", "平均枚",
              "  ".join("%-9s" % ("取/提" + s) for s in SCENES), "店抜", "休抜", "使抜"))
        never = []
        for n in ids:
            r = tot.get(n)
            if r is None or (not sum(r[OFFERED:OFFERED + 4]) and not r[RUNS_IN_DECK]): never.append(n); continue
            off = sum(r[OFFERED:OFFERED + 4]); tk = sum(r[TAKEN:TAKEN + 4])
            per = "  ".join("%4d/%-4d" % (r[TAKEN + i], r[OFFERED + i]) for i in range(4))
            print("%-8s %7d %7d %s %7d %s %s | %s | %5d %5d %5d" % (n, off, tk, pct(tk, off), r[ALL_PASSED], pct(r[RUNS_IN_DECK], runs),
                  avg(r[END_COPIES], r[RUNS_IN_DECK]), per, r[REM_SHOP], r[REM_REST], r[LEFT_RUN]))
        if never: print("提示もされず山札にも無かった: " + "・".join(never))

        # ── 節 4 ──
        print("\n== 4. 提示されたのに取られなかった時、 代わりに取られた端子 (上位 %d) ==" % top_pairs)
        by = {}
        for k, c in pairs.items():
            a, b = k.split("\t", 1)
            by.setdefault(a, []).append((c, b))
        for n in ids:
            if n not in by: continue
            lst = sorted(by[n], reverse=True)
            print("%-8s 計 %6d: %s" % (n, sum(c for c, _ in lst), "  ".join("%s %d" % (b, c) for c, b in lst[:top_pairs])))

    if not has_pol:
        print("\n(節 5〜6: 記録なし ── #106 の計装の前のワーカー)")
        return
    # ── 節 5 ──
    print("\n== 5. BOT の点 (ChooseTerminalReward) ==")
    print("%-8s | %6s %6s %6s %6s %6s %5s | %6s %6s %6s %6s %6s %5s" % ("端子", "報候補", "報取得", "取得率", "点取", "点否", "足切", "店候補", "店取得", "取得率", "点取", "点否", "足切"))
    for n in ids:
        r = pol.get(n)
        if r is None or (r[RW] == 0 and r[SHOP] == 0): continue
        def part(o):
            return "%6d %6d %s %s %s %5d" % (r[o], r[o + 1], pct(r[o + 1], r[o]), avg(r[o + 2], r[o + 1]), avg(r[o + 3], r[o] - r[o + 1]), r[o + 4])
        print("%-8s | %s | %s" % (n, part(RW), part(SHOP)))

    # ── 節 6 ──
    print("\n== 6. 配線で挿さなかった理由 (Optimal / Super の実際の判断・場にあり止まっていない) ==")
    print("%-8s %8s | %6s %6s %6s %6s | %6s %6s %8s | %6s %6s | %6s %6s %6s" % ("端子", "場の回数", "発動", "不発c", "手なa", "選ばb",
          "b同点", "b生死", "b差平均", "ミス発", "対象無", "dw", "fw", "draws"))
    for n in ids:
        r = pol.get(n)
        if r is None or not r[HAND]: continue
        h = r[HAND]; free = r[NOTCH] - r[TIED] - r[FORCED]
        print("%-8s %8d | %s %s %s %s | %s %s %s | %6d %6d | %s %s %s" % (n, h, pct(r[CHFIRE], h), pct(r[PLUGNO], h), pct(r[NOFEAS], h), pct(r[NOTCH], h),
              pct(r[TIED], r[NOTCH]), pct(r[FORCED], r[NOTCH]), avg(r[GAPSUM], free, 8), r[MISFIRE], r[FIRES_NOTGT],
              avg(r[DW], h), avg(r[FW], h), avg(r[DRAWS], h)))


if __name__ == "__main__":
    main()
