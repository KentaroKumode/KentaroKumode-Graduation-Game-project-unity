namespace InventorySystem.Shop
{
    /// <summary>
    /// ショップの1スロット。商品ID・価格・売却済みフラグを保持。
    /// 武器強化素材スロットは在庫無限なので sold は使わず、materialPurchaseCount でカウント。
    /// </summary>
    public class ShopSlot
    {
        public ShopSlotKind kind;
        public string itemId;        // null可（強化素材スロット）
        public int price;            // 表示価格 (特売割引適用後)
        public bool sold;
        /// <summary>2026-06-22: メタバフ「特売品」 で適用された割引率 (0-100, 0=非特売)。
        /// price は既に discountPct を反映済の値。 表示時に「特売」 マーク + 元価格表示のために保持。</summary>
        public int discountPct;
        /// <summary>特売前の元価格 (UI 表示用、 計算では使わない)。</summary>
        public int originalPrice;
    }

    public enum ShopSlotKind
    {
        Passive,
        Consumable,
        Weapon,
        Dice,
        WeaponMaterial,      // 武器強化素材（マグナイト等）。在庫無限。
        InventoryExpansion,  // インベントリ拡張 (1列追加)。 価格は run.inventoryUnlockedRows から動的決定。
    }
}
