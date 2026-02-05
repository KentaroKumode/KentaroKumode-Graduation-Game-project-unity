using UnityEngine;
using TMPro;
using System.Text;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// アイテムプレビュー用テキスト表示システム
    /// ItemPreviewStatusUIと統合してアイテム情報を平面に表示
    /// </summary>
    public class ItemPreviewTextRenderer : MonoBehaviour
    {
        [Header("テキスト表示設定")]
        [SerializeField] private TextRenderer3D textRenderer;
        [SerializeField] private bool autoCreateTextRenderer = true;
        [SerializeField] private bool showItemName = true;
        [SerializeField] private bool showDescription = true;
        [SerializeField] private bool showStats = true;
        [SerializeField] private bool showFlavorText = false;
        
        [Header("レイアウト設定")]
        [SerializeField] private string textSeparator = "\n";
        [SerializeField] private string statFormat = "{0}: {1}";
        [SerializeField] private Color nameColor = Color.white;
        [SerializeField] private Color descriptionColor = Color.gray;
        [SerializeField] private Color statsColor = Color.yellow;
        [SerializeField] private Color flavorColor = Color.cyan;
        
        [Header("プレート設定")]
        [SerializeField] private Vector3 textPlateOffset = new Vector3(0, 0, 0.5f);
        
        // プライベートフィールド
        private ItemDataV2 currentItem;
        
        void Start()
        {
            if (autoCreateTextRenderer && textRenderer == null)
            {
                GameObject textObj = new GameObject("TextRenderer3D");
                textObj.transform.SetParent(transform, false);
                textRenderer = textObj.AddComponent<TextRenderer3D>();
            }
        }
        
        /// <summary>
        /// アイテム情報を表示
        /// </summary>
        public void DisplayItem(ItemDataV2 itemData)
        {
            currentItem = itemData;
            
            if (textRenderer == null)
            {
                Debug.LogWarning("[ItemPreviewTextRenderer] TextRenderer3Dが見つかりません");
                return;
            }
            
            string displayText = GenerateItemText(itemData);
            textRenderer.UpdateDisplayText(displayText);
            
            Debug.Log($"[ItemPreviewTextRenderer] Displaying item: {itemData.displayName}");
        }
        
        /// <summary>
        /// アイチE��チE��ストを生�E
        /// </summary>
        private string GenerateItemText(ItemDataV2 itemData)
        {
            StringBuilder sb = new StringBuilder();
            
            // アイチE��吁E
            if (showItemName && !string.IsNullOrEmpty(itemData.displayName))
            {
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(nameColor)}>{itemData.displayName}</color>");
            }
            
            // 説明文
            if (showDescription && !string.IsNullOrEmpty(itemData.description))
            {
                sb.Append(textSeparator);
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(descriptionColor)}>{itemData.description}</color>");
            }
            
            // スチE�Eタス
            if (showStats)
            {
                string statsText = GenerateStatsText(itemData);
                if (!string.IsNullOrEmpty(statsText))
                {
                    sb.Append(textSeparator);
                    sb.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(statsColor)}>{statsText}</color>");
                }
            }
            
            // フレーバ�EチE��スチE
            if (showFlavorText && !string.IsNullOrEmpty(itemData.flavorText))
            {
                sb.Append(textSeparator);
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(flavorColor)}\"{itemData.flavorText}\"</color>");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// アイチE��のスチE�EタスチE��ストを生�E
        /// </summary>
        private string GenerateStatsText(ItemDataV2 itemData)
        {
            List<string> stats = new List<string>();
            
            // サイズ惁E��
            stats.Add(string.Format(statFormat, "Size", $"{itemData.size.x}x{itemData.size.y}"));
            
            // ダイス情報（武器のみ）
            if (itemData.hasWeaponStats)
            {
                var dice = itemData.weaponStats;
                stats.Add(string.Format(statFormat, "Attack", $"{dice.count}d({dice.minValue}-{dice.maxValue})"));
            }
            
            // レアリチE��とカチE��リ
            stats.Add(string.Format(statFormat, "Rarity", itemData.rarity));
            stats.Add(string.Format(statFormat, "Category", itemData.category));
            
            // 売却価格
            if (itemData.economy != null && itemData.economy.baseValue > 0)
                stats.Add(string.Format(statFormat, "Value", $"{itemData.economy.baseValue}G"));
            
            return string.Join(textSeparator, stats);
        }
        
        /// <summary>
        /// チE��スト表示をクリア
        /// </summary>
        public void ClearDisplay()
        {
            if (textRenderer != null)
            {
                textRenderer.UpdateDisplayText("");
            }
            
            currentItem = null;
        }
        
        /// <summary>
        /// 表示設定を更新
        /// </summary>
        public void UpdateDisplaySettings(bool showName, bool showDesc, bool showStat, bool showFlavor)
        {
            showItemName = showName;
            showDescription = showDesc;
            showStats = showStat;
            showFlavorText = showFlavor;
            
            if (currentItem != null)
            {
                DisplayItem(currentItem);
            }
        }
        
        /// <summary>
        /// チE��スト色設定を更新
        /// </summary>
        public void UpdateColors(Color name, Color desc, Color stat, Color flavor)
        {
            nameColor = name;
            descriptionColor = desc;
            statsColor = stat;
            flavorColor = flavor;
            
            if (currentItem != null)
            {
                DisplayItem(currentItem);
            }
        }
        
        /// <summary>
        /// チE��ストレンダラーの位置を調整
        /// </summary>
        public void SetTextPlateOffset(Vector3 offset)
        {
            textPlateOffset = offset;
            
            if (textRenderer != null)
            {
                textRenderer.transform.localPosition = textPlateOffset;
            }
        }
        
        [ContextMenu("Preview Current Item")]
        public void PreviewCurrentItem()
        {
            if (currentItem != null)
            {
                DisplayItem(currentItem);
            }
            else
            {
                Debug.LogWarning("[ItemPreviewTextRenderer] No current item to preview");
            }
        }
    }
}
