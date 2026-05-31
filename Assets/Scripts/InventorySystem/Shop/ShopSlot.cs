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
        public int price;
        public bool sold;
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
