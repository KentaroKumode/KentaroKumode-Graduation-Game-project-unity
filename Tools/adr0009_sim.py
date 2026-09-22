# -*- coding: utf-8 -*-
"""ADR-0009 相互攻撃モデルのヘッドレス事前シミュレータ。

Accepted 判定 (docs/adr/0009-mutual-attack-combat.md Verification) の 5 条件を
実 enemies.json の敵値を入力にスイープする。C# 実装 (ADR-0008 W7) の前段検証。

ターン構造 (ADR-0009 8 段) を忠実にモデル化:
  0. 充電消費判定 (1 + P//N、不足で全パッシブ停止)
  1. 敵ロール → 敵攻撃合計 確定 (基礎攻撃値×段階倍率 + 敵ダイス)
  2. 予告 (ポリシーは実値を知って配線する = 完全情報)
  3. 自ロール
  4. リロールは省略 (保守側の近似)
  5. 配線 (3^dice 全列挙で効用最大の割当)
  6. 解決: 自攻撃 → 敵攻撃 (撃破時は敵攻撃なし)
  7. ターン終了

簡略化 (C# 本実装との差分・レポートに明記):
  - 会心/固有パッシブ/消耗品/リロールなし。パッシブは「電力を食う汎用装置」に抽象化
    (攻撃+2 / ブロック+2 / シールド+3 蓄積 のいずれか)。
  - 敵パッシブなし (基礎攻撃値+HP のみ)。

usage: python tools/adr0009_sim.py [--trials 2000] [--out AutoRunLogs/adr0009_sim]
"""
import argparse
import io
import json
import os
import random
import statistics
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- 敵モデル


class Enemy:
    def __init__(self, eid, hp, dice, dmax, threat, base_atk, profile):
        self.id = eid
        self.hp = hp
        self.dice = dice
        self.dmax = dmax
        self.threat = threat
        self.base_atk = base_atk   # enemies.json 新設予定パラメータ (シミュ内で合成)
        self.profile = profile     # 'rush' | 'std' | 'gentle' | 'spike'


# エスカレーション: T5/10/15/20 閾値の段階倍率 (ADR-0009 柱4)
PROFILES = {
    "rush":   [1.00, 1.40, 1.80, 2.20, 2.60],
    "mid":    [1.00, 1.30, 1.60, 1.95, 2.30],
    "std":    [1.00, 1.25, 1.50, 1.75, 2.00],
    "gentle": [1.00, 1.15, 1.30, 1.45, 1.60],
}
THRESHOLDS = [5, 10, 15, 20]
SPIKE_TURN = 12          # spike 型: この T のみ ×2.5 (断罪周期の先行例)
SPIKE_MULT = 2.5


def stage_of(turn):
    s = 0
    for t in THRESHOLDS:
        if turn > t:
            s += 1
    return s


BETA = 1.0  # 敵ダイスロールの攻撃寄与率 (旧ロール勝負用ダイスは攻撃加算には熱すぎる)


def enemy_attack_value(e, turn, slope_key, rng):
    mult = PROFILES[slope_key][stage_of(turn)]
    if e.profile == "spike" and turn == SPIKE_TURN:
        mult = max(mult, SPIKE_MULT)
    roll = sum(rng.randint(1, e.dmax) for _ in range(e.dice))
    # 倍率は攻撃値全体 (基礎+ダイス寄与) に適用 ── 基礎値のみだと
    # シールド蓄積系の成長 (+9/T) を追い越せずグラインド無敵化する
    return int(round(mult * (e.base_atk + BETA * roll)))


def load_enemies(alpha, hp_scale, profile_map):
    path = os.path.join(ROOT, "Assets", "Resources", "enemies.json")
    with io.open(path, encoding="utf-8-sig") as f:
        data = json.load(f)["enemies"]
    out = {}
    for en in data:
        eid = en["id"]
        base_atk = max(1, int(round(alpha * en.get("threat", 1))))
        hp = max(1, int(round(en["maxHP"] * hp_scale)))
        out[eid] = Enemy(eid, hp, en["diceCount"], en["diceMaxValue"],
                         en.get("threat", 1), base_atk, profile_map.get(eid, "std"))
    return out


# ---------------------------------------------------------------- 自機ビルド


class Build:
    """w_*: 配線効用の重み (= ビルドの性格)。passive_kind: 電力装置の効果先。"""

    def __init__(self, name, atk_power, dice, dmax, max_hp,
                 passives, passive_kind, w_atk, w_def, w_chg):
        self.name = name
        self.atk_power = atk_power
        self.dice = dice
        self.dmax = dmax
        self.max_hp = max_hp
        self.passives = passives          # 通電対象パッシブ数 P
        self.passive_kind = passive_kind  # 'atk' | 'block' | 'shield'
        self.w_atk, self.w_def, self.w_chg = w_atk, w_def, w_chg


def make_builds(passives_override=None):
    """Verification 3 の 4 原型 (T4 武器・ボス帯想定 HP)。"""
    p = passives_override
    return [
        Build("アグロ",       8, 4, 6, 55, p if p is not None else 2, "atk",    1.30, 0.55, 0.45),
        Build("バランス",     5, 4, 6, 60, p if p is not None else 3, "atk",    1.00, 1.00, 0.60),
        Build("ガード",       4, 4, 6, 65, p if p is not None else 3, "block",  0.80, 1.35, 0.60),
        Build("シールド蓄積", 4, 4, 6, 60, p if p is not None else 4, "shield", 0.70, 1.10, 0.90),
    ]


# ---------------------------------------------------------------- 電力経済 (柱5)

CHARGE_MAX = 10


def power_cost(p_count, n_per):
    """ターン開始消費 = 1 + (発動可能パッシブ N 個ごとに +1)。"""
    return 1 + p_count // n_per if p_count > 0 else 0


# ---------------------------------------------------------------- 配線ポリシー


def enumerate_allocs(k):
    """k 個のダイスを (攻撃, ブロック, 充電) に振る全割当 → (a本,b本,c本) の組。
    出目は個別なので個数組ではなくインデックス割当が要るが、効用は
    「どの出目を攻撃に置くか」で決まる → 出目降順ソート済み前提で
    『攻撃には高い目から・ブロックは次・充電は残り(出目不問)』が支配的。
    よって (攻撃本数, ブロック本数, 充電本数) の組で全列挙して良い。"""
    out = []
    for a in range(k + 1):
        for b in range(k - a + 1):
            out.append((a, b, k - a - b))
    return out


def choose_wiring(rolls, enemy_atk, my_hp, enemy_hp, shield, build,
                  charge, cost_next, powered):
    """完全情報 (敵実値・自出目) での配線。効用最大の (a,b,c) を返す。"""
    faces = sorted(rolls, reverse=True)
    k = len(faces)
    pref = [0]
    for v in faces:
        pref.append(pref[-1] + v)

    atk_bonus = 2 * build.passives if (powered and build.passive_kind == "atk") else 0
    blk_bonus = 2 * build.passives if (powered and build.passive_kind == "block") else 0

    best, best_u = (k, 0, 0), -1e18
    for a, b, c in enumerate_allocs(k):
        dmg = build.atk_power + pref[a] + atk_bonus
        block = (pref[a + b] - pref[a]) + blk_bonus
        taken = max(0, enemy_atk - block)
        eff_taken = max(0, taken - shield)
        gained = min(CHARGE_MAX, charge + c) - charge

        # 実点数ベースの交換レート評価 (割合比較はレース価値を過小評価し
        # 亀化するため不採用)。ブロック価値は敵攻撃実値でキャップ = 過剰
        # ブロックは無価値 → 余りは自然に攻撃/充電へ流れる。
        u = build.w_atk * min(dmg, enemy_hp) \
            + build.w_def * min(block, enemy_atk)
        if dmg >= enemy_hp:
            u += 50.0 + enemy_atk             # 今ターン撃破 = 敵攻撃自体が消える
        if eff_taken >= my_hp:
            u -= 10000.0                      # 致死回避を最優先
        # 充電価値: 次ターンの消費に届いていない分は高価値、余剰は低価値
        need = max(0, cost_next - charge)
        u += build.w_chg * (min(gained, need) * 4 + max(0, gained - need) * 0.5)
        u += 0.01 * a  # タイブレーク: 同効用なら攻撃 (レース進行) を選ぶ
        if u > best_u:
            best_u, best = u, (a, b, c)
    return best, pref


# ---------------------------------------------------------------- 戦闘 1 回


def fight(build, enemy, slope_key, rng, initial_charge=0, log_wiring=None):
    """1 戦闘。returns (won, turns, hp_lost, stall_max, blackouts)."""
    my_hp = build.max_hp
    e_hp = enemy.hp
    shield = 0
    charge = initial_charge
    stall = stall_max = 0
    blackouts = 0
    heals = 2          # 緊急回復 (BOT の消耗品運用の近似: HP25%以下で+15、2回まで)
    turn = 0
    while turn < 60:
        turn += 1
        # 0. 充電消費判定
        cost = power_cost(build.passives, N_PER)
        if build.passives > 0 and charge >= cost:
            charge -= cost
            powered = True
        else:
            powered = build.passives == 0
            if build.passives > 0:
                blackouts += 1
        # 緊急回復 (AutoRunner の emergencyHeal 相当)
        if heals > 0 and my_hp * 4 <= build.max_hp:
            my_hp = min(build.max_hp, my_hp + 15)
            heals -= 1
        # シールド蓄積装置 (persistent)
        if powered and build.passive_kind == "shield":
            shield += 3 * (build.passives // 2 + 1)
        # 1-2. 敵ロール + 予告
        e_atk = enemy_attack_value(enemy, turn, slope_key, rng)
        # 3. 自ロール
        rolls = [rng.randint(1, build.dmax) for _ in range(build.dice)]
        # 5. 配線
        cost_next = power_cost(build.passives, N_PER)
        (a, b, c), pref = choose_wiring(
            rolls, e_atk, my_hp, e_hp, shield, build, charge, cost_next, powered)
        if log_wiring is not None:
            log_wiring[0] += a
            log_wiring[1] += b
            log_wiring[2] += c
        atk_bonus = 2 * build.passives if (powered and build.passive_kind == "atk") else 0
        blk_bonus = 2 * build.passives if (powered and build.passive_kind == "block") else 0
        dmg = build.atk_power + pref[a] + atk_bonus
        block = (pref[a + b] - pref[a]) + blk_bonus
        charge = min(CHARGE_MAX, charge + c)
        # 6. 解決 (自先制)
        prev_e_hp = e_hp
        e_hp -= dmg
        if e_hp <= 0:
            return True, turn, build.max_hp - my_hp, stall_max, blackouts
        taken = max(0, e_atk - block)
        if taken <= shield:
            shield -= taken
        else:
            my_hp -= (taken - shield)
            shield = 0
        if my_hp <= 0:
            return False, turn, build.max_hp, stall_max, blackouts
        # 膠着検出 (敵 HP が減らないターンの連続数)
        stall = stall + 1 if e_hp >= prev_e_hp else 0
        stall_max = max(stall_max, stall)
    return False, 60, build.max_hp - my_hp, stall_max, blackouts  # タイムアウト=敗北扱い


# ---------------------------------------------------------------- スイープ本体

# 旧パイプライン基準 (AutoRunLogs/batch_20260623_082331_n10000 サマリ)
BASELINE_TURNS = {
    "boss_layer2": 2.1, "boss_layer3": 4.6, "boss_layer4": 18.1,
    "boss_layer5": 23.2, "boss_layer5_hidden": 23.9, "boss_layer6": 8.1,
}
ROSTER = ["boss_layer2", "boss_layer3", "boss_layer4",
          "boss_layer5", "boss_layer5_hidden",
          "minotaur", "golem", "death_knight", "wraith"]

N_PER = 3  # 柱5 の N (パッシブ N 個ごとに消費+1)。スイープで上書き


def run_sweep(trials, alpha, hp_scale, beta, out_lines):
    global N_PER, BETA
    BETA = beta
    rng = random.Random(20260715)
    profile_map = {"boss_layer4": "gentle", "boss_layer5": "std",
                   "boss_layer5_hidden": "rush", "boss_layer6": "std",
                   "minotaur": "rush", "golem": "gentle",
                   "death_knight": "spike", "wraith": "std"}
    enemies = load_enemies(alpha, hp_scale, profile_map)

    w = out_lines.append
    w(f"\n{'='*72}\nalpha={alpha} (base_atk=alpha×threat) / 敵HPスケール={hp_scale}"
      f" / 敵ダイス寄与β={beta}\n{'='*72}")

    # ---- V1/V2: ターン数分布 + 膠着 (バランスビルド・標準傾斜)
    N_PER = 3
    w("\n---- V1/V2: ターン数分布と膠着 (バランス・傾斜std・充電初期5) ----")
    w(f"{'敵':22s} {'勝率':>6s} {'平均T':>6s} {'中央T':>5s} {'P90T':>5s} "
      f"{'最大膠着':>8s} {'旧平均T':>8s}")
    build = make_builds()[1]
    for eid in ROSTER:
        e = enemies[eid]
        res = [fight(build, e, "std", rng, initial_charge=5) for _ in range(trials)]
        wins = [r for r in res if r[0]]
        turns = [r[1] for r in res]
        stalls = max(r[3] for r in res)
        base = BASELINE_TURNS.get(eid)
        w(f"{eid:22s} {len(wins)/len(res)*100:5.1f}% {statistics.mean(turns):6.1f} "
          f"{statistics.median(turns):5.0f} {sorted(turns)[int(len(turns)*0.9)]:5d} "
          f"{stalls:8d} {('%.1f' % base) if base else '-':>8s}")

    # ---- V3: 傾斜 × ビルド (勝率と HP 収支)
    w("\n---- V3: 傾斜×ビルド 勝率% / 平均HP損耗 (ボス帯 5 体平均) ----")
    boss_set = ["boss_layer3", "boss_layer4", "boss_layer5", "boss_layer5_hidden", "golem"]
    header = f"{'ビルド':14s}" + "".join(f"{s:>16s}" for s in PROFILES)
    w(header)
    for b in make_builds():
        row = f"{b.name:14s}"
        for slope in PROFILES:
            res = []
            for eid in boss_set:
                res += [fight(b, enemies[eid], slope, rng, initial_charge=5)
                        for _ in range(trials // 2)]
            wr = sum(1 for r in res if r[0]) / len(res) * 100
            hp = statistics.mean(r[2] for r in res)
            row += f"{wr:7.1f}%/{hp:5.1f}HP"
        w(row)

    # ---- V4: 配線分散 (ビルド別・全ターンの端子割合)
    w("\n---- V4: 配線分散 (攻撃/ブロック/充電 の配置本数割合・傾斜std) ----")
    for b in make_builds():
        tally = [0, 0, 0]
        for eid in boss_set:
            for _ in range(trials // 4):
                fight(b, enemies[eid], "std", rng, initial_charge=5, log_wiring=tally)
        tot = max(1, sum(tally))
        w(f"{b.name:14s} 攻撃 {tally[0]/tot*100:5.1f}% / ブロック {tally[1]/tot*100:5.1f}%"
          f" / 充電 {tally[2]/tot*100:5.1f}%")

    # ---- V5: 電力経済 (パッシブ数 × N × 初期充電 → 勝率/停電)
    w("\n---- V5: 電力経済 パッシブ数×N (バランス型・傾斜std・ボス帯) ----")
    w(f"{'P':>3s} {'N':>3s} {'初期chg':>7s} {'勝率':>7s} {'停電/戦':>8s} {'平均T':>6s}")
    for n_per in (2, 3, 4):
        N_PER = n_per
        for p in (0, 2, 4, 6):
            for init_c in (0, 5):
                bb = make_builds(passives_override=p)[1]
                res = []
                for eid in boss_set:
                    res += [fight(bb, enemies[eid], "std", rng, initial_charge=init_c)
                            for _ in range(trials // 2)]
                wr = sum(1 for r in res if r[0]) / len(res) * 100
                bo = statistics.mean(r[4] for r in res)
                tt = statistics.mean(r[1] for r in res)
                w(f"{p:3d} {n_per:3d} {init_c:7d} {wr:6.1f}% {bo:8.2f} {tt:6.1f}")
    N_PER = 3


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trials", type=int, default=2000)
    ap.add_argument("--grid", type=str,
                    default="1.2:0.5:0.4,2.0:0.5:0.4,1.2:0.7:0.4,2.0:0.7:0.3,1.6:0.6:0.5",
                    help="alpha:hp_scale:beta のカンマ区切り")
    ap.add_argument("--out", type=str, default="AutoRunLogs/adr0009_sim")
    args = ap.parse_args()

    lines = ["ADR-0009 相互攻撃モデル ヘッドレスシミュレート",
             f"trials/敵={args.trials} / 乱数固定 / 8段ターン構造 / 完全情報配線(3^5全列挙)",
             "簡略化: 会心・消耗品・リロール・敵パッシブなし。パッシブ=抽象電力装置",
             f"旧基準: batch_20260623 n10000 (ボス平均T: {BASELINE_TURNS})"]
    for pair in args.grid.split(","):
        a, h, b = pair.split(":")
        run_sweep(args.trials, float(a), float(h), float(b), lines)

    out_dir = os.path.join(ROOT, args.out)
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, "sim_report.txt")
    with io.open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    sys.stdout.write("\n".join(lines) + f"\n\nwritten: {out_path}\n")


if __name__ == "__main__":
    main()
