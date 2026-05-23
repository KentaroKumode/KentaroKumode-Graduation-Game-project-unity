using System.Collections.Generic;

namespace InventorySystem.Shop
{
    /// <summary>
    /// 1ショップ訪問あたりの在庫。
    /// パッシブ2 / 消費2 / 武器1 / 強化素材1 の計6スロット。
    /// </summary>
    public class ShopInventory
    {
        public List<ShopSlot> slots = new List<ShopSlot>();

        /// <summary>武器強化素材を購入した回数（価格を倍々にするため）</summary>
        public int materialPurchaseCount;

        /// <summary>強化素材スロットの基準価格 (1/5 デノミ後: 旧15→3、2^N倍で 3/6/12/24...)</summary>
        public int materialBasePrice = 3;

        /// <summary>このショップの価格倍率（フロアデバフ等から設定）</summary>
        public float priceMultiplier = 1f;

        /// <summary>このショップで購入された通常品の数（嫉妬デバフが1で打ち止めにする）</summary>
        public int purchaseCount;

        /// <summary>強化素材スロットの現在価格 = base × 2^purchaseCount × priceMultiplier</summary>
        public int CurrentMaterialPrice
            => UnityEngine.Mathf.CeilToInt(materialBasePrice * (1 << materialPurchaseCount) * priceMultiplier);
    }
}
