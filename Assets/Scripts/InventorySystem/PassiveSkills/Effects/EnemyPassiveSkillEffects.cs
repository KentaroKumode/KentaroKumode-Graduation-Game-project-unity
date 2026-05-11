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

    /// <summary>〈灰燼の烙印〉戦闘開始時、プレイヤーに出血スタックを 3 付与。
    /// プレイヤーが遺品の儀式を拒んだ罰。開幕から燃やされる。</summary>
    public class Boss6Ashen : IPassiveSkillEffect
    {
        public string SkillId => "boss6_ashen";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点で「相手 = プレイヤー」に bleed 付与
            ctx.enemyBleedStacks += 3;
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
            ctx.playerDiceTotal += 2;
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
                int bonus = UnityEngine.Mathf.Min(5, UnityEngine.Mathf.Max(0, (int)streak - 2));
                if (bonus > 0) ctx.playerDiceTotal += bonus; // 敵視点で自分のダイス合計加算
            }
        }
        public static void ResetStreak(CombatContext ctx)
        {
            if (ctx == null) return;
            ctx.accumulatedValues[Key] = 0f;
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
                // 敵視点で「自分が被ダメ受けた」 → finalDamage が受けた量
                int dmg = ctx.finalDamage;
                if (dmg >= 10)
                {
                    ctx.accumulatedValues[Key] = dmg;
                    UnityEngine.Debug.Log($"[鏡映の応答] 蓄積: 次ターン{dmg}反射");
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

    /// <summary>業火の審判官 — 審判の炎: 毎ターン終了時、固定2 + 所持パッシブ数×0.5ダメ（切り上げ、軽減無視）</summary>
    public class JudgmentFlames : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentFlames";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int passives = run?.ownedPassiveItems?.Count ?? 0;
            int weight = UnityEngine.Mathf.CeilToInt(passives * 0.5f);
            int dmg = 2 + weight;
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
            UnityEngine.Debug.Log($"[審判の炎] {dmg}ダメ (固定2 + 所持パッシブ{passives}個×0.5={weight}) → プレイヤー残HP={ctx.enemyCurrentHP}");
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
