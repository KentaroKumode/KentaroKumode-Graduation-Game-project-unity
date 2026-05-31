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

    /// <summary>追撃IV — 与ダメージ+8</summary>
    public class PursuitIV : IPassiveSkillEffect
    {
        public string SkillId => "PursuitIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.finalDamage += 8; }
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

    /// <summary>反撃IV — ロール敗北時、敵に軽減不可4ダメージ</summary>
    public class CounterIV : IPassiveSkillEffect
    {
        public string SkillId => "CounterIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.fixedDamageToEnemy += 4; }
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
                ctx.playerDiceTotal += 2;            }
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
                ctx.playerDiceTotal += 3;            }
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
                ctx.playerDiceTotal += 4;            }
        }
    }

    /// <summary>筋力IV — 各ダイス出目+4</summary>
    public class MightIV : IPassiveSkillEffect
    {
        // 2026-05-30 LEG 1.3倍化: ダイス合計+5 → +6
        public string SkillId => "MightIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null)
            {
                ctx.playerDiceTotal += 6;
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

    /// <summary>頑強IV — 被ダメージ-4</summary>
    public class FortitudeIV : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 4);
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

    /// <summary>心眼IV (2026-05-30 LEG1.3倍化: +4 → +5) — 会心ダイス+5</summary>
    public class InsightIV : IPassiveSkillEffect
    {
        public string SkillId => "InsightIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.criticalBonus += 5; }
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

    /// <summary>活力IV (2026-05-30 LEG1.3倍化: +4 → +5) — ターン終了時HP+5回復</summary>
    public class VitalityIV : IPassiveSkillEffect
    {
        public string SkillId => "VitalityIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 5);
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

    /// <summary>聖なる守り — ロール敗北時 被ダメ50%軽減 + 軽減前の被ダメを記録し、
    /// 次のロール勝利時に「記録ダメ×2」を確定ダメ(軽減無視)として敵に与える。
    /// 反撃発動でリセット。</summary>
    public class HolyShield : IPassiveSkillEffect
    {
        public string SkillId => "HolyShield";
        private const string EchoKey = "holyshield_echo";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPreReceiveDamage,
            PassiveSkillTrigger.OnPreDealDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPreReceiveDamage)
            {
                if (ctx.playerLostRoll && ctx.finalDamage > 0)
                {
                    // 軽減「前」の被ダメを記録 (×2 で次回反撃に乗せる)
                    ctx.AddAccumulated(EchoKey, ctx.finalDamage);
                    ctx.finalDamage = (int)(ctx.finalDamage * 0.5f);
                }
                return;
            }
            // OnPreDealDamage: ロール勝利時、 蓄積されたエコーを ×2 で確定ダメに乗せて消費
            if (ctx.playerWonRoll)
            {
                int echo = (int)ctx.GetAccumulated(EchoKey);
                if (echo > 0)
                {
                    int dmg = echo * 2;
                    ctx.fixedDamageToEnemy += dmg;
                    ctx.accumulatedValues[EchoKey] = 0f;
                    UnityEngine.Debug.Log($"[聖なる守り] 蓄積エコー反撃 +{dmg} (基{echo}×2)");
                }
            }
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
            // OnPostRoll 中は ctx.diceDifference が未確定（最終確定は OnPostRoll 後）。
            // 現在の合計から自前で差を取り、発火順や鮮度に依存しないようにする。
            if (System.Math.Abs(ctx.playerDiceTotal - ctx.enemyDiceTotal) <= 3)
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

    /// <summary>血令 — ゾロ目で勝利時、ダイス合計×2.5(会心倍率)を確定ダメ(軽減不能)として与える。
    /// OnPreDealDamage は勝利時のみ発火。通常の与ダメは確定ダメに置換する。</summary>
    public class BloodDecree : IPassiveSkillEffect
    {
        public string SkillId => "BloodDecree";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;
            int first = ctx.playerDice[0];
            for (int i = 1; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] != first) return; // ゾロ目でなければ何もしない

            int dmg = UnityEngine.Mathf.CeilToInt(ctx.playerDiceTotal * 2.5f);
            ctx.fixedDamageToEnemy += dmg; // 確定（軽減不能）ダメージ
            ctx.finalDamage = 0;           // 通常の(軽減される)与ダメは確定ダメに置換
            UnityEngine.Debug.Log($"[血令] ゾロ目勝利: 確定ダメ {dmg} (ダイス合計{ctx.playerDiceTotal}×2.5)");
        }
    }

    // ============================================================
    //  ユニークパッシブ — 短剣系
    // ============================================================

    /// <summary>処刑 — 勝利時、次ターン敵最大ダイス1固定 (最強ダイスを潰す)</summary>
    public class Execute : IPassiveSkillEffect
    {
        public string SkillId => "Execute";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };
        // クラス名と同名メソッドを避けるため明示的インターフェース実装
        void IPassiveSkillEffect.Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.pendingDiceOverrides.Add(
                new DiceOverrideRequest(DiceOverrideRequest.TargetDice.Highest, 1, SkillId));
        }
    }

    /// <summary>蝕夜 — オーバーダメ×4を蓄積→戦闘開始時に確定ダメで放出。
    /// 蓄積はラン中ずっと持続し、 戦闘ごとに膨張する (雪だるま式)。
    /// ラン跨ぎでは IRunResettable でリセット (StartNewRun で発火)。</summary>
    public class Nightfall : IPassiveSkillEffect, IRunResettable
    {
        public string SkillId => "Nightfall";
        private int persistentOverdamage = 0;
        public void ResetRunState() { persistentOverdamage = 0; }
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
                // 過剰ダメを 400% (×4) で持ち越し (dagger_t4 強化)
                persistentOverdamage += ctx.overDamageAccumulated * 4;
                ctx.overDamageAccumulated = 0;
            }
        }
    }

    /// <summary>出血 — ダメージを与えるたび、敵に出血+1（ターン1回制限を撤廃）</summary>
    public class Sting : IPassiveSkillEffect
    {
        public string SkillId => "Sting";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
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
                ctx.playerCurrentHP = System.Math.Max(1, ctx.playerCurrentHP - 2);
                int cur = (int)ctx.GetAccumulated("curseDebuff");
                // 2026-05-31 ナーフ: 蓄積上限 5 → 3 (呪チェーン全体ナーフ)
                if (cur < 3) ctx.AddAccumulated("curseDebuff", 1);
                return;
            }
            int debuff = (int)ctx.GetAccumulated("curseDebuff");
            if (debuff > 0)
            {
                ctx.enemyDiceTotal = System.Math.Max(0, ctx.enemyDiceTotal - debuff);
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
                    // 2026-05-31 ナーフ: 狂戦士ダイス補正 +10 → +5 (呪Ⅳ突出抑制)
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                    {
                        ctx.playerDiceTotal += 5;
                    }
                    break;
                case PassiveSkillTrigger.OnPreDealDamage:
                    // 2026-05-31 ナーフ: 累積被ダメ反撃 ×1.0 → ×0.5
                    if (ctx.GetAccumulated(BERSERK_KEY) >= 1)
                        ctx.fixedDamageToEnemy += UnityEngine.Mathf.CeilToInt(ctx.GetAccumulated(DMG_KEY) * 0.5f);
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

    /// <summary>盟約 — ロール敗北時、次ターンのダイス合計+3</summary>
    public class ReversalFlame : IPassiveSkillEffect
    {
        public string SkillId => "ReversalFlame";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (!ctx.nextTurnBuffs.ContainsKey("diceBonus"))
                ctx.nextTurnBuffs["diceBonus"] = 0f;
            ctx.nextTurnBuffs["diceBonus"] += 3f;
        }
    }

    /// <summary>堅忍 — ロール敗北時の被ダメージ-3（下振れを救う防御ダイス）</summary>
    public class Steadfast : IPassiveSkillEffect
    {
        public string SkillId => "Steadfast";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0)
                ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 3);
        }
    }

    /// <summary>鉄壁 — ロール敗北時の被ダメージ-1（堅実な低位防御）</summary>
    public class IronWall : IPassiveSkillEffect
    {
        public string SkillId => "IronWall";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0)
                ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 1);
        }
    }

    /// <summary>諸刃 — ロール勝利するたびに【負傷】を負う (回復・シールド獲得が負傷Lv分低下、 上限20)。
    /// 同時に「負傷Lv × 2」 を与ダメに加算 (傷つくほど刃が冴える、 上振れも備えた両刃)。</summary>
    public class Moroha : IPassiveSkillEffect
    {
        public string SkillId => "Moroha";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnRollWin,
            PassiveSkillTrigger.OnPreDealDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnRollWin)
            {
                // healShieldReduction は獲得回復量・シールド量をスタック分減衰させる
                if (ctx.healShieldReduction < 20) ctx.healShieldReduction++;
                return;
            }
            // OnPreDealDamage: 勝利時のみ与ダメに +負傷Lv×2 を加算 (両刃の上振れ)
            if (ctx.playerWonRoll && ctx.healShieldReduction > 0)
                ctx.finalDamage += ctx.healShieldReduction * 2;
        }
    }

    /// <summary>貪欲 — 与えたダメージの10%をHPとして回復する（メリット・デメリット型／高出目だが守りは無い）。</summary>
    public class Greed : IPassiveSkillEffect
    {
        public string SkillId => "Greed";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // BeginNewTurn で毎ターン0にリセットされるため OnTurnStart で再適用
            ctx.lifestealPct = 0.1f;
        }
    }

    // ============================================================
    //  汎用パッシブ — 吸血（Lifesteal）: ロール勝利時、最終与ダメの2/4/6/8%回復
    //  ctx.lifestealPct に加算し、CombatManager 勝利分岐が totalDmg×pct を HealPlayer で回復
    //  （負傷/回復封印/天衣無縫を尊重）。毎ターン0リセットのため OnTurnStart で再適用。
    // ============================================================
    public class LifestealI : IPassiveSkillEffect
    {
        public string SkillId => "LifestealI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.02f; }
    }
    public class LifestealII : IPassiveSkillEffect
    {
        public string SkillId => "LifestealII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.04f; }
    }
    public class LifestealIII : IPassiveSkillEffect
    {
        public string SkillId => "LifestealIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.06f; }
    }
    public class LifestealIV : IPassiveSkillEffect
    {
        // 2026-05-30 LEG 1.3倍化: 8% → 10%
        public string SkillId => "LifestealIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.10f; }
    }

    // ============================================================
    //  汎用パッシブ — 不屈（Indomitable）リワーク 2026-05-30:
    //  敵threatを 2/4/6/8 軽減 + 戦闘開始時シールド 5/10/15/20 を獲得 (consShield に加算)。
    //  純粋な「ヘイト軽減」 だけでは効果薄かったので 「初手の盾」 を併設。
    // ============================================================
    internal static class IndomitableHelper
    {
        public static void Apply(CombatContext ctx, int threatReduce, int shieldAmount)
        {
            ctx.enemyThreat = System.Math.Max(0, ctx.enemyThreat - threatReduce);
            if (shieldAmount > 0)
            {
                ctx.consShield += shieldAmount;
                ctx.shieldGainedTotal += shieldAmount;
            }
        }
    }
    public class IndomitableI : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { IndomitableHelper.Apply(ctx, 2, 5); }
    }
    public class IndomitableII : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { IndomitableHelper.Apply(ctx, 4, 10); }
    }
    public class IndomitableIII : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { IndomitableHelper.Apply(ctx, 6, 15); }
    }
    public class IndomitableIV : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { IndomitableHelper.Apply(ctx, 8, 20); }
    }

    // ============================================================
    //  汎用パッシブ — シールドバッシュ（ShieldBash）: ロール勝利時、与ダメの 5/10/15/20% をシールド化
    //  ctx.shieldOnWinPct に加算し、CombatManager 勝利分岐が totalDmg×pct を consShield へ（天衣無縫減衰を適用）。
    //  毎ターン0リセットのため OnTurnStart で再適用。
    // ============================================================
    public class ShieldBashI : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.shieldOnWinPct += 0.05f; }
    }
    public class ShieldBashII : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.shieldOnWinPct += 0.10f; }
    }
    public class ShieldBashIII : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.shieldOnWinPct += 0.15f; }
    }
    public class ShieldBashIV : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.shieldOnWinPct += 0.20f; }
    }

    // ============================================================
    //  汎用パッシブ — 貸与された時間（LentTime）リワーク版:
    //   敗北時、 被ダメの 15/30/45/60% を「貸与時間」として肩代わり (軽減) し蓄積。
    //   蓄積が最大HP×同割合に達すると、 一括清算ではなく Tier別 3/4/5/6ターンに分割して
    //   毎ターン終了時 軽減不可ダメを支払う。 分割中にロール勝利1回で残債務帳消し (返済免除)。
    //   上限到達中にさらに被弾しても新規借入は受け付けない (返済中ロック)。
    // ============================================================
    internal static class LentTimeHelper
    {
        // Tier別 分割ターン数
        public static int PaybackTurns(int tier)
        {
            switch (tier) { case 1: return 3; case 2: return 4; case 3: return 5; case 4: return 6; default: return 4; }
        }
        // OnRollWin: 分割返済中なら帳消し / 蓄積中なら帳消し
        public static void OnWin(CombatContext ctx)
        {
            if (ctx.lentTimePaybackRemainTurns > 0 || ctx.lentTimeStacks > 0)
                UnityEngine.Debug.Log($"[貸与された時間] 勝利→ 帳消し ({ctx.lentTimeStacks + ctx.lentTimePaybackTotal} ダメ免除)");
            ctx.lentTimeStacks = 0;
            ctx.lentTimePaybackRemainTurns = 0;
            ctx.lentTimePaybackTotal = 0;
        }
        // OnPreReceiveDamage: 被ダメの pct を肩代わり蓄積、 上限到達で分割返済モードへ移行
        // ユーザー指定: 返済支払いがあったターンは新規借入を行わない
        public static void OnReceive(CombatContext ctx, float pct, int tier)
        {
            if (ctx.lentTimePaybackRemainTurns > 0) return; // 返済中は借入ロック
            if (ctx.lentTimePaidThisTurn) return;            // 同ターンに支払い済みなら借入もしない
            int dmg = ctx.finalDamage;
            if (dmg <= 0) return;
            int portion = UnityEngine.Mathf.CeilToInt(dmg * pct);
            ctx.finalDamage = System.Math.Max(0, dmg - portion);
            ctx.lentTimeStacks += portion;
            ctx.lentTimeTier = tier;
            int cap = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * pct);
            if (ctx.lentTimeStacks >= cap)
            {
                ctx.lentTimePaybackTotal = ctx.lentTimeStacks;
                ctx.lentTimePaybackRemainTurns = PaybackTurns(tier);
                ctx.lentTimeStacks = 0;
                UnityEngine.Debug.Log($"[貸与された時間] 上限到達 → {ctx.lentTimePaybackRemainTurns}T 分割返済開始 (総債務{ctx.lentTimePaybackTotal})");
            }
        }
        // OnTurnEnd: 分割返済の1回分を支払い (HP下限1ガード + 蓄積停止フラグセット)
        public static void OnTurnEnd(CombatContext ctx)
        {
            if (ctx.lentTimePaybackRemainTurns <= 0) return;
            int payment = UnityEngine.Mathf.CeilToInt((float)ctx.lentTimePaybackTotal / ctx.lentTimePaybackRemainTurns);
            // HP下限保護: 現HP > 1 でないと支払いしない、 また支払い量を HP-1 まで丸める
            int hpFloor = System.Math.Max(0, ctx.playerCurrentHP - 1);
            int actualPay = System.Math.Min(payment, hpFloor);
            if (actualPay > 0)
            {
                ctx.fixedDamageToPlayer += actualPay;
                ctx.lentTimePaidThisTurn = true; // このターン新規借入禁止
            }
            ctx.lentTimePaybackTotal = System.Math.Max(0, ctx.lentTimePaybackTotal - payment); // 元本は予定通り減らす
            ctx.lentTimePaybackRemainTurns--;
            UnityEngine.Debug.Log($"[貸与された時間] T終了支払 -{actualPay} (予定{payment}, HP1下限保護, 残債{ctx.lentTimePaybackTotal} / 残{ctx.lentTimePaybackRemainTurns}T)");
        }
    }
    public class LentTimeI : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.15f, 1);
        }
    }
    public class LentTimeII : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.30f, 2);
        }
    }
    public class LentTimeIII : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.45f, 3);
        }
    }
    public class LentTimeIV : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.60f, 4);
        }
    }

    /// <summary>完全 (2026-05-31 v4 outgoing移行): 重複あり → outgoing += 1.0 (×2 相当の倍率寄与)。
    /// 全て異なる → 半減は finalDamage 直接 ÷2 を維持 (ペナルティはoutgoing合算前に効かせる)。
    /// ダイス1個では判定不能のため無効。</summary>
    public class Perfection : IPassiveSkillEffect
    {
        public string SkillId => "Perfection";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return; // 1個は判定不能=無効
            if (trigger != PassiveSkillTrigger.OnPreDealDamage) return;
            if (ctx.finalDamage <= 0) return;
            bool ordered = HasDuplicate(ctx.playerDice);
            if (ordered)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += 1.0f; // 倍率系統合
            }
            else
            {
                ctx.finalDamage = System.Math.Max(1, ctx.finalDamage / 2); // 不完全ペナルティは即時
            }
        }

        /// <summary>出目に同値ペアが1組でもあるか。</summary>
        private static bool HasDuplicate(int[] dice)
        {
            for (int i = 0; i < dice.Length; i++)
                for (int j = i + 1; j < dice.Length; j++)
                    if (dice[i] == dice[j]) return true;
            return false;
        }
    }

    /// <summary>永劫 — 勝利した戦闘をランを跨いで永続蓄積し、10戦ごとにダイス合計+1（最大+5）。</summary>
    public class Eternal : IPassiveSkillEffect
    {
        public string SkillId => "Eternal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            var state = MetaProgression.MetaProgressManager.Instance?.State;
            if (state == null) return;

            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                int bonus = System.Math.Min(5, state.eternalStacks / 10);
                if (bonus > 0)
                {
                    ctx.playerDiceTotal += bonus;                }
            }
            else if (trigger == PassiveSkillTrigger.OnBattleEnd)
            {
                // 勝利（敵HP0）で1スタック蓄積し、ランを跨いで永続化
                if (ctx.enemyCurrentHP <= 0)
                {
                    state.eternalStacks++;
                    MetaProgression.MetaProgressManager.Instance?.Save();
                }
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
                    // 全ダイスが最大出目なら outgoing +=1.0 (×2相当)。 2026-05-31 outgoing移行
                    bool allMax = true;
                    foreach (var d in ctx.playerDice)
                        if (d < maxFace) { allMax = false; break; }
                    if (allMax)
                    {
                        if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                        ctx.outgoingDamageMultiplier += 1.0f;
                    }
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
            ctx.playerDiceTotal += 3;        }
    }

    // ============================
    //  武器Tier段階補正（ダイス合計フラット加算。少ダイス低Tierを底上げし進行を線形化）
    // ============================

    /// <summary>停戦協定 — 完全な引き分け(差0)の時、ロールを打ち切り、敵の最大HPの10%を軽減不能ダメージで与える。
    /// この引き分けターンは停戦協定以外の効果（出血・他の固定ダメ等）を発動させない。</summary>
    public class Truce : IPassiveSkillEffect
    {
        public string SkillId => "Truce";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollDraw };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 引き分け時のみ。他の効果を抑止して停戦の一撃だけを通す。
            ctx.truceThisTurn = true;
            int dmg = System.Math.Max(1, UnityEngine.Mathf.CeilToInt(ctx.enemyMaxHP * 0.1f));
            ctx.fixedDamageToEnemy = dmg;          // 上書き＝他の固定ダメを無効化
            ctx.fixedDamageToPlayer = 0;
            ctx.pursuitDamage = 0;
            ctx.nullifyAllDamage = true;
            ctx.nullifyPursuitDamage = true;
            UnityEngine.Debug.Log($"[停戦協定] 完全引き分け → 敵最大HP10% = {dmg} 軽減不能ダメージ");
        }
    }

    /// <summary>天工開物 — 武器強化のたび強化素材を1つ返還（実効果は GameManager.TryUpgradeWeapon の所持判定）。
    /// 戦闘パッシブとしては何もしない no-op（表示名解決＋未登録警告の抑止用に登録する）。</summary>
    public class TenkouKaibutsu : IPassiveSkillEffect
    {
        public string SkillId => "TenkouKaibutsu";
        public PassiveSkillTrigger[] Triggers => System.Array.Empty<PassiveSkillTrigger>();
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { }
    }

    /// <summary>背水の狂刃 (2026-05-31 outgoing 移行): HP≤50% で outgoing+=0.3、 ≤25% で outgoing+=0.8。</summary>
    public class Bloodlust : IPassiveSkillEffect
    {
        public string SkillId => "Bloodlust";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.playerMaxHP <= 0) return;
            float add = 0f;
            if (ctx.playerCurrentHP * 4 <= ctx.playerMaxHP) add = 0.8f;        // ≤25%
            else if (ctx.playerCurrentHP * 2 <= ctx.playerMaxHP) add = 0.3f;   // ≤50%
            if (add > 0f)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += add;
            }
        }
    }

    /// <summary>ヘルメスの靴 — 各戦闘の初回ロールでダイス合計+5。</summary>
    public class Hermes : IPassiveSkillEffect
    {
        public string SkillId => "Hermes";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (!ctx.isFirstRoll) return;
            ctx.playerDiceTotal += 5;        }
    }

    /// <summary>飢餓丸 — ターン開始時HP-1(軽減不能)。10ターン目の発動後、与ダメ+10(戦闘中永続)＋次の被ダメ-10(1回)。</summary>
    public class HungerPill : IPassiveSkillEffect
    {
        public string SkillId => "HungerPill";
        public PassiveSkillTrigger[] Triggers => new[]
            { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    ctx.fixedDamageToPlayer += 1; // 軽減不能の飢餓ダメ（このターンの被ダメ処理で適用）
                    if (ctx.currentTurn >= 10 && ctx.GetAccumulated("hunger_awake") <= 0)
                    {
                        ctx.accumulatedValues["hunger_awake"] = 1;   // 以降、与ダメ+10永続
                        ctx.accumulatedValues["hunger_guard"] = 1;   // 次の被ダメ-10（1回）
                        UnityEngine.Debug.Log("[飢餓丸] 覚醒: 与ダメ+10(永続) / 次被ダメ-10(1回)");
                    }
                    break;
                case PassiveSkillTrigger.OnPreDealDamage:
                    if (ctx.finalDamage > 0 && ctx.GetAccumulated("hunger_awake") > 0)
                        ctx.finalDamage += 10;
                    break;
                case PassiveSkillTrigger.OnPreReceiveDamage:
                    if (ctx.finalDamage > 0 && ctx.GetAccumulated("hunger_guard") > 0)
                    {
                        ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 10);
                        ctx.accumulatedValues["hunger_guard"] = 0; // 1回限り
                    }
                    break;
            }
        }
    }

    /// <summary>黄金卿の剣 (2026-05-31 v3 消費Gold基準) — 消費した累積ゴールド×0.01 を outgoing に加算。
    /// 100Gで+1.0 (×2)、 200Gで+2.0 (×3)。 「使えば使うほど強くなる」軸への切替。
    /// 旧仕様 (保有Gold×0.04) は「貯めるほど強い → 出費抑制 = 戦略歪み」だったため変更。</summary>
    public class GoldKingBlade : IPassiveSkillEffect
    {
        public string SkillId => "GoldKingBlade";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int spent = GameLoop.GameManager.Instance?.Run?.coinsSpent ?? 0;
            if (spent <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.01f * spent;
        }
    }

    /// <summary>軽量 — ダイス合計+3（武器T1）。</summary>
    public class Lightweight : IPassiveSkillEffect
    {
        public string SkillId => "Lightweight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerDiceTotal += 3;        }
    }

    /// <summary>熟練 — ダイス合計+2（武器T2）。</summary>
    public class Mastery : IPassiveSkillEffect
    {
        public string SkillId => "Mastery";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerDiceTotal += 2;        }
    }

    /// <summary>技量 — ダイス合計+1（武器T3）。</summary>
    public class Skill : IPassiveSkillEffect
    {
        public string SkillId => "Skill";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerDiceTotal += 1;        }
    }

    // ============================================================
    //  汎用パッシブ — 利刃（BladeEdge）: 敵基礎防御%を剥がす + 勝利時最低保証
    //  Lv1-4: 軽減相殺 15/20/25/30pt、勝利時最低保証 1/2/3/4
    //  ※ 装甲(基礎防御)を持つ敵にのみ有効。プレイヤー火力が高いほど回収絶対量が増える対タンク兵装。
    // ============================================================

    /// <summary>利刃I — 敵基礎防御 -15pt、勝利時最低保証1</summary>
    public class BladeEdgeI : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.armorPenPct = 0.15f; ctx.winMinDamage = System.Math.Max(ctx.winMinDamage, 1); }
    }

    /// <summary>利刃II — 敵基礎防御 -20pt、勝利時最低保証2</summary>
    public class BladeEdgeII : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.armorPenPct = 0.20f; ctx.winMinDamage = System.Math.Max(ctx.winMinDamage, 2); }
    }

    /// <summary>利刃III — 敵基礎防御 -25pt、勝利時最低保証3</summary>
    public class BladeEdgeIII : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.armorPenPct = 0.25f; ctx.winMinDamage = System.Math.Max(ctx.winMinDamage, 3); }
    }

    /// <summary>利刃IV — 敵基礎防御 -30pt、勝利時最低保証4</summary>
    public class BladeEdgeIV : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.armorPenPct = 0.30f; ctx.winMinDamage = System.Math.Max(ctx.winMinDamage, 4); }
    }

    // ============================================================
    //  処刑・対タンク・シナジー触媒（2026-05-29 追加）
    // ============================================================

    /// <summary>賞金首狩り (リワーク 2026-05-30) — ターン終了時、敵HPが閾値%以下なら即処刑。
    /// 通常敵: 閾値 10/20/30/40% / ボス: 閾値 5/10/15/20% (耐性)。
    /// 報酬: 処刑時 最大HP×(lv×3%) 回復 + GOLD 1/2/3/4 (全Tier獲得)。</summary>
    internal static class BountyHunterHelper
    {
        public static bool IsBoss(CombatContext ctx)
        {
            var enemy = CombatSystem.CombatManager.Instance?.CurrentEnemy;
            return enemy != null && enemy.id != null && enemy.id.StartsWith("boss_layer");
        }
        public static void Try(CombatContext ctx, int normalPct, int bossPct, int lv)
        {
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            int thresholdPct = IsBoss(ctx) ? bossPct : normalPct;
            if (ctx.enemyCurrentHP * 100 > ctx.enemyMaxHP * thresholdPct) return;
            ctx.enemyCurrentHP = 0;

            int heal = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.03f * lv);
            if (heal > 0)
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);

            int gold = lv; // Lv1-4 で +1/2/3/4 GOLD (全Tier獲得)
            if (gold > 0)
            {
                var run = GameLoop.GameManager.Instance?.Run;
                if (run != null) run.coins += gold;
            }
            UnityEngine.Debug.Log($"[賞金首狩り] 敵HP{thresholdPct}%以下 → 処刑 (HP+{heal}, +{gold}G, ボス={IsBoss(ctx)})");
        }
    }

    public class BountyHunterI : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 10, 5, 1); }
    }
    public class BountyHunterII : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 20, 10, 2); }
    }
    public class BountyHunterIII : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 30, 15, 3); }
    }
    public class BountyHunterIV : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 40, 20, 4); }
    }

    /// <summary>治癒阻害（Silver）— 敵が得る回復量を50%減少。</summary>
    public class GrievousI : IPassiveSkillEffect
    {
        public string SkillId => "GrievousI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.enemyHealReductionPct = 0.5f; }
    }
    /// <summary>治癒遮断（Gold）— 敵が得る回復量を完全に無効化。</summary>
    public class GrievousII : IPassiveSkillEffect
    {
        public string SkillId => "GrievousII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.enemyHealReductionPct = 1.0f; }
    }

    /// <summary>天梯 (2026-05-31 outgoing 移行) — 連続昇順 (階段) で outgoing +=1.0。
    /// 旧 finalDamage×2 → 倍率系統合。</summary>
    public class Skyladder : IPassiveSkillEffect
    {
        public string SkillId => "Skyladder";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.playerDice == null || ctx.playerDice.Length < 3) return;
            var sorted = (int[])ctx.playerDice.Clone();
            System.Array.Sort(sorted);
            for (int i = 1; i < sorted.Length; i++)
                if (sorted[i] != sorted[i - 1] + 1) return; // 連番でなければ無効
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 1.0f;
            UnityEngine.Debug.Log($"[天梯] 階段成立 → outgoing +1.0");
        }
    }

    /// <summary>天極 — 出目が全て同値（ゾロ目）なら会心を確定させ、会心倍率+1.0。多ダイス武器ほど至難の最高役。</summary>
    public class ApexCrit : IPassiveSkillEffect
    {
        public string SkillId => "ApexCrit";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;
            int first = ctx.playerDice[0];
            for (int i = 1; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] != first) return; // ゾロ目でなければ無効
            ctx.criticalBonus += 99;        // 会心確定
            ctx.criticalMultiplier += 1.0f; // 会心倍率+1
        }
    }

    /// <summary>重畳 (2026-05-31 v5 %ベース) — ロール勝利毎に与ダメ倍率を加算。
    /// Lv1-4: 勝利毎 +2/4/5/10%、 上限 +20/40/60/80% (outgoing 加算)。
    /// stack 値は % 値 (整数) で保存し、 OnDeal で outgoing += stack/100 を反映。</summary>
    internal static class ConquerorHelper
    {
        public const string Key = "conqueror_stack";

        public static void OnWin(CombatContext ctx, int perWinPct, int capPct)
        {
            int s = (int)ctx.GetAccumulated(Key);
            if (s < capPct) ctx.accumulatedValues[Key] = System.Math.Min(capPct, s + perWinPct);
        }
        public static void OnDeal(CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int pct = (int)ctx.GetAccumulated(Key);
            if (pct <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += pct / 100f;
        }
    }
    public class ConquerorI : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 2, 20); else ConquerorHelper.OnDeal(ctx); }
    }
    public class ConquerorII : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 4, 40); else ConquerorHelper.OnDeal(ctx); }
    }
    public class ConquerorIII : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 5, 60); else ConquerorHelper.OnDeal(ctx); }
    }
    public class ConquerorIV : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 10, 100); else ConquerorHelper.OnDeal(ctx); }
    }

    /// <summary>命脈 (再強化 2026-05-30 v2): 戦闘開始時 max HP×10% 回復 + HP50% 割れ瞬間に
    /// シールド (max HP×50%) を獲得 (1戦闘1回)。 LEG責任ある量に底上げ。</summary>
    public class Lifeline : IPassiveSkillEffect
    {
        public string SkillId => "Lifeline";
        private const string UsedKey = "lifeline_used";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                int heal = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.10f);
                int before = ctx.playerCurrentHP;
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
                int gained = ctx.playerCurrentHP - before;
                if (gained > 0) UnityEngine.Debug.Log($"[命脈] 戦闘開始時 +{gained} HP");
                return;
            }
            if (ctx.GetAccumulated(UsedKey) > 0) return;
            if (ctx.playerCurrentHP <= 0) return;
            if (ctx.playerCurrentHP * 100 > ctx.playerMaxHP * 50) return;
            ctx.accumulatedValues[UsedKey] = 1;
            int shield = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.50f);
            ctx.consShield += shield;
            ctx.shieldGainedTotal += shield;
            UnityEngine.Debug.Log($"[命脈] HP50%割れ → シールド+{shield}");
        }
    }

    /// <summary>リピーター（触媒）— 2026-05-31 削除済み（火力暴走の主犯のため無効化）。
    /// 過去アイテム互換のためクラス自体は残すが、 効果は no-op。</summary>
    public class Repeater : IPassiveSkillEffect
    {
        public string SkillId => "Repeater";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { /* removed */ }
    }

    /// <summary>蒼白の槍騎士 (リワーク 2026-05-30) — 軽減無視ダメージを 2.0倍 (旧1.5倍) にする。
    /// 蒼白の穂先は鎧の理を完全に嗤う。</summary>
    public class PalePikeKnight : IPassiveSkillEffect
    {
        public string SkillId => "PalePikeKnight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.fixedDamageMultiplier = 2.0f; }
    }

    /// <summary>共鳴 (2026-05-31 outgoing 移行) — 発動中パッシブ数の超過分 (>5) × 0.05 を outgoing に加算。
    /// 例 active=10 → outgoing +=0.25 (×1.25 相当)。 倍率系統合。</summary>
    public class Resonance : IPassiveSkillEffect
    {
        public string SkillId => "Resonance";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int n = PassiveSkillManager.Instance?.ActivePlayerSkillCount ?? 0;
            int over = n - 5;
            if (over <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.05f * over;
        }
    }

    /// <summary>天命 — 敵ダイス合計が自分の2倍以上、かつHPが最大の30%以上なら、そのロール敗北ダメージでHPが1以下にならない。</summary>
    public class Judgement : IPassiveSkillEffect
    {
        public string SkillId => "Judgement";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger != PassiveSkillTrigger.OnPreReceiveDamage) return;

            // 条件: 敵ダイス合計 ≥ 自分の2倍 かつ 現在HP ≥ 最大HPの30%
            if (ctx.playerDiceTotal <= 0) return;
            if (ctx.enemyDiceTotal < ctx.playerDiceTotal * 2) return;
            if (ctx.playerMaxHP <= 0 || ctx.playerCurrentHP * 100 < ctx.playerMaxHP * 30) return;

            // このターンの被ダメではHPが1以下にならない(=最低2残す)ように主ダメージを上限化
            int maxAllowed = ctx.playerCurrentHP - 2;
            if (maxAllowed < 0) maxAllowed = 0;
            if (ctx.finalDamage > maxAllowed) ctx.finalDamage = maxAllowed;
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
