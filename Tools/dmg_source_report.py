#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""与ダメ出どころ別の発動率と実効値 (DmgSourceDiag, 2026-09-20)。

実効 = 発動率 × 発動時の平均 = 合計量 ÷ 所持攻撃数。 額面ではなく「所持している攻撃 1 回あたり何が乗ったか」。
使い方: python Tools/dmg_source_report.py <session> [--boss]
"""
import collections, glob, io, json, os, sys

W = 17
FIELDS = ["与ダメ%", "会心倍率", "非会心%", "会心率", "会心確定", "貫通", "脆弱"]
PCT = {0, 1, 2, 3, 5, 6}


def names():
    d = json.load(io.open("Assets/Data/InventorySystem/items.json", encoding="utf-8"))
    m = {}
    sk = {s["id"]: s["name"] for s in d["skills"]}
    wep = collections.defaultdict(list)
    for it in d["items"]:
        if it.get("skill"):
            m[it["id"]] = "{}〈{}〉".format(it["name"], it["skill"])
        elif it["category"] != "Weapon":
            m[it["id"]] = it["name"]
        for s in it.get("skills", []) or []:
            wep[s].append(it["name"])
    for s, ws in wep.items():
        m[s] = "〈{}〉[{}]".format(sk.get(s, s), "・".join(ws) if len(ws) <= 3 else "{}ほか{}種".format(ws[0], len(ws) - 1))
    # 武器の強化 (WeaponProgression) でだけ付く段は items.json に無い
    m.setdefault("大鉈III", "〈大鉈III〉[斧の強化で付与]")
    return m


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sess = sys.argv[1]
    key = "dmgSrcBoss" if "--boss" in sys.argv else "dmgSrcAll"
    agg = collections.defaultdict(lambda: [0.0] * W)
    for f in glob.glob(os.path.join(sess, "*_c*", "response.json")):
        j = json.load(io.open(f, encoding="utf-8"))
        for i, nm in enumerate(j.get("dmgSrcNames", [])):
            row = j[key][i * W:(i + 1) * W]
            a = agg[nm]
            for k in range(W):
                a[k] += row[k]
    nm = names()
    tot = agg.pop("__total__", None)
    rows = []
    for src, a in agg.items():
        held = a[0]
        if held <= 0:
            continue
        for f in range(7):
            fired, s = a[3 + 2 * f], a[4 + 2 * f]
            if fired <= 0:
                continue
            rows.append((src, f, held, fired, s))
    print("[{}]  攻撃 {:,.0f} 回 (会心 {:.1%})".format("ボス戦" if key == "dmgSrcBoss" else "全戦闘", tot[0], tot[1] / tot[0]))
    for f in range(7):
        rs = sorted([r for r in rows if r[1] == f], key=lambda r: -abs(r[4] / r[2]))
        if not rs:
            continue
        print("\n■ {}".format(FIELDS[f]))
        print("  {:<44}{:>9}{:>9}{:>11}{:>11}".format("出どころ", "所持攻撃", "発動率", "発動時平均", "実効"))
        for src, _, held, fired, s in rs:
            avg, eff = s / fired, s / held
            if f == 4:
                a_s, e_s = "確定", "{:.1%}".format(eff)
            else:
                a_s, e_s = "{:+.1%}".format(avg), "{:+.1%}".format(eff)
            print("  {:<44}{:>9,.0f}{:>9.1%}{:>11}{:>11}".format(nm.get(src, src)[:44], held, fired / held, a_s, e_s))
    n = tot[0]
    print("\n■ 最終値の平均 (帰属漏れの確認用)")
    print("  与ダメ% 合計 {:+.1%} / 会心時の会心倍率 ×{:.2f} / 非会心時の非会心% {:+.1%} / 会心率 {:.1%} / 貫通 {:.1%}".format(
        tot[4] / n, tot[6] / max(1, tot[1]), tot[8] / max(1, tot[2]), tot[10] / n, tot[14] / n))
    s_out = sum(r[4] for r in rows if r[1] == 0) / n
    s_cm = sum(r[4] for r in rows if r[1] == 1) / max(1, tot[1])
    s_nc = sum(r[4] for r in rows if r[1] == 2) / max(1, tot[2])
    s_cr = sum(r[4] for r in rows if r[1] == 3) / n
    print("  帰属の合計  与ダメ% {:+.1%} / 会心倍率 +{:.2f} (基準2.0) / 非会心% {:+.1%} / 会心率 {:.1%}".format(s_out, s_cm, s_nc, s_cr))


if __name__ == "__main__":
    main()
