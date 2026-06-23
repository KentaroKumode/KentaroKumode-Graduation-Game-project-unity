using System.Collections.Generic;
using System.Linq;
using GameLoop;
using GameLoop.Contracts;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// AutoRunner 用の契約自動取捨選択 AI。
    ///
    /// 評価軸:
    ///   1. 基本価値 (旅団ごと、 レベル線形 or 段階的)
    ///   2. 協力ボーナス (既存契約と alliance なら +30%)
    ///   3. 敵対ペナルティ (取得すると既存 L3 が没収される場合に重く減算)
    ///   4. 影武者は 「ラン全体保険」 として常に高評価
    ///   5. 経済系 (商業連合隊・サーカス・補給キャラバン) は floor 早期ほど高評価
    ///
    /// 維持費不足時は (value - cost) 昇順で切り捨て候補を選ぶ。
    /// </summary>
    public static class ContractAiPicker
    {
        /// <summary>旅団ごとの L1 基本価値。 レベルアップで線形にスケール (L2 = ×2、 L3 = ×3)。
        /// 数値は 「コスト3G に対しての主観評価」 で 6 以上なら採用ライン。</summary>
        private static readonly Dictionary<ContractKind, float> BaseValueL1 = new Dictionary<ContractKind, float>
        {
            { ContractKind.Knights,         6f },  // 軽減 -1 ≒ 体感大
            { ContractKind.Assassins,       4f },  // 通常戦特化 (序盤強・終盤頭打ち)
            { ContractKind.Mercenaries,     5f },  // DoT 安定
            { ContractKind.MerchantsLeague, 5f },  // 5G/層 ≒ 投資回収可
            { ContractKind.SupplyCaravan,   4f },  // ショップ任意 (経済前提)
            { ContractKind.Missionaries,    3f },  // 希望 -1 はマップ動線で効く
            { ContractKind.Alchemist,       4f },  // 10% で BRONZE は控えめ
            { ContractKind.WanderingDoctor, 5f },  // 戦闘ごと回復は高効率
            { ContractKind.OrphanCircus,    2f },  // 効果無し、 windfall 期待値
            { ContractKind.BodyDoubles,     8f },  // 保険は AutoRunner で価値特大
            { ContractKind.Hunters,         6f },  // 会心ビルドで強い
            { ContractKind.Tacticians,      5f },  // 振り直しはロール救済
        };

        /// <summary>協力ボーナス係数 (alliance 既契約と合致なら value ×1.30)。</summary>
        public const float AllianceBonus = 1.30f;
        /// <summary>敵対した既存契約を没収する場合のペナルティ係数。</summary>
        public const float RivalryPenaltyL3 = -6f;  // L3 没収は重い
        public const float RivalryPenaltyL2 = -3f;
        public const float RivalryPenaltyL1 = -1f;
        /// <summary>採用判定の基準: value >= cost × AcceptThreshold なら取得。</summary>
        public const float AcceptThreshold = 1.5f;

        /// <summary>提示された契約候補の中から、 取得すべきものを選ぶ。
        /// ゴールド予算内で 高価値順に採用。</summary>
        public static List<ContractKind> PickOffers(RunState run, List<ContractKind> offers, int budget)
        {
            var picks = new List<ContractKind>();
            if (run == null || offers == null) return picks;
            int remaining = budget;
            // 価値降順
            var scored = offers
                .Select(k => (kind: k, value: EvaluateNewContract(run, k, level: 1)))
                .OrderByDescending(s => s.value)
                .ToList();
            foreach (var (kind, value) in scored)
            {
                int cost = ContractCost.For(1);
                if (cost > remaining) continue;
                if (value < cost * AcceptThreshold) continue;
                picks.Add(kind);
                remaining -= cost;
            }
            return picks;
        }

        /// <summary>既存契約をレベルアップさせるか判定。 各層で 1 契約まで延長候補にする。</summary>
        public static ContractKind? PickExtension(RunState run, int budget)
        {
            if (run?.activeContracts == null) return null;
            ContractKind? best = null;
            float bestGain = 0f;
            foreach (var c in run.activeContracts)
            {
                if (c.level >= ContractCost.MaxLevel) continue;
                int nextLv = c.level + 1;
                int extraCost = ContractCost.For(nextLv);
                if (extraCost > budget) continue;
                float curV = ValueAtLevel(run, c.kind, c.level);
                float nextV = ValueAtLevel(run, c.kind, nextLv);
                float gain = nextV - curV;
                if (gain < extraCost * AcceptThreshold) continue;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = c.kind;
                }
            }
            return best;
        }

        /// <summary>維持費不足時、 (value - cost) 昇順で切り捨て候補を返す。</summary>
        public static List<ContractKind> PickShortfallReleases(RunState run, int shortfallAmount)
        {
            var toRelease = new List<ContractKind>();
            if (run?.activeContracts == null || shortfallAmount <= 0) return toRelease;
            var scored = run.activeContracts
                .Select(c => (kind: c.kind, gain: ValueAtLevel(run, c.kind, c.level) - c.CurrentMaintenanceCost, cost: c.CurrentMaintenanceCost))
                .OrderBy(s => s.gain)
                .ToList();
            int saved = 0;
            foreach (var (kind, _, cost) in scored)
            {
                if (saved >= shortfallAmount) break;
                toRelease.Add(kind);
                saved += cost;
            }
            return toRelease;
        }

        // ===== 評価 =====

        private static float EvaluateNewContract(RunState run, ContractKind k, int level)
        {
            float value = ValueAtLevel(run, k, level);
            // 敵対既存契約があれば没収ペナルティ
            if (ContractRelations.TryGetRival(k, out var rival))
            {
                var existing = ContractManager.Instance.Find(run, rival);
                if (existing != null)
                {
                    switch (existing.level)
                    {
                        case 3: value += RivalryPenaltyL3; break;
                        case 2: value += RivalryPenaltyL2; break;
                        default: value += RivalryPenaltyL1; break;
                    }
                }
            }
            return value;
        }

        private static float ValueAtLevel(RunState run, ContractKind k, int level)
        {
            if (!BaseValueL1.TryGetValue(k, out var baseV)) baseV = 4f;
            float v = baseV * level;
            // 協力既存契約があれば +30%
            if (ContractRelations.TryGetAlly(k, out var ally)
                && ContractManager.Instance.IsActive(run, ally))
            {
                v *= AllianceBonus;
            }
            // 序盤バイアス: 経済系は floor<=3 で +20%、 攻撃系は floor>=4 で +20%
            int floor = run?.currentFloor ?? 1;
            if (IsEconomicKind(k) && floor <= 3) v *= 1.20f;
            if (IsCombatKind(k) && floor >= 4) v *= 1.20f;
            return v;
        }

        private static bool IsEconomicKind(ContractKind k)
            => k == ContractKind.MerchantsLeague
            || k == ContractKind.SupplyCaravan
            || k == ContractKind.OrphanCircus
            || k == ContractKind.Alchemist;

        private static bool IsCombatKind(ContractKind k)
            => k == ContractKind.Mercenaries
            || k == ContractKind.Knights
            || k == ContractKind.Assassins
            || k == ContractKind.Hunters
            || k == ContractKind.BodyDoubles
            || k == ContractKind.Tacticians;
    }
}
