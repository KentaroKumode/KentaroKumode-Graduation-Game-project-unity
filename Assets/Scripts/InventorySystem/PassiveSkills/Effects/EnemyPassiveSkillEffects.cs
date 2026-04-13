namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  敵専用パッシブスキル（EnemyPassiveSkillEffects）
    //  プレイヤーのスキルと同じ IPassiveSkillEffect を実装し、
    //  PassiveSkillRegistry に登録することで共通フレームワークで動作する。
    //
    //  ※ 敵スキルは「敵視点」で記述する。
    //     CombatManager が敵スキル発動時は context のプレイヤー/敵を
    //     入れ替えて呼び出す仕組みのため、ここでは「自分(=敵)が勝った」
    //     = playerWonRoll として統一的に記述できる。
    // ============================================================

    // ----------------------------------------------------------
    //  1～3層: シンプルなステータス型
    // ----------------------------------------------------------

    /// <summary>罠師 — ロール勝利時、次ターン相手のダイス合計値-1</summary>
    public class Trapper : IPassiveSkillEffect
    {
        public string SkillId => "Trapper";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("enemyDiceDebuff", out current);
            ctx.nextTurnBuffs["enemyDiceDebuff"] = current + 1;
        }
    }

    /// <summary>不死者 — 毎ターン開始時、HP1回復</summary>
    public class Undying : IPassiveSkillEffect
    {
        public string SkillId => "Undying";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 1);
        }
    }

    /// <summary>疾駆 — 初回ロール時、ダイス合計値+2</summary>
    public class Sprint : IPassiveSkillEffect
    {
        public string SkillId => "Sprint";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.isFirstRoll)
            {
                ctx.playerDiceTotal += 2;
            }
        }
    }

    /// <summary>剛力 — ロール勝利時、ダメージ+2</summary>
    public class BruteForce : IPassiveSkillEffect
    {
        public string SkillId => "BruteForce";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerWonRoll)
            {
                ctx.finalDamage += 2;
            }
        }
    }

    /// <summary>飛翔 — 追撃ダメージを受けない</summary>
    public class Flight : IPassiveSkillEffect
    {
        public string SkillId => "Flight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPrePursuitDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.nullifyPursuitDamage = true;
        }
    }

    // ----------------------------------------------------------
    //  4～5層: 複合型
    // ----------------------------------------------------------

    /// <summary>硬鱗 — 受けるダメージを-2（最低0）</summary>
    public class HardScales : IPassiveSkillEffect
    {
        public string SkillId => "HardScales";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 2);
            }
        }
    }

    /// <summary>尾撃 — ロール敗北時、相手に1の固定ダメージ</summary>
    public class TailStrike : IPassiveSkillEffect
    {
        public string SkillId => "TailStrike";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.fixedDamageToEnemy += 1;
        }
    }

    /// <summary>暴走 — ロール敗北時、次ターンのダイス合計値+3</summary>
    public class Rampage : IPassiveSkillEffect
    {
        public string SkillId => "Rampage";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("diceBonus", out current);
            ctx.nextTurnBuffs["diceBonus"] = current + 3;
        }
    }

    /// <summary>虚体 — 受けるダメージを50%軽減</summary>
    public class Ethereal : IPassiveSkillEffect
    {
        public string SkillId => "Ethereal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                ctx.finalDamage = ctx.finalDamage / 2;
            }
        }
    }

    /// <summary>呪縛 — ロール勝利時、次ターン相手のダイス合計値-2</summary>
    public class Curse : IPassiveSkillEffect
    {
        public string SkillId => "Curse";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("enemyDiceDebuff", out current);
            ctx.nextTurnBuffs["enemyDiceDebuff"] = current + 2;
        }
    }

    /// <summary>不動 — 追撃ダメージを受けない</summary>
    public class Immovable : IPassiveSkillEffect
    {
        public string SkillId => "Immovable";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPrePursuitDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.nullifyPursuitDamage = true;
        }
    }

    /// <summary>反撃態勢 — ロール敗北時、次ターンのダメージ+3</summary>
    public class CounterStance : IPassiveSkillEffect
    {
        public string SkillId => "CounterStance";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            float current = 0f;
            ctx.nextTurnBuffs.TryGetValue("damageBonus", out current);
            ctx.nextTurnBuffs["damageBonus"] = current + 3;
        }
    }

    // ----------------------------------------------------------
    //  6～7層: ユニーク型（高度な戦略を持つ敵専用スキル）
    // ----------------------------------------------------------

    /// <summary>多頭攻撃 — ロール勝利時、追撃ダイスを1個追加</summary>
    public class MultiHead : IPassiveSkillEffect
    {
        public string SkillId => "MultiHead";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // extraPursuitDice をコンテキスト蓄積で管理
            ctx.AddAccumulated("extraPursuitDice", 1);
        }
    }

    /// <summary>再生 — 毎ターン開始時、HP2回復</summary>
    public class Regeneration : IPassiveSkillEffect
    {
        public string SkillId => "Regeneration";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 2);
        }
    }

    /// <summary>魔王の威圧 — 戦闘開始時、相手の最大HPを3減少</summary>
    public class DemonAura : IPassiveSkillEffect
    {
        public string SkillId => "DemonAura";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵スキルとして発動時、対象（相手）の最大HPを減少
            // CombatManager側で enemyMaxHPReduction として処理
            ctx.AddAccumulated("enemyMaxHPReduction", 3);
        }
    }

    /// <summary>地獄の業火 — ロール勝利時、追加で2の固定ダメージ</summary>
    public class Hellfire : IPassiveSkillEffect
    {
        public string SkillId => "Hellfire";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.fixedDamageToEnemy += 2;
        }
    }

    /// <summary>吸血 — ダメージを与えた時、その50%分HPを回復</summary>
    public class Lifesteal : IPassiveSkillEffect
    {
        public string SkillId => "Lifesteal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerWonRoll && ctx.finalDamage > 0)
            {
                int heal = ctx.finalDamage / 2;
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
            }
        }
    }

    /// <summary>夜の王 — 5ターン目以降、ダイス1個追加</summary>
    public class NightLord : IPassiveSkillEffect
    {
        public string SkillId => "NightLord";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.currentTurn >= 5)
            {
                // extraDice をコンテキスト蓄積で管理（毎ターンリフレッシュ）
                ctx.accumulatedValues["extraDice"] = 1;
            }
        }
    }

    /// <summary>死の宣告 — 10ターン以内に倒さなければ即死攻撃（999ダメージ）</summary>
    public class DeathSentence : IPassiveSkillEffect
    {
        public string SkillId => "DeathSentence";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.currentTurn > 10)
            {
                // 即死級ダメージを固定ダメージとして付与
                ctx.fixedDamageToEnemy += 999;
            }
        }
    }

    /// <summary>威圧オーラ — ロール敗北時（プレイヤー勝利時）、scratchダメージを付与
    /// scratch = max(0, enemyThreat - |diceDiff|)
    /// ※SwapPerspective内で実行されるため、enemyThreatは元の敵threat値のまま</summary>
    public class ScratchAura : IPassiveSkillEffect
    {
        public string SkillId => "ScratchAura";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int diff = System.Math.Abs(ctx.diceDifference);
            int scratch = System.Math.Max(0, ctx.enemyThreat - diff);
            if (scratch > 0)
                ctx.scratchDamage += scratch;
        }
    }
}
