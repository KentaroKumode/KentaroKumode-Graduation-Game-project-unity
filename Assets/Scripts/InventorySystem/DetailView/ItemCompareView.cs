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
                if (item.critRatePct > 0f)
                    text += $"会心率: {item.CriticalRateLabel()}\n";
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
            
            // 武器ダイス比較
            if (current.hasWeaponStats && newItem.hasWeaponStats)
            {
                var cd = current.weaponDice;
                var nd = newItem.weaponDice;
                text += $"ダイス: {cd.count}d{cd.maxValue} → {nd.count}d{nd.maxValue}\n";
            }
            
            // 会心率比較（2026-07-26: 分子 N/9 表記 → 実効会心率 % 表記）
            if (current.critRatePct > 0f || newItem.critRatePct > 0f)
            {
                text += GetStatComparison("会心率", current.critRatePct, newItem.critRatePct,
                    current.CriticalRateLabel(), newItem.CriticalRateLabel());
            }
            
            // 価格比較
            text += GetStatComparison("売却価格", current.sellPrice.min, newItem.sellPrice.min);
            
            return text;
        }
        
        /// <summary>
        /// 個別ステータスの比較
        /// </summary>
        private string GetStatComparison(string statName, int currentValue, int newValue)
            => GetStatComparison(statName, currentValue, newValue,
                currentValue.ToString(), newValue.ToString());

        /// <summary>個別ステータスの比較（表示文字列を別指定できる版。会心率の % 表記などに使う）。
        /// 増減の判定は素値 (currentValue/newValue) で行い、表示だけ Label を使う。</summary>
        private string GetStatComparison(string statName, float currentValue, float newValue,
            string currentLabel, string newLabel)
        {
            float diff = newValue - currentValue;

            if (diff > 0)
            {
                // 上昇（緑）
                return $"{statName}: {currentLabel} → <color=green>{newLabel} ↑</color>\n";
            }
            else if (diff < 0)
            {
                // 下降（赤）
                return $"{statName}: {currentLabel} → <color=red>{newLabel} ↓</color>\n";
            }
            else
            {
                // 変化なし
                return $"{statName}: {currentLabel} → {newLabel}\n";
            }
        }
    }
}
