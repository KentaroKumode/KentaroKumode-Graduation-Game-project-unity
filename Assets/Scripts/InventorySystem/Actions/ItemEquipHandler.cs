using UnityEngine;
using InventorySystem.PassiveSkills;

namespace InventorySystem
{
    /// <summary>
    /// アイテム装備処理
    /// </summary>
    public class ItemEquipHandler : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private ItemCompareView compareView;
        
        private CompleteItemData currentlyEquippedWeapon;
        private CompleteItemData currentlyEquippedArmor;
        private CompleteItemData currentlyEquippedDice;
        
        /// <summary>
        /// アイテムを装備
        /// </summary>
        public void EquipItem(CompleteItemData item, ItemSlot slot)
        {
            if (item == null || !item.IsEquippable)
            {
                Debug.LogWarning("[ItemEquipHandler] Item is not equippable");
                return;
            }
            
            // 現在の装備を取得
            CompleteItemData currentEquip = item.category == ItemCategory.Weapon ? 
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
        private void ExecuteEquip(CompleteItemData item, ItemSlot slot)
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
            else if (item.category == ItemCategory.Dice && currentlyEquippedDice != null)
            {
                UnequipDice();
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
            else if (item.category == ItemCategory.Dice)
            {
                currentlyEquippedDice = item;
            }
            
            // パッシブスキル登録
            PassiveSkillManager.Instance.AddItemSkills(item);
            
            // スロットに装備マーク
            if (slot != null)
            {
                slot.SetEquipped(true);
            }
            
            // イベント発火
            InventoryManager.Instance?.EquipItem(item);
            
            // 効果音
            InventorySoundManager.Instance?.PlayItemEquip();
            
            Debug.Log($"[ItemEquipHandler] Equipped: {item.displayName}");
        }
        
        /// <summary>
        /// 武器を解除
        /// </summary>
        private void UnequipWeapon()
        {
            if (currentlyEquippedWeapon != null)
            {
                // パッシブスキル解除
                PassiveSkillManager.Instance.RemoveItemSkills(currentlyEquippedWeapon);
                
                // イベント発火
                InventoryManager.Instance?.UnequipItem(currentlyEquippedWeapon);
                
                Debug.Log($"[ItemEquipHandler] Unequipped weapon: {currentlyEquippedWeapon.displayName}");
                
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
                // パッシブスキル解除
                PassiveSkillManager.Instance.RemoveItemSkills(currentlyEquippedArmor);
                
                // イベント発火
                InventoryManager.Instance?.UnequipItem(currentlyEquippedArmor);
                
                Debug.Log($"[ItemEquipHandler] Unequipped armor: {currentlyEquippedArmor.displayName}");
                
                // TODO: スロットの装備マークを解除
                currentlyEquippedArmor = null;
            }
        }
        
        /// <summary>
        /// ダイスを解除
        /// </summary>
        private void UnequipDice()
        {
            if (currentlyEquippedDice != null)
            {
                PassiveSkillManager.Instance.RemoveItemSkills(currentlyEquippedDice);
                InventoryManager.Instance?.UnequipItem(currentlyEquippedDice);
                Debug.Log($"[ItemEquipHandler] Unequipped dice: {currentlyEquippedDice.displayName}");
                currentlyEquippedDice = null;
            }
        }
        
        /// <summary>
        /// 現在の装備を取得
        /// </summary>
        public CompleteItemData GetCurrentEquipment(ItemCategory category)
        {
            if (category == ItemCategory.Weapon)
                return currentlyEquippedWeapon;
            else if (category == ItemCategory.Armor)
                return currentlyEquippedArmor;
            else if (category == ItemCategory.Dice)
                return currentlyEquippedDice;
            
            return null;
        }
    }
}
