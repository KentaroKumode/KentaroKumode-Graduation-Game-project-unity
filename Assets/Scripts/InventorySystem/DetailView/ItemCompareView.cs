using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// 装備アイテムの比較表示
    /// </summary>
    public class ItemCompareView : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private GameObject comparePanel;
        [SerializeField] private TextMeshProUGUI currentItemText;
        [SerializeField] private TextMeshProUGUI newItemText;
        [SerializeField] private TextMeshProUGUI comparisonText;
        
        /// <summary>
        /// 比較表示
        /// </summary>
        public void ShowComparison(CompleteItemData currentItem, CompleteItemData newItem)
        {
            if (comparePanel != null)
            {
                comparePanel.SetActive(true);
            }
            
            // 現在の装備
            if (currentItemText != null && currentItem != null)
            {
                currentItemText.text = $"{currentItem.displayName}\n{GetStatsText(currentItem)}";
            }
            
            // 新しいアイテム
            if (newItemText != null)
            {
                newItemText.text = $"{newItem.displayName}\n{GetStatsText(newItem)}";
            }
            
            // 比較
            if (comparisonText != null && currentItem != null)
            {
                comparisonText.text = GetComparisonText(currentItem, newItem);
            }
        }
        
        /// <summary>
        /// 比較非表示
        /// </summary>
        public void HideComparison()
        {
            if (comparePanel != null)
            {
                comparePanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// ステータステキスト取得
        /// </summary>
        private string GetStatsText(CompleteItemData item)
        {
            string text = "";
            if (item.hasWeaponStats && item.weaponStats != null)
            {
                text += $"ダイス: {item.weaponStats.ToString()}\n";
            }
            text += $"レアリティ: {item.rarity}\n";
            text += $"カテゴリ: {item.category}\n";
            return text;
        }
        
        /// <summary>
        /// 比較テキスト取得（矢印と色付き）
        /// </summary>
        private string GetComparisonText(CompleteItemData current, CompleteItemData newItem)
        {
            string text = "";
            
            // レアリティ比較
            text += $"レアリティ: {current.rarity} → {newItem.rarity}\n";
            
            // 武器ダメージ比較
            if (current.hasWeaponStats && newItem.hasWeaponStats)
            {
                text += GetStatComparison("防御力", current.defense, newItem.defense);
            }
            
            // HP
            if (current.health > 0 || newItem.health > 0)
            {
                text += GetStatComparison("HP", current.health, newItem.health);
            }
            
            // MP
            if (current.mana > 0 || newItem.mana > 0)
            {
                text += GetStatComparison("MP", current.mana, newItem.mana);
            }
            
            return text;
        }
        
        /// <summary>
        /// 個別ステータスの比較
        /// </summary>
        private string GetStatComparison(string statName, int currentValue, int newValue)
        {
            int diff = newValue - currentValue;
            
            if (diff > 0)
            {
                // 上昇（緑）
                return $"{statName}: {currentValue} → <color=green>{newValue} ↑</color>\n";
            }
            else if (diff < 0)
            {
                // 下降（赤）
                return $"{statName}: {currentValue} → <color=red>{newValue} ↓</color>\n";
            }
            else
            {
                // 変化なし
                return $"{statName}: {currentValue} → {newValue}\n";
            }
        }
    }
}
