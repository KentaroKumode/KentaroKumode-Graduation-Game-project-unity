using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム一時保持エリア（画面左下カード重ね表示）
    /// 
    /// <para><b>機能:</b></para>
    /// <list type="bullet">
    ///   <item>インベントリ満杯時のアイテム一時保管</item>
    ///   <item>面積降順ソートで重ね表示（大サイズ→下）</item>
    ///   <item>D&amp;Dでインベントリへ配置可能</item>
    ///   <item>最大保持数制限</item>
    /// </list>
    /// 
    /// <para><b>ソートルール:</b></para>
    /// 1. 面積(sizeX × sizeY)降順
    /// 2. 面積同一ならsizeX降順
    /// 3. 最も大きいアイテムが一番下
    /// 
    /// <para><b>使い方:</b></para>
    /// <code>
    /// var holdingArea = ItemHoldingArea.Instance;
    /// holdingArea.AddItem(newItem);        // 一時保持に追加
    /// holdingArea.TryMoveToInventory(item); // インベントリへ移動試行
    /// </code>
    /// </summary>
    public class ItemHoldingArea : MonoBehaviour
    {
        // =================================================================
        //  シングルトン
        // =================================================================
        
        private static ItemHoldingArea instance;
        public static ItemHoldingArea Instance
        {
            get
            {
                if (instance == null)
                    instance = FindObjectOfType<ItemHoldingArea>();
                return instance;
            }
        }

        // =================================================================
        //  Inspector設定
        // =================================================================

        [Header("表示設定")]
        [SerializeField] private Transform holdingAreaAnchor;         // カード表示の基準位置（画面左下）
        [SerializeField] private Vector3 cardStackOffset = new Vector3(0f, 0.05f, 0.02f); // カード間のオフセット（少し上にずらす）
        [SerializeField] private float cardScale = 0.3f;              // カード表示スケール
        
        [Header("制限")]
        [SerializeField] private int maxHoldItems = 5;                // 最大保持数
        
        [Header("参照")]
        [SerializeField] private Camera displayCamera;                // カード表示用カメラ

        // =================================================================
        //  内部状態
        // =================================================================

        /// <summary>保持中のアイテムデータ</summary>
        private List<HeldItemEntry> heldItems = new List<HeldItemEntry>();

        /// <summary>保持アイテム1件のデータ</summary>
        private class HeldItemEntry
        {
            public CompleteItemData itemData;
            public GameObject cardInstance;   // 3Dカードの表示オブジェクト
            public int area;                  // sizeX * sizeY
        }

        // イベント
        public event System.Action<CompleteItemData> OnItemAdded;
        public event System.Action<CompleteItemData> OnItemRemoved;
        public event System.Action OnHoldingAreaChanged;

        /// <summary>現在の保持アイテム数</summary>
        public int Count => heldItems.Count;
        
        /// <summary>保持エリアが空か</summary>
        public bool IsEmpty => heldItems.Count == 0;
        
        /// <summary>保持エリアが満杯か</summary>
        public bool IsFull => heldItems.Count >= maxHoldItems;

        // =================================================================
        //  ライフサイクル
        // =================================================================
        
        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            
            if (displayCamera == null)
                displayCamera = Camera.main;
        }

        void OnDestroy()
        {
            // メモリリーク防止
            ClearAll();
            
            OnItemAdded = null;
            OnItemRemoved = null;
            OnHoldingAreaChanged = null;
            
            if (instance == this) instance = null;
        }

        // =================================================================
        //  公開 API
        // =================================================================

        /// <summary>
        /// アイテムを一時保持エリアに追加
        /// </summary>
        /// <returns>追加成功ならtrue</returns>
        public bool AddItem(CompleteItemData item)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemHoldingArea] ⚠️ Cannot add null item");
                return false;
            }
            
            if (IsFull)
            {
                Debug.LogWarning($"[ItemHoldingArea] ⚠️ Holding area full ({maxHoldItems} items max)");
                return false;
            }
            
            // エントリ作成
            var entry = new HeldItemEntry
            {
                itemData = item,
                cardInstance = null,
                area = item.sizeX * item.sizeY
            };
            
            heldItems.Add(entry);
            
            // ソート（面積降順 → sizeX降順）
            SortHeldItems();
            
            // 表示更新
            RebuildDisplay();
            
            OnItemAdded?.Invoke(item);
            OnHoldingAreaChanged?.Invoke();
            
            Debug.Log($"[ItemHoldingArea] ➕ Added: {item.displayName} ({item.sizeX}×{item.sizeY}, area={entry.area}) [{heldItems.Count}/{maxHoldItems}]");
            return true;
        }

        /// <summary>
        /// アイテムを一時保持エリアから除去
        /// </summary>
        public bool RemoveItem(CompleteItemData item)
        {
            if (item == null) return false;
            
            var entry = heldItems.FirstOrDefault(e => e.itemData == item);
            if (entry == null)
            {
                Debug.LogWarning($"[ItemHoldingArea] ⚠️ Item not found in holding area: {item.displayName}");
                return false;
            }
            
            // 3Dカード破棄
            if (entry.cardInstance != null)
            {
                Destroy(entry.cardInstance);
                entry.cardInstance = null;
            }
            
            heldItems.Remove(entry);
            
            // 表示更新
            RebuildDisplay();
            
            OnItemRemoved?.Invoke(item);
            OnHoldingAreaChanged?.Invoke();
            
            Debug.Log($"[ItemHoldingArea] ➖ Removed: {item.displayName} [{heldItems.Count}/{maxHoldItems}]");
            return true;
        }

        /// <summary>
        /// アイテムをインベントリに移動試行
        /// </summary>
        /// <returns>配置成功ならtrue</returns>
        public bool TryMoveToInventory(CompleteItemData item)
        {
            if (item == null) return false;
            
            var invManager = InventoryManager.Instance;
            if (invManager == null)
            {
                Debug.LogWarning("[ItemHoldingArea] ⚠️ InventoryManager not found");
                return false;
            }
            
            // 自動配置試行
            bool placed = invManager.TryAddItemAuto(item);
            if (placed)
            {
                RemoveItem(item);
                Debug.Log($"[ItemHoldingArea] ✅ Moved to inventory: {item.displayName}");
                return true;
            }
            
            Debug.Log($"[ItemHoldingArea] ❌ No space for: {item.displayName} ({item.sizeX}×{item.sizeY})");
            return false;
        }

        /// <summary>
        /// 全アイテムのインベントリ移動を試行
        /// </summary>
        /// <returns>移動できたアイテム数</returns>
        public int TryMoveAllToInventory()
        {
            int moved = 0;
            
            // コピーを使用（ループ中にリスト変更されるため）
            var itemsCopy = heldItems.Select(e => e.itemData).ToList();
            
            foreach (var item in itemsCopy)
            {
                if (TryMoveToInventory(item))
                    moved++;
            }
            
            Debug.Log($"[ItemHoldingArea] 📦 Moved {moved}/{itemsCopy.Count} items to inventory");
            return moved;
        }

        /// <summary>
        /// 保持エリアの全アイテムを取得（読み取り専用）
        /// </summary>
        public IReadOnlyList<CompleteItemData> GetHeldItems()
        {
            return heldItems.Select(e => e.itemData).ToList().AsReadOnly();
        }

        /// <summary>
        /// 指定インデックスのアイテムを取得
        /// </summary>
        public CompleteItemData GetItemAt(int index)
        {
            if (index < 0 || index >= heldItems.Count) return null;
            return heldItems[index].itemData;
        }

        /// <summary>
        /// 保持エリアを全クリア
        /// </summary>
        public void ClearAll()
        {
            foreach (var entry in heldItems)
            {
                if (entry.cardInstance != null)
                {
                    Destroy(entry.cardInstance);
                    entry.cardInstance = null;
                }
            }
            
            heldItems.Clear();
            OnHoldingAreaChanged?.Invoke();
            
            Debug.Log("[ItemHoldingArea] 🗑️ All items cleared");
        }

        // =================================================================
        //  ソート
        // =================================================================

        /// <summary>
        /// ソート: 面積降順 → sizeX降順
        /// 最も大きいアイテムが一番下（リスト先頭 = 下）
        /// </summary>
        private void SortHeldItems()
        {
            heldItems.Sort((a, b) =>
            {
                // 面積降順
                int areaCompare = b.area.CompareTo(a.area);
                if (areaCompare != 0) return areaCompare;
                
                // 面積同一ならsizeX降順
                return b.itemData.sizeX.CompareTo(a.itemData.sizeX);
            });
        }

        // =================================================================
        //  表示管理
        // =================================================================

        /// <summary>
        /// カード表示を再構築
        /// </summary>
        private void RebuildDisplay()
        {
            // 既存カードをすべて破棄
            foreach (var entry in heldItems)
            {
                if (entry.cardInstance != null)
                {
                    Destroy(entry.cardInstance);
                    entry.cardInstance = null;
                }
            }
            
            if (holdingAreaAnchor == null)
            {
                Debug.LogWarning("[ItemHoldingArea] ⚠️ holdingAreaAnchor not set - skipping display");
                return;
            }
            
            // 下から順にカードを配置（リスト先頭 = 一番下）
            for (int i = 0; i < heldItems.Count; i++)
            {
                var entry = heldItems[i];
                CreateCardDisplay(entry, i);
            }
        }

        /// <summary>
        /// 1枚のカード3D表示を生成
        /// </summary>
        private void CreateCardDisplay(HeldItemEntry entry, int stackIndex)
        {
            if (entry.itemData.fbxModel == null)
            {
                Debug.LogWarning($"[ItemHoldingArea] ⚠️ No FBX model for: {entry.itemData.displayName}");
                return;
            }
            
            // FBXモデルをインスタンス化
            var card = Instantiate(entry.itemData.fbxModel);
            card.name = $"HeldCard_{stackIndex}_{entry.itemData.displayName}";
            
            // 保持エリアアンカーの子に配置
            card.transform.SetParent(holdingAreaAnchor, false);
            
            // 位置: 上にずらしながら重ねる
            card.transform.localPosition = cardStackOffset * stackIndex;
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = Vector3.one * cardScale;
            
            // 物理を無効化
            var rb = card.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            
            // コライダーを有効化（D&D用レイキャスト検出）
            var colliders = card.GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = true;
            }
            
            // コライダーがなければBoxColliderを追加
            if (colliders.Length == 0)
            {
                var bc = card.AddComponent<BoxCollider>();
                bc.size = new Vector3(entry.itemData.sizeX * 0.5f, 0.05f, entry.itemData.sizeY * 0.5f);
            }
            
            entry.cardInstance = card;
        }

        // =================================================================
        //  D&D ヘルパー
        // =================================================================

        /// <summary>
        /// 指定されたGameObjectが保持エリアのカードかチェック
        /// </summary>
        /// <param name="obj">チェック対象</param>
        /// <param name="itemData">見つかったアイテムデータ（out）</param>
        /// <returns>保持カードならtrue</returns>
        public bool IsHeldCard(GameObject obj, out CompleteItemData itemData)
        {
            itemData = null;
            if (obj == null) return false;
            
            foreach (var entry in heldItems)
            {
                if (entry.cardInstance == obj || 
                    (entry.cardInstance != null && obj.transform.IsChildOf(entry.cardInstance.transform)))
                {
                    itemData = entry.itemData;
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 指定されたGameObjectが保持エリアのカードかチェック（簡易版）
        /// </summary>
        public bool IsHeldCard(GameObject obj)
        {
            return IsHeldCard(obj, out _);
        }

        /// <summary>
        /// GameObjectからアイテムデータを取得
        /// </summary>
        public CompleteItemData GetItemByCard(GameObject obj)
        {
            if (IsHeldCard(obj, out CompleteItemData item))
                return item;
            return null;
        }

        /// <summary>
        /// 一番上のカード（最後に追加されたアイテム）を取得
        /// </summary>
        public CompleteItemData GetTopItem()
        {
            if (heldItems.Count == 0) return null;
            return heldItems[heldItems.Count - 1].itemData;
        }

        // =================================================================
        //  デバッグ
        // =================================================================

        /// <summary>保持エリアの状態をログ出力</summary>
        [ContextMenu("Debug: Print Held Items")]
        public void DebugPrintHeldItems()
        {
            Debug.Log($"[ItemHoldingArea] === Held Items ({heldItems.Count}/{maxHoldItems}) ===");
            for (int i = 0; i < heldItems.Count; i++)
            {
                var entry = heldItems[i];
                string pos = i == 0 ? "(BOTTOM)" : i == heldItems.Count - 1 ? "(TOP)" : "";
                Debug.Log($"  [{i}] {entry.itemData.displayName} - {entry.itemData.sizeX}×{entry.itemData.sizeY} (area={entry.area}) {pos}");
            }
        }
    }
}
