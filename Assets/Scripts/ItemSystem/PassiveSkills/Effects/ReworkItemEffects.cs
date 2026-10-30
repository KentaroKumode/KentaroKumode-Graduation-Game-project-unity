using UnityEngine;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  アイテム再編 (2026-10-31)。 設計は docs/GAME.md §8-6、 計算の位置は DAMAGE_CALC_REFERENCE.md §3-H。
    //
    //  品どうしが 1+1>2 になるよう、 どの品も「何かを増やす」か「何かを条件にする」形にした。
    //  増やすもの・条件にするものは 臨界・充電・シールド・毒・出血・会心・金 の 7 つに寄せてある。
    //  キーワードの出来事 (爆発・充電を得る/使う・過充電・シールドの吸収/半減/割れ・毒の増加/ダメージ/上限・
    //  出血の減衰/消費/0) は Keywords.Kw が配る。 1 つの連鎖の中で同じ品は 1 度しか働かない。
    //
    //  効果を据え置いた品 (八日目の灰燼・雷神槍・過負荷チューナー・素手禁じの蛇血瓶・鋸刃の山刀・先血の腕輪・
    //  医家の反り刃・逆綴じの止血帯・末頁の血花太刀・医書裏の開き針・溶金の領主剣・切り分けた帯封) は元のクラスのまま。
    // ============================================================

    /// <summary>この品群で使う小さな道具。</summary>
    internal static class Rw
    {
        public static GameLoop.RunState Run => GameLoop.GameManager.Instance?.Run;
        public static int Steps => Run?.stepsSinceBattle ?? 0;
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
        public static bool Won(CombatContext ctx) => ctx.enemyCurrentHP <= 0;

        /// <summary>プレイヤーの攻撃が通った (ダメージ後トリガーの中で使う)。</summary>
        public static bool PlayerHit(CombatContext ctx) => ctx.playerWonRoll && ctx.finalDamage > 0;

        public static void Gold(int n, string label)
        {
            var run = Run;
            if (run != null && n > 0) GameLoop.GoldIncome.Gain(run, n, label);
        }

        /// <summary>このターン 1 回だけ (key に今のターン番号を書く)。</summary>
        public static bool OncePerTurn(CombatContext ctx, string key)
        {
            if (Count(ctx, key) == ctx.currentTurn) return false;
            ctx.accumulatedValues[key] = ctx.currentTurn;
            return true;
        }

        /// <summary>この戦闘で 1 回だけ。</summary>
        public static bool OncePerBattle(CombatContext ctx, string key)
        {
            if (Count(ctx, key) > 0) return false;
            ctx.accumulatedValues[key] = 1;
            return true;
        }

        public static void Shield(CombatContext ctx, int n, string label)
        {
            if (n <= 0) return;
            ctx.consShield += n;
            CombatSystem.ShieldDiag.Note(label, n);
            ctx.shieldGainedTotal += n;
        }

        public static void Poison(CombatContext ctx, int n)
        {
            if (n > 0) ctx.AddStatus(StatusTarget.Enemy, "poison", n);
        }

        public static void Log(string s) => Debug.Log(s);
    }

    // ============================================================
    //  臨界
    // ============================================================

    /// <summary>過熱石: 3 個とも攻撃端子に挿したターン、 臨界 +15。</summary>
    public class OverheatStone : IPassiveSkillEffect
    {
        public string SkillId => "過熱石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (!Fam.AllAttack(ctx)) return;
            Kw.AddHeat(ctx, 15);
            Rw.Log($"[過熱石] 全部攻撃 → 臨界 +15 (現 {ctx.rinkaiMeter})");
        }
    }

    /// <summary>炉番の外套: 受けた被ダメージの半分を臨界にする。</summary>
    public class HearthCloak : IPassiveSkillEffect
    {
        public string SkillId => "炉番の外套";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int taken = Rw.Count(ctx, MutualKeys.HpLossFromEnemy);
            if (taken / 2 > 0) Kw.AddHeat(ctx, taken / 2);
        }
    }

    /// <summary>乾き火口箱: 戦闘開始時 臨界 +12。</summary>
    public class DryTinderbox : IPassiveSkillEffect
    {
        public string SkillId => "乾き火口箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            Kw.AddHeat(ctx, 12);
        }
    }

    /// <summary>朝にも熱い竈: 戦闘開始時、 前の戦闘の終わりに残っていた臨界の半分から始まる
    /// (RunState.pendingRinkaiCarryover を CombatManager が戦闘開始時に足す)。</summary>
    public class MorningOven : IPassiveSkillEffect
    {
        public string SkillId => "朝にも熱い竈";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            var run = Rw.Run;
            if (run == null || ctx.rinkaiMeter <= 0) return;
            run.pendingRinkaiCarryover = System.Math.Max(run.pendingRinkaiCarryover, ctx.rinkaiMeter / 2);
        }
    }

    /// <summary>溢れ取りの鋳型: 爆発のとき、 閾値を超えて溜まっていた臨界 1 につき追加ダメージ +2。</summary>
    public class OverflowMold : IPassiveSkillEffect
    {
        public string SkillId => "溢れ取りの鋳型";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int excess = Rw.Count(ctx, Kw.BurstExcessKey);
            if (excess > 0) Kw.Add(ctx, Kw.BurstBonusKey, excess * 2);
        }
    }

    /// <summary>二度目の打金: 爆発した次のターンも、 追加ダメージの半分が乗る。</summary>
    public class SecondStrike : IPassiveSkillEffect
    {
        public string SkillId => "二度目の打金";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int turn = Rw.Count(ctx, Kw.BurstAppliedTurnKey);
            int dmg = Rw.Count(ctx, Kw.BurstAppliedDmgKey);
            if (turn <= 0 || turn != ctx.currentTurn - 1 || dmg / 2 <= 0) return;
            ctx.fixedDamageToEnemy += dmg / 2;
            Rw.Log($"[二度目の打金] 前のターンの爆発の半分 +{dmg / 2}");
        }
    }

    /// <summary>帳簿外の連爆【規則】: 爆発で臨界を 0 にせず閾値ぶんだけ減らす。 溜まっていれば同じターンに何度でも爆発する。</summary>
    public class OffBookChain : IPassiveSkillEffect
    {
        public string SkillId => "帳簿外の連爆";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.rinkaiMultiBurst = true;
        }
    }

    /// <summary>恒熱の炉壁: 爆発のたび、 消えた臨界の 20% をシールドにする。</summary>
    public class HeatWall : IPassiveSkillEffect
    {
        public string SkillId => "恒熱の炉壁";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Rw.Shield(ctx, Kw.EventAmount(ctx) * 20 / 100, SkillId);
        }
    }

    /// <summary>早沸かし釜【規則】: 爆発の閾値が 50 から 35 に下がる。</summary>
    public class QuickKettle : IPassiveSkillEffect
    {
        public string SkillId => "早沸かし釜";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.rinkaiThreshold = System.Math.Min(ctx.rinkaiThreshold, 35);
        }
    }

    /// <summary>焦げ柄の点火スパナ: 爆発のたび充電 +5。</summary>
    public class IgnitionSpanner : IPassiveSkillEffect
    {
        public string SkillId => "焦げ柄の点火スパナ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            ctx.AddCharge(5);
        }
    }

    /// <summary>荒砥: 爆発した攻撃が通ると敵に出血 +4。</summary>
    public class RoughWhetstone : IPassiveSkillEffect
    {
        public string SkillId => "荒砥";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (!ctx.playerWonRoll || Rw.Count(ctx, Kw.BurstThisAttackKey) <= 0) return;
            if (!Rw.OncePerTurn(ctx, "rw.arato")) return;
            ctx.AddEnemyBleed(4);
        }
    }

    /// <summary>無害灯: 臨界が 10 以上の間、 受けるダメージ −2。</summary>
    public class HarmlessLamp : IPassiveSkillEffect
    {
        public string SkillId => "無害灯";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (ctx.rinkaiMeter >= 10) ctx.playerFlatDamageReduction += 2;
        }
    }

    /// <summary>工廠の材料箱: 爆発のたび、 この戦闘中の自分の防御 +2。</summary>
    public class ArsenalCrate : IPassiveSkillEffect
    {
        public string SkillId => "工廠の材料箱";
        private const string Key = "rw.arsenal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (t == PassiveSkillTrigger.OnRinkaiBurst) { Kw.Add(ctx, Key, 2); ctx.playerDefense += 2; return; }
            ctx.playerDefense += Rw.Count(ctx, Key);   // 防御は毎ターン基礎値から組み直されるので積み直す
        }
    }

    /// <summary>継ぎ革の旅靴: マップを 1 歩進むごとに、 次の戦闘の開始時の臨界 +3。</summary>
    public class PatchedBoots : IPassiveSkillEffect
    {
        public string SkillId => "継ぎ革の旅靴";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            if (Rw.Steps > 0) Kw.AddHeat(ctx, Rw.Steps * 3);
        }
    }

    /// <summary>希望の灯片: 戦闘終了時に臨界が 25 以上残っていれば、 最大 HP +2 (永続)。</summary>
    public class HopeShard : IPassiveSkillEffect
    {
        public string SkillId => "希望の灯片";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            var run = Rw.Run;
            if (run == null || !Rw.Won(ctx) || ctx.rinkaiMeter < 25) return;
            run.playerMaxHP += 2;
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + 2);
            Rw.Log($"[希望の灯片] 臨界 {ctx.rinkaiMeter} を残して勝利 → 最大HP +2 ({run.playerMaxHP})");
        }
    }

    // ============================================================
    //  充電
    // ============================================================

    /// <summary>無限モーター: ブロックで敵の攻撃を完全に止めたターン、 止めて余ったブロックを充電にする。</summary>
    public class InfiniteMotor : IPassiveSkillEffect
    {
        public string SkillId => "無限モーター";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.EnemyAtkPreDefense) <= 0) return;   // 敵が攻撃していない
            if (Rw.Count(ctx, MutualKeys.HpLossFromEnemy) > 0) return;        // 止めきれていない
            int spare = ctx.blockSumThisTurn - Rw.Count(ctx, MutualKeys.EnemyAtk);
            if (spare > 0) ctx.AddCharge(spare);
        }
    }

    /// <summary>呼雷粉: 会心のたび充電 +3。</summary>
    public class ThunderPowder : IPassiveSkillEffect
    {
        public string SkillId => "呼雷粉";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.AddCharge(3);
    }

    /// <summary>十五年目の計測器: 過充電になったとき、 過充電で失った充電 1 につき次の攻撃 +2。</summary>
    public class OldGauge : IPassiveSkillEffect
    {
        public string SkillId => "十五年目の計測器";
        private const string Key = "rw.gauge";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnOvercharge, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnOvercharge) { Kw.Add(ctx, Key, Kw.EventAmount(ctx) * 2); return; }
            int bonus = Rw.Count(ctx, Key);
            if (bonus <= 0) return;
            ctx.AddPlayerAttackOrDiceBonus(bonus);
            ctx.accumulatedValues[Key] = 0;
            Rw.Log($"[十五年目の計測器] 針が振り切れた分 攻撃 +{bonus}");
        }
    }

    /// <summary>逆さ避雷針: 敵の攻撃を受けるとき、 充電を 5 使って被ダメ −5 (1 ターン 1 回)。</summary>
    public class InvertedRod : IPassiveSkillEffect
    {
        public string SkillId => "逆さ避雷針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.playerWonRoll || ctx.finalDamage <= 0 || ctx.GetCharge() < 5) return;
            if (!Rw.OncePerTurn(ctx, "rw.rod")) return;
            if (!ctx.ConsumeCharge(5)) return;
            ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 5);
        }
    }

    /// <summary>雷壺【規則】: 充電の上限が無くなる。 代わりにターン開始時、 上限を超えている分の半分が捨てられる。</summary>
    public class ThunderJar : IPassiveSkillEffect
    {
        public string SkillId => "雷壺";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.chargeCapRemoved = true;
    }

    /// <summary>焦げ導線: 充電を使うたび (リロールを含む)、 使った量 × 2 の臨界。</summary>
    public class ScorchedWire : IPassiveSkillEffect
    {
        public string SkillId => "焦げ導線";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, Kw.EventAmount(ctx) * 2);
        }
    }

    /// <summary>帯電の胸甲: 充電を 3 得るたびシールド +2。</summary>
    public class ChargedCuirass : IPassiveSkillEffect
    {
        public string SkillId => "帯電の胸甲";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeGain };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => Rw.Shield(ctx, Kw.Tally(ctx, "rw.cuirass", Kw.EventAmount(ctx), 3) * 2, SkillId);
    }

    /// <summary>雷鯰の棘: 充電 7 以上の間、 攻撃が通るたび敵に毒 +1。</summary>
    public class CatfishSpine : IPassiveSkillEffect
    {
        public string SkillId => "雷鯰の棘";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && ctx.GetCharge() >= 7) Rw.Poison(ctx, 1);
        }
    }

    /// <summary>灼線鞭: 充電を得るたび、 得た量だけ臨界。</summary>
    public class SearingWhip : IPassiveSkillEffect
    {
        public string SkillId => "灼線鞭";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnChargeGain };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, Kw.EventAmount(ctx));
        }
    }

    /// <summary>貸与された時間【規則】: 充電を 10 使うたび、 エスカレーションの段階が上がるのを 1 ターン遅らせる。</summary>
    public class LentTime : IPassiveSkillEffect
    {
        public string SkillId => "貸与された時間";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Kw.Tally(ctx, "rw.lent", Kw.EventAmount(ctx), 10);
            if (n <= 0) return;
            CombatSystem.YachtRoleEffects.AddEscalationDelay(ctx, n);
            Rw.Log($"[貸与された時間] 充電を払って段階を {n} ターン遅らせる");
        }
    }

    // ============================================================
    //  シールド
    // ============================================================

    /// <summary>擦り減った小盾: ブロック端子に挿したダイス 1 個につきシールド +2。</summary>
    public class WornBuckler : IPassiveSkillEffect
    {
        public string SkillId => "擦り減った小盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => Rw.Shield(ctx, Rw.Count(ctx, MutualKeys.BlockDiceCount) * 2, SkillId);
    }

    /// <summary>肩当て: 3 個ともブロック端子に挿したターン、 シールド +8。</summary>
    public class Pauldron : IPassiveSkillEffect
    {
        public string SkillId => "肩当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Fam.AllBlock(ctx)) Rw.Shield(ctx, 8, SkillId);
        }
    }

    /// <summary>鏡返しの小盾: シールドが攻撃を吸収するたび、 吸収した量の 30% を敵に返す。</summary>
    public class MirrorBuckler : IPassiveSkillEffect
    {
        public string SkillId => "鏡返しの小盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, Kw.AbsorbIsAttackKey) <= 0) return;   // 敵の直接攻撃だけ返す
            int r = Kw.EventAmount(ctx) * 30 / 100;
            if (r > 0) Kw.Add(ctx, Kw.ReflectKey, r);
        }
    }

    /// <summary>聖路の白靴: マップを 1 歩進むごとに、 次の戦闘の開始時のシールド +2。</summary>
    public class HolyRoadShoes : IPassiveSkillEffect
    {
        public string SkillId => "聖路の白靴";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, Rw.Steps * 2, SkillId);
    }

    /// <summary>相棒の魂: シールドが割られて 0 になったとき、 割られる前の量の半分で張り直す (1 戦闘 1 回)。</summary>
    public class CompanionSoul : IPassiveSkillEffect
    {
        public string SkillId => "相棒の魂";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldBreak };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int half = Kw.EventAmount(ctx) / 2;
            if (half <= 0 || !Rw.OncePerBattle(ctx, "rw.companion")) return;
            Rw.Shield(ctx, half, SkillId);
            Rw.Log($"[相棒の魂] 割られたシールドを {half} で張り直す");
        }
    }

    /// <summary>二拍目の心臓: HP が 50% を下回った瞬間にシールド (最大 HP の 50%) (1 戦闘 1 回)。
    /// 旧〈命脈〉から戦闘開始時の回復を外した。</summary>
    public class SecondBeatHeart : IPassiveSkillEffect
    {
        public string SkillId => "二拍目の心臓";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0 || ctx.playerCurrentHP <= 0) return;
            if (ctx.playerCurrentHP * 100 > ctx.playerMaxHP * 50) return;
            if (!Rw.OncePerBattle(ctx, "rw.secondBeat")) return;
            int shield = Mathf.CeilToInt(ctx.playerMaxHP * 0.50f);
            Rw.Shield(ctx, shield, SkillId);
            Rw.Log($"[二拍目の心臓] HP50% を下回った → シールド +{shield}");
        }
    }

    /// <summary>騎士団の切り札: 敵の HP が 30% 以下になったターン、 シールドを全部使い、 使った量 × 2 を攻撃に加える (1 戦闘 1 回)。</summary>
    public class KnightsTrump : IPassiveSkillEffect
    {
        public string SkillId => "騎士団の切り札";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyMaxHP <= 0 || ctx.enemyCurrentHP * 100 > ctx.enemyMaxHP * 30) return;
            int sh = ctx.consShield;
            if (sh <= 0 || !Rw.OncePerBattle(ctx, "rw.trump")) return;
            ctx.consShield = 0;
            ctx.AddPlayerAttackOrDiceBonus(sh * 2);
            Rw.Log($"[騎士団の切り札] シールド {sh} を全部使い 攻撃 +{sh * 2}");
        }
    }

    /// <summary>敗残兵の部隊章: シールドを 5 失うたび (吸収・半減とも)、 この戦闘中の攻撃 +1。</summary>
    public class VeteranPatch : IPassiveSkillEffect
    {
        public string SkillId => "敗残兵の部隊章";
        private const string Atk = "rw.veteranAtk";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb, PassiveSkillTrigger.OnShieldDecay, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired)
            {
                int a = Rw.Count(ctx, Atk);
                if (a > 0) ctx.AddPlayerAttackOrDiceBonus(a);
                return;
            }
            int n = Kw.Tally(ctx, "rw.veteran", Kw.EventAmount(ctx), 5);
            if (n > 0) Kw.Add(ctx, Atk, n);
        }
    }

    /// <summary>脈なしの鋼心臓【規則】: シールドがターン開始時に半減しなくなる。</summary>
    public class PulselessHeart : IPassiveSkillEffect
    {
        public string SkillId => "脈なしの鋼心臓";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.shieldNoHalve = true;
    }

    /// <summary>燻りの革胸当て: ターン開始の半減で失ったシールドを臨界にする。</summary>
    public class SmolderingBreastplate : IPassiveSkillEffect
    {
        public string SkillId => "燻りの革胸当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnShieldDecay };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, Kw.EventAmount(ctx));
        }
    }

    /// <summary>避雷の鉢金: シールドが吸収したダメージ 3 につき充電 +1。</summary>
    public class LightningHeadguard : IPassiveSkillEffect
    {
        public string SkillId => "避雷の鉢金";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Kw.Tally(ctx, "rw.headguard", Kw.EventAmount(ctx), 3);
            if (n > 0) ctx.AddCharge(n);
        }
    }

    /// <summary>毒棘の小盾: シールドが攻撃を吸収するたび敵に毒 +1。</summary>
    public class ThornBuckler : IPassiveSkillEffect
    {
        public string SkillId => "毒棘の小盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, Kw.AbsorbIsAttackKey) > 0) Rw.Poison(ctx, 1);
        }
    }

    /// <summary>鎧縫いの針: 敵の出血 1 につき、 敵の防御を 1 無視する。</summary>
    public class ArmorNeedle : IPassiveSkillEffect
    {
        public string SkillId => "鎧縫いの針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyBleedStacks > 0) ctx.playerArmorPen += ctx.enemyBleedStacks;
        }
    }

    /// <summary>力みの籠手: シールドを合計 10 得るたび、 この戦闘中の攻撃 +1。</summary>
    public class StrainGauntlet : IPassiveSkillEffect
    {
        public string SkillId => "力みの籠手";
        private const string Atk = "rw.gauntletAtk";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldGain, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired)
            {
                int a = Rw.Count(ctx, Atk);
                if (a > 0) ctx.AddPlayerAttackOrDiceBonus(a);
                return;
            }
            int n = Kw.Tally(ctx, "rw.gauntlet", Kw.EventAmount(ctx), 10);
            if (n > 0) Kw.Add(ctx, Atk, n);
        }
    }

    // ============================================================
    //  毒
    // ============================================================

    /// <summary>緑染みの小刀: 戦闘開始時 毒 +2。</summary>
    public class GreenKnife : IPassiveSkillEffect
    {
        public string SkillId => "緑染みの小刀";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Poison(ctx, 2);
    }

    /// <summary>毒塗りの楔: ブロック端子に 2 個以上挿したターン、 敵が攻撃してきたら毒 +2。</summary>
    public class PoisonWedge : IPassiveSkillEffect
    {
        public string SkillId => "毒塗りの楔";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.BlockDiceCount) >= 2 && Rw.Count(ctx, MutualKeys.EnemyAtkPreDefense) > 0)
                Rw.Poison(ctx, 2);
        }
    }

    /// <summary>毒草摘みの杖: マップを 2 歩進むごとに、 次の戦闘の開始時の毒 +1。</summary>
    public class HerbStaff : IPassiveSkillEffect
    {
        public string SkillId => "毒草摘みの杖";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Poison(ctx, Rw.Steps / 2);
    }

    /// <summary>口縫いの毒糸: 〈封じ〉の役が成立したとき毒 +3。</summary>
    public class StitchThread : IPassiveSkillEffect
    {
        public string SkillId => "口縫いの毒糸";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var fired = ctx.firedRolesThisTurn;
            if (fired.Contains(CombatSystem.RoleKind.Pair2) || fired.Contains(CombatSystem.RoleKind.Triple2))
                Rw.Poison(ctx, 3);
        }
    }

    /// <summary>石抜きの毒指輪: 毒が上限に達したとき、 敵の最大 HP の 15% を軽減不可で削り、 毒を 5 に戻す (何度でも)。</summary>
    public class HollowPoisonRing : IPassiveSkillEffect
    {
        public string SkillId => "石抜きの毒指輪";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonCap };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyMaxHP <= 0) return;
            int dmg = ctx.RatioDamageToEnemy(0.15f);
            Kw.DamageEnemy(ctx, dmg);
            ctx.AddStatus(StatusTarget.Enemy, "poison", 5 - ctx.EnemyPoison);
            Rw.Log($"[石抜きの毒指輪] 毒が満ちた → 最大HP の 15% = {dmg} を削り、 毒を 5 に戻す");
        }
    }

    /// <summary>百一人切りの鉈: ターン終了時、 敵 HP が「毒 × 2」% 以下なら倒す (ボスはその半分)。</summary>
    public class HundredCleaver : IPassiveSkillEffect
    {
        public string SkillId => "百一人切りの鉈";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            int pct = ctx.EnemyPoison * 2;
            if (BountyHunterHelper.IsBoss(ctx)) pct /= 2;
            if (pct <= 0 || ctx.enemyCurrentHP * 100 > ctx.enemyMaxHP * pct) return;
            if (!ctx.CanExecuteEnemy()) return;
            ctx.enemyCurrentHP = 0;
            Rw.Log($"[百一人切りの鉈] 敵HP {pct}% 以下 → 処刑");
        }
    }

    /// <summary>廃庭の霧吹き【規則】: 毒のダメージが、 ターン開始に加えて敵が攻撃するたびにも入る。</summary>
    public class RuinedGardenSprayer : IPassiveSkillEffect
    {
        public string SkillId => "廃庭の霧吹き";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.poisonTickOnEnemyAttack = true;
    }

    /// <summary>主より長い香炉: 毒のダメージが入るたび、 その量の臨界。</summary>
    public class LongCenser : IPassiveSkillEffect
    {
        public string SkillId => "主より長い香炉";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPoisonTick };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, Kw.EventAmount(ctx));
        }
    }

    /// <summary>重増しの拳套: 敵の毒が増えるたび、 この戦闘中の攻撃 +1。</summary>
    public class WeightingGauntlet : IPassiveSkillEffect
    {
        public string SkillId => "重増しの拳套";
        private const string Atk = "rw.weightAtk";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonGain, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPoisonGain) { Kw.Add(ctx, Atk, 1); return; }
            int a = Rw.Count(ctx, Atk);
            if (a > 0) ctx.AddPlayerAttackOrDiceBonus(a);
        }
    }

    /// <summary>毒固めの腕環: ターン開始時、 敵の毒 2 につきシールド +1。</summary>
    public class VenomBangle : IPassiveSkillEffect
    {
        public string SkillId => "毒固めの腕環";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, ctx.EnemyPoison / 2, SkillId);
    }

    /// <summary>腐し刃: 毒のダメージが入るたび敵に出血 +1。</summary>
    public class RotBlade : IPassiveSkillEffect
    {
        public string SkillId => "腐し刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonTick };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.AddEnemyBleed(1);
    }

    // ============================================================
    //  出血
    // ============================================================

    /// <summary>血日に澄む佩玉: 敵の出血 1 につき会心率 +2%。</summary>
    public class BloodDayPendant : IPassiveSkillEffect
    {
        public string SkillId => "血日に澄む佩玉";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critRatePctAdd += ctx.enemyBleedStacks * 2f;
    }

    /// <summary>魂喰らいの血石: 出血を消費・減衰で失うたび、 失った分だけシールド。</summary>
    public class SoulBloodstone : IPassiveSkillEffect
    {
        public string SkillId => "魂喰らいの血石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay, PassiveSkillTrigger.OnBleedConsume };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, Kw.EventAmount(ctx), SkillId);
    }

    /// <summary>血脈の災環: 出血が自然に 1 減るたび、 軽減不可ダメージ 3。</summary>
    public class BloodlineRing : IPassiveSkillEffect
    {
        public string SkillId => "血脈の災環";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Kw.DamageEnemy(ctx, 3 * Kw.EventAmount(ctx));
    }

    /// <summary>一人抱えの破城斧: シールドが 10 以上あるターン、 シールドを 10 使って、 その攻撃は敵の防御を無視する。</summary>
    public class SiegeAxe : IPassiveSkillEffect
    {
        public string SkillId => "一人抱えの破城斧";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.consShield < 10 || ctx.enemyDefense <= 0) return;   // 防御の無い敵に盾を払わない
            ctx.consShield -= 10;
            ctx.playerArmorPen += CombatSystem.DefenseFormula.IgnoreAll;
            Rw.Log("[一人抱えの破城斧] シールド 10 を使い、 この攻撃は敵の防御を無視");
        }
    }

    /// <summary>血車: 敵の出血が 0 になったとき、 その戦闘の出血の最大値だけ充電。</summary>
    public class BloodWheel : IPassiveSkillEffect
    {
        public string SkillId => "血車";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedZero };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int max = Rw.Count(ctx, Kw.BleedMaxKey);
            if (max > 0) ctx.AddCharge(max);
        }
    }

    /// <summary>鉛入りの握斧: 非会心の攻撃が通るたび敵に出血 +2。</summary>
    public class LeadHandAxe : IPassiveSkillEffect
    {
        public string SkillId => "鉛入りの握斧";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && !ctx.isCritical) ctx.AddEnemyBleed(2);
        }
    }

    /// <summary>激情の刃: HP が 50% 以下の間、 攻撃が通るたび敵に出血 +2。</summary>
    public class FuryBlade : IPassiveSkillEffect
    {
        public string SkillId => "激情の刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && ctx.playerCurrentHP * 2 <= ctx.playerMaxHP) ctx.AddEnemyBleed(2);
        }
    }

    // ============================================================
    //  金
    // ============================================================

    /// <summary>銭皿: 戦闘終了時、 残っているシールド 3 につき 1G。</summary>
    public class CoinDish : IPassiveSkillEffect
    {
        public string SkillId => "銭皿";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx)) Rw.Gold(ctx.consShield / 3, SkillId);
        }
    }

    /// <summary>天工開物: 武器を強化するたび、 以後の戦闘の開始時の臨界 +5 (累積)。 強化の回数は RunState が数える。</summary>
    public class TenkouKaibutsu : IPassiveSkillEffect
    {
        public string SkillId => "天工開物";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            int n = Rw.Run?.heavenlyCraftUpgrades ?? 0;
            if (n > 0) Kw.AddHeat(ctx, 5 * n);
        }
    }

    /// <summary>幸運の硬貨: 同じ目が 2 個以上出たターン、 2G。</summary>
    public class LuckyCoin : IPassiveSkillEffect
    {
        public string SkillId => "幸運の硬貨";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var d = ctx.playerDice;
            if (d == null) return;
            for (int i = 0; i < d.Length; i++)
                for (int j = i + 1; j < d.Length; j++)
                    if (d[i] == d[j]) { Rw.Gold(2, SkillId); return; }
        }
    }

    /// <summary>戦果の天秤: 戦闘に勝ったとき、 その戦闘で出した会心の回数だけゴールド。</summary>
    public class SpoilsScale : IPassiveSkillEffect
    {
        public string SkillId => "戦果の天秤";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx)) Rw.Gold(Rw.Count(ctx, Kw.CritCountKey), SkillId);
        }
    }

    /// <summary>爆破コンテストの賞金: 臨界の爆発でとどめを刺すたび +4G。</summary>
    public class BlastPrize : IPassiveSkillEffect
    {
        public string SkillId => "爆破コンテストの賞金";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (Rw.Won(ctx) && Rw.Count(ctx, Kw.BurstThisAttackKey) > 0) Rw.Gold(4, SkillId);
        }
    }

    // ============================================================
    //  会心
    // ============================================================

    /// <summary>測量師の片眼鏡: マップを 1 歩進むごとに、 次の戦闘の会心率 +1% (その戦闘の間)。</summary>
    public class SurveyorMonocle : IPassiveSkillEffect
    {
        public string SkillId => "測量師の片眼鏡";
        private const string Key = "rw.surveyor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[Key] = Rw.Steps; return; }
            ctx.critRatePctAdd += Rw.Count(ctx, Key);
        }
    }

    /// <summary>烈刃: 臨界 2 につき会心率 +1%。</summary>
    public class FierceBlade : IPassiveSkillEffect
    {
        public string SkillId => "烈刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            ctx.critRatePctAdd += ctx.rinkaiMeter / 2;
        }
    }

    /// <summary>遅効の毒刃: 敵の毒が増えた次のターン、 増えた毒 1 につき会心率 +5%。</summary>
    public class SlowPoisonBlade : IPassiveSkillEffect
    {
        public string SkillId => "遅効の毒刃";
        private const string Cur = "rw.slowPoison.cur", Last = "rw.slowPoison.last";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonGain, PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnPoisonGain: Kw.Add(ctx, Cur, Kw.EventAmount(ctx)); break;
                case PassiveSkillTrigger.OnTurnStart:
                    ctx.accumulatedValues[Last] = ctx.GetAccumulated(Cur);
                    ctx.accumulatedValues[Cur] = 0;
                    break;
                default: ctx.critRatePctAdd += 5 * Rw.Count(ctx, Last); break;
            }
        }
    }

    /// <summary>瑠璃砥石: 役が成立したターン、 会心率 +15%。</summary>
    public class LapisWhetstone : IPassiveSkillEffect
    {
        public string SkillId => "瑠璃砥石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.firedRolesThisTurn.Count > 0) ctx.critRatePctAdd += 15f;
        }
    }

    /// <summary>燧石の指輪: 会心のたび臨界 +8。</summary>
    public class FlintRing : IPassiveSkillEffect
    {
        public string SkillId => "燧石の指輪";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, 8);
        }
    }

    /// <summary>寸鉄【規則】: 会心率が 100% を超えた分が会心ダメージの倍率になる (超えた 10% につき +0.1)。</summary>
    public class Suntetsu : IPassiveSkillEffect
    {
        public string SkillId => "寸鉄";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critOverflowToMultiplier = true;
    }

    /// <summary>研ぎ知らずの銑鉄棍【規則】: 会心が出なくなる。 会心になるはずだった攻撃は敵の防御を無視し、 この戦闘中の敵の防御を 1 下げる。</summary>
    public class UnwhettedClub : IPassiveSkillEffect
    {
        public string SkillId => "研ぎ知らずの銑鉄棍";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critBecomesArmorBreak = true;
    }
}
