using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// 3Dオブジェクトからのドラッグ用仮想スロット
    /// </summary>
    public class VirtualItemSlot
    {
        private ItemData itemData;
        private int gridX;
        private int gridY;
        
        public ItemData ItemData => itemData;
        public int GridX => gridX;
        public int GridY => gridY;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public VirtualItemSlot(ItemData item, int x, int y)
        {
            itemData = item;
            gridX = x;
            gridY = y;
        }
        
        /// <summary>
        /// アイテムデータを設定
        /// </summary>
        public void SetItem(ItemData item, int x, int y)
        {
            itemData = item;
            gridX = x;
            gridY = y;
        }
        
        /// <summary>
        /// アイテムをクリア
        /// </summary>
        public void ClearItem()
        {
            itemData = null;
            gridX = -1;
            gridY = -1;
        }
    }
}