namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  汎用パッシブ — 追撃（Pursuit）: 与ダメージ追加
    // ============================================================

    /// <summary>追撃I — 与ダメージ+2</summary>
    public class PursuitI : IPassiveSkillEffect
    {
        public string SkillId => "PursuitI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.finalDamage += 2; }
    }

    /// <summary>追撃II — 与ダメージ+4</summary>
    public class PursuitII : IPassiveSkillEffect
    {
        public string SkillId => "PursuitII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.finalDamage += 4; }
    }

    /// <summary>追撃III — 与ダメージ+6</summary>
    public class PursuitIII : IPassiveSkillEffect
    {
        public string SkillId => "PursuitIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.finalDamage += 6; }
    }

    // ============================================================
    //  汎用パッシブ — 反撃（Counter）
    // ============================================================

    /// <summary>反撃I — ロール敗北時、敵に軽減不可1ダメージ</summary>
    public class CounterI : IPassiveSkillEffect
    {
        public string SkillId => "CounterI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.fixedDamageToEnemy += 1; }
    }

    /// <summary>反撃II — ロール敗北時、敵に軽減不可2ダメージ</summary>
    public class CounterII : IPassiveSkillEffect
    {
        public string SkillId => "CounterII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.fixedDamageToEnemy += 2; }
    }

    /// <summary>反撃III — ロール敗北時、敵に軽減不可3ダメージ</summary>
    public class CounterIII : IPassiveSkillEffect
    {
        public string SkillId => "CounterIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.fixedDamageToEnemy += 3; }
    }

    // ============================================================
    //  汎用パッシブ — 筋力（Might）: 各ダイス出目+N
    // ============================================================

    /// <summary>筋力I — 各ダイス出目+1</summary>
    public class MightI : IPassiveSkillEffect
    {
        public string SkillId => "MightI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null)
            {
                ctx.playerDiceTotal += 1 * ctx.playerDice.Length;
                ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
            }
        }
    }

    /// <summary>筋力II — 各ダイス出目+2</summary>
    public class MightII : IPassiveSkillEffect
    {
        public string SkillId => "MightII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null)
            {
                ctx.playerDiceTotal += 2 * ctx.playerDice.Length;
                ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
            }
        }
    }

    /// <summary>筋力III — 各ダイス出目+3</summary>
    public class MightIII : IPassiveSkillEffect
    {
        public string SkillId => "MightIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null)
            {
                ctx.playerDiceTotal += 3 * ctx.playerDice.Length;
                ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
            }
        }
    }

    // ============================================================
    //  汎用パッシブ — 頑強（Fortitude）
    // ============================================================

    /// <summary>頑強I — 被ダメージ-1</summary>
    public class FortitudeI : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 1);
        }
    }

    /// <summary>頑強II — 被ダメージ-2</summary>
    public class FortitudeII : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 2);
        }
    }

    /// <summary>頑強III — 被ダメージ-3</summary>
    public class FortitudeIII : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 3);
        }
    }

    // ============================================================
    //  汎用パッシブ — 心眼（Insight）
    // ============================================================

    /// <summary>心眼I — 会心ダイス+1</summary>
    public class InsightI : IPassiveSkillEffect
    {
        public string SkillId => "InsightI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.criticalBonus += 1; }
    }

    /// <summary>心眼II — 会心ダイス+2</summary>
    public class InsightII : IPassiveSkillEffect
    {
        public string SkillId => "InsightII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.criticalBonus += 2; }
    }

    /// <summary>心眼III — 会心ダイス+3</summary>
    public class InsightIII : IPassiveSkillEffect
    {
        public string SkillId => "InsightIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.criticalBonus += 3; }
    }

    // ============================================================
    //  汎用パッシブ — 活力（Vitality）
    // ============================================================

    /// <summary>活力I — ターン終了時HP+1回復</summary>
    public class VitalityI : IPassiveSkillEffect
    {
        public string SkillId => "VitalityI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 1);
        }
    }

    /// <summary>活力II — ターン終了時HP+2回復</summary>
    public class VitalityII : IPassiveSkillEffect
    {
        public string SkillId => "VitalityII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 2);
        }
    }

    /// <summary>活力III — ターン終了時HP+3回復</summary>
    public class VitalityIII : IPassiveSkillEffect
    {
        public string SkillId => "VitalityIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 3);
        }
    }

    // ============================================================
    //  ユニークパッシブ — 盾系
    // ============================================================

    /// <summary>パリィ — 敵の威圧による削りダメージを無効化</summary>
    public class Parry : IPassiveSkillEffect
    {
        public string SkillId => "Parry";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreScratchDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.nullifyScratchDamage = true;
        }
    }

    /// <summary>聖なる守り — ロール敗北時、受けるダメージ50%</summary>
    public class HolyShield : IPassiveSkillEffect
    {
        public string SkillId => "HolyShield";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
                ctx.finalDamage = (int)(ctx.finalDamage * 0.5f);
        }
    }

    // ============================================================
    //  ユニークパッシブ — 剣系
    // ============================================================

    /// <summary>切り返し — 敗北時、受けたダメージの50%を敵に反射</summary>
    public class Riposte : IPassiveSkillEffect
    {
        public string SkillId => "Riposte";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                int reflected = UnityEngine.Mathf.CeilToInt(ctx.finalDamage * 0.5f);
                ctx.fixedDamageToEnemy += reflected;
            }
        }
    }

    /// <summary>虚空 — ダイス差≤3で双方ダメ0化+軽減不可3ダメージ</summary>
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
    //  ユニークパッシブ — 斧系
    // ============================================================

    /// <summary>復讐 — 敗北でダイス+1蓄積、勝利でリセット</summary>
    public class Frenzy : IPassiveSkillEffect
    {
        public string SkillId => "Frenzy";
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
                    ctx.AddAccumulated("frenzyDiceBonus", 1);
                    break;
                case PassiveSkillTrigger.OnRollWin:
                    ctx.accumulatedValues["frenzyDiceBonus"] = 0;
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    ctx.playerDiceTotal += (int)ctx.GetAccumulated("frenzyDiceBonus");
                    break;
            }
        }
    }

    /// <summary>血令 — ゾロ目時、合計値を固定ダメ+会心+200%+会心ダイス+5</summary>
    public class BloodDecree : IPassiveSkillEffect
    {
        public string SkillId => "BloodDecree";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;
            bool allSame = true;
            int first = ctx.playerDice[0];
            for (int i = 1; i < ctx.playerDice.Length; i++)
            {
                if (ctx.playerDice[i] != first) { allSame = false; break; }
            }
            if (allSame)
            {
                ctx.fixedDamageToEnemy += ctx.playerDiceTotal;
                ctx.nullifyAllDamage = true;
                ctx.criticalMultiplier += 2.0f;
                ctx.criticalBonus += 5;
            }
        }
    }

    // ============================================================
    //  ユニークパッシブ — 短剣系
    // ============================================================

    /// <summary>処刑 — 勝利時、次ターン敵最小ダイス1固定</summary>
    public class Execute : IPassiveSkillEffect
    {
        public string SkillId => "Execute";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };
        // クラス名と同名メソッドを避けるため明示的インターフェース実装
        void IPassiveSkillEffect.Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.pendingDiceOverrides.Add(
                new DiceOverrideRequest(DiceOverrideRequest.TargetDice.Lowest, 1, SkillId));
        }
    }

    /// <summary>蝕夜 — オーバーダメ×2蓄積→戦闘開始時放出</summary>
    public class Nightfall : IPassiveSkillEffect
    {
        public string SkillId => "Nightfall";
        private int persistentOverdamage = 0;
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPostDealDamage,
            PassiveSkillTrigger.OnBattleStart
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                if (persistentOverdamage > 0)
                    ctx.fixedDamageToEnemy += persistentOverdamage;
                return;
            }
            if (ctx.overDamageAccumulated > 0)
            {
                persistentOverdamage += ctx.overDamageAccumulated * 2;
                ctx.overDamageAccumulated = 0;
            }
        }
    }

    /// <summary>出血 — ダメージを与えた時、敵に出血+1（1ターン1回）</summary>
    public class Sting : IPassiveSkillEffect
    {
        public string SkillId => "Sting";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPostDealDamage
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                ctx.accumulatedValues["sting_fired"] = 0;
                return;
            }
            if (ctx.GetAccumulated("sting_fired") > 0) return;
            ctx.accumulatedValues["sting_fired"] = 1;
            ctx.enemyBleedStacks++;
        }
    }

    // ============================================================
    //  ユニークパッシブ — デッドエンド武器
    // ============================================================

    /// <summary>業火 — 戦闘開始時に敵を炎上(3ターン, 毎ターン3ダメ)</summary>
    public class Ignite : IPassiveSkillEffect
    {
        public string SkillId => "Ignite";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnTurnStart
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                ctx.enemyBurnTurns = 3;
                ctx.enemyBurnDamage = 3;
                return;
            }
            if (ctx.enemyBurnTurns > 0)
            {
                ctx.fixedDamageToEnemy += ctx.enemyBurnDamage;
                ctx.enemyBurnTurns--;
            }
        }
    }

    // ============================================================
    //  ユニークパッシブ — 投資武器（聖剣ライン）
    // ============================================================

    /// <summary>黎明の光 — 初回ロール時ダイス+3</summary>
    public class HolyMemory : IPassiveSkillEffect
    {
        public string SkillId => "HolyMemory";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.isFirstRoll) ctx.playerDiceTotal += 3;
        }
    }

    /// <summary>薄暮の光 — ターン開始時HP+2回復、敗北時被ダメ50%</summary>
    public class HolyAura : IPassiveSkillEffect
    {
        public string SkillId => "HolyAura";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreReceiveDamage
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 2);
                return;
            }
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
                ctx.finalDamage = (int)(ctx.finalDamage * 0.5f);
        }
    }

    /// <summary>終焉 — 戦闘開始時、敵MaxHPの30%を軽減不可ダメージ</summary>
    public class Terminus : IPassiveSkillEffect
    {
        public string SkillId => "Terminus";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int damage = UnityEngine.Mathf.CeilToInt(ctx.enemyMaxHP * 0.3f);
            ctx.fixedDamageToEnemy += damage;
        }
    }

    // ============================================================
    //  ユニークパッシブ — 呪い武器
    // ============================================================

    /// <summary>呪縛 — 毎ターン自分1ダメ、敵ダイス合計-1蓄積デバフ</summary>
    public class CurseBind : IPassiveSkillEffect
    {
        public string SkillId => "CurseBind";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPostRoll
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                ctx.playerCurrentHP = System.Math.Max(1, ctx.playerCurrentHP - 1);
                ctx.AddAccumulated("curseDebuff", 1);
                return;
            }
            int debuff = (int)ctx.GetAccumulated("curseDebuff");
            if (debuff > 0)
            {
                ctx.enemyDiceTotal = System.Math.Max(0, ctx.enemyDiceTotal - debuff);
                ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
            }
        }
    }

    /// <summary>刹那の惜別 — 被ダメ記録+踏みとどまり→狂戦士化(ダイス+10/蓄積×3固定/会心確定)</summary>
    public class Abyss : IPassiveSkillEffect
    {
        public string SkillId => "Abyss";
        private const string DMG_KEY = "abyss_dmgTaken";
        private const string TRIGGERED_KEY = "abyss_triggered";
        private const string BERSERK_KEY = "abyss_berserk";

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
                    ctx.accumulatedValues[DMG_KEY] = 0;
                    ctx.accumulatedValues[TRIGGERED_KEY] = 0;
                    ctx.accumulatedValues[BERSERK_KEY] = 0;
                    break;
                case PassiveSkillTrigger.OnPostReceiveDamage:
                    if (ctx.finalDamage > 0 && ctx.playerLostRoll)
                        ctx.AddAccumulated(DMG_KEY, ctx.finalDamage);
                    break;
                case PassiveSkillTrigger.OnPreReceiveDamage:
                    if (ctx.GetAccumulated(TRIGGERED_KEY) < 1 &&
                        ctx.finalDamage >= ctx.playerCurrentHP)
                    {
                        ctx.finalDamage = ctx.playerCurrentHP - 1;
                        ctx.accumulatedValues[TRIGGERED_KEY] = 1;
                        ctx.accumulatedValues[BERSERK_KEY] = 1;
                    }
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                    {
                        ctx.playerDiceTotal += 10;
                        ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
                    }
                    break;
                case PassiveSkillTrigger.OnPreDealDamage:
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                        ctx.fixedDamageToEnemy += (int)ctx.GetAccumulated(DMG_KEY) * 3;
                    break;
                case PassiveSkillTrigger.OnCriticalCheck:
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                        ctx.criticalBonus += 99;
                    break;
            }
        }
    }

    // ============================================================
    //  ダイス固有パッシブ
    // ============================================================

    /// <summary>煌玉 — 最大出目のダイスがある時、会心ダイス+1</summary>
    public class Shimmer : IPassiveSkillEffect
    {
        public string SkillId => "Shimmer";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.equippedDiceFaces == null || ctx.playerDice == null) return;
            int maxFace = 0;
            foreach (var f in ctx.equippedDiceFaces)
                if (f > maxFace) maxFace = f;
            foreach (var d in ctx.playerDice)
            {
                if (d >= maxFace) { ctx.criticalBonus += 1; return; }
            }
        }
    }

    /// <summary>盟約 — ロール敗北時、次ターンのダイス合計+2</summary>
    public class ReversalFlame : IPassiveSkillEffect
    {
        public string SkillId => "ReversalFlame";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (!ctx.nextTurnBuffs.ContainsKey("diceBonus"))
                ctx.nextTurnBuffs["diceBonus"] = 0f;
            ctx.nextTurnBuffs["diceBonus"] += 2f;
        }
    }

    /// <summary>堅実 — ダイス合計が(ダイス数×3)以下の時+2</summary>
    public class Steadfast : IPassiveSkillEffect
    {
        public string SkillId => "Steadfast";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null && ctx.playerDiceTotal <= ctx.playerDice.Length * 3)
            {
                ctx.playerDiceTotal += 2;
                ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
            }
        }
    }

    /// <summary>星命 — ゾロ目時、追撃ダメージ+出目値</summary>
    public class StarFate : IPassiveSkillEffect
    {
        public string SkillId => "StarFate";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;
            bool allSame = true;
            int first = ctx.playerDice[0];
            for (int i = 1; i < ctx.playerDice.Length; i++)
            {
                if (ctx.playerDice[i] != first) { allSame = false; break; }
            }
            if (allSame) ctx.pursuitDamage += first;
        }
    }

    /// <summary>運命 — 最大出目なら与ダメ×2、最低出目なら被ダメ0</summary>
    public class Destiny : IPassiveSkillEffect
    {
        public string SkillId => "Destiny";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.equippedDiceFaces == null || ctx.playerDice == null) return;
            int maxFace = 0, minFace = int.MaxValue;
            foreach (var f in ctx.equippedDiceFaces)
            {
                if (f > maxFace) maxFace = f;
                if (f < minFace) minFace = f;
            }
            switch (trigger)
            {
                case PassiveSkillTrigger.OnPreDealDamage:
                    // 全ダイスが最大出目なら与ダメ×2
                    bool allMax = true;
                    foreach (var d in ctx.playerDice)
                        if (d < maxFace) { allMax = false; break; }
                    if (allMax) ctx.finalDamage *= 2;
                    break;
                case PassiveSkillTrigger.OnPreReceiveDamage:
                    // 全ダイスが最低出目なら被ダメ0
                    bool allMin = true;
                    foreach (var d in ctx.playerDice)
                        if (d > minFace) { allMin = false; break; }
                    if (allMin) ctx.finalDamage = 0;
                    break;
            }
        }
    }

    // ============================
    //  ダイス固有パッシブ（新規）
    // ============================

    /// <summary>星導 — 全ダイスが異なる値の時、ダイス合計+3</summary>
    public class Starguide : IPassiveSkillEffect
    {
        public string SkillId => "Starguide";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var d in ctx.playerDice)
            {
                if (!seen.Add(d)) return; // 重複があれば即終了
            }
            // 全て異なる値
            ctx.playerDiceTotal += 3;
            ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
        }
    }

    /// <summary>裁定 — ダイス合計が敵の2倍以上で追撃+5、敵が2倍以上で被ダメ0</summary>
    public class Judgement : IPassiveSkillEffect
    {
        public string SkillId => "Judgement";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnPreDealDamage:
                    // プレイヤーダイス合計が敵の2倍以上なら追撃+5
                    if (ctx.enemyDiceTotal > 0 && ctx.playerDiceTotal >= ctx.enemyDiceTotal * 2)
                        ctx.pursuitDamage += 5;
                    break;
                case PassiveSkillTrigger.OnPreReceiveDamage:
                    // 敵ダイス合計がプレイヤーの2倍以上なら被ダメ0
                    if (ctx.playerDiceTotal > 0 && ctx.enemyDiceTotal >= ctx.playerDiceTotal * 2)
                        ctx.finalDamage = 0;
                    break;
            }
        }
    }

    // ============================================================
    //  竜閃（ユニーク武器）— 安定性の対極の斬鉄剣
    // ============================================================

    /// <summary>無我無心 — カスタムダイス以外の補正を一切受けない（戦闘中持続）。</summary>
    public class MugaMushin : IPassiveSkillEffect
    {
        public string SkillId => "MugaMushin";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.rollPurity = true;
        }
    }

    /// <summary>画竜点睛 — 出目がその時点の最大値なら、ロール即勝利＋(出目+10)＋会心確定。</summary>
    public class GaryoTensei : IPassiveSkillEffect
    {
        public string SkillId => "GaryoTensei";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length == 0) return;

            // その時点で取りうる最大値: カスタムダイス装備中はその最大面、無ければ playerDiceMax
            int maxVal;
            if (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
            {
                maxVal = ctx.equippedDiceFaces[0];
                for (int i = 1; i < ctx.equippedDiceFaces.Length; i++)
                    if (ctx.equippedDiceFaces[i] > maxVal) maxVal = ctx.equippedDiceFaces[i];
            }
            else
            {
                maxVal = ctx.playerDiceMax > 0 ? ctx.playerDiceMax : 6;
            }

            // ダイス数1前提だが、いずれかが最大値なら発動
            int hit = 0;
            for (int i = 0; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] >= maxVal) { hit = ctx.playerDice[i]; break; }

            if (hit > 0)
            {
                ctx.garyoProc = true;
                ctx.garyoDieValue = hit;
                UnityEngine.Debug.Log($"[画竜点睛] 発動 出目{hit}=最大 → 即勝利＋({hit}+10)会心");
            }
        }
    }
}
