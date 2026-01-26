using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム装備処理
    /// </summary>
    public class ItemEquipHandler : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private ItemCompareView compareView;
        
        private ItemData currentlyEquippedWeapon;
        private ItemData currentlyEquippedArmor;
        
        /// <summary>
        /// アイテムを装備
        /// </summary>
        public void EquipItem(ItemData item, ItemSlot slot)
        {
            if (item == null || !item.IsEquippable())
            {
                Debug.LogWarning("[ItemEquipHandler] Item is not equippable");
                return;
            }
            
            // 現在の装備を取得
            ItemData currentEquip = item.category == ItemCategory.Weapon ? 
                currentlyEquippedWeapon : currentlyEquippedArmor;
            
            // 比較表示
            if (compareView != null && currentEquip != null)
            {
                compareView.ShowComparison(currentEquip, item);
            }
            
            // 確認ダイアログ（後で実装）
            // TODO: WarningDialogで確認
            
            // 装備実行
            ExecuteEquip(item, slot);
        }
        
        /// <summary>
        /// 装備を実行
        /// </summary>
        private void ExecuteEquip(ItemData item, ItemSlot slot)
        {
            // 前の装備を解除
            if (item.category == ItemCategory.Weapon && currentlyEquippedWeapon != null)
            {
                UnequipWeapon();
            }
            else if (item.category == ItemCategory.Armor && currentlyEquippedArmor != null)
            {
                UnequipArmor();
            }
            
            // 新しい装備を設定
            if (item.category == ItemCategory.Weapon)
            {
                currentlyEquippedWeapon = item;
            }
            else if (item.category == ItemCategory.Armor)
            {
                currentlyEquippedArmor = item;
            }
            
            // スロットに装備マーク
            if (slot != null)
            {
                slot.SetEquipped(true);
            }
            
            // イベント発火
            InventoryManager.Instance?.EquipItem(item);
            
            // 効果音
            InventorySoundManager.Instance?.PlayItemEquip();
            
            Debug.Log($"[ItemEquipHandler] Equipped: {item.itemName}");
        }
        
        /// <summary>
        /// 武器を解除
        /// </summary>
        private void UnequipWeapon()
        {
            if (currentlyEquippedWeapon != null)
            {
                // TODO: スロットの装備マークを解除
                currentlyEquippedWeapon = null;
            }
        }
        
        /// <summary>
        /// 防具を解除
        /// </summary>
        private void UnequipArmor()
        {
            if (currentlyEquippedArmor != null)
            {
                // TODO: スロットの装備マークを解除
                currentlyEquippedArmor = null;
            }
        }
        
        /// <summary>
        /// 現在の装備を取得
        /// </summary>
        public ItemData GetCurrentEquipment(ItemCategory category)
        {
            if (category == ItemCategory.Weapon)
                return currentlyEquippedWeapon;
            else if (category == ItemCategory.Armor)
                return currentlyEquippedArmor;
            
            return null;
        }
    }
}
