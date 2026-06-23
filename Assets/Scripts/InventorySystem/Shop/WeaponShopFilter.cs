namespace InventorySystem.Shop
{
    /// <summary>
    /// 武器のショップ出現可否フィルタ。
    /// 2026-06-22: LEGENDARY (T4 武器) は出現可。 確率は ShopManager.weaponTierWeights で
    /// 0.5% に絞り、 フルラン 10 回に 1〜2 回ペースの超レア出現に。 MYTHIC は引き続き排除。
    /// </summary>
    public static class WeaponShopFilter
    {
        /// <summary>武器カテゴリでショップ出現可能か判定。</summary>
        public static bool IsShopAllowed(CompleteItemData item)
        {
            if (item == null) return false;
            if (item.category != ItemCategory.Weapon) return false;

            // MYTHIC は全カテゴリで排出しない
            if (item.rarity == ItemRarity.MYTHIC) return false;

            return true;
        }
    }
}
