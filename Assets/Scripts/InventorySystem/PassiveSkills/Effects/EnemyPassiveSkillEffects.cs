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
                int steal = System.Math.Min(1, run.coins); // 1/5 デノミ (旧5G→1G)
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
            ctx.enemyThreat += 3;
            UnityEngine.Debug.Log($"[威圧+] 脅威 +3 → {ctx.enemyThreat}");
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

    /// <summary>天衣無縫: 覚者(寂照/初眼/無相/妙覚)に付与。覚者がロール勝利するたび、
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

    // ============================================================
    //  6層 SinAltar 由来の永続デバフが付与する敵専用パッシブ
    //  CombatManager.ApplySinDebuffsToBossIfApplicable() から動的に注入される
    // ============================================================

    /// <summary>〈ゴルゴダの心〉ボスは毎ターン scratch+1 を上乗せ。
    /// プレイヤーが HP の儀式を拒んだ罰。じわじわ削られる。</summary>
    /// <summary>〈ゴルゴダの心〉プレイヤーが血の儀を拒んだ罰。
    /// 灰燼戦開始時、プレイヤー最大HPを半減（戦闘中のみ）。
    /// 血を流す覚悟を持たぬ者には命そのものが許されない。</summary>
    public class Boss6Golgotha : IPassiveSkillEffect
    {
        public string SkillId => "boss6_golgotha";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点: enemyMaxHP / enemyCurrentHP = プレイヤーの値
            int beforeMax = ctx.enemyMaxHP;
            int beforeCur = ctx.enemyCurrentHP;
            ctx.enemyMaxHP = System.Math.Max(1, ctx.enemyMaxHP / 2);
            if (ctx.enemyCurrentHP > ctx.enemyMaxHP)
                ctx.enemyCurrentHP = ctx.enemyMaxHP;
            UnityEngine.Debug.Log($"[ゴルゴダの心] プレイヤー最大HP半減 {beforeMax}→{ctx.enemyMaxHP} (HP {beforeCur}→{ctx.enemyCurrentHP})");
        }
    }

    /// <summary>〈断絶した時間〉経過ターンが進むほどボスのダイス合計に +(turn-1) ボーナス。
    /// プレイヤーが金銭の儀式を拒んだ罰。長期戦＝即死。
    /// 1ターン目: +0、2ターン目: +1、3ターン目: +2 … と加速する。</summary>
    public class Boss6SeveredTime : IPassiveSkillEffect
    {
        public string SkillId => "boss6_severed_time";
        // OnTurnStart で enemyDiceTotalBonus に積む。
        // 旧実装は OnPostRoll で playerDiceTotal を直接いじっていたが、
        // 敵 OnPostRoll は ProcessPostRoll 完了(=勝敗判定済み)の後に発火するため
        // ボーナスが判定に乗らず、AutoRunner で 75万ターンの実質無敵化が発生していた。
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // ProcessPostRoll 内 (L445) で enemyDiceTotal += enemyDiceTotalBonus が判定前に乗る
            int bonus = System.Math.Max(0, ctx.currentTurn - 1);
            ctx.enemyDiceTotalBonus = bonus;
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
            // ボス難易度オートチューナー: 審判の炎の総ダメ上限を直接調整 (低い=不可避チップ緩和)。 基準13
            int cap = AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.ChipCap);
            int dmg = System.Math.Min(cap, 1 + System.Math.Max(1, ctx.currentTurn) + sin);
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
            if (ctx.enemyCurrentHP <= 0 && dmg > 0) ctx.lastDamageCause = DeathCause.Chip; // 死因タグ
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

    /// <summary>真我 — 覚者の到達した境地。 ダイス合計に固定値を加算する素ロール強化（7層全形態）。
    /// ボス難易度オートチューナー専用の「ダイス上限を超える」難度レバー: 通常のダイス調整(個数≤5・最高出目≤9)が
    /// 上限に張り付き、 かつプレイヤーのロール勝率が90%を超えて張り付く場合に、 この値(基準1)を上下させて
    /// ロール勝負を引き締める。 bossDiceBonus 経由で勝敗判定前に enemyDiceTotal へ加算される。
    /// OnBattleStart で設定（形態swap時は CombatManager が bossDiceBonus を0クリア→各形態の本パッシブが再設定）。</summary>
    public class TrueSelf : IPassiveSkillEffect
    {
        public string SkillId => "TrueSelf";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 妙覚はサドンデス専用のため真我は無効 (素ロール加算しない)
            if (AutoTest.BossTuning.IsMyokaku(ctx.bossId)) return;
            int bonus = UnityEngine.Mathf.Max(0, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.TrueSelf));
            ctx.bossDiceBonus += bonus; // bossDiceBonus は swap時に0クリアされるので加算でよい
            UnityEngine.Debug.Log($"[真我] 素ロール +{bonus} (ダイス合計加算)");
        }
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
                ctx.enemyMaxHP = System.Math.Max(1, ctx.enemyMaxHP - loss);
                if (ctx.enemyCurrentHP > ctx.enemyMaxHP) ctx.enemyCurrentHP = ctx.enemyMaxHP;
            }

            bool hadShield = ctx.consShield > 0;
            ctx.consShield = 0; // シールド完全破壊

            if (loss > 0 || hadShield)
                UnityEngine.Debug.Log($"[焦土] 被ダメ{ctx.playerDamageThisTurn}→最大HP-{loss} ({beforeMax}→{ctx.enemyMaxHP})"
                    + (hadShield ? " ＋シールド破壊" : ""));
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
                            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - counter);
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

    // ===========================================================================
    //  覚者×7形態大連戦 (boss_layer7 → p2 → p3 → p4 → p5 → p6 → p7)
    //  各形態は自身の HP=0 を検知して次形態へ SwapEnemy する。
    //  プレイヤー状態(HP/buffs/currentTurn) は全形態で持ち越し。
    // ===========================================================================

    /// <summary>形態1【逆観】高出目=自爆。ロール敗北時、プレイヤー最大出目×1 を追加の固定ダメに変換。
    /// 注: 旧実装(OnPostRoll で playerDiceTotal +=) は勝敗判定後で効かなかった。
    /// プレイヤー敗北時の被ダメに乗せる方式に変更 (高出目を振ったほど被弾増)。</summary>
    public class AwakenedP1Inverse : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP1Inverse";
        // 〈逆観〉: 強い出目ほど自分を蝕む。毎ターン player の最大出目を player に固定ダメ (軽減無視)。
        // OnPostRoll で fixedDamageToEnemy (= 敵視点ではプレイヤーへの固定ダメ) に積む。
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                if (ctx.enemyDice != null && ctx.enemyDice.Length > 0)
                {
                    int maxFace = 0;
                    foreach (var d in ctx.enemyDice) if (d > maxFace) maxFace = d;
                    // 逆観: プレイヤー最大出目 ＋ 経過ターンに比例した重圧（長期戦ほど致死的に）。
                    int escalate = ctx.currentTurn / 3; // T3で+1, T9で+3, T15で+5…
                    int total = maxFace + escalate;
                    if (total > 0)
                    {
                        ctx.fixedDamageToEnemy += total;
                        UnityEngine.Debug.Log($"[覚者・逆観] 最大出目{maxFace}+重圧{escalate} → 固定ダメ{total}(軽減無視)");
                    }
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                AwakenedChainHelper.CheckAndSwap(ctx, "boss_layer7_p2", "第二形態：業火・残響");
            }
        }
    }

    /// <summary>形態2【爆ぜ火】プレイヤーロール敗北時、即時5固定ダメ(軽減無視)。HP=0で第三形態へ。</summary>
    public class AwakenedP2BurstFire : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP2BurstFire";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // OnRollWin (敵視点): enemy won = player lost roll
            if (trigger == PassiveSkillTrigger.OnRollWin)
            {
                // ボス難易度オートチューナー: 爆ぜ火の敗北時固定ダメを直接調整 (基準5)
                int burst = UnityEngine.Mathf.Max(0, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.BurstDamage));
                ctx.fixedDamageToEnemy += burst; // 敵視点 fixedDamageToEnemy はプレイヤーへの軽減無視ダメ
                if (burst > 0) ctx.lastDamageCause = DeathCause.Burst; // 死因タグ
                UnityEngine.Debug.Log($"[覚者・爆ぜ火] +{burst} 固定ダメ(軽減無視)");
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                AwakenedChainHelper.CheckAndSwap(ctx, "boss_layer7_p3", "第三形態：覚者・無相");
            }
        }
    }

    /// <summary>形態3【鏡映】覚者への与ダメと同量をプレイヤーHPからも引く。HP=0で第四形態へ。</summary>
    public class AwakenedP3Mirror : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP3Mirror";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPreReceiveDamage)
            {
                int dmg = ctx.finalDamage;
                if (dmg > 0)
                {
                    // 反射は与ダメの50%に軽減（100%反射はHP180・防御35%相手で数学的にほぼ勝てないため）。
                    // 反射は fixedDamageToEnemy 経由 (敵視点なので real fixedDamageToPlayer に対応)。
                    // ctx.enemyCurrentHP を直接書くと CombatManager の ctx.playerCurrentHP = playerHP 同期で上書きされる。
                    // ボス難易度オートチューナー: 鏡映反射率%を直接調整 (基準50%、 上限90%、 低い=易化)
                    float reflectRate = UnityEngine.Mathf.Clamp(AutoTest.BossTuning.Param(ctx.bossId, AutoTest.BossParam.ReflectPct) / 100f, 0f, 0.9f);
                    int reflect = UnityEngine.Mathf.CeilToInt(dmg * reflectRate);
                    ctx.fixedDamageToEnemy += reflect;
                    ctx.lastDamageCause = DeathCause.Reflect; // 死因タグ
                    UnityEngine.Debug.Log($"[覚者・鏡映] 与ダメ{dmg}の{reflectRate:P0}={reflect}を反射 → プレイヤーへ固定ダメ");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                AwakenedChainHelper.CheckAndSwap(ctx, "boss_layer7_p4", "第四形態：シュヴァリエ・残影");
            }
        }
    }

    /// <summary>形態4【一閃返し】1度限り、被ダメ完全無効+2倍反射。HP=0で第五形態へ。</summary>
    public class AwakenedP4Riposte : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP4Riposte";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnTurnEnd };
        private const string KUsed = "awakened_p4_riposte_used";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPreReceiveDamage)
            {
                if (ctx.GetAccumulated(KUsed) > 0) return;
                int dmg = ctx.finalDamage;
                if (dmg <= 0) return;
                ctx.accumulatedValues[KUsed] = 1;
                // 反射は撤廃（2倍→等倍でも低HP到達プレイヤーを即死させ p4 が96%死亡の壁だったため）。
                // 「初撃完全無効」のみ＝テンポ損のみで即死はしない見切りの一閃。
                ctx.finalDamage = 0;
                UnityEngine.Debug.Log($"[覚者・一閃返し] 被ダメ{dmg}を完全無効化（反射なし）");
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                AwakenedChainHelper.CheckAndSwap(ctx, "boss_layer7_p5", "第五形態：覚者・寂照");
            }
        }
    }

    /// <summary>形態5【寂照】静謐の圧迫感: 毎T 所持パッシブ数 ÷4 の固定ダメ(軽減無視)。
    /// 加えて、消費品/レイピア使用→ランダムなパッシブを永久喪失。HP=0で第六形態へ。</summary>
    public class AwakenedP5Silent : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP5Silent";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            var run = GameLoop.GameManager.Instance?.Run;

            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                // 毎ターン 所持パッシブ数/4 (切り捨て、最低1) の固定ダメ(軽減無視)
                int owned = run?.ownedPassiveItems?.Count ?? 0;
                if (owned > 0)
                {
                    int dmg = UnityEngine.Mathf.Max(1, owned / 4);
                    ctx.fixedDamageToEnemy += dmg;
                    UnityEngine.Debug.Log($"[覚者・寂照] 静謐の圧迫: 所持{owned}個 → 固定ダメ {dmg}");
                }
                return;
            }

            // OnTurnEnd: 消費品使用ならパッシブ喪失 + チェーン swap 判定
            if (ctx.consumablesUsedThisTurn)
            {
                if (run?.ownedPassiveItems != null && run.ownedPassiveItems.Count > 0)
                {
                    var candidates = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                    {
                        var id = run.ownedPassiveItems[i];
                        if (id != null && !id.StartsWith("curse_") && id != "chevalier_rapier")
                            candidates.Add(i);
                    }
                    if (candidates.Count > 0)
                    {
                        int pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                        string lost = run.ownedPassiveItems[pick];
                        InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, pick);
                        UnityEngine.Debug.Log($"[覚者・寂照] パッシブ永久喪失: {lost}");
                    }
                }
            }
            AwakenedChainHelper.CheckAndSwap(ctx, "boss_layer7_p6", "第六形態：灰燼・薄火");
        }
    }

    /// <summary>形態6【業火の遺志】毎T 覚者ダイス合計 +(灰燼-内部T)。形態内ランプアップ。
    /// enemyDiceTotalBonus 経由で ProcessPostRoll 判定前に効かせる。</summary>
    public class AwakenedP6EmberWill : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP6EmberWill";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                if (ctx.GetAccumulated("ember_entered") <= 0)
                    ctx.accumulatedValues["ember_entered"] = ctx.currentTurn;
                int enteredAt = (int)ctx.GetAccumulated("ember_entered");
                int emberT = ctx.currentTurn - enteredAt + 1;
                ctx.enemyDiceTotalBonus = emberT;
                UnityEngine.Debug.Log($"[覚者・業火の遺志] 灰燼-T{emberT}: 覚者ダイス合計 +{emberT}");
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                AwakenedChainHelper.CheckAndSwap(ctx, "boss_layer7_p7", "最終形態：覚者・妙覚");
            }
        }
    }

    /// <summary>形態7【妙覚】(2026-06-01 リワーク):
    ///   ・T1: ボス 0d0 の自由攻撃ターン (CombatManager が enemy 0d0 強制)。 削りきれば通常勝利(R11)。
    ///   ・T2以降: サドンデス。 ダイス強制なし=素のロール勝負(バフ/デバフ有効)。
    ///       プレイヤーがロール敗北 → 99ダメージ(軽減/シールド有効) + 妙覚HP全回復。
    ///   ・サドンデスを SuddenDeathSurviveTurns ターン生存 → 【解脱】特殊勝利(R12)。
    ///   SuddenDeath軸で 99 をスケール可 (オートチューナー)。</summary>
    public class AwakenedP7Myokaku : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedP7Myokaku";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreDealDamage,
            PassiveSkillTrigger.OnTurnEnd,
        };
        private const string ENTER = "myokaku_entered";
        private const int SuddenDeathSurviveTurns = 7; // サドンデスをこのターン数生存で解脱

        private static int MyokakuTurn(CombatContext ctx)
        {
            int entered = (int)ctx.GetAccumulated(ENTER);
            return entered > 0 ? ctx.currentTurn - entered + 1 : 1;
        }

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                {
                    if (ctx.GetAccumulated(ENTER) <= 0)
                        ctx.accumulatedValues[ENTER] = ctx.currentTurn;
                    int mt = MyokakuTurn(ctx);
                    // T1 のみ自由攻撃 (ボス0d0)。 T2以降は素のロール勝負 (サドンデス)。
                    ctx.myokakuFreeHit = (mt == 1);
                    break;
                }

                case PassiveSkillTrigger.OnPreDealDamage:
                {
                    // T2以降のサドンデス中、 プレイヤーがロール敗北(=敵視点 playerWonRoll) したとき
                    if (!ctx.myokakuFreeHit && ctx.playerWonRoll)
                    {
                        // サドンデスダメ (軽減/シールド有効 → 後段 ApplyLossDamageModifiers が適用)。 基準99。 ※7層専用=通常は不調整
                        ctx.finalDamage = UnityEngine.Mathf.Max(1, AutoTest.BossTuning.ParamInt(ctx.bossId, AutoTest.BossParam.SuddenDeathDamage));
                        ctx.lastDamageCause = DeathCause.SuddenDeath;
                        ctx.playerCurrentHP = ctx.playerMaxHP; // 妙覚HP全回復 (敵視点: playerCurrentHP=ボスHP)
                        UnityEngine.Debug.Log($"[覚者・妙覚] サドンデス: ロール敗北 → {ctx.finalDamage}ダメ(軽減可) + 妙覚HP全回復");
                    }
                    break;
                }

                case PassiveSkillTrigger.OnTurnEnd:
                {
                    int mt = MyokakuTurn(ctx);
                    int sdTurns = mt - 1; // T1は自由攻撃、 サドンデスはT2から
                    // 敵視点: enemyCurrentHP = プレイヤー現在HP。 生存して規定ターン経過なら解脱。
                    if (sdTurns >= SuddenDeathSurviveTurns && ctx.enemyCurrentHP > 0)
                    {
                        ctx.gedatsuPending = true;
                        ctx.playerCurrentHP = 0; // ボスHP=0 → 戦闘終了処理(解脱)に乗せる
                        UnityEngine.Debug.Log($"[覚者・妙覚] ★解脱★ サドンデス{SuddenDeathSurviveTurns}ターン生存 → プレイヤー勝利");
                    }
                    break;
                }
            }
        }
    }

    /// <summary>覚者連戦のヘルパ。各形態の OnTurnEnd で呼ぶ。
    /// 敵パッシブ実行中は perspective が swap されているため、SwapEnemy を直接呼ばず
    /// ctx.pendingEnemySwapId に予約。CombatManager が enemy トリガー完了後に処理する。</summary>
    internal static class AwakenedChainHelper
    {
        public static void CheckAndSwap(CombatContext ctx, string nextId, string logLabel)
        {
            // 敵視点(swapped): ctx.playerCurrentHP = ボス自身のHP
            if (ctx.playerCurrentHP > 0) return;
            // 既に予約済みなら無視
            if (!string.IsNullOrEmpty(ctx.pendingEnemySwapId)) return;
            ctx.pendingEnemySwapId = nextId;
            ctx.pendingEnemySwapLabel = logLabel;
            UnityEngine.Debug.Log($"[覚者連戦] HP=0検知 → 次形態 {logLabel} を予約");
        }
    }

    /// <summary>[旧] 覚者 — 悟達の試練: 単体戦時代の試練。連戦化により廃止。
    /// 残置: コンパイル互換のため。Register からは外す。</summary>
    public class AwakenedTrial : IPassiveSkillEffect
    {
        public string SkillId => "AwakenedTrial";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnTurnEnd,
            PassiveSkillTrigger.OnPreDealDamage,    // 無心後は与ダメ無効化
            PassiveSkillTrigger.OnPreReceiveDamage, // 煩悩: 装備数に応じ被ダメ軽減
        };

        private const string KQuietude     = "awakened_quietude";    // 観想スタック
        private const string KEnlightened  = "awakened_enlightened"; // 無心化フラグ
        private const string KHPSnap       = "awakened_hp_snap";     // T開始時プレイヤーHP

        private const int QuietudeThreshold  = 7;
        private const int BaseDiceCount      = 5;  // enemies.json 5d9 と一致
        private const int EnlightenedDice    = 2;  // 弱体化後 → 2d (extraDice = -3)
        private const int VoidInterval       = 3;  // 【空】: 3Tに1回ダイス0
        private const int KarmaHealPer       = 20; // 【因果】: 消費品/レイピア使用毎にボスHP回復
        private const float BonnouPerPassive = 0.3f; // 【煩悩】: 装備数 × 0.3 被ダメ軽減

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    ctx.accumulatedValues[KQuietude] = 0;
                    ctx.accumulatedValues[KEnlightened] = 0;
                    UnityEngine.Debug.Log("[覚者] 戦闘開始: 観想0/7 ─ 戦意を捨てれば剣を収める");
                    break;

                case PassiveSkillTrigger.OnTurnStart:
                    if (ctx.GetAccumulated(KEnlightened) > 0)
                    {
                        // 弱体化後: 5d9 → 2d6相当 (extraDice = -3)、空も廃止
                        ctx.accumulatedValues["extraDice"] = -(BaseDiceCount - EnlightenedDice);
                    }
                    else if (ctx.currentTurn > 0 && ctx.currentTurn % VoidInterval == 0)
                    {
                        // 【空】: ダイス0個に
                        ctx.accumulatedValues["extraDice"] = -BaseDiceCount;
                        UnityEngine.Debug.Log($"[覚者・空] T{ctx.currentTurn}: 振らず ─ 殴りの好機");
                    }
                    ctx.accumulatedValues[KHPSnap] = ctx.enemyCurrentHP;
                    break;

                case PassiveSkillTrigger.OnPreReceiveDamage:
                    // 【煩悩】: プレイヤー所持パッシブ数 × 0.3 軽減（弱体化後は無効）
                    if (ctx.GetAccumulated(KEnlightened) > 0) break;
                    if (ctx.finalDamage <= 0) break;
                    {
                        var run = GameLoop.GameManager.Instance?.Run;
                        int passives = run?.ownedPassiveItems?.Count ?? 0;
                        int mitigation = (int)(passives * BonnouPerPassive);
                        if (mitigation > 0)
                        {
                            int before = ctx.finalDamage;
                            ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - mitigation);
                            UnityEngine.Debug.Log($"[覚者・煩悩] 装備{passives}個 → 被ダメ {before}→{ctx.finalDamage} (-{mitigation})");
                        }
                    }
                    break;

                case PassiveSkillTrigger.OnPreDealDamage:
                    if (ctx.GetAccumulated(KEnlightened) > 0 && ctx.playerWonRoll)
                    {
                        ctx.nullifyAllDamage = true;
                        UnityEngine.Debug.Log("[覚者・無心] 与ダメ放棄");
                    }
                    break;

                case PassiveSkillTrigger.OnTurnEnd:
                    // 【因果】: 消費品/レイピア使用ならボス自己回復（弱体化後は無効）
                    if (ctx.GetAccumulated(KEnlightened) == 0 && ctx.consumablesUsedThisTurn)
                    {
                        int before = ctx.playerCurrentHP;
                        ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + KarmaHealPer);
                        int healed = ctx.playerCurrentHP - before;
                        if (healed > 0)
                            UnityEngine.Debug.Log($"[覚者・因果] 消費品 → 自己回復 +{healed} ({ctx.playerCurrentHP}/{ctx.playerMaxHP})");
                    }

                    if (ctx.GetAccumulated(KEnlightened) > 0) break;

                    int snap = (int)ctx.GetAccumulated(KHPSnap);
                    bool hpIncreased = ctx.enemyCurrentHP > snap;
                    bool actionTaken = ctx.consumablesUsedThisTurn || hpIncreased;

                    if (actionTaken)
                    {
                        if (ctx.GetAccumulated(KQuietude) > 0)
                            UnityEngine.Debug.Log("[覚者] 観想中断: 行動検知 → 0リセット");
                        ctx.accumulatedValues[KQuietude] = 0;
                    }
                    else
                    {
                        int newCount = (int)ctx.GetAccumulated(KQuietude) + 1;
                        ctx.accumulatedValues[KQuietude] = newCount;
                        UnityEngine.Debug.Log($"[覚者] 観想 {newCount}/{QuietudeThreshold}");
                        if (newCount >= QuietudeThreshold)
                        {
                            ctx.accumulatedValues[KEnlightened] = 1;
                            ctx.accumulatedValues["extraDice"] = -(BaseDiceCount - EnlightenedDice);
                            UnityEngine.Debug.Log("[覚者] ★無心の境地★ ─ 5d9→2d6・全特性停止・与ダメ無効化");
                        }
                    }
                    break;
            }
        }
    }
}
