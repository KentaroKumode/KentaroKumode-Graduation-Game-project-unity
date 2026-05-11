namespace InventorySystem.Shop
{
    /// <summary>
    /// 武器のショップ出現可否フィルタ。
    /// LEGENDARY 以上は全カテゴリ（武器含む）でショップ排除済み（ShopManager.tierWeights から除外）。
    /// 武器固有の追加制限が必要になればここに実装する。
    /// </summary>
    public static class WeaponShopFilter
    {
        /// <summary>武器カテゴリでショップ出現可能か判定。</summary>
        public static bool IsShopAllowed(CompleteItemData item)
        {
            if (item == null) return false;
            if (item.category != ItemCategory.Weapon) return false;

            // LEGENDARY/MYTHIC は重み付けで既に除外されているので、
            // 通常はここまで到達しないが念のため明示的に弾く
            if (item.rarity == ItemRarity.LEGENDARY) return false;
            if (item.rarity == ItemRarity.MYTHIC) return false;

            return true;
        }
    }
}
