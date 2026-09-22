#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""層ごとの死亡分布 (2026-09-20 改訂)。 バランス方針 (GAME.md §13-5) の目標と並べる。

**「死亡」の定義は「ランが終わった場所」** (bandScore)。 救済 (ちいさな灯火 /
ラストスタンド / フルーレ・バレエ) で生き返ったランは、 その層では死んでいない。

戦闘で HP0 になった回数 (SkillDiag.ByFloorKind) は**別物**なので、 診断として右側に併記する。
両者の差 = 救済の件数で、 **救済は死亡を下の層へ押し流す** ── 4 層でやられて
救済され 6 層で力尽きたランは、 左では 6 層、 右では 4 層に出る。 左だけを見て
敵を調整すると、 押し流した側の層 (4 層) を強いまま残すことになる。

帯 (bandScore): 1=1〜2F / 2=3F道中 / 3=3Fボス / 4,5=4F / 6=5F道中とΛ / 7=5Fボス
/ 8=5F・6Fクリア(門不通過) / 9=6F / 10=Null Point(ヴェスカ) / 11=完全クリア。
1F と 2F は帯では分かれないので 1F ボス到達数 (bossEntryCount[1]) で割る。
Λ と 5F道中 は帯 6 に同居するので `lambdaRunDeaths` (ラン単位) で切り出す
── **戦闘側の計装で引いてはいけない** (救済ぶんだけ引きすぎて 5F道中 が 0 になる)。

使い方: python Tools/floor_death_report.py <session>[#<arm>] ...
"""
import collections, glob, io, json, os, sys

# 表示名 → (SkillDiag の層添字, 種類の並び)。 層 0 = Λ、 8 = ヴェスカ。
HP0_MAP = {
    "1層 道中": (1, (0, 1)), "1層 ボス": (1, (2,)), "2層": (2, (0, 1)),
    "3層 道中": (3, (0, 1)), "3層 ボス": (3, (2,)), "4層": (4, (0, 1)),
    "5層 道中": (5, (0, 1)), "Λ層 (時間の狭間)": (0, (0, 1, 2)), "5層 ボス": (5, (2,)),
    "6層": (6, (0, 1, 2)), "7層 決戦(ヴェスカ)": (8, (0, 1, 2)),
}
# GAME.md §13-5 の到達者あたり死亡率の目標 (下限, 上限)。
# **目標は層単位**なので、 道中とボスに分かれる層は合成した値と比べる
# ── 行ごとに同じ目標を当てると「道中も 12% 死ね」と読めてしまう。
GROUPS = [
    ("1層", ["1層 道中", "1層 ボス"], (0, .01)),
    ("2層", ["2層"], (.03, .05)),
    ("3層", ["3層 道中", "3層 ボス"], (.12, .15)),
    ("4層", ["4層"], (.03, .05)),
    ("5層", ["5層 道中", "5層 ボス"], (.20, .25)),
    ("Λ層", ["Λ層 (時間の狭間)"], None),          # 深さはプレイヤーの選択なので目標を置かない
    ("(門の鍵なし)", ["5・6層で打ち切り(門の鍵なし)"], None),
    ("6層", ["6層"], (.25, .30)),
    ("7層", ["7層 決戦(ヴェスカ)"], (.45, .50)),
]


def load(sess):
    band = collections.Counter(); boss = [0] * 9; n = 0
    bstats = collections.defaultdict(lambda: [0] * 4)
    fk = [0] * 270; lam_runs = 0; rev = [0, 0, 0]; reentry = 0
    pat = (sess.split("#")[1] if "#" in sess else "*") + "_c*"
    for f in glob.glob(os.path.join(sess.split("#")[0], pat, "response.json")):
        j = json.load(io.open(f, encoding="utf-8"))
        for b in j["bandScores"]:
            if b >= 0: band[b] += 1; n += 1
        for i, v in enumerate(j.get("bossEntryCount", [])): boss[i] += v
        for i, v in enumerate(j.get("floorKind", [])): fk[i] += v
        lam_runs += j.get("lambdaRunDeaths", 0)
        rev[0] += j.get("torchRevivals", 0); rev[1] += j.get("lastStandRevivals", 0)
        rev[2] += j.get("fleuretRevivals", 0)
        reentry += j.get("finishCombatReentry", 0)
        for i, nm in enumerate(j.get("bossNames", [])):
            for k in range(4): bstats[nm][k] += j["bossStats"][i * 4 + k]
    return n, band, boss, bstats, fk, lam_runs, rev, reentry


def hp0(fk, name):
    fl, kinds = HP0_MAP[name]
    return sum(fk[(fl * 3 + k) * 10 + 1] for k in kinds)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    for sess in sys.argv[1:]:
        n, band, boss, bs, fk, lam, rev, reentry = load(sess)
        if n == 0:
            print("\n[{}]  該当なし".format(sess)); continue
        f1boss_death = bs["boss_layer1"][3]
        d_f1_road = n - boss[1]
        rows = [
            ("1層 道中", d_f1_road), ("1層 ボス", f1boss_death),
            ("2層", band[1] - d_f1_road - f1boss_death),
            ("3層 道中", band[2]), ("3層 ボス", band[3]), ("4層", band[4] + band[5]),
            ("5層 道中", band[6] - lam), ("Λ層 (時間の狭間)", lam), ("5層 ボス", band[7]),
            ("5・6層で打ち切り(門の鍵なし)", band[8]),
            ("6層", band[9]), ("7層 決戦(ヴェスカ)", band[10]), ("完全クリア", band[11]),
        ]
        print("\n[{}]  {:,} ラン".format(os.path.basename(sess), n))
        if reentry:
            print("  ** FinishCombat 再入 {:,} 回 — 戦闘側の計装が二重に積まれている **".format(reentry))
        print("  {:<24}{:>8}{:>9}{:>10}{:>14}{:>10}".format(
            "", "ラン数", "全体比", "死亡率", "目標", "HP0 回数"))
        d = dict(rows)
        alive = n
        for gname, names, lo_hi in GROUPS:
            enter = alive                      # この層に到達したラン数
            for name in names:
                v = d[name]
                rate = v / max(1, alive)
                h = "{:,}".format(hp0(fk, name)) if name in HP0_MAP else "—"
                tg = "—"
                if lo_hi and len(names) == 1:
                    lo, hi = lo_hi
                    tg = "{:.0%}〜{:.0%}{}".format(lo, hi, " " if lo <= rate <= hi else "*")
                print("  {:<24}{:>8,}{:>9.1%}{:>10.1%}{:>14}{:>10}"
                      .format(name, v, v / n, rate, tg, h))
                alive -= v
            if lo_hi and len(names) > 1:
                # 層としての死亡率 = 1 - (層に入って出られた割合)
                grate = 1 - alive / max(1, enter)
                lo, hi = lo_hi
                print("  {:<24}{:>8,}{:>9}{:>10.1%}{:>14}{:>10}".format(
                    "  └ " + gname + " 合計", enter - alive, "",
                    grate, "{:.0%}〜{:.0%}{}".format(lo, hi, " " if lo <= grate <= hi else "*"), ""))
        v = d["完全クリア"]
        print("  {:<24}{:>8,}{:>9.1%}".format("完全クリア", v, v / n))
        print("  救済 {:,} 件 (灯火 {:,} / ラストスタンド {:,} / フルーレ {:,})"
              "  ── この件数ぶん、 HP0 の層より下の層で死亡が計上される"
              .format(sum(rev), rev[0], rev[1], rev[2]))
        print("  目標の * は圏外。 「死亡率」は到達者あたり (§13-5)。")


if __name__ == "__main__":
    main()
