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
                || !run.ownedPassiveItems.Contains(ItemIds.ChevalierRapier)) return false;
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
                    ctx.nextTurnBuffs[InventorySystem.PassiveSkills.CombatContext.CritRateBuffKey] = 0.50f;
                    Debug.Log("[レイピア] コントラタック解除: 次T ダイス+1 / 会心率+50% (真の決闘術)");
                }
                else
                {
                    Debug.Log("[レイピア] コントラタック解除: 次T ダイス+1 (剣聖未撃破のため会心補正なし)");
                }
            }
            return true;
        }

        /// <summary>[計装 2026-09-14] ヴェスカ連戦の<b>段別</b>消耗品使用数
        /// (index 0=p1 / 1=p2 / 2=p3 / 3=p4)。
        ///
        /// <para><b>「p1〜p3 は致命傷にならないだけで、 消耗はさせているのか」を分けるため。</b>
        /// 段の勝率 (p1 100% / p2 99.9% / p3 98.6%) だけを見ると何もしていないように読めるが、
        /// 通貨は HP と消耗品なので、 そこを数えないと結論が出せない。</para></summary>
        public static readonly long[] VescaPhaseConsumables = new long[4];
        public static void ResetVescaPhaseStats()
        { System.Array.Clear(VescaPhaseConsumables, 0, VescaPhaseConsumables.Length); }

        private static void NoteVescaPhaseUse()
        {
            var c = ActiveCtx();
            string b = c != null ? c.bossId : null;
            if (string.IsNullOrEmpty(b) || !b.StartsWith(BossIds.Layer7Prefix)) return;
            int i = b.EndsWith("_p4") ? 3 : b.EndsWith("_p3") ? 2 : b.EndsWith("_p2") ? 1 : 0;
            VescaPhaseConsumables[i]++;
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
                MetaProgression.Achievements.AchievementService.NoteConsumableUsed();
                // 覚者「悟達の試練」用: 当ターン アクション発生をマーク
                var actCtx = ActiveCtx();
                if (actCtx != null) actCtx.consumablesUsedThisTurn = true;
                NoteVescaPhaseUse();
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
                // ===== 3 系統 × Tier1〜4 (2026-08-04 再編。 詳細と経緯は ItemIds.ConsHealFamily 近傍) =====
                //   **Tier 軸は効果量のみ。持続は全 Tier「その戦闘中」で固定。**
                //   旧設計は「効果量↓ × 持続↑」だったため実効量が戦闘の長さで逆転し、
                //   通常戦では高 Tier ほど弱いという状態になっていた。

                // 回復: 最大HP割合の即時回復
                case "小回復薬": return HealPct(run, ctx, 0.25f);
                case "回復薬": return HealPct(run, ctx, 0.40f);
                case "上回復薬": return HealPct(run, ctx, 0.60f);
                case "完全回復薬": return HealPct(run, ctx, 1.00f);

                // シールド: 戦闘中持続 (expireTurn=-1)。 **戦闘中にも使用できる** ──
                //   通常戦が 3〜4T になり「削られてから張る」判断が成立するようになったため。
                case "木の護符": return AddShield(run, ctx, 15);
                case "鉄の護符": return AddShield(run, ctx, 30);
                case "銀の護符": return AddShield(run, ctx, 50);
                case "惜別の護符": return AddShield(run, ctx, 80);

                // 攻撃強化: 与ダメ +X% (その戦闘中)。 outgoingDamageMultiplier は**加算プール**
                //   (実測 ×2.00) なので、 +75% でも最終与ダメは ×1.375 に薄まる。
                case "鬼火の油": return AddTimedDmgMult(run, ctx, 15, -1);
                case "燐の油": return AddTimedDmgMult(run, ctx, 30, -1);
                case "業火の膏薬": return AddTimedDmgMult(run, ctx, 50, -1);
                case "天火の膏薬": return AddTimedDmgMult(run, ctx, 75, -1);

                // 希望回復: +5/10/15/20 (2026-08-05 追加)。 **上限は伸びず、 現在値だけ戻す。**
                //   希望は横移動 -5 / 戦闘 -N と一方的に減るだけで回復源が無かった。
                //   前哨基地での自動回復 (減少分の20%) は毎層リセットになり、 発狂到達率が
                //   15.6% → 0.4% と資源性を失ったので棄却。 **ゴールドを払う形**に置き換えた。
                case "湯気の立つ椀": return RecoverHope(run, 5);
                case "古い手紙": return RecoverHope(run, 10);
                case "凱旋の記憶": return RecoverHope(run, 15);
                case "希望の欠片": return RecoverHope(run, 20);

                // ===== 賢者の石: 10ゴールドを武器強化素材1に変換 (最大5回/ラン) =====
                case ItemIds.PhilStone:
                {
                    if (run.philStoneUsed >= 5) return false;
                    run.weaponMaterials++; run.philStoneUsed++;
                    return true;
                }

                // 2026-06-28: 職業スターター消耗品 4 種 (剣士/騎士/狂戦士/暗殺者)
                case ClassStarter.PolishId: // 瞬間研磨剤 (剣士): 次の 1 撃だけ 与ダメージ+150%
                    if (ctx != null) ctx.polishArmed = true;
                    else run.pendingPolishArmed = true;
                    return true;

                case ClassStarter.OathId: // 不抜の聖紋 (騎士): HP 80% 維持中 被ダメ -40% (戦闘内のみ意味あり)
                    if (ctx != null)
                    {
                        // 戦闘中使用なら直ちに装着。 現 HP が既に 80% 未満なら oathBroken で実質無効
                        ctx.oathArmed = true;
                        if (ctx.playerMaxHP > 0 && ctx.playerCurrentHP * 5 < ctx.playerMaxHP * 4)
                            ctx.oathBroken = true;
                    }
                    else run.pendingOathArmed = true;
                    return true;

                case ClassStarter.PainkillerId: // 痛覚遮断剤 (狂戦士): 戦闘中使用専用
                    if (ctx == null) { Debug.Log("[痛覚遮断剤] 戦闘外では使用不可"); return false; }
                    ctx.painkillerArmedThisTurn = true;
                    return true;

                case ClassStarter.DaggerId: // 仕込み刃 (暗殺者): 次のロール敗北で無効化+反射
                    if (ctx != null) ctx.daggerArmed = true;
                    else run.pendingDaggerArmed = true;
                    return true;

                default:
                    return false;
            }
        }

        // ===== 効果適用ヘルパー =====

        private static bool HealPct(RunState run, CombatContext ctx, float pct)
        {
            // T4-A〈破綻〉: 全ての回復量 −25% (§15-2 v3.0)。
            pct *= MetaProgression.MetaDebuffApplicator.GetHealMultiplier();
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
            a = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(a, run);
            int before = run.playerHP;
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + a);
            MetaProgression.MetaDebuffApplicator.NoteHeal(a, run.playerHP - before, run);
            return true;
        }

        /// <summary>攻撃バフ (cons_atk_*)。 ダイス合計+bonus を turns ターン持続 (-1=戦闘中永続)。
        /// 既存バフより短ければ turns は延長のみ、 bonus は加算。</summary>
        private static bool AddTimedDice(RunState run, CombatContext ctx, int bonus, int turns)
        {
            if (ctx != null)
            {
                ctx.consDiceRoll += bonus;
                if (turns == -1 || ctx.consDiceRollTurnsLeft == -1) ctx.consDiceRollTurnsLeft = -1;
                else ctx.consDiceRollTurnsLeft = Mathf.Max(ctx.consDiceRollTurnsLeft, turns);
            }
            else if (run != null)
            {
                run.pendingConsDiceRoll += bonus;
                if (turns == -1 || run.pendingConsDiceRollTurns == -1) run.pendingConsDiceRollTurns = -1;
                else run.pendingConsDiceRollTurns = Mathf.Max(run.pendingConsDiceRollTurns, turns);
            }
            return true;
        }

        /// <summary>与ダメ倍率バフ (cons_dmg_*)。 outgoing +pct% を turns ターン持続 (-1=戦闘中永続)。</summary>
        private static bool AddTimedDmgMult(RunState run, CombatContext ctx, int pct, int turns)
        {
            if (ctx != null)
            {
                ctx.consDmgMultPct += pct;
                if (turns == -1 || ctx.consDmgMultTurnsLeft == -1) ctx.consDmgMultTurnsLeft = -1;
                else ctx.consDmgMultTurnsLeft = Mathf.Max(ctx.consDmgMultTurnsLeft, turns);
            }
            else if (run != null)
            {
                run.pendingConsDmgMultPct += pct;
                if (turns == -1 || run.pendingConsDmgMultTurns == -1) run.pendingConsDmgMultTurns = -1;
                else run.pendingConsDmgMultTurns = Mathf.Max(run.pendingConsDmgMultTurns, turns);
            }
            return true;
        }

        /// <summary>希望回復 (cons_hope_*)。 **上限 hopeCap でクランプされ、 上限自体は伸びない。**
        /// 佯狂者の冠で希望0固定中は <see cref="HopeSystem.Recover"/> 側が弾く。
        /// 既に満タンなら消費させない (false を返す) ── 無駄撃ちを防ぐ。</summary>
        private static bool RecoverHope(RunState run, int amount)
        {
            if (run == null) return false;
            if (run.hope >= run.hopeCap) return false;
            int before = run.hope;
            HopeSystem.Recover(run, amount);
            return run.hope > before;
        }

        /// <summary>開幕シールド (cons_def_*)。 戦闘中持続 (expireTurn=-1)、 天衣無縫減衰を適用。</summary>
        private static bool AddShield(RunState run, CombatContext ctx, int amount)
        {
            if (ctx != null)
            {
                int gained = Mathf.Max(0, amount - ctx.healShieldReduction);
                ctx.consShield += gained;
                CombatSystem.ShieldDiag.Note("消耗品", gained);
                ctx.shieldGainedTotal += gained;
                ctx.consShieldExpireTurn = -1;
            }
            else if (run != null)
            {
                run.pendingConsShield += amount;
                run.pendingConsShieldTurns = -1;
            }
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
                    case "crit":    ctx.consCritPct += v / 100f; break;   // v は % 表記
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
                        case "crit":   run.pendingConsCritPct = v / 100f;         run.pendingConsCritBattles        = Mathf.Max(run.pendingConsCritBattles, carry); break;
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
                    case "crit":    run.pendingConsCritPct += v / 100f;
                                    run.pendingConsCritBattles        = Mathf.Max(run.pendingConsCritBattles, durationBattles); break;
                    case "reduce":  run.pendingConsFlatReduce += v;
                                    run.pendingConsFlatReduceBattles  = Mathf.Max(run.pendingConsFlatReduceBattles, durationBattles); break;
                    case "dmgmult": run.pendingConsDmgMultPct += v; break;
                }
            }
            return true;
        }

        // 2026-08-04: RestoreHope は cons_food_* 専用だったため、 同系統の廃止とあわせて削除。

        // ===== ボット支援: 種別判定 =====

        /// <summary>戦闘開始直後に使うべきバフ系（攻撃/防御/与ダメ倍率）か。</summary>
        public static bool IsCombatBuff(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            // cons_atk_* は 2026-08-04 の再編で廃止 (与ダメ% の cons_dmg_* へ統合)。
            var fam = ItemIds.ConsFamilyOf(id);
            return fam == ItemIds.ConsShieldFamily || fam == ItemIds.ConsPowerFamily;
        }

        /// <summary>緊急回復に使える即時回復系か。</summary>
        public static bool IsHeal(string id) => ItemIds.ConsFamilyOf(id) == ItemIds.ConsHealFamily;

        /// <summary>id の即時回復割合（heal系以外は0）。</summary>
        public static float HealRatio(string id)
        {
            switch (id)
            {
                case "小回復薬": return 0.25f;
                case "回復薬": return 0.40f;
                case "上回復薬": return 0.60f;
                case "完全回復薬": return 1.00f;
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

        // 2026-08-04: 食料 (希望回復) 系 cons_food_* を廃止。 消費アイテムを
        //   回復 / シールド / 攻撃強化 の 3 系統に絞ったため、 IsFood / FoodHopeAmount /
        //   TryUseBestFood もここで削除した。 希望の回復手段はイベント・報酬側に残っている。
    }
}
