"""敵 HP の減りの経路・品別 (HpLossDiag) を、 種類 × 層で表にする。
使い方: python hploss_report.py <learn_parallel のセッション> [層ごとに出す上位の品の数]"""
import glob, json, sys
from collections import defaultdict

root = sys.argv[1]
TOP = int(sys.argv[2]) if len(sys.argv) > 2 else 6
KINDS = ["通常", "エリート", "ボス"]
LAYERS = 5
C = 3 * LAYERS

tot = defaultdict(lambda: [0.0] * C)
for f in glob.glob(root + "/round_000/c*/response.json"):
    r = json.load(open(f, encoding="utf-8"))
    names = r.get("hpLossNames", [])
    arr = r.get("hpLoss", [])
    for i, n in enumerate(names):
        for j in range(C):
            tot[n][j] += arr[i * C + j]

if not tot:
    print("hpLoss が空 (計装が効いていない)")
    sys.exit(1)


def cell(key, k, l):
    return tot[key][k * LAYERS + l] if key in tot else 0.0


channels = sorted(n for n in tot if n.startswith("経路:"))
print("■ 敵 HP が減った経路 (1 戦あたり HP / その戦闘の総 HP に対する %)\n")
for k in range(3):
    for l in range(LAYERS):
        n = cell("戦闘", k, l)
        if n < 30:
            continue
        hp = cell("総HP", k, l)
        left = cell("終了HP", k, l)
        lost = hp - left
        turns = cell("ターン", k, l) / n
        parts = []
        s = 0.0
        for ch in channels:
            v = cell(ch, k, l)
            s += v
            if v / hp >= 0.005:
                parts.append((v, ch[3:]))
        parts.sort(reverse=True)
        resid = lost - s
        txt = " / ".join(f"{name} {100 * v / hp:.0f}%" for v, name in parts[:7])
        print(f"{KINDS[k]} 層{l + 1}  戦闘 {int(n):>6}  敵HP {hp / n:>6.0f}  ターン {turns:.1f}  | {txt}"
              f" | 計装外 {100 * resid / hp:+.0f}% | 取りこぼし(未撃破) {100 * left / hp:.0f}%")

print("\n■ 品ごとの要求量 (1 戦あたり。 固定=軽減を通らない固定ダメ / 直接=預けて通すダメージ。 倍率・頭打ちの前の値)")
srcs = [n for n in tot if n.startswith("品:") or n.startswith("状態:") or n.startswith("端子:")]
for k in range(3):
    for l in range(LAYERS):
        n = cell("戦闘", k, l)
        if n < 30:
            continue
        hp = cell("総HP", k, l)
        rows = sorted(((cell(s, k, l), s) for s in srcs), reverse=True)[:TOP]
        rows = [(v, s) for v, s in rows if v > 0]
        if not rows:
            continue
        txt = " / ".join(f"{s.replace('品:', '')} {v / n:.0f}({100 * v / hp:.0f}%)" for v, s in rows)
        print(f"{KINDS[k]} 層{l + 1}: {txt}")


# ── 毒 ──
print("\n■ 毒: ターン開始時の平均スタック (種類 × ターン)")
for k in range(3):
    row = []
    for t in range(1, 9):
        n = sum(cell(f"毒段n:T{t}", k, l) for l in range(LAYERS))
        s = sum(cell(f"毒段:T{t}", k, l) for l in range(LAYERS))
        row.append(f"T{t}:{s / n:.1f}" if n >= 50 else f"T{t}:-")
    print(f"{KINDS[k]:<5} " + "  ".join(row))

print("\n■ 毒の付与量の出どころ (1 戦あたり・上限で溢れた分も) 種類別")
srcs = sorted({n[len("毒付与:"):] for n in tot if n.startswith("毒付与:")})
for k in range(3):
    fights = sum(cell("戦闘", k, l) for l in range(LAYERS))
    rows = []
    for s in srcs:
        a = sum(cell("毒付与:" + s, k, l) for l in range(LAYERS))
        o = sum(cell("毒溢れ:" + s, k, l) for l in range(LAYERS))
        rows.append((a, o, s))
    rows.sort(reverse=True)
    tota = sum(r[0] for r in rows)
    toto = sum(r[1] for r in rows)
    print(f"{KINDS[k]} (戦闘 {int(fights)}): 付与 {tota / fights:.1f}/戦  うち上限で溢れた {toto / fights:.1f}/戦 ({100 * toto / max(1, tota):.0f}%)")
    for a, o, s in rows[:8]:
        print(f"    {s:<22} {a / fights:5.2f}/戦  溢れ {o / fights:5.2f}")
