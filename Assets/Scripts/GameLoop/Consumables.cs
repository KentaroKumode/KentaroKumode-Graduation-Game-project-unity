using UnityEngine;
using CombatSystem;
using InventorySystem;
using InventorySystem.PassiveSkills;

namespace GameLoop
{
    /// <summary>
    /// RunState に対する消費アイテム使用処理（マップ進行モデル / 戦闘内 両対応）。
    ///
    /// ・戦闘外で使用 → 即時系はその場適用、戦闘バフ系は RunState.pending* に保持
    ///   （次戦闘開始時に CombatManager が ctx へコピー）。
    /// ・戦闘中で使用 → 戦闘バフ系は現在の CombatContext へ即時書き込み（使用ターンから発動）。
    /// 戦闘終了で ctx は破棄されるため効果は自動消滅。
    ///
    /// id 体系: cons_<family>_<tier1-4> / uniq_<name>
    /// </summary>
    public static class Consumables
    {
        private static CombatContext ActiveCtx()
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return null;
            return PassiveSkillManager.Instance?.Context;
        }

        /// <summary>シュヴァリエのレイピア: 戦闘中に使用するとコントラタックを切替（消費されない）。
        /// 1回目: プレイヤー 1d1 強制 + 被ダメ50%軽減 + 軽減x2 反射（永続トグル）。
        /// 2回目: 解除 → 次ターン ダイス+1 / クリティカル補正+9 (1ターン限定)。
        /// パッシブアイテム所持(`chevalier_rapier`)が条件。</summary>
        public static bool TryUseRapier(RunState run)
        {
            if (run == null || run.ownedPassiveItems == null
                || !run.ownedPassiveItems.Contains("chevalier_rapier")) return false;
            var ctx = ActiveCtx();
            if (ctx == null) { Debug.Log("[レイピア] 戦闘外では使用不可"); return false; }
            if (ctx.consumablesLocked) { Debug.Log("[レイピア] 死翔により使用不可"); return false; }

            // 覚者用アクションマーク（レイピア起動も観想中断とみなす）
            ctx.consumablesUsedThisTurn = true;

            if (ctx.GetAccumulated("player_contre") <= 0)
            {
                ctx.accumulatedValues["player_contre"] = 1;
                Debug.Log("[レイピア] コントラタック発動: 1d1強制 / 被ダメ-50% / 軽減x2反射");
            }
            else
            {
                ctx.accumulatedValues["player_contre"] = 0;
                ctx.accumulatedValues["rapier_release_pending"] = 1;
                // 会心+9 補正は サン=ジョリオラ撃破済みのランでのみ付与
                if (run.defeatedSaintGeorges)
                {
                    ctx.nextTurnBuffs["criticalBonus"] = 9;
                    Debug.Log("[レイピア] コントラタック解除: 次T ダイス+1 / クリティカル+9 (真の決闘術)");
                }
                else
                {
                    Debug.Log("[レイピア] コントラタック解除: 次T ダイス+1 (剣聖未撃破のため会心補正なし)");
                }
            }
            return true;
        }

        /// <summary>所持から id を1つ消費し効果適用。意味があった場合 true。</summary>
        public static bool Use(RunState run, string id)
        {
            if (run == null || string.IsNullOrEmpty(id)) return false;
            if (run.ownedConsumables == null || !run.ownedConsumables.Contains(id)) return false;
            // 精鋭ハーピィ「死翔」: この戦闘中は消費アイテム使用不可
            var lockCtx = ActiveCtx();
            if (lockCtx != null && lockCtx.consumablesLocked)
            {
                Debug.Log("[Consumables] 死翔により使用不可");
                return false;
            }

            bool ok = Apply(run, id);
            if (ok)
            {
                run.ownedConsumables.Remove(id);
                // 覚者「悟達の試練」用: 当ターン アクション発生をマーク
                var actCtx = ActiveCtx();
                if (actCtx != null) actCtx.consumablesUsedThisTurn = true;
                Debug.Log($"[Consumables] 使用: {id}");
            }
            return ok;
        }

        /// <summary>所持リストを介さず効果のみ適用（UI/グリッド経路用。削除はグリッド側が行う）。</summary>
        public static bool ApplyDirect(RunState run, string id)
            => run != null && !string.IsNullOrEmpty(id) && Apply(run, id);

        /// <summary>効果適用本体（リスト削除はしない）。</summary>
        private static bool Apply(RunState run, string id)
        {
            var ctx = ActiveCtx();

            switch (id)
            {
                // ===== 即時回復: 最大HPの割合（戦闘中はctx、外はrun） =====
                case "cons_heal_1": return HealPct(run, ctx, 0.25f);
                case "cons_heal_2": return HealPct(run, ctx, 0.50f);
                case "cons_heal_3": return HealPct(run, ctx, 0.75f);
                case "cons_heal_4": return HealPct(run, ctx, 1.00f);

                // ===== 攻撃力: 使用ターンの勝利時に与ダメ+X（単発） =====
                case "cons_atk_1": return SetBurst(run, ctx, 3);
                case "cons_atk_2": return SetBurst(run, ctx, 6);
                case "cons_atk_3": return SetBurst(run, ctx, 9);
                case "cons_atk_4": return SetBurst(run, ctx, 15);

                // ===== ダイス補正: 勝敗のみ+X（ダメージ非加算・次2戦闘ずっと） =====
                case "cons_dice_1": return AddInt(run, ctx, "dice", 1, durationBattles: 2);
                case "cons_dice_2": return AddInt(run, ctx, "dice", 2, durationBattles: 2);
                case "cons_dice_3": return AddInt(run, ctx, "dice", 3, durationBattles: 2);
                case "cons_dice_4": return AddInt(run, ctx, "dice", 4, durationBattles: 2);

                // ===== シールド: 吸収量 / 残ターン(-1=無制限) =====
                case "cons_shield_1": return SetShield(run, ctx, 8, 2);
                case "cons_shield_2": return SetShield(run, ctx, 18, 4);
                case "cons_shield_3": return SetShield(run, ctx, 35, 6);
                case "cons_shield_4": return SetShield(run, ctx, 50, -1);

                // ===== 継続回復: 初期X、毎ターン終了時+X→X-- =====
                case "cons_regen_1": return SetRegen(run, ctx, 5);
                case "cons_regen_2": return SetRegen(run, ctx, 7);
                case "cons_regen_3": return SetRegen(run, ctx, 9);
                case "cons_regen_4": return SetRegen(run, ctx, 12);

                // ===== 会心率+X(/9)（次2戦闘ずっと） =====
                case "cons_crit_1": return AddInt(run, ctx, "crit", 1, durationBattles: 2);
                case "cons_crit_2": return AddInt(run, ctx, "crit", 2, durationBattles: 2);
                case "cons_crit_3": return AddInt(run, ctx, "crit", 3, durationBattles: 2);
                case "cons_crit_4": return AddInt(run, ctx, "crit", 4, durationBattles: 2);

                // ===== 定数軽減: 被ダメ毎ターン-X（次2戦闘ずっと） =====
                case "cons_reduce_1": return AddInt(run, ctx, "reduce", 1, durationBattles: 2);
                case "cons_reduce_2": return AddInt(run, ctx, "reduce", 2, durationBattles: 2);
                case "cons_reduce_3": return AddInt(run, ctx, "reduce", 3, durationBattles: 2);
                case "cons_reduce_4": return AddInt(run, ctx, "reduce", 5, durationBattles: 2);

                // ===== 食料: 希望を回復（飢餓→希望統合・ADR-0002・戦闘外専用） =====
                case "cons_food_1": return RestoreHope(10);
                case "cons_food_2": return RestoreHope(20);
                case "cons_food_3": return RestoreHope(30);
                case "cons_food_4": return RestoreHope(45);

                // ===== ユニーク =====
                case "uniq_phil_stone":  // 賢者の石(2026-06-04): GOLD変換を廃止し、無条件で強化素材+1（最大5回/ラン）。
                {                        // ※変動サイズ素材の実装後は「ランダムサイズ1個」に拡張。
                    if (run.philStoneUsed >= 5) return false;
                    run.weaponMaterials++; run.philStoneUsed++;
                    return true;
                }
                case "uniq_forge_elixir": // 鍛冶の霊薬: 武器を1段階無償強化（次Tier→無ければ限界突破）
                    return GameLoop.GameManager.TryUpgradeWeapon(run, free: true);
                case "uniq_mirror":       // 鏡写しの水晶: 被メインダメ反射（全戦闘）
                    return SetBool(run, ctx, "reflect");
                case "uniq_gambler":      // 賭博師のダイス: 50%全最大/50%全1
                    return SetBool(run, ctx, "gambler");
                case "uniq_ambush":       // 奇襲の発煙筒: 敵開始HP-15%（戦闘開始時のみ）
                    if (ctx != null)
                    {
                        int cut = Mathf.CeilToInt(ctx.enemyMaxHP * 0.15f);
                        ctx.enemyCurrentHP = Mathf.Max(1, ctx.enemyCurrentHP - cut);
                        return true;
                    }
                    run.pendingEnemyStartHpCutPct = 15; return true;
                case "uniq_food_horn":    // 満腹の角笛: 希望を大きく回復（飢餓→希望統合・ADR-0002）
                    return RestoreHope(25);
                case "uniq_appraise":     // 鑑定の眼鏡: 次の宝箱/ショップ最低レアSILVER
                    run.nextLootMinRarity = (int)ItemRarity.SILVER;
                    return true;
                case "uniq_merchant_bell":// 商人の鈴: 次ショップ全価格半額
                    run.nextShopHalfPrice = true;
                    return true;
                case "uniq_oni_oil":      // 鬼火の油: 与ダメ+50%（全戦闘）
                    return AddInt(run, ctx, "dmgmult", 50);
                case "uniq_earth_guard":  // 鉄壁の土塊: 被ダメ毎ターン-3（全戦闘）
                    return AddInt(run, ctx, "reduce", 3);
                case "uniq_haste_powder": // 加速の粉: 初回(次)ロールのダイス合計+5
                    if (ctx != null)
                    {
                        ctx.nextTurnBuffs["diceBonus"] =
                            (ctx.nextTurnBuffs.TryGetValue("diceBonus", out var db) ? db : 0f) + 5f;
                    }
                    else run.pendingFirstRollTotal += 5;
                    return true;

                default:
                    return false;
            }
        }

        // ===== 効果適用ヘルパー =====

        private static bool HealPct(RunState run, CombatContext ctx, float pct)
        {
            if (ctx != null)
            {
                var cm = CombatManager.Instance;
                if (cm == null || cm.PlayerHP >= cm.PlayerMaxHP) return false;
                int amt = Mathf.CeilToInt(cm.PlayerMaxHP * pct);
                cm.HealPlayer(amt);   // playerHP/ctx 双方を更新
                return true;
            }
            if (run.playerHP >= run.playerMaxHP) return false;
            int a = Mathf.CeilToInt(run.playerMaxHP * pct);
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + a);
            return true;
        }

        private static bool SetBurst(RunState run, CombatContext ctx, int x)
        {
            if (ctx != null) ctx.consAtkBurst = Mathf.Max(ctx.consAtkBurst, x);
            else run.pendingConsAtkBurst = Mathf.Max(run.pendingConsAtkBurst, x);
            return true;
        }

        private static bool SetShield(RunState run, CombatContext ctx, int amt, int turns)
        {
            if (ctx != null) { int g = Mathf.Max(0, amt - ctx.healShieldReduction); ctx.consShield = g; ctx.shieldGainedTotal += g; ctx.consShieldExpireTurn = turns; } // 天衣無縫減衰＋検証計測
            else { run.pendingConsShield = amt; run.pendingConsShieldTurns = turns; }
            return true;
        }

        private static bool SetRegen(RunState run, CombatContext ctx, int x)
        {
            if (ctx != null) ctx.consRegen = Mathf.Max(ctx.consRegen, x);
            else run.pendingConsRegen = Mathf.Max(run.pendingConsRegen, x);
            return true;
        }

        // durationBattles: dice/crit/reduce が次戦闘以降も持続する戦闘数。 1=次戦闘1回のみ（既定）、 2=次戦闘から2回。
        // 戦闘中使用時は当戦闘も1戦としてカウント＝持ち越し戦闘数は durationBattles-1。
        private static bool AddInt(RunState run, CombatContext ctx, string kind, int v, int durationBattles = 1)
        {
            if (ctx != null)
            {
                switch (kind)
                {
                    case "dice":    ctx.consDiceRoll += v; break;
                    case "crit":    ctx.consCrit += v; break;
                    case "reduce":  ctx.consFlatReduce += v; break;
                    case "dmgmult": ctx.consDmgMultPct += v; break;
                }
                // 戦闘中使用でもマルチバトル系は次戦闘以降にも持ち越す（残戦闘数 = duration - 1、 当戦闘分を控除）
                if (run != null && durationBattles > 1)
                {
                    int carry = durationBattles - 1;
                    switch (kind)
                    {
                        case "dice":   run.pendingConsDiceRoll = v;     run.pendingConsDiceRollBattles    = Mathf.Max(run.pendingConsDiceRollBattles, carry); break;
                        case "crit":   run.pendingConsCrit = v;         run.pendingConsCritBattles        = Mathf.Max(run.pendingConsCritBattles, carry); break;
                        case "reduce": run.pendingConsFlatReduce = v;   run.pendingConsFlatReduceBattles  = Mathf.Max(run.pendingConsFlatReduceBattles, carry); break;
                    }
                }
            }
            else
            {
                switch (kind)
                {
                    case "dice":    run.pendingConsDiceRoll += v;
                                    run.pendingConsDiceRollBattles    = Mathf.Max(run.pendingConsDiceRollBattles, durationBattles); break;
                    case "crit":    run.pendingConsCrit += v;
                                    run.pendingConsCritBattles        = Mathf.Max(run.pendingConsCritBattles, durationBattles); break;
                    case "reduce":  run.pendingConsFlatReduce += v;
                                    run.pendingConsFlatReduceBattles  = Mathf.Max(run.pendingConsFlatReduceBattles, durationBattles); break;
                    case "dmgmult": run.pendingConsDmgMultPct += v; break;
                }
            }
            return true;
        }

        private static bool SetBool(RunState run, CombatContext ctx, string kind)
        {
            if (ctx != null)
            {
                if (kind == "reflect") ctx.consReflect = true;
                else if (kind == "gambler") ctx.gamblerArmed = true;
            }
            else
            {
                if (kind == "reflect") run.pendingConsReflect = true;
                else if (kind == "gambler") run.pendingGamblerDice = true;
            }
            return true;
        }

        private static bool RestoreHope(int amount)
        {
            var run = GameManager.Instance?.Run;
            if (run == null) return false;
            HopeSystem.ApplyFood(run, amount);
            return true;
        }

        // ===== ボット支援: 種別判定 =====

        /// <summary>戦闘開始直後に使うべきバフ系（攻撃/ダイス/シールド/会心/軽減/継続/鬼火/反射/賭博/奇襲）か。</summary>
        public static bool IsCombatBuff(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.StartsWith("cons_atk_") || id.StartsWith("cons_dice_")
                || id.StartsWith("cons_shield_") || id.StartsWith("cons_regen_")
                || id.StartsWith("cons_crit_") || id.StartsWith("cons_reduce_")
                || id == "uniq_mirror" || id == "uniq_gambler" || id == "uniq_ambush"
                || id == "uniq_oni_oil" || id == "uniq_earth_guard" || id == "uniq_haste_powder";
        }

        /// <summary>緊急回復に使える即時回復系か。</summary>
        public static bool IsHeal(string id) => !string.IsNullOrEmpty(id) && id.StartsWith("cons_heal_");

        /// <summary>id の即時回復割合（heal系以外は0）。</summary>
        public static float HealRatio(string id)
        {
            switch (id)
            {
                case "cons_heal_1": return 0.25f;
                case "cons_heal_2": return 0.50f;
                case "cons_heal_3": return 0.75f;
                case "cons_heal_4": return 1.00f;
                default: return 0f;
            }
        }

        /// <summary>
        /// HP割合が urgencyRatio 以下なら、不足分を最も無駄なく埋める即時回復を1個使う。
        /// 過剰回復を避け、不足を満たせない場合は最大の回復を使う。使ったら true。
        /// </summary>
        public static bool TryUseBestHeal(RunState run, float urgencyRatio)
        {
            if (run?.ownedConsumables == null || run.ownedConsumables.Count == 0) return false;
            if (run.playerMaxHP <= 0) return false;

            float ratio = (float)run.playerHP / run.playerMaxHP;
            if (ratio > urgencyRatio || ratio >= 1f) return false;

            float missingFrac = 1f - ratio;
            string bestCover = null; float bestCoverR = 99f;
            string bestAny = null;   float bestAnyR = -1f;
            foreach (var id in run.ownedConsumables)
            {
                float r = HealRatio(id);
                if (r <= 0f) continue;
                if (r >= missingFrac && r < bestCoverR) { bestCover = id; bestCoverR = r; }
                if (r > bestAnyR) { bestAny = id; bestAnyR = r; }
            }
            string pick = bestCover ?? bestAny;
            return pick != null && Use(run, pick);
        }

        /// <summary>id が食料（希望回復）系か。</summary>
        public static bool IsFood(string id)
            => !string.IsNullOrEmpty(id) && (id.StartsWith("cons_food_") || id == "uniq_food_horn");

        /// <summary>食料の希望回復量（食料以外は0）。</summary>
        public static int FoodHopeAmount(string id)
        {
            switch (id)
            {
                case "cons_food_1": return 10;
                case "cons_food_2": return 20;
                case "cons_food_3": return 30;
                case "cons_food_4": return 45;
                case "uniq_food_horn": return 25;
                default: return 0;
            }
        }

        /// <summary>希望が上限未満なら、不足を最も無駄なく埋める食料を1個使って希望を回復する。使ったら true。
        /// 佯狂者の冠で希望0固定中は発狂狙いのため回復しない（浪費回避）。bot/オートランナー用。</summary>
        public static bool TryUseBestFood(RunState run)
        {
            if (run?.ownedConsumables == null || run.ownedConsumables.Count == 0) return false;
            if (run.crownHopeLocked) return false; // 発狂固定中は回復不可＝浪費しない
            int missing = run.hopeCap - run.hope;
            if (missing <= 0) return false;

            string bestCover = null; int bestCoverA = int.MaxValue;
            string bestAny = null;   int bestAnyA = -1;
            foreach (var id in run.ownedConsumables)
            {
                int a = FoodHopeAmount(id);
                if (a <= 0) continue;
                if (a >= missing && a < bestCoverA) { bestCover = id; bestCoverA = a; }
                if (a > bestAnyA) { bestAny = id; bestAnyA = a; }
            }
            string pick = bestCover ?? bestAny;
            return pick != null && Use(run, pick);
        }
    }
}
