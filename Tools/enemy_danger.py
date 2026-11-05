"""enemies.json の各敵に脅威度 (danger・内部値) を書き込む。

脅威度 = 実効 HP × 1 ターンの平均攻撃 ÷ 100 (整数・最低 1)。 層の中で「左 (始点側) ほど脅威度の低い敵が出る」抽選に使う
(GameLoop.FloorManager.PickEnemyForNode・docs/GAME.md §5)。 敵の数値やパッシブを変えたら実行し直す。

  実効 HP   = 最大 HP ÷ (1 − 防御の軽減率)。 虚体 (被ダメ半減) は ×2
              防御 = defense + 精鋭 2 + 硬鱗 4。 割れる装甲は初期の 6 割、 積み上がる装甲は 9 (3 ターン目あたり) とみなす
              軽減率は DefenseFormula と同じ 0.6 × 防御 ÷ (防御 + 10)
  平均攻撃 = 基礎攻撃 + 攻撃ロールの平均。 構える敵は 3 ターンに 1 度 guardAttackMul を掛けた平均

ボス・商人 (floor 99) は抽選に入らないので書かない。
使い方: python Tools/enemy_danger.py
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
P = ROOT / "Assets/Resources/enemies.json"


def reduction(d):
    return 0.0 if d <= 0 else 0.6 * d / (d + 10)


def danger(e):
    ps = [p.get("internalName") for p in e.get("passiveSkills", [])]
    d = e.get("defense", 0) + (2 if "EliteVigor" in ps else 0) + (4 if "HardScales" in ps else 0)
    if "CrackingArmor" in ps:
        d *= 0.6
    if "StackingArmor" in ps:
        d = max(d, 9)
    ehp = e["maxHP"] / (1 - reduction(d)) * (2 if "Ethereal" in ps else 1)
    atk = e.get("baseAttack", 0) + (e.get("attackRollMin", 0) + e.get("attackRollMax", 0)) / 2
    if e.get("guardPeriod"):
        atk *= (e["guardPeriod"] - 1 + (e.get("guardAttackMul") or 1)) / e["guardPeriod"]
    return max(1, round(ehp * atk / 100))


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
    out = json.dumps(d, ensure_ascii=False, indent=2)
    if crlf:
        out = out.replace("\n", "\r\n")
    P.write_bytes(out.encode("utf-8"))
    for e in sorted((e for e in d["enemies"] if "danger" in e), key=lambda e: e["danger"]):
        print(f'{e["danger"]:5d}  F{e["floor"]}  {e["displayName"]}')
    print(n, "体に書いた")


if __name__ == "__main__":
    main()
