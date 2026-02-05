using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// 3Dオブジェクトからのドラッグ用仮想スロット
    /// </summary>
    public class VirtualItemSlot
    {
        private CompleteItemData itemData;
        private int gridX;
        private int gridY;
        
        public CompleteItemData ItemData => itemData;
        public int GridX => gridX;
        public int GridY => gridY;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public VirtualItemSlot(CompleteItemData item, int x, int y)
        {
            itemData = item;
            gridX = x;
            gridY = y;
        }
        
        /// <summary>
        /// アイテムデータを設定
        /// </summary>
        public void SetItem(CompleteItemData item, int x, int y)
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