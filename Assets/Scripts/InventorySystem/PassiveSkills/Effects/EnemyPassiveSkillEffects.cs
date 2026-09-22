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
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + ctx.ReduceEnemyHeal(1));
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

    /// <summary>ダークナイト — 研ぎ澄まし: 決闘が長引くほど剣技が冴える。
    /// 3ターンごとに「ロール勝利時の与ダメージ」+1（累積）。相互火力不足
    /// によるこう着（長期戦）を、敵の決め手火力を逓増させて終局へ導く。
    /// 敵パッシブは敵視点で勝敗反転発火するため OnRollWin = 敵がロール勝利
    /// （＝敵が与ダメするターン）。T1-2:+0 / T3-5:+1 / T6-8:+2 / T9-11:+3 …</summary>
    public class HoningDuel : IPassiveSkillEffect
    {
        public string SkillId => "HoningDuel";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int bonus = System.Math.Max(0, ctx.currentTurn) / 3; // 3ターンごとに+1
            if (bonus <= 0) return;
            ctx.currentBuffs.TryGetValue("damageBonus", out var cur);
            ctx.currentBuffs["damageBonus"] = cur + bonus; // 今ターンの敵与ダメに加算
        }
    }

    /// <summary>精鋭 — エリートマス(4層以降)の敵に付与される強化。
    /// HP2倍・threat+2 はスポーン時にステータスへ適用済み。本パッシブは
    /// 「3ターンごとに自身のダイス出目合計+1（累積）」を担う。
    /// 敵視点で playerDiceTotal = 自身のダイス合計。
    /// T1-2:+0 / T3-5:+1 / T6-8:+2 / T9-11:+3 …</summary>
    public class EliteVigor : IPassiveSkillEffect
    {
        public string SkillId => "EliteVigor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                // エリート汎用: 基礎防御10%（被ダメ%軽減）。利刃で剥がせる。
                ctx.enemyDamageReductionPct += 0.10f;
                return;
            }
            int bonus = System.Math.Max(0, ctx.currentTurn) / 3; // 3ターンごとに+1
            if (bonus > 0) ctx.playerDiceTotal += bonus; // 敵視点で自身のダイス合計
        }
    }

    // ==========================================================
    //  エリート固有パッシブ（4層以降エリートマス・基敵ごとに1種）
    //  逆スケール設計: 元々弱い敵ほど強力、強い敵ほど控えめ。
    //  敵視点: playerDiceTotal=自ダイス合計 / playerCurrentHP=自HP /
    //          enemyCurrentHP=実プレイヤーHP / finalDamage=与/被ダメ。
    // ==========================================================

    /// <summary>精鋭スライム — 腐食粘塊: 毎ロール自ダイス合計+3（最弱→強力）</summary>
    public class EliteSlime : IPassiveSkillEffect
    {
        public string SkillId => "EliteSlime";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.playerDiceTotal += 3;
    }

    /// <summary>精鋭ゴブリン — 群狼の戦術: 毎ロール自ダイス合計+3（最弱→強力）</summary>
    public class EliteGoblin : IPassiveSkillEffect
    {
        public string SkillId => "EliteGoblin";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.playerDiceTotal += 3;
    }

    /// <summary>精鋭コボルド — 早業: ロールの度にプレイヤーのGOLDを5枚奪い、
    /// 50%の確率で被ダメージを回避（0に）。通常の与ダメージは別途そのまま発生。</summary>
    public class EliteKobold : IPassiveSkillEffect
    {
        public string SkillId => "EliteKobold";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                var run = GameLoop.GameManager.Instance?.Run;
                if (run == null || run.coins <= 0) return;
                int steal = System.Math.Min(5, run.coins); // 2026-08-10 経済リスケール: ゴールド量なので ×5
                run.coins -= steal;
                UnityEngine.Debug.Log($"[精鋭コボルド・早業] GOLD {steal} 強奪 (残{run.coins})");
            }
            else if (ctx.playerLostRoll && ctx.finalDamage > 0
                     && GameLoop.GameRng.Value("enemy.koboldDodge") < 0.5f)
            {
                ctx.finalDamage = 0; // 50%で被ダメ回避
                UnityEngine.Debug.Log("[精鋭コボルド・早業] 回避成功（被ダメ0）");
            }
        }
    }

    /// <summary>精鋭スケルトン — 不死の軍勢: 致命ダメージを受けたとき2回まで
    /// HP1で踏みとどまり全回復。発動のたびに自ダイス合計+2（戦闘終了まで永続）。
    /// 敵視点: OnTurnEnd で playerCurrentHP=自HP（被ダメ反映後）が0以下なら復活。
    /// 戦闘終了判定はターン終端なので OnTurnEnd 復活で成立する。</summary>
    public class EliteSkeleton : IPassiveSkillEffect
    {
        public string SkillId => "EliteSkeleton";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnPostRoll };
        private const string RevKey = "eskl_rev";
        private const string DiceKey = "eskl_dice";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                ctx.accumulatedValues.TryGetValue(DiceKey, out var ds);
                if (ds > 0) ctx.playerDiceTotal += 3 * (int)ds; // 発動毎+3、最大2回=合計+6
                return;
            }
            // OnTurnEnd: 自HP(=playerCurrentHP)が致命なら2回まで踏みとどまる
            if (ctx.playerCurrentHP > 0) return;
            ctx.accumulatedValues.TryGetValue(RevKey, out var used);
            if ((int)used >= 2) return;
            ctx.accumulatedValues[RevKey] = used + 1;
            ctx.accumulatedValues.TryGetValue(DiceKey, out var ds2);
            ctx.accumulatedValues[DiceKey] = ds2 + 1;
            ctx.playerCurrentHP = ctx.playerMaxHP; // HP全回復で踏みとどまる
            UnityEngine.Debug.Log($"[精鋭スケルトン・不死の軍勢] 踏みとどまり({(int)used + 1}/2) " +
                                  $"HP全回復、以降ロール+{3 * ((int)ds2 + 1)}");
        }
    }

    /// <summary>精鋭ダイアウルフ — 血盟の疾走: 自ダイス合計+2、勝利時さらに与ダメ+2</summary>
    public class EliteWolf : IPassiveSkillEffect
    {
        public string SkillId => "EliteWolf";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll) ctx.playerDiceTotal += 2;
            else if (ctx.playerWonRoll) ctx.finalDamage += 2;
        }
    }

    /// <summary>精鋭ハーピィ — 死翔: この戦闘中プレイヤーは消費アイテム使用不可。
    /// 毎ロール自ダイス合計 +3＋経過ターン（毎ターン+1の無限累積）。追撃免疫と相乗。</summary>
    public class EliteHarpy : IPassiveSkillEffect
    {
        public string SkillId => "EliteHarpy";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                ctx.consumablesLocked = true; // 消費アイテム使用不可
                return;
            }
            // +3 固定 ＋ 経過ターン（T1:+4, T2:+5 … 無限累積）
            ctx.playerDiceTotal += 3 + System.Math.Max(0, ctx.currentTurn);
        }
    }

    /// <summary>精鋭13番目の死 — 死の重圧: 13番目の宣告が灯ったターン中、
    /// 自ダイス合計+13（宣告フラグ decree13th_armed が立つターンのみ）。</summary>
    public class EliteDecree13 : IPassiveSkillEffect
    {
        public string SkillId => "EliteDecree13";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            // Decree13th が OnTurnStart に立てる宣告フラグ。立っているターンのみ+13。
            if (ctx.currentBuffs.TryGetValue("decree13th_armed", out var armed) && armed > 0f)
                ctx.playerDiceTotal += 13;
        }
    }

    /// <summary>精鋭オーク — 痛恨の一撃: ロール勝利時+8ダメージ。
    /// ロール敗北で自ダイス数+1スタック（勝利でリセット）。オーク基本2個＋
    /// 最大+3＝合計5個まで。次ターン開始時に extraDice として反映。</summary>
    public class EliteOrc : IPassiveSkillEffect
    {
        public string SkillId => "EliteOrc";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnRollLose,
            PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnStart };
        private const string StackKey = "eorc_dice"; // 追加ダイス数スタック（上限3=合計5個）
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    ctx.accumulatedValues.TryGetValue(StackKey, out var s);
                    ctx.accumulatedValues["extraDice"] = s; // 今ターンのダイス数加算に反映
                    break;
                case PassiveSkillTrigger.OnPreDealDamage:
                    if (ctx.playerWonRoll) ctx.finalDamage += 8;
                    break;
                case PassiveSkillTrigger.OnRollLose:
                    ctx.accumulatedValues.TryGetValue(StackKey, out var sl);
                    ctx.accumulatedValues[StackKey] = System.Math.Min(3, (int)sl + 1);
                    break;
                case PassiveSkillTrigger.OnRollWin:
                    ctx.accumulatedValues[StackKey] = 0f; // 勝利でリセット
                    break;
            }
        }
    }

    /// <summary>精鋭リザードマン — 重甲: 被弾時さらに-2軽減（硬鱗と累積=-4）、敗北時に固定1反射</summary>
    public class EliteLizard : IPassiveSkillEffect
    {
        public string SkillId => "EliteLizard";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPreReceiveDamage)
            {
                if (ctx.playerLostRoll && ctx.finalDamage > 0)
                    ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 2);
            }
            else ctx.fixedDamageToEnemy += 1;
        }
    }

    /// <summary>精鋭レイス — 霊体: 2ターンに1度（偶数ターン）「霊体状態」となり、
    /// すべての被ダメージを1に減少。霊体でないターンはダイス数+1。</summary>
    public class EliteWraith : IPassiveSkillEffect
    {
        public string SkillId => "EliteWraith";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            bool ghost = ctx.currentTurn > 0 && ctx.currentTurn % 2 == 0; // 2ターンに1度
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // 霊体でないときダイス数+1（NightLord と同じ extraDice 機構）
                ctx.accumulatedValues["extraDice"] = ghost ? 0f : 1f;
            }
            else if (ghost && ctx.playerLostRoll && ctx.finalDamage > 1)
            {
                ctx.finalDamage = 1; // 霊体: すべての被ダメージを1に
            }
        }
    }

    /// <summary>精鋭ストーンゴーレム — 巌の意志: 毎ターン意志スタック+1。
    /// 撃破された瞬間、スタック分の確定ダメージをプレイヤーへ。
    /// （硬鱗との二重軽減・×2反撃が過剰だったため緩和。精鋭勝率を25%域へ）。</summary>
    public class EliteGolem : IPassiveSkillEffect
    {
        public string SkillId => "EliteGolem";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        private const string WillKey = "egolem_will";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // OnTurnEnd: 意志+1。敵視点で playerCurrentHP=自HP。撃破時に反撃
            ctx.accumulatedValues.TryGetValue(WillKey, out var w);
            int will = (int)w + 1;
            ctx.accumulatedValues[WillKey] = will;
            if (ctx.playerCurrentHP <= 0)
            {
                int dmg = will;
                ctx.EnemyDealUnmitigable(dmg, null);   // シールドは肩代わりする (計上は従来どおり無し)
                UnityEngine.Debug.Log($"[精鋭ゴーレム・巌の意志] 撃破時反撃 意志{will} → プレイヤー残HP={ctx.enemyCurrentHP}");
            }
        }
    }

    /// <summary>精鋭ミノタウロス — 際限なき暴走: 毎ロール自ダイス合計+1（既に強い→控えめ）</summary>
    public class EliteMinotaur : IPassiveSkillEffect
    {
        public string SkillId => "EliteMinotaur";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.playerDiceTotal += 1;
    }

    /// <summary>精鋭ダークナイト — 闇技: 勝利時、与ダメ+2（4Fへ降格に伴いナーフ）</summary>
    public class EliteDarkKnight : IPassiveSkillEffect
    {
        public string SkillId => "EliteDarkKnight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (ctx.playerWonRoll) ctx.finalDamage += 2; }
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
        // キメラを 6F→4F に降格した際に +2→+1 へ控えめ化（現在唯一の利用元）。
        public string SkillId => "Regeneration";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + ctx.ReduceEnemyHeal(1));
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
                int heal = ctx.ReduceEnemyHeal(ctx.finalDamage / 2);
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
        // 脅威(threat)による削りダメージは CombatManager 側で全エネミー共通処理に昇格したため、
        // このパッシブは no-op (二重適用を避ける)。データ互換のためクラスは残す。
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { }
    }

    /// <summary>威圧+: 戦闘開始時に脅威(threat)を +3 する。精鋭/ボスが脅威上限(8)に届くための補助。</summary>
    public class IntimidatePlus : IPassiveSkillEffect
    {
        public string SkillId => "IntimidatePlus";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // ボス難易度オートチューナー: 威圧の脅威加算をボス別に直接調整 (基準3)。
            int add = UnityEngine.Mathf.Max(0, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.IntimidateThreat));
            ctx.enemyThreat += add;
            UnityEngine.Debug.Log($"[威圧+] 脅威 +{add} → {ctx.enemyThreat}");
        }
    }

    /// <summary>威圧++: 戦闘開始時に脅威(threat)を +5 する。ボス専用の最高位脅威(上限10)。</summary>
    public class IntimidatePlusPlus : IPassiveSkillEffect
    {
        public string SkillId => "IntimidatePlusPlus";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.enemyThreat += 5;
            UnityEngine.Debug.Log($"[威圧++] 脅威 +5 → {ctx.enemyThreat}");
        }
    }

    /// <summary>貪欲(偽の商人): 表示専用マーカー。実際のスケーリング(プレイヤー所持パッシブ数に応じた
    /// HP+3/個・ダイス合計+1/3個)は GameManager.StartFalseMerchantCombat が戦闘開始時に適用する
    /// （HP は複製データ、ダイス合計は ctx.enemyDiceTotalBonus 経由）。</summary>
    public class GreedyMerchant : IPassiveSkillEffect
    {
        public string SkillId => "GreedyMerchant";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { }
    }

    /// <summary>狂暴化: 全ボスに付与される時限エンレイジ。
    /// 50ターン経過後、エネミーのダイス合計+10、プレイヤーの回復を完全封印、
    /// エネミーが受けるダメージ3倍（プレイヤーが押し切る窓は残す）。</summary>
    public class Berserk : IPassiveSkillEffect
    {
        public const int EnrageTurn = 50;
        public const int DiceBonus = 10;
        public const float EnemyDamageTakenMult = 3f;

        public string SkillId => "Berserk";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.currentTurn <= EnrageTurn) return;

            // 回復封印・敵被ダメ倍化は毎ターン再適用 (BeginNewTurn でリセットされるため)
            ctx.healBlocked = true;
            ctx.enemyDamageTakenMultiplier = EnemyDamageTakenMult;

            // ダイス合計+10 は一度だけ積む (enemyDiceTotalBonus は累積でリセットされない)
            if (ctx.GetAccumulated("berserk_dice_applied") == 0)
            {
                ctx.enemyDiceTotalBonus += DiceBonus;
                ctx.accumulatedValues["berserk_dice_applied"] = 1;
                UnityEngine.Debug.Log($"[Berserk] 狂暴化発動 (T{ctx.currentTurn}): 敵ダイス+{DiceBonus}, 回復封印, 敵被ダメ×{EnemyDamageTakenMult}");
            }
        }
    }

    /// <summary>天衣無縫: ボスがロール勝利するたび、
    /// プレイヤーが以降獲得する回復量・シールド量を-1（上限10スタック）。
    /// 長期戦＝タンク戦術を覚者連戦を通じて逓減させる。敵視点 OnRollWin = 覚者がロール勝利。
    /// ※上限20は7形態1020HPの長期戦で持続力を完全枯渇させ0%クリアの主因だったため10へ緩和。</summary>
    public class FlawlessRobe : IPassiveSkillEffect
    {
        public const int MaxStacks = 10;
        public string SkillId => "FlawlessRobe";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // ボス難易度オートチューナー: 天衣無縫の上限スタックを直接調整 (低い=回復封じ弱化=ジリ貧緩和)
            int maxStacks = UnityEngine.Mathf.Max(1, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.RobeStacks));
            if (ctx.healShieldReduction >= maxStacks) return;
            ctx.healShieldReduction++;
            UnityEngine.Debug.Log($"[天衣無縫] 回復/シールド減衰 +1 → -{ctx.healShieldReduction}(上限{maxStacks})");
        }
    }

    // [廃止 2026-09-14] 旧 SinAltar 儀式 (血/貪欲/遺品) の敵専用パッシブ 3 種
    //   ── Boss6Golgotha / Boss6SeveredTime / Boss6Ashen。
    //   〈門〉リワークで罰はすべてプレイヤー側の規則になった (GameLoop.GateFlaws)。

    // ============================================================
    //  13番目の死: 13番目の宣告
    //  毎ターン開始時に 13% で宣告フラグを立て、
    //  ターン終了時に本体が生存していれば、両者ダイス合計の会心ダメージを
    //  プレイヤーに与える（軽減無効）。
    // ============================================================

    // ============================================================
    //  各層ボス専用パッシブ
    // ============================================================

    /// <summary>ゴブリン王 — 号令: 毎ターン、自身のダイス合計+2</summary>
    public class GoblinKingsCall : IPassiveSkillEffect
    {
        public string SkillId => "GoblinKingsCall";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点なので playerDiceTotal = ボス自身のダイス合計。 加算量はオートチューナーで調整 (基準3)。
            ctx.playerDiceTotal += UnityEngine.Mathf.Max(0, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.GoblinCall));
        }
    }

    /// <summary>凍れる吟遊詩人 — 凍えの旋律: 連続未使用ターン経過で敵与ダメ+1ずつ蓄積（上限+5）</summary>
    public class FrozenBardSong : IPassiveSkillEffect
    {
        public string SkillId => "FrozenBardSong";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnPostRoll,
        };
        private const string Key = "frozen_unused_streak";

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                ctx.accumulatedValues.TryGetValue(Key, out var streak);
                ctx.accumulatedValues[Key] = streak + 1f;
            }
            else if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                ctx.accumulatedValues.TryGetValue(Key, out var streak);
                int bonus = UnityEngine.Mathf.Min(8, UnityEngine.Mathf.Max(0, (int)streak - 1));
                if (bonus > 0) ctx.playerDiceTotal += bonus; // 敵視点で自分のダイス合計加算
            }
        }
        public static void ResetStreak(CombatContext ctx)
        {
            if (ctx == null) return;
            ctx.accumulatedValues[Key] = 0f;
        }
    }

    /// <summary>毒沼の主 — 毒の侵蝕: 毎ターン終了時に毒スタック+1（上限5）、
    /// その後スタック分の固定ダメ（軽減無視）。長期戦ほど加速度的に蝕む。
    /// T1:1 T2:2 … T5:5 で頭打ち。累計はT5までで15＝速攻なら軽傷、長引けば致命。</summary>
    public class MiasmaCorrosion : IPassiveSkillEffect
    {
        public string SkillId => "MiasmaCorrosion";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        private const string Key = "miasma_poison_stack";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // ボス難易度オートチューナー: 毒スタック上限を直接調整 (低い=DOT緩和)。 基準5
            int cap = UnityEngine.Mathf.Max(1, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.PoisonStackCap));
            ctx.accumulatedValues.TryGetValue(Key, out var stack);
            int s = System.Math.Min(cap, (int)stack + 1);
            ctx.accumulatedValues[Key] = s;
            ctx.EnemyDealUnmitigable(s, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
            UnityEngine.Debug.Log($"[毒の侵蝕] 毒×{s} ダメ (上限{cap}, 軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>鏡の双子 — 鏡映の応答: 10以上ダメを与えたターンの次ターン開始時に同値反射</summary>
    public class MirrorTwinsResponse : IPassiveSkillEffect
    {
        /// <summary>計装: 被弾イベント数 / そのうち反射が成立した数 / 反射の合計ダメージ。
        /// **閾値を勘で置き直さないため**に置いている ── 5% 版は 1 度も発火せず、
        /// 9,000 ラン 回して結果が 1 バイトも変わらないという形でしか気づけなかった。
        /// AutoRunner がバッチ頭で Reset し、 サマリへ 1 戦あたりの発火数を出す。</summary>
        public static long Hits, Fires, ReflectDamage;
        /// <summary>1 ヒットのダメージ分布 (最大HP に対する % で 1% 刻み・0..49、 50 以上は末尾)。
        /// **閾値を percentile で置くために要る** ── 平均だけでは分位が分からず、
        /// 5%→発火0 / 10%→発火87% と両極に振れた (2026-09-09 に 2 回外した)。</summary>
        public static readonly long[] HitPctHist = new long[51];
        public static void ResetStats()
        {
            Hits = Fires = ReflectDamage = 0;
            System.Array.Clear(HitPctHist, 0, HitPctHist.Length);
        }

        /// <summary>累積分布から「発火率 target% になる閾値(最大HP比%)」を引く。</summary>
        public static string DescribePercentiles()
        {
            long tot = 0;
            foreach (var v in HitPctHist) tot += v;
            if (tot == 0) return "ヒット記録なし";
            var sb = new System.Text.StringBuilder();
            long cum = 0; int[] want = { 10, 20, 30, 50 }; int wi = 0;
            for (int p = 0; p < HitPctHist.Length && wi < want.Length; p++)
            {
                cum += HitPctHist[p];
                while (wi < want.Length && 100.0 * cum / tot >= want[wi])
                { sb.Append($"P{want[wi]}={p}% "); wi++; }
            }
            return sb.ToString().TrimEnd();
        }

        public string SkillId => "MirrorTwinsResponse";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnPostReceiveDamage, PassiveSkillTrigger.OnTurnStart,
        };
        private const string Key = "mirror_pending";

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostReceiveDamage)
            {
                // 敵視点で「自分が被ダメ受けた」 → finalDamage が受けた量。
                // 反転: 弱い小突きほど反射、 閾値以上の高火力は反射0＝鏡を叩き割る。
                //
                // **閾値は絶対値ではなくボス最大HPの割合 (2026-09-09 修正)。**
                //   旧: `const int Thr = 12`。 2026-08-10 の経済リスケール以降、 プレイヤーは
                //   4層ボス戦で 1 ターン約 190 出すので **12 未満は永久に成立せず、 この固有スキルは
                //   完全に不発だった**。 結果 4層ボスは実質パッシブ 1 つ (IntimidatePlus) だけになり、
                //   被ダメ 1.35/ターン ── 3層ボス 3.38・5層ボス 2.87 の半分以下で、
                //   実測の脅威が 1層ボス(41.7%HP) を大きく下回る 20.2%HP まで落ちていた。
                //   割合にしておけば、 今後ダメージ規模が動いても意図 (小突きを罰する) が保たれる。
                //   敵視点では ctx.playerMaxHP がボス自身の最大HP。
                //
                //   **5% では 1 度も発火しなかった** (2026-09-09 実測: 9,000 ラン で結果が
                //   1 バイトも変わらなかった)。 4層ボスは HP 2400 を 12.3 ターンで削られるので
                //   1 ターン平均 191、 ロールに勝ったターンだけに絞ると 1 ヒット 300 前後。
                //   閾値 120 では届かない。 **maxHP/10 = 「12 ターンで削り切るペースを下回るヒット」**
                //   **閾値は分位から選ぶ。 平均から推定すると外す。** 実測の分位 (2026-09-09):
                //     P10=2% P20=2% P30=3% P50=5%   ← 1 ヒットのボス最大HP比
                //   平均は 8% 付近だが分布は右に裾を引いていて、 中央値は 5%。 そのため
                //     5%  → 発火  0% (旧トリガー配線では死亡ターンのみ・実質不発)
                //     10% → 発火 87% / 被ダメ 77.0%HP (5層ボス 56.7% を追い越す)
                //     7%  → 発火 70% / 被ダメ 65.6%HP
                //   と両極に振れた。 **4% ＝ 発火 35〜40%** が狙い (被ダメ 45〜50%HP =
                //   3層ボス 41.9 と 5層ボス 56.7 の間)。 次に動かすときも P30/P50 を見ること。
                int Thr = UnityEngine.Mathf.Max(12, ctx.playerMaxHP * 4 / 100);
                // ボス難易度オートチューナー: 反射ダメ上限を直接調整 (低い=反射事故の緩和)。 基準9
                int reflectCap = UnityEngine.Mathf.Max(0, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.MirrorReflectCap));
                int dmg = ctx.finalDamage;
                if (dmg > 0)
                {
                    Hits++;
                    if (ctx.playerMaxHP > 0)
                    {
                        int bucket = (int)(100L * dmg / ctx.playerMaxHP);   // 敵視点: playerMaxHP = ボス最大HP
                        HitPctHist[UnityEngine.Mathf.Clamp(bucket, 0, HitPctHist.Length - 1)]++;
                    }
                }
                if (dmg > 0 && dmg < Thr && reflectCap > 0)
                {
                    int reflect = System.Math.Min(reflectCap, Thr - dmg);
                    Fires++; ReflectDamage += reflect;
                    ctx.accumulatedValues[Key] = reflect;
                    UnityEngine.Debug.Log($"[鏡映の応答] 蓄積: 次ターン{reflect}反射 (与ダメ{dmg}<{Thr}＝小突きを罰す, 上限{reflectCap})");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                if (!ctx.accumulatedValues.TryGetValue(Key, out var pending) || pending <= 0f) return;
                int dmg = (int)pending;
                ctx.EnemyDealUnmitigable(dmg, DeathCause.Reflect);   // 軽減は無視・シールドは肩代わりする
                ctx.accumulatedValues[Key] = 0f;
                UnityEngine.Debug.Log($"[鏡映の応答] 反射 {dmg}ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");
            }
        }
    }

    /// <summary>業火の審判官 — 審判の炎: 毎ターン終了時の確定ダメ（軽減無視）。
    /// = 1 + **経過ターン÷2(切上)** + 罪。罪 = ラン中の総戦闘回数/8（上限2）。総ダメ上限は ChipCap。
    /// 「速攻」かつ「無駄な戦闘を避けた」者ほど有利。
    ///
    /// 2026-07-28: ターン係数を **1/2** に緩和。 旧 `1+経過T+罪` は 5.3T の平均戦闘で
    /// 累計 30 前後の**軽減不可**が確定し、 配線・ブロックで一切対処できないまま
    /// 5層勝率 45% / 主死因 Chip 98% の壁になっていた。 軽減不可の継続ダメは
    /// ADR-0009 の「被弾を効率よく受けるレース判断」(§6-2) を無効化するため、 総量を抑える。</summary>
    public class JudgmentFlames : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentFlames";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int sin = System.Math.Min(2, (run?.totalBattles ?? 0) / 8); // 罪: プレイで操作可能
            // ボス難易度オートチューナー: 審判の炎の総ダメ上限を直接調整 (低い=不可避チップ緩和)。 基準13
            int cap = AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.ChipCap);
            // 2026-08-15: ターン係数を 1/2 → **1/3**。 出現漏れ (怪しい商人) と裏ボス遮断漏れを
            //   直した土俵で測り直したところ、 この敵が単独の主死因 (致命 276 / 主死因 Chip 97%) だった。
            //   平均 14.7 ターンで累積 108 ＝ プレイヤー最大HP (93〜99) を上回り、
            //   **通常攻撃を全て捌いてもチップだけで死ぬ**規模になっていた。 1/3 で 15T 累積 90。
            //   上限 (ChipCap 10) に当たるのが T≥19 になるので、 実質ターン係数だけで決まる。
            int turnTerm = UnityEngine.Mathf.CeilToInt(System.Math.Max(1, ctx.currentTurn) / 3f);
            int dmg = System.Math.Min(cap, 1 + turnTerm + sin);
            ctx.EnemyDealUnmitigable(dmg, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
            UnityEngine.Debug.Log($"[審判の炎] {dmg}ダメ (1+経過T{ctx.currentTurn}+罪{sin}, 上限{cap}, 軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>灰燼の王 — 王の業炎: 灰の烙印スタックで毎ターン開始時固定ダメ</summary>
    public class RoyalEmber : IPassiveSkillEffect
    {
        public string SkillId => "RoyalEmber";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnTurnStart,
        };
        private const string Key = "ash_brand_stack";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                ctx.accumulatedValues.TryGetValue(Key, out var stack);
                int dmg = (int)stack;
                if (dmg > 0)
                {
                    ctx.EnemyDealUnmitigable(dmg, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
                    UnityEngine.Debug.Log($"[王の業炎] 灰の烙印×{dmg}ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                ctx.accumulatedValues.TryGetValue(Key, out var stack);
                ctx.accumulatedValues[Key] = stack + 1f;
            }
        }
    }

    /// <summary>灰燼の王 — 烈炎 (2026-08-04): **亀戦法への罰**。
    ///   ・ブロック端子へ <see cref="BlockDiceThreshold"/> 本以上配線したターン終了時、 烈炎スタック +1
    ///   ・ターン開始時、 スタック分の軽減無視ダメージ
    ///
    /// 導入の経緯: 6 層ボス戦は防御配線率 95%・遮断率 59% で「守れば止まる」が完全に成立しており、
    /// 層別死亡率 9.2% (目標 50%) と通過儀礼になっていた。 攻撃力や HP を上げても、
    /// ブロックは定額減算で出力上限 (~17) があるだけで、 その上限内に収まる限り無害なまま。
    /// 数値ではなく **「守ると損をする」構造** を足すことで、 速攻と耐久の二択を立てる。
    ///
    /// スタックは戦闘通して保持 (accumulatedValues。 RoyalEmber の灰の烙印と同じ扱い)。
    /// テレグラフ (MutualTurnTelegraph.blazeStacks / blazePenalizesBlock) で配線前に開示するので、
    /// 「知らないうちに焼かれる」にはならない。</summary>
    public class BlazeBrand : IPassiveSkillEffect
    {
        public string SkillId => "BlazeBrand";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnTurnEnd,
        };
        /// <summary>この本数以上をブロック端子へ配線するとスタックが増える。</summary>
        public const int BlockDiceThreshold = 2;
        public const string StackKey = "blaze_stack";
        public const string ActiveKey = "blaze_active";
        /// <summary>配線本数の参照先 (CombatManager が毎ターン書き込む)。</summary>
        private const string BlockCountKey = "mutualBlockDiceCount";

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // テレグラフ生成は OnTurnStart の後なので、 ここで所持フラグを立てておく。
                ctx.accumulatedValues[ActiveKey] = 1f;

                ctx.accumulatedValues.TryGetValue(StackKey, out var stack);
                int dmg = (int)stack;
                if (dmg > 0)
                {
                    ctx.EnemyDealUnmitigable(dmg, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
                    UnityEngine.Debug.Log($"[烈炎] ×{dmg} ダメ (軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                ctx.accumulatedValues.TryGetValue(BlockCountKey, out var blk);
                if ((int)blk >= BlockDiceThreshold)
                {
                    ctx.accumulatedValues.TryGetValue(StackKey, out var stack);
                    ctx.accumulatedValues[StackKey] = stack + 1f;
                    UnityEngine.Debug.Log($"[烈炎] ブロック{(int)blk}本 → スタック {(int)stack}→{(int)stack + 1}");
                }
            }
        }
    }

    /// <summary>灰燼の王 — 業の連鎖: ロール勝利累積で敵与ダメ+1ずつ（上限+5、敗北でリセット）</summary>
    public class SinChain : IPassiveSkillEffect
    {
        public string SkillId => "SinChain";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPostRoll,
        };
        private const string Key = "sin_chain_count";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点: OnRollLose = 敵がロール負け = プレイヤーがロール勝ち
            if (trigger == PassiveSkillTrigger.OnRollLose)
            {
                ctx.accumulatedValues.TryGetValue(Key, out var c);
                ctx.accumulatedValues[Key] = UnityEngine.Mathf.Min(5f, c + 1f);
            }
            else if (trigger == PassiveSkillTrigger.OnRollWin)
            {
                // 敵がロール勝ち = プレイヤー敗北 = リセット
                ctx.accumulatedValues[Key] = 0f;
            }
            else if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                ctx.accumulatedValues.TryGetValue(Key, out var c);
                int bonus = (int)c;
                if (bonus > 0) ctx.playerDiceTotal += bonus; // 敵視点で自分のダイス加算
            }
        }
    }

    /// <summary>灰燼の王 — 永劫の燃焼: プレイヤーHP割合で敵与ダメ加算</summary>
    public class EternalBurning : IPassiveSkillEffect
    {
        public string SkillId => "EternalBurning";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点で enemyCurrentHP / enemyMaxHP = 実プレイヤーHP割合
            if (ctx.enemyMaxHP <= 0) return;
            float ratio = (float)ctx.enemyCurrentHP / ctx.enemyMaxHP;
            int bonus = 0;
            if (ratio <= 0.10f) bonus = 5;
            else if (ratio <= 0.25f) bonus = 3;
            else if (ratio <= 0.50f) bonus = 2;
            if (bonus > 0) ctx.playerDiceTotal += bonus;
        }
    }

    /// <summary>灰燼の王 — 灰燼への回帰: HP50%以下なら毎ターン開始時 max HPの5% 回復</summary>
    public class ReturnToAshes : IPassiveSkillEffect
    {
        public string SkillId => "ReturnToAshes";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点で playerCurrentHP / playerMaxHP = 自身（ボス）のHP
            if (ctx.playerMaxHP <= 0) return;
            if (ctx.playerCurrentHP * 2 > ctx.playerMaxHP) return; // 50%超なら何もしない
            int heal = ctx.ReduceEnemyHeal(UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.05f));
            int oldHP = ctx.playerCurrentHP;
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
            UnityEngine.Debug.Log($"[灰燼への回帰] +{heal} ({oldHP}→{ctx.playerCurrentHP})");
        }
    }

    // ============================================================
    //  灰燼の王 リワーク（見切り＆カウンター型）
    //  体感: 致命的な大技を最適行動(ロール勝利)で間一髪回避しつつ、
    //  一撃ずつ削る。敵視点: playerCurrentHP/Max=ボス自身HP /
    //  enemyCurrentHP=実プレイヤー / playerWonRoll=ボスがロール勝利。
    // ============================================================

    /// <summary>断罪周期はボスHP割合で短縮: >60%→3T / ≤60%→2T / ≤30%→毎T。
    /// 断罪ターン = currentTurn % 周期 == 0。</summary>
    internal static class EmberKing
    {
        public static int Period(CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return 3;
            float r = (float)ctx.playerCurrentHP / ctx.playerMaxHP;
            return r <= 0.30f ? 1 : (r <= 0.60f ? 2 : 3);
        }
        public static bool IsJudgment(CombatContext ctx)
        {
            int p = Period(ctx);
            return ctx.currentTurn > 0 && p > 0 && ctx.currentTurn % p == 0;
        }
    }

    /// <summary>業火の断罪 — 断罪ターンを予告（灰の予兆を統合）。
    /// 断罪ターンにボスがロール勝利すると、 ボス最終ダメージ ×8+8 の致命ダメ
    /// （軽減/シールド有効経路で適用）。 プレイヤーがロール勝利＝間一髪回避し、
    /// 鎧貫通の確定反撃18をボスへ。</summary>
    public class JudgmentBlaze : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentBlaze";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreDealDamage,
            PassiveSkillTrigger.OnRollLose,
            PassiveSkillTrigger.OnTurnEnd };
        private const string CtrKey = "ek_counter";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    // 灰の予兆統合: 断罪ターンを予告（読める＝運でなく対応の問題）
                    if (EmberKing.IsJudgment(ctx))
                    {
                        // ボス難易度オートチューナー: 断罪Tの敵ダイス上乗せを直接調整
                        // (低い=見切りロールに勝ちやすく=易化)。 基準+10。
                        int jdice = AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.JudgmentDice);
                        ctx.enemyDiceTotalBonus += jdice; // 断罪Tは敵ダイス上乗せ (見切り難度)
                        // 断罪Tの累積回数 (この戦闘で何回目か) をカウント
                        int n = (int)ctx.GetAccumulated("judg_counter") + 1;
                        ctx.accumulatedValues["judg_counter"] = n;
                        UnityEngine.Debug.Log($"[灰の予兆] 業火の断罪 第{n}回（T{ctx.currentTurn}・周期{EmberKing.Period(ctx)}）—敵ダイス+{jdice}、ロール勝利で間一髪回避");
                    }
                    break;

                case PassiveSkillTrigger.OnPreDealDamage:
                    // 敵視点: playerWonRoll = ボスがロール勝利 = プレイヤー敗北。
                    // 断罪Tに発動 → 係数は 1回目=4、 以降+2ずつ (2回目=6、 3回目=8、 4回目=10...)。
                    // ctx.finalDamage = base × coef + coef （軽減/シールド有効経路）。
                    if (ctx.playerWonRoll && EmberKing.IsJudgment(ctx))
                    {
                        int n = (int)ctx.GetAccumulated("judg_counter");
                        if (n < 1) n = 1;
                        // ボス難易度オートチューナー: 断罪係数の基礎を直接調整
                        // (低い=即死性が下がり、 断罪ロールに負けても耐えられる=易化)。 係数 = base + 2n、 基準base=2。
                        int coefBase = AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.JudgmentCoefBase);
                        int coef = UnityEngine.Mathf.Max(1, coefBase + 2 * n); // 基準1回目=4、2回目=6...
                        int before = ctx.finalDamage;
                        ctx.finalDamage = ctx.finalDamage * coef + coef;
                        ctx.lastDamageCause = DeathCause.Judgment; // 死因タグ
                        UnityEngine.Debug.Log($"[業火の断罪] 致命の一撃 第{n}回 {before}→{ctx.finalDamage}（×{coef}+{coef}）");
                    }
                    break;
                case PassiveSkillTrigger.OnRollLose:
                    // ボスがロール敗北＝プレイヤーが見切った
                    if (EmberKing.IsJudgment(ctx))
                        ctx.accumulatedValues[CtrKey] = 18; // 鎧貫通カウンター予約（火力強化）
                    break;
                case PassiveSkillTrigger.OnTurnEnd:
                    if (ctx.accumulatedValues.TryGetValue(CtrKey, out var c) && c > 0f)
                    {
                        int dmg = (int)c;
                        ctx.playerCurrentHP = System.Math.Max(0, ctx.playerCurrentHP - dmg);
                        ctx.accumulatedValues[CtrKey] = 0f;
                        UnityEngine.Debug.Log($"[業火の断罪] 間一髪回避→反撃 {dmg} → 灰燼残HP={ctx.playerCurrentHP}");
                    }
                    break;
            }
        }
    }

    /// <summary>灰塵の威圧 — 常時ダイス合計+3。 HP60%/30% 閾値を跨ぐ被弾は、
    /// そのターン中 閾値+1HP で止まる（一度割れば次から効果なし）。
    /// ImmortalEmber の B60/B30 ラチェットと連動。</summary>
    public class EmberAura : IPassiveSkillEffect
    {
        public string SkillId => "EmberAura";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    ctx.enemyDiceTotalBonus += 3; // 常時 +3 (固定。 ボスダイス調整は base dice の期待値側で行う)
                    break;
                case PassiveSkillTrigger.OnPreReceiveDamage:
                    // 敵視点: playerCurrentHP = ボス自身のHP、 ctx.finalDamage = 被ダメ予定
                    if (ctx.playerMaxHP <= 0 || ctx.finalDamage <= 0) return;
                    int cur = ctx.playerCurrentHP;
                    int dmg = ctx.finalDamage;
                    float maxHP = ctx.playerMaxHP;
                    int t60 = UnityEngine.Mathf.CeilToInt(maxHP * 0.60f);
                    int t30 = UnityEngine.Mathf.CeilToInt(maxHP * 0.30f);

                    // 60% 閾値ガード: 1戦闘1回のみ。被弾後60%以下になる一撃を 閾値+1HP で止める
                    if (cur > t60 && cur - dmg <= t60
                        && ctx.GetAccumulated("ember_guard_60_used") == 0f)
                    {
                        int allowed = cur - (t60 + 1);
                        if (allowed < 0) allowed = 0;
                        ctx.finalDamage = allowed;
                        ctx.accumulatedValues["ember_guard_60_used"] = 1f;
                        UnityEngine.Debug.Log($"[灰塵の威圧] 60%閾値ガード: {dmg}→{allowed} (HP{cur}→{t60 + 1})");
                        return;
                    }
                    // 30% 閾値ガード: 1戦闘1回のみ
                    if (cur > t30 && cur - dmg <= t30
                        && ctx.GetAccumulated("ember_guard_30_used") == 0f)
                    {
                        int allowed = cur - (t30 + 1);
                        if (allowed < 0) allowed = 0;
                        ctx.finalDamage = allowed;
                        ctx.accumulatedValues["ember_guard_30_used"] = 1f;
                        UnityEngine.Debug.Log($"[灰塵の威圧] 30%閾値ガード: {dmg}→{allowed} (HP{cur}→{t30 + 1})");
                    }
                    break;
            }
        }
    }

    /// <summary>灰塵の鎧 — 被弾時、受けるダメージ-5。軽減後が10超なら10に丸める
    /// （通常打は「なんとか一撃」）。断罪ターンは鎧無効＝削りの本命窓。</summary>
    public class AshArmor : IPassiveSkillEffect
    {
        public string SkillId => "AshArmor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 断罪ターンは鎧無効（ロール勝利の差分ダメが素通り＝削りの本命）
            if (EmberKing.IsJudgment(ctx)) return;

            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                // ボス難易度オートチューナー: 灰塵の鎧の軽減量を直接調整 (低い=タンク性弱化=ジリ貧緩和)。 基準-9
                int reduction = AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.RegenReduction);
                const int cap = 10; // 軽減後キャップ (固定)
                int reduced = System.Math.Max(0, ctx.finalDamage - reduction);
                ctx.finalDamage = System.Math.Min(reduced, cap);
            }
        }
    }

    /// <summary>不滅の残り火 — 失ったHP(最大-現在)の一定%を毎ターン回復（最低1）。
    /// >60%:0% / ≤60%:3% / ≤30%:6%。断罪ターンは回復なし。
    /// 一度割った 60%/30% ラインより上には二度と戻れない（ラチェット）。
    /// 断罪周期短縮(≤60%→2T/≤30%→毎T)は EmberKing.Period が自動反映。</summary>
    public class ImmortalEmber : IPassiveSkillEffect
    {
        public string SkillId => "ImmortalEmber";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        private const string B60 = "ash_below60";
        private const string B30 = "ash_below30";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;

            // 敵視点: playerCurrentHP/playerMaxHP = ボス自身のHP
            float maxHP = ctx.playerMaxHP;
            int cur = ctx.playerCurrentHP;
            float r = cur / maxHP;

            // ラチェット閾値の踏破を記録（断罪ターンでも更新する）
            if (r <= 0.60f) ctx.accumulatedValues[B60] = 1f;
            if (r <= 0.30f) ctx.accumulatedValues[B30] = 1f;
            bool below60 = ctx.GetAccumulated(B60) > 0f;
            bool below30 = ctx.GetAccumulated(B30) > 0f;

            // 一度割ったラインが回復上限（30%踏破→30%、60%踏破→60%、未踏破→満タン）
            int cap = below30 ? UnityEngine.Mathf.FloorToInt(maxHP * 0.30f)
                    : below60 ? UnityEngine.Mathf.FloorToInt(maxHP * 0.60f)
                    : ctx.playerMaxHP;

            // 断罪ターンは回復なし（ラチェット記録のみ済ませて終了）
            if (EmberKing.IsJudgment(ctx)) return;

            int missing = ctx.playerMaxHP - cur;
            if (missing <= 0) return;

            float pct = below30 ? 0.08f : below60 ? 0.05f : 0f; // 2026-05-31: 3%/6% → 5%/8% (長期戦化耐性)
            if (pct <= 0f) return; // >60%: 再生なし

            int heal = ctx.ReduceEnemyHeal(System.Math.Max(1, UnityEngine.Mathf.FloorToInt(missing * pct)));
            int target = System.Math.Min(cap, cur + heal);
            if (target <= cur) return;

            ctx.playerCurrentHP = target;
            UnityEngine.Debug.Log($"[不滅の残り火] +{target - cur} ({cur}→{target}/{ctx.playerMaxHP}) 上限{cap} 周期{EmberKing.Period(ctx)}");
        }
    }

    /// <summary>星火燎原 — ボスがロール敗北するたびボスのダイス合計に +1（無限累積・リセットなし）。
    /// 粘って勝ち続けるほどボスが確実に追い付き、いずれ断罪を刺して決着する膠着解消クロック。
    /// 加算は ProcessPostRoll の勝敗判定前に enemyDiceTotalBonus 経由で反映（次ロール以降）。</summary>
    public class StarfireProliferation : IPassiveSkillEffect
    {
        public string SkillId => "StarfireProliferation";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };
        private const string Key = "starfire_stack";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点: OnRollLose = ボスがロール敗北したターン
            int stack = (int)ctx.GetAccumulated(Key) + 1;
            ctx.accumulatedValues[Key] = stack;
            const int cap = 20; // 累積上限 (固定)。 ボスダイス調整は base dice の期待値側で行う
            ctx.enemyDiceTotalBonus = System.Math.Min(stack, cap); // 次ロール以降、勝敗判定前にボス合計へ
            UnityEngine.Debug.Log($"[星火燎原] ボス敗北 → ダイス合計補正 累計+{System.Math.Min(stack, cap)}(上限{cap})");
        }
    }

    /// <summary>強者 — このボスはダイス合計に+4の威風を持つ（5層ボス）。OnBattleStartで設定。</summary>
    public class StrongOne : IPassiveSkillEffect
    {
        public string SkillId => "StrongOne";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.bossDiceBonus = 4; }
    }

    /// <summary>玉座 — このボスはダイス合計に+8の威風を持つ（6層ボス）。OnBattleStartで設定。</summary>
    public class Throne : IPassiveSkillEffect
    {
        public string SkillId => "Throne";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.bossDiceBonus = 8; }
    }

    /// <summary>刹那 — このボスはダイス合計に+12の威風を持つ（7層ボス・覚者連戦は全形態に再付与）。OnBattleStartで設定。</summary>
    public class Setsuna : IPassiveSkillEffect
    {
        public string SkillId => "Setsuna";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.bossDiceBonus = 12; }
    }

    /// <summary>焦土 — プレイヤーがロール敗北するたび、そのターンの被ダメの10%だけ
    /// プレイヤーの最大HPを削り、シールドを完全破壊する。
    /// 重い一撃を受けるほど地盤が焼け落ちる。消耗戦の膠着を許さない。
    /// 敵視点: OnRollWin = ボスがロール勝利 = プレイヤー敗北。
    /// enemyMaxHP/enemyCurrentHP = 実プレイヤーの最大/現在HP（OnTurnEnd で適用＝SyncHPで反映）。</summary>
    public class ScorchedEarth : IPassiveSkillEffect
    {
        public const float MaxHpLossRatio = 0.10f; // 被ダメの10%を最大HPから削る
        public string SkillId => "ScorchedEarth";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // このターンにプレイヤーが敗北した場合のみ（敵視点 playerWonRoll = ボス勝利）
            if (!ctx.playerWonRoll) return;

            int loss = UnityEngine.Mathf.FloorToInt(ctx.playerDamageThisTurn * MaxHpLossRatio);
            int beforeMax = ctx.enemyMaxHP;
            if (loss > 0)
            {
                ctx.SetEnemyMaxHP(System.Math.Max(1, ctx.enemyMaxHP - loss));
                if (ctx.enemyCurrentHP > ctx.enemyMaxHP) ctx.enemyCurrentHP = ctx.enemyMaxHP;
            }

            bool hadShield = ctx.consShield > 0;
            ctx.consShield = 0; // シールド完全破壊

            if (loss > 0 || hadShield)
                UnityEngine.Debug.Log($"[焦土] 被ダメ{ctx.playerDamageThisTurn}→最大HP-{loss} ({beforeMax}→{ctx.enemyMaxHP})"
                    + (hadShield ? " ＋シールド破壊" : ""));
        }
    }

    // ================================================================
    //  6層 灰燼の王 リワーク (ADR-0009 相互攻撃モデル対応・2026-07-16)
    //  旧 JudgmentBlaze/AshArmor/ImmortalEmber/EmberAura/StarfireProliferation/
    //  ScorchedEarth は OnPreDealDamage 依存で相互攻撃パイプ下では発火経路が壊れていた。
    //  以下 5 パッシブで再構築: OnTurnStart / OnPreReceiveDamage / OnRollWin に集約。
    // ================================================================

    /// <summary>予兆ターン判定 (2026-07-16 二相化): Phase1 (HP>50%) は周期4T (T1/5/9/13...)、
    /// Phase2 (HP≤50%) は周期2T (T1/3/5/7...) に加速。</summary>
    internal static class EmberOmenTiming
    {
        public static int PeriodFor(float hpRatio) => hpRatio <= 0.5f ? 3 : 4;
        public static bool IsOmen(int turn, float hpRatio)
        {
            if (turn < 1) return false;
            int p = PeriodFor(hpRatio);
            return ((turn - 1) % p) == 0;
        }
        public static int NextOmen(int turn, float hpRatio)
        {
            int p = PeriodFor(hpRatio);
            int rel = (turn - 1) % p;
            return turn + (rel == 0 ? p : (p - rel));
        }
    }

    /// <summary>灰塵の外殻 — 被ダメが15を超えた分を半減 (例: 30→22, 50→32)。
    /// 大玉より小玉連打が有利という設計信号。 会心一撃を丸ごと通さず、 かつ通常火力は素通し。</summary>
    public class AshCarapace : IPassiveSkillEffect
    {
        public const int Threshold = 15;
        public string SkillId => "AshCarapace";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= Threshold) return;
            int excess = ctx.finalDamage - Threshold;
            int reduced = Threshold + excess / 2;
            UnityEngine.Debug.Log($"[灰塵の外殻] 被ダメ {ctx.finalDamage}→{reduced} ({Threshold}超は半減)");
            ctx.finalDamage = reduced;
        }
    }

    /// <summary>業火の予兆 — T1/5/9/13... の周期4Tで発動 (BossTuning.JudgmentDice 基準+15)。
    /// 予兆ターンは敵ダイス合計を上乗せ。 予告が前ターンに出るので配線で対策可能。
    /// 旧 JudgmentBlaze の即死一撃を「相互攻撃モデルで対応可能な予告火力」に転換。</summary>
    public class EmberOmen : IPassiveSkillEffect
    {
        public string SkillId => "EmberOmen";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int bonus = AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.JudgmentDice);
            float hpRatio = ctx.playerMaxHP > 0 ? (float)ctx.playerCurrentHP / ctx.playerMaxHP : 1f;
            // [計装] 発火したかではなく、 **何を計算したか**をそのまま残す。
            CombatSystem.BossCombatTrace.NoteEvent("omen",
                $"turn={ctx.currentTurn} bossId='{ctx.bossId}' bonus={bonus}"
                + $" ratio={hpRatio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
                + $" period={EmberOmenTiming.PeriodFor(hpRatio)}"
                + $" fired={EmberOmenTiming.IsOmen(ctx.currentTurn, hpRatio)}");
            if (EmberOmenTiming.IsOmen(ctx.currentTurn, hpRatio))
            {
                ctx.enemyDiceTotalBonus += bonus;
                UnityEngine.Debug.Log($"[業火の予兆] T{ctx.currentTurn} 発動 → 敵ダイス+{bonus} (周期{EmberOmenTiming.PeriodFor(hpRatio)}T)");
            }
            else
            {
                int next = EmberOmenTiming.NextOmen(ctx.currentTurn, hpRatio);
                UnityEngine.Debug.Log($"[業火の予兆] T{ctx.currentTurn} 予告: 次はT{next} (周期{EmberOmenTiming.PeriodFor(hpRatio)}T)");
            }
        }
    }

    /// <summary>灰の再生 (2026-07-16 二相化 / 2026-08-11 に 3% → **2%**) —
    /// HP>50% で毎T 最大HPの2%回復 (Phase1: 灰をまといながら耐える)。
    ///
    /// 5% の頃は HP1200 に対し 60/T の再生で、 これを上回る素ダメージが出ないビルドは
    /// **HP を 1 も削れない**二値の壁になっていた (6層勝率 5.7%)。 2% まで下げ、難度は攻撃側で担保する。
    /// HP≤50% で回復完全停止 (Phase2: 「決着期」に突入 = プレイヤーの削り勝ちが可視化)。
    /// 予兆ターンは回復なし = 集中砲火で HP50% ラインを越える窓。</summary>
    public class AshRegrowth : IPassiveSkillEffect
    {
        public string SkillId => "AshRegrowth";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;

            float r = (float)ctx.playerCurrentHP / ctx.playerMaxHP;
            if (r <= 0.5f) return; // Phase2: 再生停止

            if (EmberOmenTiming.IsOmen(ctx.currentTurn, r)) return; // 予兆Tは回復なし

            int heal = ctx.ReduceEnemyHeal(System.Math.Max(1,
                UnityEngine.Mathf.FloorToInt(ctx.playerMaxHP * 0.02f)));
            int after = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
            if (after > ctx.playerCurrentHP)
            {
                UnityEngine.Debug.Log($"[灰の再生] Phase1 +{after - ctx.playerCurrentHP} ({ctx.playerCurrentHP}→{after})");
                ctx.playerCurrentHP = after;
            }
        }
    }

    /// <summary>憤怒の残滓 (EmberFury) — 2026-07-16 追加。 HP≤50% で敵ダイス合計 +5 常時。
    /// Phase2 突入で「再生ゼロ・高火力連打」の後半戦を演出する。
    /// EmberOmen (周期2Tに加速) + AshRegrowth (停止) と合わせて瞬発力勝負を強制。</summary>
    public class EmberFury : IPassiveSkillEffect
    {
        public const int Bonus = 3;
        public string SkillId => "EmberFury";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;
            float r = (float)ctx.playerCurrentHP / ctx.playerMaxHP;
            if (r > 0.5f) return; // Phase1 は不発
            ctx.enemyDiceTotalBonus += Bonus;
            UnityEngine.Debug.Log($"[憤怒の残滓] Phase2 敵ダイス+{Bonus} (HP {ctx.playerCurrentHP}/{ctx.playerMaxHP})");
        }
    }

    /// <summary>焦土契約 — プレイヤーが収支マイナスで終わったターン、 次ターンから敵ダイス+2 (最大5スタック=+10)。
    /// 収支トリガー (敵視点 OnRollWin = プレイヤーが収支負け) で発火。 一度得たスタックは戦闘終了まで持続。
    /// 旧 ScorchedEarth の「被ダメ×10%を最大HP削り＋シールド全破壊」を廃止し、
    /// 「収支負けの累積が持続圧に変わる」相互攻撃ネイティブの脅威に。</summary>
    public class ScorchedPact : IPassiveSkillEffect
    {
        public const int StackCap = 5;
        /// <summary>1 スタックあたりの敵攻撃加算。 2026-07-28 に 2 → 1。
        /// β (ダイス寄与率 0.3) 撤去で、 この値がそのまま攻撃値へ届くようになったため。</summary>
        public const int AttackPerStack = 1;
        public string SkillId => "ScorchedPact";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnRollWin };
        private const string Key = "scorched_stack";
        /// <summary>前ターンに自分が enemyDiceTotalBonus へ積んだ量。 差分更新用。</summary>
        private const string KeyApplied = "scorched_applied";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    // enemyDiceTotalBonus は「累積・BeginNewTurn でリセットしない」フィールドなので、
                    // 毎ターン `+= stack*N` すると雪だるまになる (T5 で +20、 T10 で +45…)。
                    // JSON の「最大+X」も効かなくなっていた。
                    // 他パッシブ (業火の予兆/憤怒の残滓) の寄与を消さないよう `=` は使わず、
                    // **自分が前ターンに積んだ分との差分だけ**を足し引きする。
                    int stack = (int)ctx.GetAccumulated(Key);
                    int now   = stack * AttackPerStack;
                    int prev  = (int)ctx.GetAccumulated(KeyApplied);
                    if (now != prev)
                    {
                        ctx.enemyDiceTotalBonus += (now - prev);
                        ctx.accumulatedValues[KeyApplied] = now;
                    }
                    if (now > 0)
                        UnityEngine.Debug.Log($"[焦土契約] T{ctx.currentTurn} スタック{stack}/{StackCap} → 敵攻撃+{now} (累積ではなく現在値)");
                    break;
                case PassiveSkillTrigger.OnRollWin:
                    // 敵視点 OnRollWin = 収支トリガーで敵勝利 = プレイヤーの収支マイナス
                    int s = System.Math.Min(StackCap, (int)ctx.GetAccumulated(Key) + 1);
                    ctx.accumulatedValues[Key] = s;
                    UnityEngine.Debug.Log($"[焦土契約] プレイヤー収支負け → スタック={s}/{StackCap}");
                    break;
            }
        }
    }

    /// <summary>13番目の死神の宣告。即死級ではないが層を問わず事故率が固定で残る。</summary>
    public class Decree13th : IPassiveSkillEffect
    {
        public string SkillId => "Decree13th";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnTurnEnd,
        };

        // currentBuffs に格納するキー（同ターン内のみ有効）
        private const string FlagKey = "decree13th_armed";

        /// <summary>宣告ダメージの倍率。 **定数**であって、 プレイヤーの会心倍率ではない。
        /// 2.0 は <see cref="MetaProgression.MetaBuffApplicator.GetCriticalMultiplier"/> の
        /// 基準値と同じ ＝ 会心倍率を積んでいないプレイヤーに対する従来の実挙動を保つ値。</summary>
        private const float DecreeMultiplier = 2.0f;

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;

            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // 13% で宣告フラグを立てる
                if (GameLoop.GameRng.Value("enemy.decree13") < 0.13f)
                {
                    ctx.currentBuffs[FlagKey] = 1f;
                    UnityEngine.Debug.Log($"[Decree13th] 死神の声が響く...（T{ctx.currentTurn}）");
                }
                return;
            }

            if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                if (!ctx.currentBuffs.TryGetValue(FlagKey, out float armed) || armed <= 0f) return;

                // 敵視点のため playerCurrentHP = 13番目の死本体のHP
                if (ctx.playerCurrentHP <= 0)
                {
                    UnityEngine.Debug.Log("[Decree13th] 死神は既に倒れた。回避成功");
                    ctx.currentBuffs.Remove(FlagKey);
                    return;
                }

                int total = ctx.playerDiceTotal + ctx.enemyDiceTotal;
                // **プレイヤー側の ctx.criticalMultiplier を読んではいけない** (2026-08-08 修正)。
                //   敵パッシブの文脈では player/enemy の HP は入れ替わるが、
                //   criticalMultiplier は入れ替わらない単一フィールド ＝ プレイヤーの値。
                //   旧実装はこれを掛けていたため、 **プレイヤーが会心倍率を積むほど
                //   この軽減全バイパスの一撃が強くなる**という反転が起きていた。
                //   実測 (単軸スイープ 500ラン): 遺物〈会心倍率+135%〉で基準 2.0 → 4.7 になり、
                //   2層エリートのこの一撃が 2.35 倍化。 5層クリアが 53.6% → 32.8%、
                //   1〜3F の被ダメ/戦が 12.7 → 15.5 と全 16 軸で最悪になった。
                int dmg = UnityEngine.Mathf.CeilToInt(total * DecreeMultiplier);

                // 軽減フックは全バイパスするが、 シールドは肩代わりとして機能する (2026-08-15)
                ctx.EnemyDealUnmitigable(dmg, null);   // 計上は従来どおり無し
                UnityEngine.Debug.Log(
                    $"[Decree13th] 成就: ({ctx.playerDiceTotal}+{ctx.enemyDiceTotal})×{DecreeMultiplier:F1} = {dmg} ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");

                ctx.currentBuffs.Remove(FlagKey);
            }
        }
    }

    /// <summary>シュヴァリエ・サン=ジョリオラ — 形態転換: 二形態を循環する5層裏ボス。
    /// 【形態1 コントラタック】戦闘開始時 シールド100展開。自身は 0d0（プレイヤー必勝）。
    ///   被ダメージは全てシールド経由(ダイス合計分)、ロール勝利した瞬間プレイヤー自身が
    ///   ダイス合計+プリオリテ×2 の不可避反撃を受け、さらにシールドへ+25確定ダメ。
    ///   シールドが0になったターンは無敵(あふれた分を無効化)、次T形態2へ。
    /// 【形態2 オポジション】4d6 + プリオリテ で殴り合い。ボス累積3勝で形態1へ帰還。
    ///   プレイヤーがロール勝利すると次Tボスダイス合計+4、そのT ボスがロール敗北すれば+15ダメ。
    ///   次Tが形態1帰還となる場合、報酬を確実に与えるため形態2を1T延長する。
    /// 【プリオリテ】形態2→1 に再突入する毎にスタック+5（戦闘内累積・上限なし）。
    ///   各スタック効果: 形態1シールド初期 -20 (最低20まで) / 形態1反撃 +3。
    ///   シールドが薄くなるほど往復は速くなるが、毎サイクル受ける反撃が指数的に重くなる
    ///   = "粘る者ほど無駄に身を削る" 罠。0d0仕様を壊さないためダイス合計には触れない。</summary>
    public class SaintGeorgesPhases : IPassiveSkillEffect
    {
        public string SkillId => "SaintGeorgesPhases";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreReceiveDamage,
            PassiveSkillTrigger.OnRollLose,   // 敵視点: ボスが敗北 = プレイヤー勝利
            PassiveSkillTrigger.OnRollWin,    // 敵視点: ボスが勝利 = プレイヤー敗北
            PassiveSkillTrigger.OnTurnEnd,
        };

        // accumulatedValues キー（perspective 非依存・共有スカラ）
        private const string KPhase           = "sg_phase";            // 1 or 2
        private const string KShield          = "sg_shield";           // 形態1シールド残量
        private const string KBossWins        = "sg_boss_wins";        // 形態2のボス累積勝利
        private const string KPendingP1ToP2   = "sg_pending_p1_to_p2"; // シールド破壊→次T形態2
        private const string KPendingP2ToP1   = "sg_pending_p2_to_p1"; // ボス3勝→次T形態1
        private const string KP2RewardPending = "sg_p2_reward_pending"; // P2: 連勝予約
        private const string KP2RewardActive  = "sg_p2_reward_active";  // P2: 当ターン報酬有効
        private const string KP2ExtendPending = "sg_p2_extend_pending"; // P2: 帰還1T延長予約
        private const string KPrioriteStacks  = "sg_priorite_stacks";   // プリオリテ累積（往復ペナルティ）

        private const int BaseDiceCount   = 4; // enemies.json と一致させること
        private const int BaseShield      = 140; // HP +40% に比例して上方修正
        private const int MinShield       = 28; // プリオリテ削減後の下限 (20→28, +40%)
        private const int PrioriteShieldDecPer = 28; // /stack でシールド初期量を減らす
        private const int PrioriteCounterPer   = 7;  // /stack で反撃ダメ加算

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    ctx.accumulatedValues[KPhase] = 1;
                    // 2026-05-31: プリオリテ初期+1 (BaseShield - PrioriteShieldDecPer = 140-28=112 で開始)
                    ctx.accumulatedValues[KPrioriteStacks] = 1;
                    int initShield = System.Math.Max(MinShield, BaseShield - PrioriteShieldDecPer);
                    ctx.accumulatedValues[KShield] = initShield;
                    UnityEngine.Debug.Log($"[サン=ジョリオラ] 戦闘開始: コントラタック構え シールド{initShield} (プリオリテ初期+1)");
                    break;

                case PassiveSkillTrigger.OnTurnStart:
                    ApplyPendingTransitions(ctx);
                    int phaseStart = (int)ctx.GetAccumulated(KPhase);
                    if (phaseStart == 1)
                    {
                        // 形態1: ボスはダイスを振らない (0d0)
                        ctx.accumulatedValues["extraDice"] = -BaseDiceCount;
                    }
                    else
                    {
                        // 形態2: 報酬予約→有効化、ボスダイス合計+4
                        if (ctx.GetAccumulated(KP2RewardPending) > 0)
                        {
                            ctx.accumulatedValues[KP2RewardActive] = 1;
                            ctx.accumulatedValues[KP2RewardPending] = 0;
                            ctx.enemyDiceTotalBonus += 4;
                            UnityEngine.Debug.Log("[サン=ジョリオラ・オポジション] 報酬ターン突入: ボスダイス合計+4");
                        }
                    }
                    break;

                case PassiveSkillTrigger.OnPreReceiveDamage:
                {
                    // 形態1のみシールド経由
                    if ((int)ctx.GetAccumulated(KPhase) != 1) break;
                    if (ctx.finalDamage <= 0) break;

                    float shield = ctx.GetAccumulated(KShield);
                    if (shield <= 0)
                    {
                        // シールド既に0 → このターンは無敵
                        ctx.finalDamage = 0;
                        UnityEngine.Debug.Log("[サン=ジョリオラ] シールド0(無敵): ダメージ無効");
                        break;
                    }

                    int absorbed = System.Math.Min((int)shield, ctx.finalDamage);
                    shield -= absorbed;
                    ctx.finalDamage -= absorbed;
                    ctx.accumulatedValues[KShield] = shield;

                    if (shield <= 0)
                    {
                        // シールド破壊ターン: 残ダメを全て無効化、次T形態2へ
                        ctx.finalDamage = 0;
                        ctx.accumulatedValues[KPendingP1ToP2] = 1;
                        UnityEngine.Debug.Log($"[サン=ジョリオラ] シールド破壊! 吸収{absorbed} + 超過分は無敵で無効化 / 次T 形態2");
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"[サン=ジョリオラ] シールド吸収{absorbed} → 残{shield}");
                    }
                    break;
                }

                case PassiveSkillTrigger.OnRollLose:
                {
                    // 敵視点: ボスがロール敗北 = プレイヤーがロール勝利
                    int phaseLose = (int)ctx.GetAccumulated(KPhase);
                    if (phaseLose == 1)
                    {
                        // 反撃: プレイヤー自身のダイス合計 + プリオリテ×3 を不可避ダメで返す
                        // 敵視点では ctx.enemyDiceTotal = プレイヤー実ダイス合計、ctx.enemyCurrentHP = プレイヤー実HP
                        int playerTotal = ctx.enemyDiceTotal;
                        int stacks = (int)ctx.GetAccumulated(KPrioriteStacks);
                        int counter = playerTotal + stacks * PrioriteCounterPer;
                        if (counter > 0)
                        {
                            ctx.EnemyDealUnmitigable(counter, null);   // 軽減は無視・シールドは肩代わりする
                            UnityEngine.Debug.Log($"[サン=ジョリオラ・コントラタック] 反撃{counter} (素{playerTotal}+プリオリテ{stacks}×{PrioriteCounterPer}, 不可避) → プレイヤーHP={ctx.enemyCurrentHP}");
                        }
                        // シールドへ +25 確定ダメ
                        float s = ctx.GetAccumulated(KShield);
                        if (s > 0)
                        {
                            float newS = System.Math.Max(0, s - 25);
                            ctx.accumulatedValues[KShield] = newS;
                            UnityEngine.Debug.Log($"[サン=ジョリオラ・コントラタック] +25確定ダメ シールド{s}→{newS}");
                            if (newS <= 0)
                                ctx.accumulatedValues[KPendingP1ToP2] = 1;
                        }
                    }
                    else // 形態2
                    {
                        // 当ターン報酬有効中なら +15 ダメ
                        if (ctx.GetAccumulated(KP2RewardActive) > 0)
                        {
                            // 敵視点: ctx.playerCurrentHP = ボス自身のHP
                            ctx.playerCurrentHP = System.Math.Max(0, ctx.playerCurrentHP - 15);
                            UnityEngine.Debug.Log($"[サン=ジョリオラ・連勝報酬] +15ダメ → ボスHP={ctx.playerCurrentHP}");
                        }
                        // 次ターン分の報酬予約
                        ctx.accumulatedValues[KP2RewardPending] = 1;
                        // 形態1帰還を1T遅延する予約
                        ctx.accumulatedValues[KP2ExtendPending] = 1;
                    }
                    break;
                }

                case PassiveSkillTrigger.OnRollWin:
                {
                    // 敵視点: ボスがロール勝利
                    if ((int)ctx.GetAccumulated(KPhase) != 2) break;
                    int wins = (int)ctx.GetAccumulated(KBossWins) + 1;
                    ctx.accumulatedValues[KBossWins] = wins;
                    UnityEngine.Debug.Log($"[サン=ジョリオラ・オポジション] ボス勝利 累計{wins}/3");
                    if (wins >= 3)
                    {
                        if (ctx.GetAccumulated(KP2ExtendPending) > 0)
                        {
                            // 延長: 形態2を1T継続させ、勝利カウントを2に戻す
                            ctx.accumulatedValues[KBossWins] = 2;
                            ctx.accumulatedValues[KP2ExtendPending] = 0;
                            UnityEngine.Debug.Log("[サン=ジョリオラ] 連勝報酬完了まで形態2延長 (勝利数2へ戻す)");
                        }
                        else
                        {
                            ctx.accumulatedValues[KPendingP2ToP1] = 1;
                            UnityEngine.Debug.Log("[サン=ジョリオラ] 次T 形態1帰還を予約 → シールド再展開");
                        }
                    }
                    break;
                }

                case PassiveSkillTrigger.OnTurnEnd:
                    // 報酬ターン終了処理: +4 ボーナス除去、active クリア
                    if (ctx.GetAccumulated(KP2RewardActive) > 0)
                    {
                        ctx.accumulatedValues[KP2RewardActive] = 0;
                        ctx.enemyDiceTotalBonus = System.Math.Max(0, ctx.enemyDiceTotalBonus - 4);
                    }
                    break;
            }
        }

        private void ApplyPendingTransitions(CombatContext ctx)
        {
            if (ctx.GetAccumulated(KPendingP1ToP2) > 0)
            {
                ctx.accumulatedValues[KPhase] = 2;
                ctx.accumulatedValues[KShield] = 0;
                ctx.accumulatedValues[KBossWins] = 0;
                ctx.accumulatedValues[KPendingP1ToP2] = 0;
                UnityEngine.Debug.Log("[サン=ジョリオラ] 形態1→2 移行: オポジション(4d6) 開始");
            }
            if (ctx.GetAccumulated(KPendingP2ToP1) > 0)
            {
                // プリオリテ累積: 帰還の代償としてシールド -20 (最低 MinShield)、反撃 +3
                int newStacks = (int)ctx.GetAccumulated(KPrioriteStacks) + 1;
                ctx.accumulatedValues[KPrioriteStacks] = newStacks;
                int newShield = System.Math.Max(MinShield, BaseShield - newStacks * PrioriteShieldDecPer);

                ctx.accumulatedValues[KPhase] = 1;
                ctx.accumulatedValues[KShield] = newShield;
                ctx.accumulatedValues[KBossWins] = 0;
                ctx.accumulatedValues[KPendingP2ToP1] = 0;
                // 報酬関連はクリア（形態跨ぎでは持ち越さない）
                ctx.accumulatedValues[KP2RewardPending] = 0;
                ctx.accumulatedValues[KP2RewardActive] = 0;
                ctx.accumulatedValues[KP2ExtendPending] = 0;
                UnityEngine.Debug.Log($"[サン=ジョリオラ] プリオリテ{newStacks} 主張! 形態2→1 移行: シールド{newShield} (反撃+{newStacks * PrioriteCounterPer})");
            }
        }
    }

}
