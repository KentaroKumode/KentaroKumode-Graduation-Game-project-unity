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
        
        private CompleteItemData itemData;
        private int gridX;
        private int gridY;
        private bool isEquipped = false;
        
        public CompleteItemData ItemData => itemData;
        public int GridX => gridX;
        public int GridY => gridY;
        public bool IsEquipped => isEquipped;
        
        /// <summary>
        /// アイテムを設定
        /// </summary>
        public void SetItem(CompleteItemData item, int x, int y)
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
                if (itemData.itemIcon != null)
                {
                    iconImage.sprite = itemData.itemIcon;
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
                rarityFrame.color = RarityColorUtility.GetRarityColor(itemData.rarity);
            }
        }
        
    }
}
