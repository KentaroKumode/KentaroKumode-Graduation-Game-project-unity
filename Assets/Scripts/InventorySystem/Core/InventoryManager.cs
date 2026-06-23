using UnityEngine;
using System;

namespace InventorySystem
{
    /// <summary>
    /// インベントリシステムの中央管理コンポーネント
    /// イベント駆動で各サブコンポーネントと通信
    /// </summary>
    public class InventoryManager : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private PlacementValidator placementValidator;
        
        // イベント定義
        public event Action<CompleteItemData, int, int> OnItemAdded;           // アイテム追加（item, gridX, gridY）
        public event Action<int, int> OnItemRemoved;                   // アイテム削除（gridX, gridY）
        public event Action<CompleteItemData> OnItemEquipped;                  // アイテム装備
        public event Action<CompleteItemData> OnItemUnequipped;                // アイテム装備解除
        public event Action<CompleteItemData> OnItemUsed;                      // アイテム使用
        public event Action<CompleteItemData> OnItemDiscarded;                 // アイテム破棄
        public event Action<int> OnGridExpanded;                       // グリッド拡張（新しい行数）
        public event Action<ItemCategory> OnFilterChanged;             // フィルター変更
        public event Action OnInventoryOpened;                         // インベントリ開く
        public event Action OnInventoryClosed;                         // インベントリ閉じる
        
        private static InventoryManager instance;
        public static InventoryManager Instance => instance;
        
        void Awake()
        {
            // シングルトン
            if (instance != null && instance != this)
            {
                Debug.LogWarning($"[InventoryManager] Duplicate instance detected. Destroying {gameObject.name}");
                Destroy(gameObject);
                return;
            }
            instance = this;
            Debug.Log("[InventoryManager] Initialized");
        }
        
        void Start()
        {
            if (gridManager == null)
                gridManager = GetComponent<GridManager>();
            if (gridManager == null)
                Debug.LogError("[InventoryManager] GridManager not found!");
            
            if (placementValidator == null)
                placementValidator = FindObjectOfType<PlacementValidator>();
        }
        
        /// <summary>
        /// アイテムを追加
        /// </summary>
        public bool AddItem(CompleteItemData item, int gridX, int gridY)
        {
            if (gridManager == null)
            {
                Debug.LogError("[InventoryManager] GridManager is null!");
                return false;
            }
            
            if (placementValidator != null && !placementValidator.CanPlaceItem(item, gridX, gridY))
            {
                Debug.LogWarning($"[InventoryManager] Cannot place item {item.displayName} at ({gridX}, {gridY})");
                return false;
            }
            else if (placementValidator == null && !gridManager.CanPlaceItem(gridX, gridY, item.size.x, item.size.y))
            {
                Debug.LogWarning($"[InventoryManager] Cannot place item {item.displayName} at ({gridX}, {gridY})");
                return false;
            }
            
            gridManager.PlaceItem(gridX, gridY, item.size.x, item.size.y, item);
            OnItemAdded?.Invoke(item, gridX, gridY);
            return true;
        }
        
        /// <summary>
        /// アイテムを自動配置（空きスペースに配置）
        /// </summary>
        public bool TryAddItemAuto(CompleteItemData item)
        {
            if (gridManager == null)
            {
                Debug.LogError("[InventoryManager] GridManager is null!");
                return false;
            }
            
            int unlockedRows = gridManager.GetUnlockedRows();
            
            // PlacementValidator経由で空きスペースを検索
            if (placementValidator != null && placementValidator.TryFindPlacement(item, unlockedRows, out int x, out int y))
            {
                return AddItem(item, x, y);
            }
            else if (placementValidator == null)
            {
                // フォールバック: PlacementValidatorがない場合は直接検索
                for (int fy = 0; fy < unlockedRows; fy++)
                {
                    for (int fx = 0; fx <= InventoryConstants.GRID_WIDTH - item.size.x; fx++)
                    {
                        if (gridManager.CanPlaceItem(fx, fy, item.size.x, item.size.y))
                            return AddItem(item, fx, fy);
                    }
                }
            }
            
            // グリッドに空きがない → HoldingAreaにフォールバック
            var holdingArea = ItemHoldingArea.Instance;
            if (holdingArea != null && holdingArea.AddItem(item))
            {
                Debug.Log($"[InventoryManager] グリッド満杯のため HoldingArea に一時保持: {item.displayName}");
                return true;
            }

            Debug.LogWarning($"[InventoryManager] No space available for item: {item.displayName} (size: {item.size.x}x{item.size.y})");
            return false;
        }
        
        /// <summary>
        /// アイテムを削除
        /// </summary>
        public void RemoveItem(int gridX, int gridY, CompleteItemData item)
        {
            if (gridManager != null)
            {
                gridManager.RemoveItem(gridX, gridY, item.size.x, item.size.y);
            }
            
            OnItemRemoved?.Invoke(gridX, gridY);
        }
        
        /// <summary>
        /// アイテムを装備
        /// </summary>
        public void EquipItem(CompleteItemData item)
        {
            if (!item.IsEquippable) return;
            OnItemEquipped?.Invoke(item);
        }
        
        /// <summary>
        /// アイテム装備を解除
        /// </summary>
        public void UnequipItem(CompleteItemData item)
        {
            if (item == null) return;
            OnItemUnequipped?.Invoke(item);
        }
        
        /// <summary>
        /// グリッドを拡張
        /// </summary>
        public void ExpandGrid()
        {
            if (gridManager == null) return;
            int newRows = gridManager.GetUnlockedRows() + 1;
            if (newRows <= InventoryConstants.GRID_HEIGHT)
            {
                gridManager.UnlockRow(newRows);
                OnGridExpanded?.Invoke(newRows);
            }
        }
        
        /// <summary>
        /// インベントリを開く
        /// </summary>
        public void OpenInventory()
        {
            OnInventoryOpened?.Invoke();
        }
        
        /// <summary>
        /// インベントリを閉じる
        /// </summary>
        public void CloseInventory()
        {
            OnInventoryClosed?.Invoke();
        }
        
        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
