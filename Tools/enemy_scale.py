"""敵の強さを段差の無い 1 本の尺度から作り、 enemies.json に書く (2026-11-04)。

考え方 (docs/GAME.md §13-6):
  - 敵 (型) を「今の強さの順」に並べ、 尺度 s (0 = 最弱 〜 1 = 最強) へ等間隔に置く。 並びは ORDER_FIX で手直しする
    (初回だけ。 一度書いた後は enemies.json の scale の順を保つ ── 並びを変えたい時は scale を手で書き換える)
  - 数値は 尺度の曲線(s) × 型の個性。 個性 = 今の数値の「HP 寄りか攻撃寄りか」(対数の比) を残す
  - HP は火力に合わせて伸ばし続けてよいが、 攻撃はブロックで受けられる範囲で頭打ちにする (ブロックは出目の合計 + 端子調律で、 火力ほど伸びない)
  - 防御を持つ型は、 その分だけ素の HP を下げる (実効の強さを尺度に合わせる)
  - 精鋭 = 同じ型の HP ×1.6・攻撃 ×1.15
  - 層には「s の範囲」で配る (GameLoop.FloorManager.ScaleWindows と同じ値)。 5 層の一本道のエリートは 4 層の候補から引く
  - ボス (ヴェスカを除く) = その層の雑魚より圧倒的に硬い (HP の下限)・攻撃は精鋭の最大 ×1.0〜1.1 の範囲
    攻撃を決まった値で戦わないボス (機構で戦う) は攻撃を触らない

**2026-11-06: enemies.json の HP は手で焼いた (値は docs/GAME.md §13-7)。 このスクリプトを再実行すると HP が戻るので、 使う前に CURVE を焼いた値に合わせ直すこと。**
数値は全部仮置き (測定は採否を取ってから)。 実行後は Tools/enemy_danger.py で脅威度を書き直す。
使い方: python Tools/enemy_scale.py          (書く)
        python Tools/enemy_scale.py --dry    (表だけ出す)
"""
import json, math, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
P = ROOT / "Assets/Resources/enemies.json"

# 尺度の曲線の制御点 (s, HP, 攻撃 = 基礎攻撃 + ロールの平均)。 点の間は単調な 3 次補間でつなぐ (段差を作らない)
#   1 層 (s 0〜0.25) は控えめ、 2 層から本格化。 攻撃は早めに頭打ち
#   2026-11-05 調整 1: 層 1 は攻撃を下げる (s=0 で 3.0・0.15 の点を追加)。 層 3〜4 は重くする (s=0.75 / 1.0 の HP・攻撃を上げる)。
#   2026-11-06: 攻撃を出目の幅へ戻す (12/44/53 → 9/12/14)。 後半の一撃 60〜115 がブロック (3 個で 10〜15) の外にあり、 配線の判断が消えていた。
#   このとき HP は書き込み前の値へ手で戻した (型の個性 k は今の HP/攻撃 の比から取るので、 攻撃を下げると HP も動く。 再実行すると 4 層の HP が 3 割下がる)。
#   通常の一撃 = 3 個のブロックの 0.5〜1.0 倍、 大技 (heavyMul 2.0) で約 2 倍。 1 層ボス → ヴェスカの伸びは ×16 → ×3。 HP はアイテムの改修後に合わせる
CURVE = [(0.0, 100, 3.0), (0.15, 150, 4.0), (0.25, 200, 6.0), (0.5, 900, 9.0), (0.75, 1700, 12.0), (1.0, 2800, 14.0)]
ELITE_HP, ELITE_ATK = 1.6, 1.15
# 層 1 (s < ELITE_EASY_BELOW) の精鋭は通常戦並みに近づける。 層 1 は本来 9 割以上が突破できる層 (2026-11-05)
ELITE_EASY_BELOW = 0.5   # 2026-11-05 調整4: 0.3 → 0.5 (2 層の精鋭を下げる。 2 層の死亡率 24% → 目標 15%)
ELITE_HP_EASY, ELITE_ATK_EASY = 1.25, 1.0
# 型の個性を攻撃に乗せる上限 (倍率)。 これを超える分は HP 側へ回す
ATK_PERSONALITY_MAX = 1.2
# 層 (1〜4) ごとの s の範囲。 GameLoop.FloorManager.ScaleWindows と揃える
WINDOWS = {1: (0.0, 0.25), 2: (0.2, 0.5), 3: (0.45, 0.75), 4: (0.7, 1.0)}
DEPTH_OF = {1: 1, 2: 3, 3: 5, 4: 6}
# 並びの手直し: (id, 置く順位)。 理由を必ず書く
ORDER_FIX = [
    ("13th_death", 8),   # 〈13 の宣告〉(倒し切れないと軽減無視の一撃) を脅威度の式が数えない。 1 層から外して 2 層の中ほどへ
]
# 型ごとの手直し: HP の倍率 (尺度の値に掛ける)
HP_FIX = {
    "13th_death": 0.25,  # 怖さは〈宣告〉で、 HP が高いと宣告が入る回数が増える。 2026-11-05: 平均の半分 → 4 分の 1 (かなり脆くしてよいとの指示)
}
VESCA_FIX = {"boss_layer7": (4950, 14.0), "boss_layer7_p2": (4950, 14.0), "boss_layer7_p3": (4400, 14.0), "boss_layer7_p4": (11000, 16.0)}   # 2026-11-06: 攻撃 80/90 → 14/16 (大技 ×2 で 28/32)
# ボスの攻撃を実数で決める (精鋭の最大からの clamp より優先)。 2026-11-06: 3 層 17 → 12・5 層 43 → 13 (大技 ×2 で 26)
#   灰燼の王 15.5 → 10 (6 + 2〜6): 予兆 (+15) で 25・半分以下で +3。 据え置いた唯一のボスとして 6 層の死亡の 24% を占め、 予告の中央値 29 = ブロック 3 個の 1.9 倍だった
BOSS_ATK_FIX = {"boss_layer2": 12.0, "boss_layer3": 12.0, "boss_layer4": 12.0, "boss_layer5": 13.0, "boss_layer5_hidden": 13.0, "boss_layer6": 10.5}
# ボス: 層 → id。 HP の下限 = max(雑魚の最大 × A, 精鋭の最大 × B)
BOSSES = {1: ["boss_layer1"], 2: ["boss_layer2", "boss_layer3", "boss_layer4"], 3: ["boss_layer5", "boss_layer5_hidden"], 4: ["boss_layer6"]}
BOSS_HP_MUL = {1: (2, 1.3), 2: (4, 2.4), 3: (5, 3), 4: (5, 3)}   # 1 層は無害なファーム用の層なので軽め (2026-11-05: 3,2 → 2,1.3)
# HP を下限ではなく**その値ちょうど**にする層 (下げられるように)。 他の層は従来どおり「下限」(増やすだけ)
BOSS_HP_EXACT = {2}   # 0 層の導入で 1 層の死亡率は 7.9% に収まったため、 1 層ボスは弱めない
BOSS_ATK_LO, BOSS_ATK_HI = 1.0, 1.1
BOSS_KEEP_ATTACK = {"boss_layer6"}   # 灰燼の王: 二相型と〈業火の予兆〉の固定ダメージで戦う。 攻撃値を上げると設計が壊れる


def pchip(xs, ys, x):
    n = len(xs); h = [xs[i + 1] - xs[i] for i in range(n - 1)]; d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n; m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0: m[i] = 0
        else:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    for i in range(n - 1):
        if xs[i] <= x <= xs[i + 1]:
            t = (x - xs[i]) / h[i]
            return ((2*t**3 - 3*t**2 + 1) * ys[i] + (t**3 - 2*t**2 + t) * h[i] * m[i]
                    + (-2*t**3 + 3*t**2) * ys[i + 1] + (t**3 - t**2) * h[i] * m[i + 1])
    return ys[-1]


HP = lambda s: pchip([c[0] for c in CURVE], [c[1] for c in CURVE], s)
ATK = lambda s: pchip([c[0] for c in CURVE], [c[2] for c in CURVE], s)


def reduction(d): return 0.0 if d <= 0 else 0.6 * d / (d + 10)


def defense_of(e):
    ps = [p.get("internalName") for p in e.get("passiveSkills", [])]
    d = e.get("defense", 0) + (4 if "HardScales" in ps else 0) + (2 if "EliteVigor" in ps else 0)
    if "CrackingArmor" in ps: d *= 0.6
    if "StackingArmor" in ps: d = max(d, 9)
    return d, ps


def avg_attack(e): return e.get("baseAttack", 0) + (e.get("attackRollMin", 0) + e.get("attackRollMax", 0)) / 2


def set_attack(e, target):
    """攻撃の平均を target にする。 ロールの幅は今の比率で縮める/伸ばす。"""
    cur = max(0.1, avg_attack(e)); f = target / cur
    lo, hi = e.get("attackRollMin", 0), e.get("attackRollMax", 0)
    if hi > 0:
        hi2 = max(1, round(hi * f)); lo2 = min(lo, hi2)
        e["attackRollMin"], e["attackRollMax"] = lo2, hi2
    e["baseAttack"] = max(1, round(target - (e.get("attackRollMin", 0) + e.get("attackRollMax", 0)) / 2))


def main():
    dry = "--dry" in sys.argv
    raw = P.read_bytes(); crlf = b"\r\n" in raw
    d = json.loads(raw.decode("utf-8")); es = d["enemies"]; byid = {e["id"]: e for e in es}
    base = [e for e in es if not e["id"].startswith("boss_layer") and 1 <= e.get("floor", 0) < 99 and not e.get("elite")]
    if all("scale" in e for e in base):
        # 2 回目以降: 一度決めた並び (scale) を保つ。 脅威度は数値を変えると動くので並びの元にしない
        base.sort(key=lambda e: (e["scale"], e["id"]))
        order = [e["id"] for e in base]
    else:
        # 初回: 今の強さ (脅威度) の順 + 手直し
        base.sort(key=lambda e: (e.get("danger", 0), e["id"]))
        order = [e["id"] for e in base]
        for eid, at in ORDER_FIX:
            order.remove(eid); order.insert(at, eid)
    N = len(order); table = []
    for i, eid in enumerate(order):
        s = round(i / (N - 1), 4)
        eh, ea_ = (ELITE_HP_EASY, ELITE_ATK_EASY) if s < ELITE_EASY_BELOW else (ELITE_HP, ELITE_ATK)
        for e, hpm, atm in ((byid[eid], 1.0, 1.0), (byid.get(eid + "_elite"), eh, ea_)):
            if e is None: continue
            src = byid[eid]      # 個性は素の型の数値から取る (精鋭も同じ個性)
            k = max(-1.2, min(1.2, math.log(src["maxHP"] / max(0.1, avg_attack(src))) - math.log(HP(s) / ATK(s))))
            dd, ps = defense_of(e)
            eth = 2 if "Ethereal" in ps else 1
            # 攻撃に掛ける個性は ±20% まで (攻撃はブロックで受けられる範囲に留める)。 残りは HP で受け持ち、 HP × 攻撃 は尺度どおりに保つ
            af = min(ATK_PERSONALITY_MAX, max(1 / ATK_PERSONALITY_MAX, math.exp(-k / 2)))
            atk = ATK(s) * af * atm
            hp = HP(s) * ATK(s) / (ATK(s) * af) * hpm * (1 - reduction(dd)) / eth * HP_FIX.get(eid, 1.0)
            old = (e["maxHP"], avg_attack(e))
            e["maxHP"] = max(1, round(hp)); set_attack(e, atk)
            e["scale"] = s
            first = min(L for L, (a, b) in WINDOWS.items() if a - 1e-9 <= s <= b + 1e-9)
            e["floor"] = DEPTH_OF[first]
            table.append((s, e["displayName"], [L for L, (a, b) in WINDOWS.items() if a - 1e-9 <= s <= b + 1e-9], old, (e["maxHP"], avg_attack(e)), dd))

    print("| s | 敵 | 層 | HP 今→新 | 攻撃 今→新 | 防御 |")
    for s, n, Ls, o, nw, dd in table:
        print(f"| {s:.2f} | {n} | {'・'.join(map(str, Ls))} | {o[0]} → {nw[0]} | {o[1]:.1f} → {nw[1]:.1f} | {dd:.0f} |")

    # ヴェスカ (最終層) の p1〜p3 は HP が 4 層のボスより低く (合計 6500 < 10775)、 5 層の死亡率が 0.1% だった (2026-11-05)。
    #   HP を実数で引き上げ、 攻撃は精鋭の最大並みへ。 p4 は停滞×エスカレーションで決まる (HP/攻撃は効かない) ので触らない。
    for vid, (vhp, vatk) in VESCA_FIX.items():
        v = byid[vid]; v["maxHP"] = vhp; set_attack(v, vatk)

    print()
    for L, ids in BOSSES.items():
        a, b = WINDOWS[L]
        pool = [e for e in es if e.get("scale", -1) >= a - 1e-9 and e.get("scale", -1) <= b + 1e-9]
        mh = max(e["maxHP"] for e in pool if not e.get("elite")); eh = max(e["maxHP"] for e in pool if e.get("elite"))
        ea = max(avg_attack(e) for e in pool if e.get("elite"))
        A, B = BOSS_HP_MUL[L]; lb = round(max(mh * A, eh * B))
        for bid in ids:
            bo = byid[bid]; old = (bo["maxHP"], avg_attack(bo))
            bo["maxHP"] = lb if L in BOSS_HP_EXACT else max(bo["maxHP"], lb)
            if bid in BOSS_ATK_FIX:
                set_attack(bo, BOSS_ATK_FIX[bid])
            elif bid not in BOSS_KEEP_ATTACK:
                set_attack(bo, min(max(avg_attack(bo), ea * BOSS_ATK_LO), ea * BOSS_ATK_HI))
            print(f"| {L} | {bo['displayName']} | 雑魚の最大 HP {mh}・精鋭の最大 HP {eh}・攻撃 {ea:.1f} | HP {old[0]} → {bo['maxHP']} | 攻撃 {old[1]:.1f} → {avg_attack(bo):.1f} |")

    if dry: return
    out = json.dumps(d, ensure_ascii=False, indent=2)
    if crlf: out = out.replace("\n", "\r\n")
    P.write_bytes(out.encode("utf-8"))
    print("書いた:", P.relative_to(ROOT))


if __name__ == "__main__":
    main()
