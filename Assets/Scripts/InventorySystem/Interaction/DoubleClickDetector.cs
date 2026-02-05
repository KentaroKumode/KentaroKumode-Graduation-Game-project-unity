using UnityEngine;
using System;

namespace InventorySystem
{
    /// <summary>
    /// ダブルクリック検出
    /// </summary>
    public class DoubleClickDetector : MonoBehaviour
    {
        [Header("設定")]
        [SerializeField] private float doubleClickTime = InventoryConstants.DOUBLE_CLICK_TIME;
        
        private float lastClickTime = 0f;
        private ItemSlot lastClickedSlot = null;
        
        public event Action<ItemSlot> OnDoubleClick;
        
        /// <summary>
        /// クリック検出
        /// </summary>
        public void RegisterClick(ItemSlot slot)
        {
            float currentTime = Time.time;
            
            if (lastClickedSlot == slot && (currentTime - lastClickTime) < doubleClickTime)
            {
                // ダブルクリック
                OnDoubleClick?.Invoke(slot);
                Debug.Log($"[DoubleClickDetector] Double clicked: {slot.ItemData.displayName}");
                
                // リセット
                lastClickedSlot = null;
                lastClickTime = 0f;
            }
            else
            {
                // シングルクリック
                lastClickedSlot = slot;
                lastClickTime = currentTime;
            }
        }
    }
}
