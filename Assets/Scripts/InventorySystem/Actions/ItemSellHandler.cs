using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// アイテム売却処理
    /// </summary>
    public class ItemSellHandler : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private TextMeshProUGUI priceOverlayText;
        
        private bool isSellMode = false;
        
        /// <summary>
        /// 売却モードを有効化
        /// </summary>
        public void EnableSellMode()
        {
            isSellMode = true;
            Debug.Log("[ItemSellHandler] Sell mode enabled");
        }
        
        /// <summary>
        /// 売却モードを無効化
        /// </summary>
        public void DisableSellMode()
        {
            isSellMode = false;
            Debug.Log("[ItemSellHandler] Sell mode disabled");
        }
        
        /// <summary>
        /// アイテムを売却
        /// </summary>
        public void SellItem(CompleteItemData item, ItemSlot slot)
        {
            if (!isSellMode || item == null)
            {
                return;
            }
            
            // 確認ダイアログ
            // TODO: WarningDialogで確認
            
            // 売却実行
            ExecuteSell(item, slot);
        }
        
        /// <summary>
        /// 売却を実行
        /// </summary>
        private void ExecuteSell(CompleteItemData item, ItemSlot slot)
        {
            // 売却価格を取得
            int sellPrice = item.sellPrice.min; // 最低価格を使用
            
            // 通貨を追加（後でCoinSystemと連携）
            // TODO: CoinSystemに売却額を渡す
            
            // アイテムを削除
            if (slot != null)
            {
                int x = slot.GridX;
                int y = slot.GridY;
                CompleteItemData itemData = slot.ItemData; // アイテムデータを取得
                
                slot.ClearItem();
                
                // イベント発火
                if (itemData != null)
                {
                    InventoryManager.Instance?.RemoveItem(x, y, itemData);
                }
            }
            
            Debug.Log($"[ItemSellHandler] Sold: {item.displayName} for {sellPrice} coins");
        }
        
        /// <summary>
        /// 価格オーバーレイを表示
        /// </summary>
        public void ShowPriceOverlay(CompleteItemData item, Vector3 position)
        {
            if (!isSellMode || item == null) return;
            
            if (priceOverlayText != null)
            {
                priceOverlayText.text = $"{item.sellPrice.min}G";
                priceOverlayText.transform.position = position;
                priceOverlayText.gameObject.SetActive(true);
            }
        }
        
        /// <summary>
        /// 価格オーバーレイを非表示
        /// </summary>
        public void HidePriceOverlay()
        {
            if (priceOverlayText != null)
            {
                priceOverlayText.gameObject.SetActive(false);
            }
        }
    }
}
