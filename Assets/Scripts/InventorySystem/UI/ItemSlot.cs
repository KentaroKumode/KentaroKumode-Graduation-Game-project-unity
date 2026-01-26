using UnityEngine;
using UnityEngine.UI;

namespace InventorySystem
{
    /// <summary>
    /// アイテムスロットUI
    /// アイテムの表示と基本的なインタラクションを管理
    /// </summary>
    public class ItemSlot : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private Image iconImage;
        [SerializeField] private GameObject equipMarkObject;
        [SerializeField] private Image rarityFrame;
        
        [Header("レアリティ色")]
        [SerializeField] private Color bronzeColor = new Color(0.8f, 0.5f, 0.3f);
        [SerializeField] private Color silverColor = new Color(0.75f, 0.75f, 0.75f);
        [SerializeField] private Color goldColor = new Color(1f, 0.84f, 0f);
        [SerializeField] private Color mythicColor = new Color(0.8f, 0.2f, 0.8f);
        
        private ItemData itemData;
        private int gridX;
        private int gridY;
        private bool isEquipped = false;
        
        public ItemData ItemData => itemData;
        public int GridX => gridX;
        public int GridY => gridY;
        public bool IsEquipped => isEquipped;
        
        /// <summary>
        /// アイテムを設定
        /// </summary>
        public void SetItem(ItemData item, int x, int y)
        {
            itemData = item;
            gridX = x;
            gridY = y;
            
            UpdateVisual();
        }
        
        /// <summary>
        /// アイテムをクリア
        /// </summary>
        public void ClearItem()
        {
            itemData = null;
            
            if (iconImage != null)
                iconImage.enabled = false;
            
            if (equipMarkObject != null)
                equipMarkObject.SetActive(false);
        }
        
        /// <summary>
        /// 装備状態を設定
        /// </summary>
        public void SetEquipped(bool equipped)
        {
            isEquipped = equipped;
            
            if (equipMarkObject != null)
            {
                equipMarkObject.SetActive(equipped);
            }
        }
        
        /// <summary>
        /// ビジュアル更新
        /// </summary>
        private void UpdateVisual()
        {
            if (itemData == null) return;
            
            // アイコン表示
            if (iconImage != null)
            {
                if (itemData.icon != null)
                {
                    iconImage.sprite = itemData.icon;
                    iconImage.enabled = true;
                }
                else
                {
                    iconImage.enabled = false;
                }
            }
            
            // レアリティ枠の色
            if (rarityFrame != null)
            {
                rarityFrame.color = GetRarityColor(itemData.rarity);
            }
        }
        
        /// <summary>
        /// レアリティに応じた色を取得
        /// </summary>
        private Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Bronze: return bronzeColor;
                case ItemRarity.Silver: return silverColor;
                case ItemRarity.Gold: return goldColor;
                case ItemRarity.Mythic: return mythicColor;
                default: return Color.white;
            }
        }
    }
}
