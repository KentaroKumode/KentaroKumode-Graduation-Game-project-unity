using UnityEngine;
using System;

namespace InventorySystem
{
    /// <summary>
    /// 右クリックメニュー処理
    /// </summary>
    public class RightClickHandler : MonoBehaviour
    {
        public event Action<ItemSlot> OnShowDetail;     // 詳細表示
        public event Action<ItemSlot> OnEquip;          // 装備
        public event Action<ItemSlot> OnUse;            // 使用
        public event Action<ItemSlot> OnDiscard;        // 破棄
        
        private ItemSlot currentSlot;
        
        /// <summary>
        /// 右クリックメニューを表示
        /// </summary>
        public void ShowContextMenu(ItemSlot slot, Vector3 position)
        {
            if (slot == null || slot.ItemData == null) return;
            
            currentSlot = slot;
            
            // TODO: UI実装時に実際のメニュー表示
            // 現在は直接詳細表示を呼び出し
            OnShowDetail?.Invoke(slot);
            
            Debug.Log($"[RightClickHandler] Context menu for: {slot.ItemData.itemName}");
        }
        
        /// <summary>
        /// 装備を実行
        /// </summary>
        public void ExecuteEquip()
        {
            if (currentSlot != null)
            {
                OnEquip?.Invoke(currentSlot);
            }
        }
        
        /// <summary>
        /// 使用を実行
        /// </summary>
        public void ExecuteUse()
        {
            if (currentSlot != null)
            {
                OnUse?.Invoke(currentSlot);
            }
        }
        
        /// <summary>
        /// 破棄を実行
        /// </summary>
        public void ExecuteDiscard()
        {
            if (currentSlot != null)
            {
                OnDiscard?.Invoke(currentSlot);
            }
        }
    }
}
