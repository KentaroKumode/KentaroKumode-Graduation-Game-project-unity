using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム破棄処理 - WarningDialog統合版
    /// 
    /// <para><b>機能:</b></para>
    /// <list type="bullet">
    ///   <item>破棄前確認ダイアログ表示</item>
    ///   <item>安全な破棄処理（確認後のみ実行）</item>
    ///   <item>WarningDialog自動検出</item>
    ///   <item>詳細ログ出力とユーザーフィードバック</item>
    /// </list>
    /// 
    /// <para><b>使い方:</b></para>
    /// 1. Inspector で WarningDialog を設定（または自動検出）
    /// 2. DiscardItem() でアイテム破棄開始
    /// 3. 確認ダイアログで「破棄する」選択時のみ実行
    /// </summary>
    public class ItemDiscardHandler : MonoBehaviour
    {
        [Header("UI連携")]
        [SerializeField] private WarningDialog warningDialog;
        
        void Awake()
        {
            // WarningDialogが手動でアサインされていない場合は自動検索
            if (warningDialog == null)
            {
                warningDialog = FindObjectOfType<WarningDialog>();
                if (warningDialog != null)
                {
                    Debug.Log("[ItemDiscardHandler] WarningDialog auto-detected");
                }
                else
                {
                    Debug.LogWarning("[ItemDiscardHandler] WarningDialog not found in scene");
                }
            }
        }
        /// <summary>
        /// アイテムを破棄（確認ダイアログ付き）
        /// </summary>
        public void DiscardItem(CompleteItemData item, ItemSlot slot)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemDiscardHandler] ⚠️ Cannot discard: Item is null");
                return;
            }
            
            // 確認ダイアログ表示
            if (warningDialog != null)
            {
                // 破棄確認ダイアログを表示（「はい」選択時にExecuteDiscardを実行）
                warningDialog.ShowDiscardConfirmation(item, () => {
                    ExecuteDiscard(item, slot);
                });
                
                Debug.Log($"[ItemDiscardHandler] 🗑️ Discard confirmation shown for: {item.displayName}");
            }
            else
            {
                Debug.LogWarning("[ItemDiscardHandler] ⚠️ WarningDialog not found - proceeding without confirmation");
                // フォールバック：確認なしで実行（開発中のみ）
                ExecuteDiscard(item, slot);
            }
        }
        
        /// <summary>
        /// 破棄を実行（確認済み）
        /// </summary>
        private void ExecuteDiscard(CompleteItemData item, ItemSlot slot)
        {
            if (item == null)
            {
                Debug.LogError("[ItemDiscardHandler] ❌ ExecuteDiscard called with null item");
                return;
            }
            
            // アイテム価値情報の記録（破棄前）。フォールバックは 1/5 デノミ込み(basePrice / 10)
            int itemValue = item.sellPrice?.min ?? UnityEngine.Mathf.Max(1, item.basePrice / 10);
            string rarityIcon = GetRarityIcon(item.rarity);
            
            // インベントリからアイテムを削除
            bool removalSuccess = false;
            if (slot != null)
            {
                int x = slot.GridX;
                int y = slot.GridY;
                CompleteItemData itemData = slot.ItemData;
                
                // スロットクリア
                slot.ClearItem();
                
                // InventoryManagerに削除を通知
                if (itemData != null)
                {
                    try
                    {
                        InventoryManager.Instance?.RemoveItem(x, y, itemData);
                        removalSuccess = true;
                        Debug.Log($"[ItemDiscardHandler] 🗑️ Removed from inventory grid [{x},{y}]");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[ItemDiscardHandler] ❌ Failed to remove from inventory: {ex.Message}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[ItemDiscardHandler] ⚠️ ItemSlot is null - item may not be properly removed");
            }
            
            // 効果音再生
            try
            {
                InventorySoundManager.Instance?.PlayItemDiscard();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ItemDiscardHandler] Failed to play discard sound: {ex.Message}");
            }
            
            // 破棄完了ログ
            if (removalSuccess)
            {
                Debug.Log($"[ItemDiscardHandler] 🗑️✅ DISCARD SUCCESS: {rarityIcon} {item.displayName}");
                Debug.Log($"[ItemDiscardHandler] 💰 Lost value: ~{itemValue} coins, Size: {item.sizeX}×{item.sizeY}");
            }
            else
            {
                Debug.LogWarning($"[ItemDiscardHandler] ⚠️ DISCARD PARTIAL: {item.displayName} (removal may have failed)");
            }
            
            // 開発中のデバッグ情報
            if (Application.isEditor)
            {
                Debug.Log($"[ItemDiscardHandler] Debug - Rarity: {item.rarity}, Category: {item.category}, BasePrice: {item.basePrice}");
            }
        }
        
        /// <summary>
        /// レアリティ別アイコン取得
        /// </summary>
        private string GetRarityIcon(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.BRONZE => "🥉",
                ItemRarity.SILVER => "🥈", 
                ItemRarity.GOLD => "🥇",
                ItemRarity.LEGENDARY => "⭐",
                ItemRarity.MYTHIC => "💎",
                _ => "❓"
            };
        }
    }
}
