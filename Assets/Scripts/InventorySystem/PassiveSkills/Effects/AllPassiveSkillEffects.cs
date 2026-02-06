namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  盾系スキル（Shield Lv1～Lv5）
    // ============================================================

    /// <summary>受け身 — ロール敗北時、ダメージを-1</summary>
    public class Breakfall : IPassiveSkillEffect
    {
        public string SkillId => "Breakfall";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 1);
            }
        }
    }

    /// <summary>棘鎧 — ロールの勝敗にかかわらず、敵に軽減不能の1ダメージを与える</summary>
    public class SpikeArmor : IPassiveSkillEffect
    {
        public string SkillId => "SpikeArmor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 勝敗・引き分け問わず、毎ターン敵に軽減不能の1ダメージ
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - 1);
        }
    }

    /// <summary>持久戦 — ロール敗北時、この戦闘中の最大HP+1(最大20)</summary>
    public class Endurance : IPassiveSkillEffect
    {
        public string SkillId => "Endurance";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int maxCap = ctx.playerBaseMaxHP + 20;
            if (ctx.playerMaxHP < maxCap)
            {
                ctx.playerMaxHP++;
                ctx.playerCurrentHP++; // 増えた分は即回復
            }
        }
    }

    /// <summary>天の加護 — ロール勝利時、次に受けるダメージを-4</summary>
    public class DivineShield : IPassiveSkillEffect
    {
        public string SkillId => "DivineShield";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 次に受けるダメージ軽減値を加算（複数回勝利で累積可能）
            ctx.damageShield += 4;
        }
    }

    /// <summary>夜明けの祝福 — ロール敗北時、ダメージを-50%</summary>
    public class DawnBlessing : IPassiveSkillEffect
    {
        public string SkillId => "DawnBlessing";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                ctx.finalDamage = ctx.finalDamage / 2;
            }
        }
    }

    // ============================================================
    //  剣系スキル（Sword Lv1～Lv5）
    // ============================================================

    /// <summary>教本剣技 — ダイス合計値が必ず3以上になる</summary>
    public class BasicSword : IPassiveSkillEffect
    {
        public string SkillId => "BasicSword";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDiceTotal < 3)
            {
                ctx.playerDiceTotal = 3;
            }
        }
    }

    /// <summary>リカバリー — 最低値のダイスをもう一度振りなおす</summary>
    public class Recovery : IPassiveSkillEffect
    {
        public string SkillId => "Recovery";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length == 0) return;

            // 最低値のダイスを見つける
            int minIndex = 0;
            for (int i = 1; i < ctx.playerDice.Length; i++)
            {
                if (ctx.playerDice[i] < ctx.playerDice[minIndex])
                    minIndex = i;
            }

            // 振り直し（ダイスの設定値に基づく）
            int oldValue = ctx.playerDice[minIndex];
            int newValue = UnityEngine.Random.Range(1, 7); // 1-6の標準ダイス
            ctx.playerDice[minIndex] = newValue;

            // 合計を再計算
            ctx.playerDiceTotal += (newValue - oldValue);
        }
    }

    /// <summary>流浪の知恵 — ロール差が1以下なら敵の追撃を無効化する</summary>
    public class WandererWit : IPassiveSkillEffect
    {
        public string SkillId => "WandererWit";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPrePursuitDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (System.Math.Abs(ctx.diceDifference) <= 1)
            {
                ctx.nullifyPursuitDamage = true;
            }
        }
    }

    /// <summary>殺龍 — ダイス差が2以下なら会心ダイスに+2</summary>
    public class DragonSlayer : IPassiveSkillEffect
    {
        public string SkillId => "DragonSlayer";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (System.Math.Abs(ctx.diceDifference) <= 2)
            {
                ctx.criticalBonus += 2;
            }
        }
    }

    /// <summary>無の境地 — ダイス差3以下なら双方のダメージを0にし、相手に軽減不可の5ダメージ</summary>
    public class VoidStance : IPassiveSkillEffect
    {
        public string SkillId => "VoidStance";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (System.Math.Abs(ctx.diceDifference) <= 3)
            {
                ctx.nullifyAllDamage = true;
                ctx.fixedDamageToEnemy += 5;
            }
        }
    }

    // ============================================================
    //  斧系スキル（Axe Lv1～Lv5）
    // ============================================================

    /// <summary>痛覚反転 — ロール敗北時、次ターンの会心ダイスに+1</summary>
    public class PainRevert : IPassiveSkillEffect
    {
        public string SkillId => "PainRevert";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("criticalBonus", out current);
            ctx.nextTurnBuffs["criticalBonus"] = current + 1;
        }
    }

    /// <summary>雄叫び — ロール敗北時、次ターンのダイス合計値に+1</summary>
    public class Warcry : IPassiveSkillEffect
    {
        public string SkillId => "Warcry";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("diceBonus", out current);
            ctx.nextTurnBuffs["diceBonus"] = current + 1;
        }
    }

    /// <summary>血の約定 — ロール敗北時、次ターンに与えるダメージ+3</summary>
    public class BloodPact : IPassiveSkillEffect
    {
        public string SkillId => "BloodPact";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("damageBonus", out current);
            ctx.nextTurnBuffs["damageBonus"] = current + 3;
        }
    }

    /// <summary>頂点捕食者 — ロール敗北時、追撃ダメージを受けない</summary>
    public class ApexPredator : IPassiveSkillEffect
    {
        public string SkillId => "ApexPredator";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.nullifyPursuitDamage = true;
        }
    }

    /// <summary>血の勅命 — ロール時、両方のダイスが最大値(6)なら相手に致命傷を付与</summary>
    public class BloodDecree : IPassiveSkillEffect
    {
        public string SkillId => "BloodDecree";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;

            // 全ダイスが最大値(6)か判定
            bool allMax = true;
            for (int i = 0; i < ctx.playerDice.Length; i++)
            {
                if (ctx.playerDice[i] < 6)
                {
                    allMax = false;
                    break;
                }
            }

            if (allMax)
            {
                ctx.enemyHasFatalWound = true;
            }
        }
    }

    // ============================================================
    //  短剣系スキル（Dagger Lv1～Lv5）
    // ============================================================

    /// <summary>手癖 — 戦闘開始時の初回ロール時、ダイス合計値に+2</summary>
    public class QuickHands : IPassiveSkillEffect
    {
        public string SkillId => "QuickHands";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.isFirstRoll)
            {
                ctx.playerDiceTotal += 2;
            }
        }
    }

    /// <summary>致命の一刺し — 会心ダメージ+100%</summary>
    public class FatalStab : IPassiveSkillEffect
    {
        public string SkillId => "FatalStab";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.isCritical)
            {
                ctx.criticalMultiplier += 1.0f; // 2.0 → 3.0 (= +100%)
            }
        }
    }

    /// <summary>一刺し — 相手にダメージを与えたとき、1ターンに1回まで相手に出血+1</summary>
    public class Sting : IPassiveSkillEffect
    {
        public string SkillId => "Sting";
        private const string PROC_KEY = "sting_procced";
        
        public PassiveSkillTrigger[] Triggers => new[] { 
            PassiveSkillTrigger.OnPostDealDamage, 
            PassiveSkillTrigger.OnTurnStart 
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // ターン開始時にフラグリセット
                ctx.accumulatedValues[PROC_KEY] = 0f;
                return;
            }

            // ダメージ与えた時、未発動ならば出血付与
            if (ctx.finalDamage > 0 && ctx.GetAccumulated(PROC_KEY) < 1f)
            {
                ctx.enemyBleedStacks++;
                ctx.accumulatedValues[PROC_KEY] = 1f;
            }
        }
    }

    /// <summary>処刑 — ロール勝利時、次ターン相手の最も低いダイス1個を1に固定</summary>
    public class Execution : IPassiveSkillEffect
    {
        public string SkillId => "Execution";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.pendingDiceOverrides.Add(
                new DiceOverrideRequest(DiceOverrideRequest.TargetDice.Lowest, 1, SkillId));
        }
    }

    /// <summary>正義への妄執 — ロール敗北時、次ターン相手の最も高いダイス1個を1に固定</summary>
    public class BlindJustice : IPassiveSkillEffect
    {
        public string SkillId => "BlindJustice";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.pendingDiceOverrides.Add(
                new DiceOverrideRequest(DiceOverrideRequest.TargetDice.Highest, 1, SkillId));
        }
    }

    /// <summary>夜 — オーバーダメージ分を記録し、この戦闘中の毎ターン開始時に記録値の合計ダメージを与える</summary>
    public class Nightfall : IPassiveSkillEffect
    {
        public string SkillId => "Nightfall";
        private const string ACCUMULATE_KEY = "nightfall_overdamage";

        public PassiveSkillTrigger[] Triggers => new[] { 
            PassiveSkillTrigger.OnPostDealDamage, 
            PassiveSkillTrigger.OnTurnStart 
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // 毎ターン開始時に蓄積分の固定ダメージを追加
                float accumulated = ctx.GetAccumulated(ACCUMULATE_KEY);
                if (accumulated > 0)
                {
                    ctx.fixedDamageToEnemy += (int)accumulated;
                }
                return;
            }

            // ダメージ結果後：オーバーダメージ分を記録
            // （将来的に敵HP情報が必要 — 現時点ではoverDamageAccumulatedフィールドを使用）
            if (ctx.overDamageAccumulated > 0)
            {
                ctx.AddAccumulated(ACCUMULATE_KEY, ctx.overDamageAccumulated);
                ctx.overDamageAccumulated = 0; // 処理済み
            }
        }
    }
}
