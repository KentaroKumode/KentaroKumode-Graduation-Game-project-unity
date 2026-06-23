using System.Linq;
using InventorySystem.PassiveSkills;
using UnityEngine;

namespace GameLoop.Contracts.Effects
{
    // ============================================================
    // 各旅団の効果実装。 1 ファイルに集約 (12 ファイルは過剰)。
    // 正本: docs/specs/contracts.md
    // ============================================================

    /// <summary>1. 傭兵団 ── ターン終了時、 敵に maxHP × 4/8/12% 軽減不能ダメ (上限50)。
    /// 影武者一座との協力中なら ターン終了時に +1G 追加。</summary>
    public class MercenariesEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Mercenaries;

        public override void OnTurnEnd(ContractInstance state, CombatContext ctx, RunState run)
        {
            if (ctx == null) return;
            // 2026-06-20 nerf (10000 ラン Tier 計測で +1.91 寄与 S Tier 単独): 4/8/12% → 3/6/9%, 上限 50 → 40
            float pct = state.level == 1 ? 0.03f : state.level == 2 ? 0.06f : 0.09f;
            int dmg = Mathf.Min(40, Mathf.CeilToInt(ctx.enemyMaxHP * pct));
            if (dmg > 0)
            {
                ctx.enemyCurrentHP = Mathf.Max(0, ctx.enemyCurrentHP - dmg);
                Debug.Log($"[傭兵団] ターン末 軽減不能ダメ {dmg} → 敵HP {ctx.enemyCurrentHP}");
            }

            // 協力 (影武者一座): +1G
            if (ContractManager.Instance.IsAllianceActive(run, Kind))
            {
                run.coins += 1;
                Debug.Log($"[傭兵団+影武者協力] +1G (coins={run.coins})");
            }
        }
    }

    /// <summary>2. 補給キャラバン ── UI 側からの問い合わせで完結 (フラグホルダー)。
    /// CanUseShop/CanUseEnhance/CanUseRest を提供。</summary>
    public class SupplyCaravanEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.SupplyCaravan;

        public static bool CanUseShop(ContractInstance s) => s != null && !s.supplyShopUsedThisLayer;
        public static bool CanUseEnhance(ContractInstance s) => s != null && s.level >= 2 && !s.supplyEnhanceUsedThisLayer;
        public static bool CanUseRest(ContractInstance s) => s != null && s.level >= 3 && !s.supplyRestUsedThisLayer;
    }

    /// <summary>3. 商業連合隊 ── 層終了時 +5/+10/+20G、 戦闘終了時 HP≤50% で その層収入0 (L3 免除)。
    /// 協力 (サーカス団): + サーカスLv × 2G ボーナス (連動消滅)。</summary>
    public class MerchantsLeagueEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.MerchantsLeague;

        public override void OnBattleEnd(ContractInstance state, RunState run, ContractBattleResult result)
        {
            if (state.level >= 3) return; // L3 はペナルティ免除
            if (result.playerMaxHp <= 0) return;
            float ratio = (float)result.finalPlayerHp / result.playerMaxHp;
            if (ratio <= 0.50f)
            {
                state.merchantsLeagueLayerLossFlag = 1;
                Debug.Log($"[商業連合隊] HP≤50% 検出 → その層の収入は0");
            }
        }

        public override void OnLayerEnd(ContractInstance state, RunState run)
        {
            if (state.merchantsLeagueLayerLossFlag != 0) return;
            int reward = state.level == 1 ? 5 : state.level == 2 ? 10 : 20;
            run.coins += reward;
            Debug.Log($"[商業連合隊] 層終了 +{reward}G (coins={run.coins})");

            // 協力 (サーカス団): + サーカスLv × 2G
            if (ContractManager.Instance.IsAllianceActive(run, Kind))
            {
                var circus = ContractManager.Instance.Find(run, ContractKind.OrphanCircus);
                if (circus != null)
                {
                    int bonus = circus.level * 2;
                    run.coins += bonus;
                    Debug.Log($"[商業連合隊+サーカス団協力] +{bonus}G (coins={run.coins})");
                }
            }
        }
    }

    /// <summary>4. 宣教師 ── 戦闘以外の希望減少を -1/-2/-3 (0にはならない)。
    /// 実体: HopeSystem 側から ContractManager.GetMissionariesHopeReduction(run) を呼ぶ静的 API で対応。
    /// 協力 (騎士): -1 追加。</summary>
    public class MissionariesEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Missionaries;
        // hook は無し。 静的 API は ContractManager.GetHopeLossReduction()
    }

    /// <summary>5. 騎士 ── 受けるダメ -1/-2/-3 (最低1通す)。
    /// 戦闘開始時に ctx.playerFlatDamageReduction を加算。
    /// 協力 (宣教師): +1 軽減追加。</summary>
    public class KnightsEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Knights;

        public override void OnBattleStart(ContractInstance state, CombatContext ctx, RunState run)
        {
            if (ctx == null) return;
            int reduction = state.level;
            if (ContractManager.Instance.IsAllianceActive(run, Kind)) reduction += 1;
            ctx.playerFlatDamageReduction += reduction;
            Debug.Log($"[騎士] 被ダメ軽減 +{reduction} (合計 {ctx.playerFlatDamageReduction})");
        }
    }

    /// <summary>6. 暗殺教団 ── 戦闘開始時、 通常戦闘の敵に HP × 33/66/99% 軽減不能ダメ。
    /// 協力 (戦術家): エリートにも 15/30/45% で発動。</summary>
    public class AssassinsEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Assassins;

        public override void OnBattleStart(ContractInstance state, CombatContext ctx, RunState run)
        {
            if (ctx == null) return;
            float pct = 0f;
            // 2026-06-20 nerf (10000 ラン Tier 計測で +1.75 寄与 S Tier): 33/66/99% → 25/50/75% / 協力エリート 15/30/45% → 10/20/30%
            if (ctx.currentEnemyKind == CombatSystem.EnemyKind.Normal)
            {
                pct = state.level == 1 ? 0.25f : state.level == 2 ? 0.50f : 0.75f;
            }
            else if (ctx.currentEnemyKind == CombatSystem.EnemyKind.Elite
                     && ContractManager.Instance.IsAllianceActive(run, Kind))
            {
                // 協力中のみエリートに発動
                pct = state.level == 1 ? 0.10f : state.level == 2 ? 0.20f : 0.30f;
            }
            if (pct <= 0f) return;

            int dmg = Mathf.CeilToInt(ctx.enemyCurrentHP * pct);
            if (dmg > 0)
            {
                ctx.enemyCurrentHP = Mathf.Max(0, ctx.enemyCurrentHP - dmg);
                Debug.Log($"[暗殺教団] 開幕 軽減不能 {(pct*100f):F0}% = {dmg}ダメ → 敵HP {ctx.enemyCurrentHP}");
            }
        }
    }

    /// <summary>7. 旅する錬金術師 ── 戦闘終了時 10/20/30% でパッシブ錬金。
    /// レアリティ重み: L1=B100 / L2=B70+S30 / L3=B50+S35+G15。
    /// 協力 (補給キャラバン): 重み +1 段 (L3 + LEGENDARY 5%)。</summary>
    public class AlchemistEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Alchemist;

        public override void OnBattleEnd(ContractInstance state, RunState run, ContractBattleResult result)
        {
            if (!result.playerWon) return;
            float chance = state.level == 1 ? 0.10f : state.level == 2 ? 0.20f : 0.30f;
            if (Random.value > chance) return;

            bool alliance = ContractManager.Instance.IsAllianceActive(run, Kind);
            int effectiveLevel = Mathf.Min(3, state.level + (alliance ? 1 : 0));
            // L3 + 協力 (= effectiveLevel 4 相当) は L3 + LEGENDARY 5%
            bool grantLegendary = state.level >= 3 && alliance;

            var rarity = RollRarity(effectiveLevel, grantLegendary);
            var passiveId = TryGenerateUnownedPassive(run, rarity);
            if (passiveId == null)
            {
                Debug.Log($"[錬金術師] {rarity} 候補なし → 諦め");
                return;
            }
            // インベ満タンなら諦め (PassiveAddHelper が null 返却 = 失敗)。
            var added = InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, passiveId);
            if (added == null)
            {
                Debug.Log($"[錬金術師] インベ満タン → 諦め ({passiveId})");
                return;
            }
            Debug.Log($"[錬金術師] 錬金成功 ({rarity}): {passiveId}");
        }

        private InventorySystem.ItemRarity RollRarity(int effectiveLevel, bool grantLegendary)
        {
            float r = Random.value;
            if (effectiveLevel <= 1) return InventorySystem.ItemRarity.BRONZE;
            if (effectiveLevel == 2) return r < 0.30f
                ? InventorySystem.ItemRarity.SILVER
                : InventorySystem.ItemRarity.BRONZE;
            if (effectiveLevel == 3 && !grantLegendary)
            {
                if (r < 0.15f) return InventorySystem.ItemRarity.GOLD;
                if (r < 0.50f) return InventorySystem.ItemRarity.SILVER;
                return InventorySystem.ItemRarity.BRONZE;
            }
            // L3 + 協力 → L3 + LEGENDARY 5% 追加
            if (r < 0.05f) return InventorySystem.ItemRarity.LEGENDARY;
            if (r < 0.20f) return InventorySystem.ItemRarity.GOLD;
            if (r < 0.55f) return InventorySystem.ItemRarity.SILVER;
            return InventorySystem.ItemRarity.BRONZE;
        }

        private string TryGenerateUnownedPassive(RunState run, InventorySystem.ItemRarity rarity)
        {
            var db = InventorySystem.ItemDatabase.Instance;
            if (db == null) return null;
            var owned = new System.Collections.Generic.HashSet<string>(run.ownedPassiveItems ?? new System.Collections.Generic.List<string>());
            var candidates = db.GetAllItems()
                .Where(it => (it.category == InventorySystem.ItemCategory.Passive
                              || it.category == InventorySystem.ItemCategory.PassiveItem)
                          && it.rarity == rarity
                          && !owned.Contains(it.internalName))
                .Select(it => it.internalName)
                .ToList();
            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }
    }

    /// <summary>8. 放浪医術官 ── 戦闘終了時 (maxHP - currentHP) × 10/20/30% 切り上げ回復。
    /// 商業連合隊の HP50% 判定の **前** に発火する必要がある (Manager 側で順序保証)。
    /// 協力 (狩猟旅団): 与ダメ × 5/10/15% 追加回復。</summary>
    public class WanderingDoctorEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.WanderingDoctor;

        public override void OnBattleEnd(ContractInstance state, RunState run, ContractBattleResult result)
        {
            if (run == null) return;
            int missing = result.playerMaxHp - result.finalPlayerHp;
            if (missing <= 0) return;
            float pct = state.level == 1 ? 0.10f : state.level == 2 ? 0.20f : 0.30f;
            int heal = Mathf.CeilToInt(missing * pct);

            // 協力 (狩猟旅団): 与ダメ × 5/10/15% 追加
            if (ContractManager.Instance.IsAllianceActive(run, Kind))
            {
                float bonusPct = state.level == 1 ? 0.05f : state.level == 2 ? 0.10f : 0.15f;
                int bonusHeal = Mathf.CeilToInt(result.totalDamageDealt * bonusPct);
                heal += bonusHeal;
                Debug.Log($"[医術官+狩猟旅団協力] 与ダメ {result.totalDamageDealt} × {(bonusPct*100f):F0}% = +{bonusHeal} 回復");
            }

            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + heal);
            // result.finalPlayerHp も同期 (後段の商業連合隊判定用)
            result.finalPlayerHp = run.playerHP;
            Debug.Log($"[医術官] +{heal} HP回復 → {run.playerHP}/{run.playerMaxHP}");
        }
    }

    /// <summary>9. 捨て子のサーカス団 ── 戦闘効果なし。 イベント側で参照。</summary>
    public class OrphanCircusEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.OrphanCircus;
    }

    /// <summary>10. 影武者一座 ── HP0 になるダメージで HP=ceil(maxHP×0.10) で復活。
    /// ラン全体で 1/2/3 回 (リチャージ無し)。 死亡判定 hook は ContractManager.TryRevive() で外部から呼ぶ。</summary>
    public class BodyDoublesEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.BodyDoubles;
        // 死亡フック点は CombatManager で ContractManager.TryReviveOnLethal() を呼ぶ形にする
    }

    /// <summary>11. 狩猟旅団 ── 戦闘開始時、 敵に脆弱付与 (armed)。 倍率 15/30/45%。</summary>
    public class HuntersEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Hunters;

        public override void OnBattleStart(ContractInstance state, CombatContext ctx, RunState run)
        {
            if (ctx == null) return;
            CombatSystem.VulnerabilityStatus.Apply(ctx, state.level);
            Debug.Log($"[狩猟旅団] 脆弱付与 (倍率 ×{1f + CombatSystem.VulnerabilityStatus.GetMultiplierForLevel(state.level):F2})");
        }
    }

    /// <summary>12. 戦術家 ── 戦闘中、 ロール振り直し 1/2/3 回 (戦闘ごとリセット)。
    /// ロール画面側から ContractManager.TryConsumeReroll() を呼ぶ。</summary>
    public class TacticiansEffect : ContractEffectBase
    {
        public override ContractKind Kind => ContractKind.Tacticians;

        public override void OnBattleStart(ContractInstance state, CombatContext ctx, RunState run)
        {
            state.tacticiansRerollsRemainingThisCombat = state.level;
            Debug.Log($"[戦術家] 振り直し残 {state.level} 回チャージ");
        }
    }
}
