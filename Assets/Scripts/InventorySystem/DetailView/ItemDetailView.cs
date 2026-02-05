using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// アイテム詳細表示
    /// 右クリックで3Dカードを表示して回転
    /// </summary>
    public class ItemDetailView : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI flavorText;
        [SerializeField] private TextMeshProUGUI statsText;
        [SerializeField] private Image rarityBadge;
        
        [Header("3Dカード")]
        [SerializeField] private Transform cardContainer;
        [SerializeField] private ItemCardRotator cardRotator;
        
        [Header("背景")]
        [SerializeField] private BackgroundBlur backgroundBlur;
        
        private GameObject currentCard;
        private CompleteItemData currentItem;
        private bool isShowing = false;
        
        void Start()
        {
            if (detailPanel != null)
            {
                detailPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// 詳細表示を開く
        /// </summary>
        public void ShowDetail(CompleteItemData item)
        {
            if (item == null) return;
            
            currentItem = item;
            isShowing = true;
            
            // 背景ぼかし
            if (backgroundBlur != null)
            {
                backgroundBlur.EnableBlur();
            }
            
            // パネル表示
            if (detailPanel != null)
            {
                detailPanel.SetActive(true);
            }
            
            // テキスト設定
            UpdateTexts();
            
            // 3Dカード表示
            ShowCard();
            
            Debug.Log($"[ItemDetailView] Showing detail for: {item.displayName}");
        }
        
        /// <summary>
        /// 詳細表示を閉じる
        /// </summary>
        public void HideDetail()
        {
            if (!isShowing) return;
            
            isShowing = false;
            
            // 背景ぼかし解除
            if (backgroundBlur != null)
            {
                backgroundBlur.DisableBlur();
            }
            
            // パネル非表示
            if (detailPanel != null)
            {
                detailPanel.SetActive(false);
            }
            
            // カード削除
            if (currentCard != null)
            {
                Destroy(currentCard);
                currentCard = null;
            }
            
            currentItem = null;
            Debug.Log("[ItemDetailView] Detail hidden");
        }
        
        /// <summary>
        /// テキスト更新
        /// </summary>
        private void UpdateTexts()
        {
            if (currentItem == null) return;
            
            // 名前
            if (itemNameText != null)
            {
                itemNameText.text = currentItem.displayName;
            }
            
            // 説明
            if (descriptionText != null)
            {
                descriptionText.text = currentItem.description;
            }
            
            // フレーバーテキスト
            if (flavorText != null)
            {
                flavorText.text = $"<i>{currentItem.flavorText}</i>";
            }
            
            // ステータス（武器・防具・パッシブのみ）
            if (statsText != null && currentItem.HasStats)
            {
                string stats = "";
                if (currentItem.attack > 0)
                    stats += $"攻撃力: {currentItem.attack}\n";
                if (currentItem.defense > 0)
                    stats += $"防御力: {currentItem.defense}\n";
                if (currentItem.health > 0)
                    stats += $"HP: {currentItem.health}\n";
                if (currentItem.mana > 0)
                    stats += $"MP: {currentItem.mana}\n";
                
                statsText.text = stats;
            }
            else if (statsText != null)
            {
                statsText.text = "";
            }
            
            // レアリティバッジ
            if (rarityBadge != null)
            {
                rarityBadge.color = GetRarityColor(currentItem.rarity);
            }
        }
        
        /// <summary>
        /// 3Dカード表示
        /// </summary>
        private void ShowCard()
        {
            if (currentItem == null || cardContainer == null) return;
            
            // 既存のカードを削除
            if (currentCard != null)
            {
                Destroy(currentCard);
            }
            
            // 新しいカード生成
            if (currentItem.cardModel != null)
            {
                currentCard = Instantiate(currentItem.cardModel, cardContainer);
                currentCard.transform.localPosition = Vector3.zero;
                currentCard.transform.localRotation = Quaternion.identity;
                
                // 回転開始
                if (cardRotator != null)
                {
                    cardRotator.StartRotation(currentCard);
                }
            }
        }
        
        /// <summary>
        /// レアリティ色取得
        /// </summary>
        private Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.BRONZE: return new Color(0.8f, 0.5f, 0.3f);
                case ItemRarity.SILVER: return new Color(0.75f, 0.75f, 0.75f);
                case ItemRarity.GOLD: return new Color(1f, 0.84f, 0f);
                case ItemRarity.LEGENDARY: return new Color(1f, 0.5f, 0f);
                case ItemRarity.MYTHIC: return new Color(0.8f, 0.2f, 0.8f);
                default: return Color.white;
            }
        }
        
        void Update()
        {
            // カメラ移動で閉じる（後でCameraMouseFollowと連携）
            if (isShowing)
            {
                // TODO: カメラ移動検知
            }
        }
    }
}
