using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム破棄処理
    /// </summary>
    public class ItemDiscardHandler : MonoBehaviour
    {
        /// <summary>
        /// アイテムを破棄
        /// </summary>
        public void DiscardItem(ItemData item, ItemSlot slot)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemDiscardHandler] Item is null");
                return;
            }
            
            // 確認ダイアログ（後で実装）
            // TODO: WarningDialogで確認
            
            // 破棄実行
            ExecuteDiscard(item, slot);
        }
        
        /// <summary>
        /// 破棄を実行
        /// </summary>
        private void ExecuteDiscard(ItemData item, ItemSlot slot)
        {
            // アイテムを削除
            if (slot != null)
            {
                int x = slot.GridX;
                int y = slot.GridY;
                ItemData itemData = slot.ItemData; // アイテムデータを取得
                
                slot.ClearItem();
                
                // イベント発火
                if (itemData != null)
                {
                    InventoryManager.Instance?.RemoveItem(x, y, itemData);
                }
            }
            
            // 効果音
            InventorySoundManager.Instance?.PlayItemDiscard();
            
            Debug.Log($"[ItemDiscardHandler] Discarded: {item.itemName}");
        }
    }
}
