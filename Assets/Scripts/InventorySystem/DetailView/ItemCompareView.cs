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
        public void ShowComparison(ItemData currentItem, ItemData newItem)
        {
            if (comparePanel != null)
            {
                comparePanel.SetActive(true);
            }
            
            // 現在の装備
            if (currentItemText != null && currentItem != null)
            {
                currentItemText.text = $"{currentItem.itemName}\n{GetStatsText(currentItem)}";
            }
            
            // 新しいアイテム
            if (newItemText != null)
            {
                newItemText.text = $"{newItem.itemName}\n{GetStatsText(newItem)}";
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
        private string GetStatsText(ItemData item)
        {
            string text = "";
            if (item.attack > 0)
                text += $"攻撃力: {item.attack}\n";
            if (item.defense > 0)
                text += $"防御力: {item.defense}\n";
            if (item.health > 0)
                text += $"HP: {item.health}\n";
            if (item.mana > 0)
                text += $"MP: {item.mana}\n";
            return text;
        }
        
        /// <summary>
        /// 比較テキスト取得（矢印と色付き）
        /// </summary>
        private string GetComparisonText(ItemData current, ItemData newItem)
        {
            string text = "";
            
            // 攻撃力
            if (current.attack > 0 || newItem.attack > 0)
            {
                text += GetStatComparison("攻撃力", current.attack, newItem.attack);
            }
            
            // 防御力
            if (current.defense > 0 || newItem.defense > 0)
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
