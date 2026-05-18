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
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
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
                int steal = System.Math.Min(5, run.coins);
                run.coins -= steal;
                UnityEngine.Debug.Log($"[精鋭コボルド・早業] GOLD {steal} 強奪 (残{run.coins})");
            }
            else if (ctx.playerLostRoll && ctx.finalDamage > 0
                     && UnityEngine.Random.value < 0.5f)
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
                ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg); // 実プレイヤーへ確定ダメ
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

    /// <summary>精鋭ダークナイト — 闇技: 勝利時、与ダメ+3（精鋭勝率を25%域へ）</summary>
    public class EliteDarkKnight : IPassiveSkillEffect
    {
        public string SkillId => "EliteDarkKnight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (ctx.playerWonRoll) ctx.finalDamage += 3; }
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

    // ============================================================
    //  6層 SinAltar 由来の永続デバフが付与する敵専用パッシブ
    //  CombatManager.ApplySinDebuffsToBossIfApplicable() から動的に注入される
    // ============================================================

    /// <summary>〈ゴルゴダの心〉ボスは毎ターン scratch+1 を上乗せ。
    /// プレイヤーが HP の儀式を拒んだ罰。じわじわ削られる。</summary>
    public class Boss6Golgotha : IPassiveSkillEffect
    {
        public string SkillId => "boss6_golgotha";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点のため、playerCurrentHP は実際にはプレイヤーのHP
            ctx.scratchDamage += 1;
        }
    }

    /// <summary>〈断絶した時間〉経過ターンが進むほどボスのダイス合計に +(turn-1) ボーナス。
    /// プレイヤーが金銭の儀式を拒んだ罰。長期戦＝即死。
    /// 1ターン目: +0、2ターン目: +1、3ターン目: +2 … と加速する。</summary>
    public class Boss6SeveredTime : IPassiveSkillEffect
    {
        public string SkillId => "boss6_severed_time";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点のため playerDiceTotal = ボス自身のダイス合計
            int bonus = System.Math.Max(0, ctx.currentTurn - 1);
            if (bonus > 0)
                ctx.playerDiceTotal += bonus;
        }
    }

    /// <summary>〈灰燼の烙印〉プレイヤーが遺品の儀を拒んだ罰。
    /// 6層ボスが致命傷を受けたとき、烙印が付与されている限り HP1 で踏みとどまり、
    /// 翌ターン以降は両者ダイスを 1d6 に強制した一撃必殺のサドンデス（決着まで継続）。
    /// 一度の戦闘につき踏みとどまりは1回。発動ターン番号を記録し、CombatManager が
    /// 「記録ターン超」でのみ即死決着を適用する（踏みとどまったその場で死なないため）。</summary>
    public class Boss6Ashen : IPassiveSkillEffect
    {
        /// <summary>踏みとどまった currentTurn を格納（>0 で発動済み判定も兼ねる）。</summary>
        public const string UsedKey = "ashen_endured_turn";

        public string SkillId => "boss6_ashen";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点: ctx.playerCurrentHP = ボス自身のHP
            if (ctx.playerCurrentHP > 0) return;
            // 一度の戦闘につき踏みとどまりは1回のみ
            if (ctx.GetAccumulated(UsedKey) > 0f) return;

            ctx.accumulatedValues[UsedKey] = ctx.currentTurn; // 発動ターンを記録
            ctx.playerCurrentHP = 1;          // HP1で踏みとどまる（SyncHPで敵HP=1へ反映）
            ctx.ashenSuddenDeath = true;      // 翌ターン以降サドンデス（決着まで継続）
            UnityEngine.Debug.Log($"[Boss6Ashen] 灰燼の烙印: ボスがHP1で踏みとどまった（T{ctx.currentTurn}） → 翌ターンからサドンデス");
        }
    }

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
            // 敵視点なので playerDiceTotal = ボス自身のダイス合計
            ctx.playerDiceTotal += 3;
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
            ctx.accumulatedValues.TryGetValue(Key, out var stack);
            int s = System.Math.Min(5, (int)stack + 1);
            ctx.accumulatedValues[Key] = s;
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - s); // 敵視点で実プレイヤー
            UnityEngine.Debug.Log($"[毒の侵蝕] 毒×{s} ダメ (軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>鏡の双子 — 鏡映の応答: 10以上ダメを与えたターンの次ターン開始時に同値反射</summary>
    public class MirrorTwinsResponse : IPassiveSkillEffect
    {
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
                // 反転: 弱い小突きほど反射、一定値(閾値12)以上の高火力は反射0＝鏡を叩き割る。
                // reflect = min(9, 閾値 - 与ダメ)。スケールした火力ほど無傷で通る。
                const int Thr = 12;
                int dmg = ctx.finalDamage;
                if (dmg > 0 && dmg < Thr)
                {
                    int reflect = System.Math.Min(9, Thr - dmg);
                    ctx.accumulatedValues[Key] = reflect;
                    UnityEngine.Debug.Log($"[鏡映の応答] 蓄積: 次ターン{reflect}反射 (与ダメ{dmg}<{Thr}＝小突きを罰す)");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                if (!ctx.accumulatedValues.TryGetValue(Key, out var pending) || pending <= 0f) return;
                int dmg = (int)pending;
                ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg); // 敵視点で実プレイヤー
                ctx.accumulatedValues[Key] = 0f;
                UnityEngine.Debug.Log($"[鏡映の応答] 反射 {dmg}ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");
            }
        }
    }

    /// <summary>業火の審判官 — 審判の炎: 毎ターン終了時の確定ダメ（軽減無視）。
    /// = 1 + 経過ターン + 罪。罪 = ラン中の総戦闘回数/8（上限2）。総ダメ上限8。
    /// 壁度緩和（DOTレースに間に合えば勝てるフェアな殴り合いへ）。
    /// 「速攻」かつ「無駄な戦闘を避けた」者ほど有利。</summary>
    public class JudgmentFlames : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentFlames";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int sin = System.Math.Min(2, (run?.totalBattles ?? 0) / 8); // 罪: プレイで操作可能
            int dmg = System.Math.Min(8, 1 + System.Math.Max(1, ctx.currentTurn) + sin);
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
            UnityEngine.Debug.Log($"[審判の炎] {dmg}ダメ (1+経過T{ctx.currentTurn}+罪{sin}, 上限8, 軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
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
                    ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
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
            int heal = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.05f);
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
    /// 断罪ターン、ボスがロール勝利すると与ダメ×4+6の
    /// 即死級メインダメ（ロール由来＝LSでも巻き戻されない＝必ず決着）。
    /// プレイヤーがロール勝利＝間一髪回避し、鎧貫通の確定反撃12をボスへ。</summary>
    public class JudgmentBlaze : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentBlaze";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnRollLose,
            PassiveSkillTrigger.OnTurnEnd };
        private const string CtrKey = "ek_counter";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    // 灰の予兆統合: 断罪ターンを予告（読める＝運でなく対応の問題）
                    if (EmberKing.IsJudgment(ctx))
                        UnityEngine.Debug.Log($"[灰の予兆] 業火の断罪（T{ctx.currentTurn}・周期{EmberKing.Period(ctx)}）—ロール勝利で間一髪回避");
                    break;

                case PassiveSkillTrigger.OnPreDealDamage:
                    if (ctx.playerWonRoll && EmberKing.IsJudgment(ctx))
                    {
                        int before = ctx.finalDamage;
                        ctx.finalDamage = ctx.finalDamage * 5 + 8; // 致命の一撃（火力強化で戦闘圧縮）
                        UnityEngine.Debug.Log($"[業火の断罪] 致命の一撃 {before}→{ctx.finalDamage}");
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
                int reduced = System.Math.Max(0, ctx.finalDamage - 5);
                ctx.finalDamage = System.Math.Min(reduced, 10);
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

            float pct = below30 ? 0.06f : below60 ? 0.03f : 0f;
            if (pct <= 0f) return; // >60%: 再生なし

            int heal = System.Math.Max(1, UnityEngine.Mathf.FloorToInt(missing * pct));
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
            ctx.enemyDiceTotalBonus = stack; // 次ロール以降、勝敗判定前にボス合計へ +stack
            UnityEngine.Debug.Log($"[星火燎原] ボス敗北 → ダイス合計補正 累計+{stack}");
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

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;

            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // 13% で宣告フラグを立てる
                if (UnityEngine.Random.value < 0.13f)
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
                float critMul = ctx.criticalMultiplier > 0f ? ctx.criticalMultiplier : 2f;
                int dmg = UnityEngine.Mathf.CeilToInt(total * critMul);

                // 敵視点で enemyCurrentHP = 実プレイヤーのHP。直接書き換えで軽減フックを全バイパス
                ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
                UnityEngine.Debug.Log(
                    $"[Decree13th] 成就: ({ctx.playerDiceTotal}+{ctx.enemyDiceTotal})×{critMul:F1} = {dmg} ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");

                ctx.currentBuffs.Remove(FlagKey);
            }
        }
    }
}
