namespace ItemSystem.Shop
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
        /// <summary>賽 (ADR-0015・2026-11-18)。 出目パーツの枠 (2026-08-17〜11-18) の置き換え。
        /// itemId が賽の id (items.json の category Dice)。 買うと装備中の賽が替わる (持つ賽は 1 個)。</summary>
        Dice,
        WeaponMaterial,      // 武器強化素材（マグナイト等）。在庫無限。
        // [削除 2026-09-11] InventoryExpansion ── インベントリ拡張 (1列追加)。
        //   陳列は 2026-09-03 に撤去済み、 容量という軸自体も 2026-07-29 に撤廃済みで、
        //   買っても何も起きない枠だった。 ShopSlotKind はランごとに生成されるだけで
        //   セーブに載らないため、 メンバ削除による値ズレの影響は無い。
    }
}
