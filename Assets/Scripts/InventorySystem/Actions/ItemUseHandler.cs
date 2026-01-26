using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム使用処理
    /// </summary>
    public class ItemUseHandler : MonoBehaviour
    {
        /// <summary>
        /// アイテムを使用
        /// </summary>
        public void UseItem(ItemData item, ItemSlot slot)
        {
            if (item == null || !item.IsUsable())
            {
                Debug.LogWarning("[ItemUseHandler] Item is not usable");
                return;
            }
            
            // 確認ダイアログ（後で実装）
            // TODO: WarningDialogで確認
            
            // 使用実行
            ExecuteUse(item, slot);
        }
        
        /// <summary>
        /// 使用を実行
        /// </summary>
        private void ExecuteUse(ItemData item, ItemSlot slot)
        {
            // アイテム効果を適用
            ApplyItemEffect(item);
            
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
            InventorySoundManager.Instance?.PlayItemUse();
            
            Debug.Log($"[ItemUseHandler] Used: {item.itemName}");
        }
        
        /// <summary>
        /// アイテム効果を適用
        /// </summary>
        private void ApplyItemEffect(ItemData item)
        {
            // TODO: 実際の効果実装
            // 例: HP回復、バフ付与など
            
            if (item.health > 0)
            {
                Debug.Log($"[ItemUseHandler] HP recovered: {item.health}");
                // プレイヤーのHP回復処理
            }
            
            if (item.mana > 0)
            {
                Debug.Log($"[ItemUseHandler] MP recovered: {item.mana}");
                // プレイヤーのMP回復処理
            }
        }
    }
}
