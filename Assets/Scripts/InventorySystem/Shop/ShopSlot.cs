namespace InventorySystem.Shop
{
    /// <summary>
    /// ショップの1スロット。商品ID・価格・売却済みフラグを保持。
    /// 武器強化素材スロットは在庫無限なので sold は使わず、materialPurchaseCount でカウント。
    /// </summary>
    public class ShopSlot
    {
        public ShopSlotKind kind;
        /// <summary>出目パーツ枠のときだけ有効。 どの (出目, Tier) を売っているか。</summary>
        public GameLoop.DiceFaceParts.Part facePart;
        public string itemId;        // null可（強化素材スロット）
        public int price;            // 表示価格 (特売割引適用後)
        public bool sold;
        /// <summary>2026-06-22: メタバフ「特売品」 で適用された割引率 (0-100, 0=非特売)。
        /// price は既に discountPct を反映済の値。 表示時に「特売」 マーク + 元価格表示のために保持。</summary>
        public int discountPct;
        /// <summary>特売前の元価格 (UI 表示用、 計算では使わない)。</summary>
        public int originalPrice;

        /// <summary>[計装] 上位互換アップグレード割引が乗っているスロットの段差 (0 = 割引なし)。
        /// 購入時に <c>AutoTest.FamilyTierStats.NoteUpgradeBought</c> を撃つためだけに持つ ──
        /// **提示だけでなく成約を数えないと、「1G でも見送られている」が見えない。**</summary>
        public int upgradeStep;
    }

    public enum ShopSlotKind
    {
        Passive,
        Consumable,
        Weapon,
        /// <summary>出目パーツ (2026-08-17)。 **旧 Dice 枠の置き換え**。
        /// ダイス 10 種は面が全部同じで更新判定が常に false ＝ BOT が 1 個も買わない死に枠だった。
        /// 「面構成で個性を出す」役割をパーツへ移し、 枠だけを引き継ぐ (陳列数は不変)。
        /// itemId は使わず、 facePart にどのパーツかを持つ。</summary>
        FacePart,
        WeaponMaterial,      // 武器強化素材（マグナイト等）。在庫無限。
        // [削除 2026-09-11] InventoryExpansion ── インベントリ拡張 (1列追加)。
        //   陳列は 2026-09-03 に撤去済み、 容量という軸自体も 2026-07-29 に撤廃済みで、
        //   買っても何も起きない枠だった。 ShopSlotKind はランごとに生成されるだけで
        //   セーブに載らないため、 メンバ削除による値ズレの影響は無い。
    }
}
