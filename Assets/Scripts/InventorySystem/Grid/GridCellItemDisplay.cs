using UnityEngine;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// GridCell上にアイテム情報を表示
    /// </summary>
    public class GridCellItemDisplay : MonoBehaviour
    {
        [SerializeField] private TextMeshPro itemNameText;
        [SerializeField] private MeshRenderer backgroundRenderer;
        
        private CompleteItemData currentItem;
        
        /// <summary>
        /// アイテムを表示
        /// </summary>
        public void DisplayItem(CompleteItemData item)
        {
            currentItem = item;
            
            if (item == null)
            {
                HideItem();
                return;
            }
            
            // テキスト表示
            if (itemNameText == null)
            {
                CreateTextDisplay();
            }
            
            itemNameText.text = item.displayName;
            itemNameText.gameObject.SetActive(true);
            
            // 背景色をレアリティで変更
            if (backgroundRenderer != null)
            {
                backgroundRenderer.material.color = GetRarityColor(item.rarity);
            }
        }
        
        /// <summary>
        /// アイテム表示を隠す
        /// </summary>
        public void HideItem()
        {
            if (itemNameText != null)
            {
                itemNameText.gameObject.SetActive(false);
            }
            
            if (backgroundRenderer != null)
            {
                backgroundRenderer.material.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            }
        }
        
        /// <summary>
        /// テキスト表示を作成
        /// </summary>
        private void CreateTextDisplay()
        {
            GameObject textObj = new GameObject("ItemNameText");
            textObj.transform.SetParent(transform, false);
            textObj.transform.localPosition = new Vector3(0, 0.1f, 0);
            textObj.transform.localRotation = Quaternion.Euler(90, 0, 0);
            
            itemNameText = textObj.AddComponent<TextMeshPro>();
            itemNameText.alignment = TextAlignmentOptions.Center;
            itemNameText.fontSize = 3;
            itemNameText.color = Color.white;
            
            // RectTransform設定
            RectTransform rectTransform = textObj.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(0.8f, 0.3f);
        }
        
        /// <summary>
        /// レアリティに応じた色
        /// </summary>
        private Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.BRONZE: return new Color(0.8f, 0.5f, 0.3f, 0.9f);
                case ItemRarity.SILVER: return new Color(0.75f, 0.75f, 0.75f, 0.9f);
                case ItemRarity.GOLD: return new Color(1f, 0.84f, 0f, 0.9f);
                case ItemRarity.LEGENDARY: return new Color(1f, 0.5f, 0f, 0.9f);
                case ItemRarity.MYTHIC: return new Color(0.8f, 0.2f, 0.8f, 0.9f);
                default: return new Color(0.3f, 0.8f, 0.3f, 0.9f);
            }
        }
    }
}
