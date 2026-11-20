"""enemies.json の各敵に脅威度 (danger・内部値) を書き込む。

脅威度 = 実効 HP × 1 ターンの平均攻撃 ÷ 100 (整数・最低 1)。 層の中で「左 (始点側) ほど脅威度の低い敵が出る」抽選に使う
(GameLoop.FloorManager.PickEnemyForNode・docs/GAME.md §5)。 敵の数値やパッシブを変えたら実行し直す。

  実効 HP   = 最大 HP ÷ (1 − 防御の軽減率)。 虚体 (被ダメ半減) は ×2
              防御 = defense + 精鋭 2 + 硬鱗 4。 割れる装甲は初期の 6 割、 積み上がる装甲は 9 (3 ターン目あたり) とみなす
              軽減率は DefenseFormula と同じ 0.6 × 防御 ÷ (防御 + 10)
  平均攻撃 = 基礎攻撃 + 攻撃ロールの平均。 構える敵は 3 ターンに 1 度 guardAttackMul を掛けた平均

2026-11-20 (docs/system-overhaul-spec.md §3-2): パッシブの上乗せを数える (値は ATK_ADD 以下の表・仮置き)。
  攻撃: 足し算 (威圧+ 3 など)・1 ターン目だけの上乗せは FIGHT_TURNS で割って按分・2 回攻撃や出目 1 つ増しは掛け算
  耐久: 重甲は防御 +4・回復を持つ敵と霊体は実効 HP に掛け算
  床: 〈死の宣告〉〈13 番目の宣告〉を持つ敵は ×1.8 し、 さらに同じ層 (尺度の窓 × エリートか) の候補の下位 1/3 の境目 + 1 を下回らない
      (FloorManager.NarrowByBand の安全な帯に入らない・境目は宣告持ちを除いた並びで決まる)

ボス・商人 (floor 99) は抽選に入らないので書かない。
使い方: python Tools/enemy_danger.py
"""
import json
from pathlib import Path

from enemy_scale import WINDOWS   # FloorManager.ScaleWindows と同じ窓 (enemy_scale.py が揃えている)

ROOT = Path(__file__).resolve().parent.parent
P = ROOT / "Assets/Resources/enemies.json"


def reduction(d):
    return 0.0 if d <= 0 else 0.6 * d / (d + 10)


FIGHT_TURNS = 4   # 1 ターン目だけの上乗せを按分する戦闘の長さ (雑魚 2〜5 ターン・GAME.md §13-5)
ATK_ADD = {"IntimidatePlus": 3, "BruteForce": 2, "EliteDarkKnight": 2, "EliteMinotaur": 1,
           "Rampage": 2, "CounterStance": 2, "HoningDuel": 2}
ATK_OPENING = {"Sprint": 2}          # 1 ターン目だけ攻撃 +N
ATK_MUL = {"MultiHead": 1.25, "NightLord": 1.25, "EliteWraith": 1.25}
EHP_MUL = {"Lifesteal": 1.15, "Undying": 1.1, "Regeneration": 1.1, "EliteWraith": 1.5}
SENTENCE = ("DeathSentence", "Decree13th")
SENTENCE_MUL = 1.8


def danger(e):
    ps = [p.get("internalName") for p in e.get("passiveSkills", [])]
    d = (e.get("defense", 0) + (2 if "EliteVigor" in ps else 0) + (4 if "HardScales" in ps else 0)
         + (4 if "EliteLizard" in ps else 0))
    if "CrackingArmor" in ps:
        d *= 0.6
    if "StackingArmor" in ps:
        d = max(d, 9)
    ehp = e["maxHP"] / (1 - reduction(d)) * (2 if "Ethereal" in ps else 1)
    for k, m in EHP_MUL.items():
        if k in ps:
            ehp *= m
    atk = e.get("baseAttack", 0) + (e.get("attackRollMin", 0) + e.get("attackRollMax", 0)) / 2
    atk += sum(v for k, v in ATK_ADD.items() if k in ps)
    atk += sum(v for k, v in ATK_OPENING.items() if k in ps) / FIGHT_TURNS
    if e.get("guardPeriod"):
        atk *= (e["guardPeriod"] - 1 + (e.get("guardAttackMul") or 1)) / e["guardPeriod"]
    for k, m in ATK_MUL.items():
        if k in ps:
            atk *= m
    v = ehp * atk / 100
    if any(k in ps for k in SENTENCE):
        v *= SENTENCE_MUL
    return max(1, round(v))


def has_sentence(e):
    return any(p.get("internalName") in SENTENCE for p in e.get("passiveSkills", []))


def pools(enemies):
    """FloorManager.BuildCandidates の尺度の窓ごとの候補 (エリートと雑魚は別)。 尺度の無い敵 (scale < 0) は入れない。"""
    out = []
    for L, (lo, hi) in WINDOWS.items():
        for elite in (False, True):
            c = [e for e in enemies if e.get("scale", -1) >= 0 and bool(e.get("elite")) == elite
                 and lo - 1e-4 <= e["scale"] <= hi + 1e-4]
            if c:
                out.append((L, elite, c))
    return out


def safe_cut(cands):
    """FloorManager.NarrowByBand の境目: 脅威度の低い順に並べた 1/3 番目の値 (これ以下が安全な帯)。"""
    n = len(cands)
    if n <= 1:
        return None
    third = max(1, (n + 2) // 3)
    return sorted(e["danger"] for e in cands)[third - 1]


def apply_sentence_floor(enemies):
    """宣告持ちを、 出る層の安全な帯の境目 + 1 まで持ち上げる。 境目は宣告持ちを除いた並びで決める (宣告持ちが上にいる限り境目は動かない)。"""
    for L, elite, c in pools(enemies):
        others = [e for e in c if not has_sentence(e)]
        n = len(c)
        third = max(1, (n + 2) // 3)
        if n <= 1 or third > len(others):
            continue
        cut = sorted(e["danger"] for e in others)[third - 1]
        for e in c:
            if has_sentence(e) and e["danger"] <= cut:
                e["danger"] = cut + 1


def main():
    raw = P.read_bytes()
    crlf = b"\r\n" in raw
    d = json.loads(raw.decode("utf-8"))
    n = 0
    for e in d["enemies"]:
        if e["id"].startswith("boss_layer") or e.get("floor", 0) >= 99:
            e.pop("danger", None)
            continue
        e["danger"] = danger(e); n += 1
    rated = [e for e in d["enemies"] if "danger" in e]
    apply_sentence_floor(rated)
    for L, elite, c in pools(rated):   # 検算: 宣告持ちは安全な帯に入らない
        cut = safe_cut(c)
        for e in c:
            assert not (cut is not None and has_sentence(e) and e["danger"] <= cut), (L, elite, e["displayName"], e["danger"], cut)
    out = json.dumps(d, ensure_ascii=False, indent=2)
    if crlf:
        out = out.replace("\n", "\r\n")
    P.write_bytes(out.encode("utf-8"))
    for e in sorted((e for e in d["enemies"] if "danger" in e), key=lambda e: e["danger"]):
        print(f'{e["danger"]:5d}  F{e["floor"]}  {e["displayName"]}')
    print(n, "体に書いた")


if __name__ == "__main__":
    main()
