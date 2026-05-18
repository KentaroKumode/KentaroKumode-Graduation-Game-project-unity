using System.Collections.Generic;
using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム候補プールからレア度重みで1個選出する汎用ヘルパー。
    /// Tier 重みは ShopManager と統一: BRONZE 54% / SILVER 35% / GOLD 10% / LEGENDARY 1%.
    /// MYTHIC は通常排出対象外（ShopManager と同じ運用）。
    /// </summary>
    public static class RarityWeightedPicker
    {
        // ShopManager と統一した Tier 重み
        private static readonly (ItemRarity rarity, float weight)[] tierWeights = new[]
        {
            (ItemRarity.BRONZE,    0.54f),
            (ItemRarity.SILVER,    0.35f),
            (ItemRarity.GOLD,      0.10f),
            (ItemRarity.LEGENDARY, 0.01f),
        };

        /// <summary>
        /// 候補プールから rarity 重みで1個選出する。
        /// minRarity が指定された場合、それ未満の rarity は除外（ボス追加レア化等で使用）。
        /// </summary>
        public static CompleteItemData Pick(List<CompleteItemData> pool, ItemRarity? minRarity = null)
        {
            if (pool == null || pool.Count == 0) return null;

            // min rarity フィルタ
            List<CompleteItemData> filtered = pool;
            if (minRarity.HasValue)
            {
                filtered = new List<CompleteItemData>();
                foreach (var it in pool)
                {
                    if (it == null) continue;
                    if (it.rarity < minRarity.Value) continue;
                    filtered.Add(it);
                }
            }
            if (filtered.Count == 0) return null;

            // プール内に存在する rarity を集計
            var available = new HashSet<ItemRarity>();
            foreach (var it in filtered) available.Add(it.rarity);

            // 利用可能 rarity の合計重み
            float total = 0f;
            foreach (var (rarity, weight) in tierWeights)
                if (available.Contains(rarity)) total += weight;

            if (total <= 0f)
                return filtered[Random.Range(0, filtered.Count)];

            // 重み付き rarity 抽選
            float r = Random.value * total;
            ItemRarity chosen = ItemRarity.BRONZE;
            foreach (var (rarity, weight) in tierWeights)
            {
                if (!available.Contains(rarity)) continue;
                if ((r -= weight) <= 0f) { chosen = rarity; break; }
            }

            // 該当 rarity 内からフラット抽選
            var byTier = new List<CompleteItemData>();
            foreach (var it in filtered)
                if (it.rarity == chosen) byTier.Add(it);
            if (byTier.Count == 0) return filtered[Random.Range(0, filtered.Count)];
            return byTier[Random.Range(0, byTier.Count)];
        }
    }
}
