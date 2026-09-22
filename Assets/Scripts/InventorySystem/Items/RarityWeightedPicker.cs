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
        /// <summary><b>レア度の重みを上位へ寄せる (2026-09-13)。</b> 0 = 素の重み / 1 = 最大の寄せ。
        ///
        /// <para>重みを <c>w^(1−bias)</c> へ変形する ── bias=1 で全 rarity が等確率になり、
        /// LEGENDARY が 1% から 25% へ跳ね上がる。 段ごとに「開幕の手札が良くなる」を
        /// 本数ではなく<b>質</b>で表現するため (兵站 r3/6/9)。</para>
        ///
        /// <para><b>重みの順序は保つ</b> ── 逆転させず、 差を縮めるだけ。
        /// 「稀少なものほど出にくい」という関係は最後まで壊れない。</para></summary>
        private static float Biased(float w, float bias)
            => bias <= 0f ? w : Mathf.Pow(w, 1f - Mathf.Clamp01(bias));

        public static CompleteItemData Pick(List<CompleteItemData> pool, ItemRarity? minRarity = null,
                                            float rarityBias = 0f)
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
                if (available.Contains(rarity)) total += Biased(weight, rarityBias);

            if (total <= 0f)
                return filtered[GameLoop.GameRng.RangeAuto("RarityWeightedPicker.2", 0, filtered.Count)];

            // 重み付き rarity 抽選
            float r = GameLoop.GameRng.Value("RarityWeightedPicker.1") * total;
            ItemRarity chosen = ItemRarity.BRONZE;
            foreach (var (rarity, weight) in tierWeights)
            {
                if (!available.Contains(rarity)) continue;
                if ((r -= Biased(weight, rarityBias)) <= 0f) { chosen = rarity; break; }
            }

            // 該当 rarity 内からフラット抽選
            var byTier = new List<CompleteItemData>();
            foreach (var it in filtered)
                if (it.rarity == chosen) byTier.Add(it);
            if (byTier.Count == 0) return filtered[GameLoop.GameRng.RangeAuto("RarityWeightedPicker.3", 0, filtered.Count)];
            return byTier[GameLoop.GameRng.RangeAuto("RarityWeightedPicker.4", 0, byTier.Count)];
        }
    }
}
