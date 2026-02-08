using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CoinSystem;

namespace InventorySystem
{
    /// <summary>
    /// アイテム売却処理 - CoinSystem統合版
    /// 
    /// <para><b>機能:</b></para>
    /// <list type="bullet">
    ///   <item>アイテム売却価格に基づいたコイン排出</item>
    ///   <item>CoinSystemController自動検出</item>
    ///   <item>売却時の詳細ログ出力</item>
    ///   <item>インベントリからのアイテム削除</item>
    ///   <item>エラー処理とフェイルセーフ</item>
    /// </list>
    /// 
    /// <para><b>使い方:</b></para>
    /// 1. Inspector で CoinSystemController を設定（または自動検出）
    /// 2. EnableSellMode() で売却モード開始
    /// 3. SellItem() でアイテム売却実行
    /// 4. DisableSellMode() で売却モード終了
    /// 
    /// <para><b>統合システム:</b></para>
    /// - CoinSystem: 物理コイン排出・音声・ディスプレイ更新
    /// - InventorySystem: アイテム削除・UI更新
    /// </summary>
    public class ItemSellHandler : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private TextMeshProUGUI priceOverlayText;
        
        [Header("CoinSystem連携")]
        [SerializeField] private CoinSystemController coinSystemController;
        
        private bool isSellMode = false;
        
        void Awake()
        {
            // CoinSystemControllerが手動でアサインされていない場合は自動検索
            if (coinSystemController == null)
            {
                coinSystemController = FindObjectOfType<CoinSystemController>();
                if (coinSystemController != null)
                {
                    Debug.Log("[ItemSellHandler] CoinSystemController auto-detected");
                }
                else
                {
                    Debug.LogWarning("[ItemSellHandler] CoinSystemController not found in scene");
                }
            }
        }
        
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
            
            // ===== CoinSystemと連携 =====
            if (coinSystemController != null && sellPrice > 0)
            {
                // コインを排出（売却価格分）
                coinSystemController.DispenseCoins(sellPrice);
                Debug.Log($"[ItemSellHandler] ✅ Sale completed: '{item.displayName}' → {sellPrice} coins dispensed");
                
                // サウンドフィードバック（CoinSystemが自動で排出音を再生）
                // 追加のUI効果があれば此処に実装
            }
            else if (coinSystemController == null)
            {
                Debug.LogWarning("[ItemSellHandler] ⚠️ CoinSystemController not found! Sale price will be lost.");
                Debug.LogWarning($"[ItemSellHandler] Lost sale: {item.displayName} worth {sellPrice} coins");
            }
            else if (sellPrice <= 0)
            {
                Debug.LogWarning($"[ItemSellHandler] ⚠️ Cannot sell {item.displayName}: Invalid price ({sellPrice})");
                return; // 無効な価格の場合は売却をキャンセル
            }
            
            // アイテムを削除
            if (slot != null)
            {
                int x = slot.GridX;
                int y = slot.GridY;
                CompleteItemData itemData = slot.ItemData;
                
                slot.ClearItem();
                
                // インベントリマネージャーにアイテム削除を通知
                if (itemData != null)
                {
                    InventoryManager.Instance?.RemoveItem(x, y, itemData);
                    Debug.Log($"[ItemSellHandler] 🗑️ Removed {itemData.displayName} from inventory grid [{x},{y}]");
                }
            }
            else
            {
                Debug.LogWarning("[ItemSellHandler] ⚠️ ItemSlot is null - item may not be properly removed from inventory");
            }
            
            // 成功ログ
            string successMessage = $"[ItemSellHandler] 💰 SALE SUCCESS: {item.displayName} → {sellPrice} coins";
            Debug.Log(successMessage);
            
            // 開発中のデバッグ情報
            if (Application.isEditor)
            {
                Debug.Log($"[ItemSellHandler] Debug Info - Rarity: {item.rarity}, BasePrice: {item.basePrice}, SellRange: {item.sellPrice.min}-{item.sellPrice.max}");
            }
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
