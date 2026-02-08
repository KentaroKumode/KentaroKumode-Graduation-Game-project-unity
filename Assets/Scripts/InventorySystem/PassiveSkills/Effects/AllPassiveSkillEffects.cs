namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  盾系スキル（Shield Lv1～Lv5）
    // ============================================================

    /// <summary>受け身 — ロール敗北時、ダメージを-2</summary>
    public class Breakfall : IPassiveSkillEffect
    {
        public string SkillId => "Breakfall";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 2);
            }
        }
    }

    /// <summary>棘鎧 — ロールの勝敗にかかわらず、敵に軽減不能の2ダメージを与える</summary>
    public class SpikeArmor : IPassiveSkillEffect
    {
        public string SkillId => "SpikeArmor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 勝敗・引き分け問わず、毎ターン敵に軽減不能の2ダメージ
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - 2);
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

    /// <summary>天の加護 — ターン終了時、HPを2回復</summary>
    public class DivineShield : IPassiveSkillEffect
    {
        public string SkillId => "DivineShield";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 2);
        }
    }

    /// <summary>夜明けの祝福 — ロール敗北時、受けるダメージを50%にする（割合軽減）</summary>
    public class DawnBlessing : IPassiveSkillEffect
    {
        public string SkillId => "DawnBlessing";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                // 割合軽減：現在のfinalDamageに対して50%にする
                // Breakfallなどの実数軽減の後に適用される想定
                ctx.finalDamage = (int)(ctx.finalDamage * 0.5f);
            }
        }
    }

    // ============================================================
    //  剣系スキル（Sword Lv1～Lv5）
    // ============================================================

    /// <summary>教本剣技 — ダイス合計値がダイス数×2以上になることを保証</summary>
    public class BasicSword : IPassiveSkillEffect
    {
        public string SkillId => "BasicSword";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int diceCount = ctx.playerDice != null ? ctx.playerDice.Length : 2;
            int minimum = diceCount * 2;
            if (ctx.playerDiceTotal < minimum)
            {
                ctx.playerDiceTotal = minimum;
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

            // 振り直し（プレイヤーの装備武器のダイス設定値に基づく）
            int oldValue = ctx.playerDice[minIndex];
            int newValue = UnityEngine.Random.Range(1, ctx.playerDiceMax + 1); // 武器のダイス最大値を使用
            ctx.playerDice[minIndex] = newValue;

            // 合計を再計算
            ctx.playerDiceTotal += (newValue - oldValue);
            
            // デバッグログ：修正されたことを確認
            UnityEngine.Debug.Log($"[Recovery] 🔄 Rerolled dice[{minIndex}]: {oldValue} → {newValue} (max: {ctx.playerDiceMax})");
            UnityEngine.Debug.Log($"[Recovery] 📊 Total updated: {ctx.playerDiceTotal - (newValue - oldValue)} → {ctx.playerDiceTotal}");
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

    /// <summary>無の境地 — ダイス差3以下なら双方のダメージを0にし、相手に軽減不可の3ダメージ</summary>
    public class VoidStance : IPassiveSkillEffect
    {
        public string SkillId => "VoidStance";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (System.Math.Abs(ctx.diceDifference) <= 3)
            {
                ctx.nullifyAllDamage = true;
                ctx.fixedDamageToEnemy += 3;
            }
        }
    }

    // ============================================================
    //  斧系スキル（Axe Lv1～Lv5）
    // ============================================================

    /// <summary>痛覚反転 — ロール勝利時、自分の減少体力の半分に等しいダメージを与える</summary>
    public class PainRevert : IPassiveSkillEffect
    {
        public string SkillId => "PainRevert";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int lostHP = ctx.playerMaxHP - ctx.playerCurrentHP;
            if (lostHP > 0)
            {
                int bonus = lostHP / 2;
                ctx.finalDamage += bonus;
            }
        }
    }

    /// <summary>雄叫び — ロール敗北時、次ターン以降のダイス合計値に+1、ロール勝利時にリセット</summary>
    public class Warcry : IPassiveSkillEffect
    {
        public string SkillId => "Warcry";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnRollLose,
            PassiveSkillTrigger.OnRollWin,
            PassiveSkillTrigger.OnPostRoll
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnRollLose:
                    // 敗北時: 蓄積ダイスボーナス+1
                    ctx.AddAccumulated("warcryDiceBonus", 1);
                    break;
                case PassiveSkillTrigger.OnRollWin:
                    // 勝利時: 蓄積リセット
                    ctx.accumulatedValues["warcryDiceBonus"] = 0;
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    // ロール後: 蓄積値をダイス合計に加算
                    ctx.playerDiceTotal += (int)ctx.GetAccumulated("warcryDiceBonus");
                    break;
            }
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

    /// <summary>血の勅命 — ロール時、ダイスがゾロ目なら、ダイス合計値を軽減不可の固定ダメージとして与える（追撃なし）。さらに会心ダメージ+200%、会心ダイス+5</summary>
    public class BloodDecree : IPassiveSkillEffect
    {
        public string SkillId => "BloodDecree";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;

            // 全ダイスが同じ値（ゾロ目）か判定
            bool allSame = true;
            int firstValue = ctx.playerDice[0];
            for (int i = 1; i < ctx.playerDice.Length; i++)
            {
                if (ctx.playerDice[i] != firstValue)
                {
                    allSame = false;
                    break;
                }
            }

            if (allSame)
            {
                // ダイス合計値を軽減不可の固定ダメージとして与える
                ctx.fixedDamageToEnemy += ctx.playerDiceTotal;
                // 通常ダメージ・追撃を無効化（固定ダメージのみ）
                ctx.nullifyAllDamage = true;
                ctx.criticalMultiplier += 2.0f;  // 会心ダメージ+200%
                ctx.criticalBonus += 5;           // 会心ダイス+5
            }
        }
    }

    // ============================================================
    //  短剣系スキル（Dagger Lv1～Lv5）
    // ============================================================

    /// <summary>不意打ち — 戦闘開始時の初回ロール時、ダイス合計値に+5</summary>
    public class Ambush : IPassiveSkillEffect
    {
        public string SkillId => "Ambush";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.isFirstRoll)
            {
                ctx.playerDiceTotal += 5;
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

    /// <summary>正義への妄執 — 相手の反撃でダメージを受けたとき、次ターン与えるダメージ+10</summary>
    public class BlindJustice : IPassiveSkillEffect
    {
        public string SkillId => "BlindJustice";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                float current = 0f;
                ctx.nextTurnBuffs.TryGetValue("damageBonus", out current);
                ctx.nextTurnBuffs["damageBonus"] = current + 10;
            }
        }
    }

    /// <summary>夜 — オーバーダメージ分を記録し、戦闘開始時に記録値の合計ダメージを与える（戦闘を跨いで蓄積）</summary>
    public class Nightfall : IPassiveSkillEffect
    {
        public string SkillId => "Nightfall";

        /// <summary>戦闘を跨いで蓄積するオーバーダメージ値（装備中は永続）</summary>
        private int persistentOverdamage = 0;

        public PassiveSkillTrigger[] Triggers => new[] { 
            PassiveSkillTrigger.OnPostDealDamage, 
            PassiveSkillTrigger.OnBattleStart 
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                // 戦闘開始時に蓄積分の軽減不可固定ダメージを与える
                if (persistentOverdamage > 0)
                {
                    ctx.fixedDamageToEnemy += persistentOverdamage;
                }
                return;
            }

            // ダメージ結果後：オーバーダメージ分の2倍を蓄積（戦闘を跨いで保持）
            if (ctx.overDamageAccumulated > 0)
            {
                persistentOverdamage += ctx.overDamageAccumulated * 2;
                ctx.overDamageAccumulated = 0; // 処理済み
            }
        }
    }

    // ============================================================
    //  合成武器スキル（Fusion Weapons）
    // ============================================================

    /// <summary>寂滅暁光 — ダイス差4以下なら双方ダメージ0化＋軽減不可10固定ダメージ＋HP10回復</summary>
    public class DawnBreker : IPassiveSkillEffect
    {
        public string SkillId => "DawnBreker";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (System.Math.Abs(ctx.diceDifference) <= 4)
            {
                ctx.nullifyAllDamage = true;
                ctx.fixedDamageToEnemy += 10;
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 10);
            }
        }
    }

    /// <summary>ブラッドムーン — 敵撃破数を累積カウントし、毎ターン撃破数×2のダメージ＋同値回復</summary>
    public class BloodMoon : IPassiveSkillEffect
    {
        public string SkillId => "BloodMoon";

        /// <summary>戦闘を跨いで蓄積する撃破数</summary>
        private int killCount = 0;

        public PassiveSkillTrigger[] Triggers => new[] { 
            PassiveSkillTrigger.OnBattleEnd, 
            PassiveSkillTrigger.OnTurnStart 
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleEnd)
            {
                // 敵を撃破した場合、カウント加算
                if (ctx.enemyCurrentHP <= 0)
                {
                    killCount++;
                }
                return;
            }

            // ターン開始時：撃破数×2のダメージ＆同値回復
            if (killCount > 0)
            {
                int amount = killCount * 2;
                ctx.fixedDamageToEnemy += amount;
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + amount);
            }
        }
    }

    /// <summary>日食 — 毎ターンスタック増加。奇数スタック:HP20回復、偶数スタック+ロール勝利時:軽減不可20ダメージ</summary>
    public class Eclipse : IPassiveSkillEffect
    {
        public string SkillId => "Eclipse";
        private const string STACK_KEY = "eclipse_stack";

        public PassiveSkillTrigger[] Triggers => new[] { 
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnRollWin
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                ctx.AddAccumulated(STACK_KEY, 1);
                int stack = (int)ctx.GetAccumulated(STACK_KEY);

                if (stack % 2 == 1)
                {
                    // 奇数スタック：HP20回復
                    ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 20);
                }
                return;
            }

            // OnRollWin: 偶数スタックならダメージ発動
            int currentStack = (int)ctx.GetAccumulated(STACK_KEY);
            if (currentStack % 2 == 0)
            {
                ctx.fixedDamageToEnemy += 20;
            }
        }
    }

    /// <summary>天帝 — 敗北時:次ターンに差+1だけダイス加算 / 勝利時:会心ダメージ+500%</summary>
    public class LoadEmperor : IPassiveSkillEffect
    {
        public string SkillId => "LoadEmperor";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnRollLose,
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnCriticalDamage
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnRollLose:
                    // 敗北時：差+1を次ターンのダイスボーナスとして蓄積
                    int diff = System.Math.Abs(ctx.diceDifference) + 1;
                    float current = 0f;
                    ctx.nextTurnBuffs.TryGetValue("diceBonus", out current);
                    ctx.nextTurnBuffs["diceBonus"] = current + diff;
                    break;

                case PassiveSkillTrigger.OnPostRoll:
                    // ロール後：前ターンからのダイスボーナスを適用
                    float bonus = ctx.GetBuff("diceBonus");
                    if (bonus > 0)
                    {
                        ctx.playerDiceTotal += (int)bonus;
                        ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
                    }
                    break;

                case PassiveSkillTrigger.OnCriticalDamage:
                    // 会心時：会心ダメージ+500%
                    if (ctx.isCritical)
                    {
                        ctx.criticalMultiplier += 5.0f;
                    }
                    break;
            }
        }
    }

    /// <summary>沈黙 — 相手の全ダイスを1に固定。ロール勝利時、大出血（出血+3）を無限スタック</summary>
    public class Silence : IPassiveSkillEffect
    {
        public string SkillId => "Silence";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnRollWin
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                // 相手のダイスの値をすべて1に固定
                if (ctx.enemyDice != null && ctx.enemyDice.Length > 0)
                {
                    for (int i = 0; i < ctx.enemyDice.Length; i++)
                    {
                        ctx.enemyDice[i] = 1;
                    }
                    ctx.enemyDiceTotal = ctx.enemyDice.Length; // 全ダイス1 → 合計=ダイス数
                    ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
                }
                return;
            }

            // ロール勝利時：大出血（出血+3スタック、上限なし）
            ctx.enemyBleedStacks += 3;
        }
    }

    /// <summary>戴冠式 — 被ダメ記録+踏みとどまり→狂戦士化（ダイス+10、蓄積×3固定ダメ/毎ターン、会心確定）</summary>
    public class Coronation : IPassiveSkillEffect
    {
        public string SkillId => "Coronation";
        private const string DAMAGE_KEY = "coronation_totalDmgTaken";
        private const string TRIGGERED_KEY = "coronation_lastStandTriggered";
        private const string BERSERK_KEY = "coronation_berserkActive";

        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostReceiveDamage,
            PassiveSkillTrigger.OnPreReceiveDamage,
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPreDealDamage,
            PassiveSkillTrigger.OnCriticalCheck
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    ctx.accumulatedValues[DAMAGE_KEY] = 0;
                    ctx.accumulatedValues[TRIGGERED_KEY] = 0;
                    ctx.accumulatedValues[BERSERK_KEY] = 0;
                    break;

                case PassiveSkillTrigger.OnPostReceiveDamage:
                    if (ctx.finalDamage > 0 && ctx.playerLostRoll)
                    {
                        ctx.AddAccumulated(DAMAGE_KEY, ctx.finalDamage);
                    }
                    break;

                case PassiveSkillTrigger.OnPreReceiveDamage:
                    // 致死ダメージで踏みとどまり（1戦闘1回）→狂戦士化
                    if (ctx.GetAccumulated(TRIGGERED_KEY) < 1 &&
                        ctx.finalDamage >= ctx.playerCurrentHP)
                    {
                        ctx.finalDamage = ctx.playerCurrentHP - 1;
                        ctx.accumulatedValues[TRIGGERED_KEY] = 1;
                        ctx.accumulatedValues[BERSERK_KEY] = 1;
                    }
                    break;

                case PassiveSkillTrigger.OnPostRoll:
                    // 狂戦士化中：ダイス合計+10（残り戦闘中永続）
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                    {
                        ctx.playerDiceTotal += 10;
                        ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
                    }
                    break;

                case PassiveSkillTrigger.OnPreDealDamage:
                    // 狂戦士化中：蓄積被ダメ×3を固定ダメージ（毎ターン）
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                    {
                        int accumulated = (int)ctx.GetAccumulated(DAMAGE_KEY);
                        ctx.fixedDamageToEnemy += accumulated * 3;
                    }
                    break;

                case PassiveSkillTrigger.OnCriticalCheck:
                    // 狂戦士化中：会心確定
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                    {
                        ctx.criticalBonus += 99;
                    }
                    break;
            }
        }
    }

    /// <summary>終局 — 戦闘開始時、敵に9999の軽減不能ダメージ</summary>
    public class TheEnd : IPassiveSkillEffect
    {
        public string SkillId => "TheEnd";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.fixedDamageToEnemy += 9999;
        }
    }
}
