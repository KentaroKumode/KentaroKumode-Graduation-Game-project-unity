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
        
        // イベント定義
        public event Action<ItemData, int, int> OnItemAdded;           // アイテム追加（item, gridX, gridY）
        public event Action<int, int> OnItemRemoved;                   // アイテム削除（gridX, gridY）
        public event Action<ItemData> OnItemEquipped;                  // アイテム装備
        public event Action<ItemData> OnItemUnequipped;                // アイテム装備解除
        public event Action<ItemData> OnItemUsed;                      // アイテム使用
        public event Action<ItemData> OnItemDiscarded;                 // アイテム破棄
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
                
                // メモリリーク防止フレームワークに通知
                if (MemoryLeakPreventionFramework.Instance != null)
                {
                    MemoryLeakPreventionFramework.RegisterStaticInstance(typeof(InventoryManager), this);
                }
                
                Destroy(gameObject);
                return;
            }
            instance = this;
            
            // メモリリーク防止フレームワークに登録
            if (MemoryLeakPreventionFramework.Instance != null)
            {
                MemoryLeakPreventionFramework.RegisterStaticInstance(typeof(InventoryManager), this);
            }
            
            Debug.Log("[InventoryManager] Initialized");
        }
        
        void Start()
        {
            // GridManagerの参照チェック
            if (gridManager == null)
            {
                gridManager = GetComponent<GridManager>();
            }
            
            if (gridManager == null)
            {
                Debug.LogError("[InventoryManager] GridManager not found!");
            }
        }
        
        /// <summary>
        /// アイテムを追加
        /// </summary>
        public bool AddItem(ItemData item, int gridX, int gridY)
        {
            if (gridManager == null)
            {
                Debug.LogError("[InventoryManager] GridManager is null!");
                return false;
            }
            
            // 配置可能かチェック（後でPlacementValidatorに移譲）
            Debug.Log($"[InventoryManager] AddItem: {item.itemName} を ({gridX}, {gridY}) に配置を試行");
            if (!CanPlaceItem(item, gridX, gridY))
            {
                Debug.LogWarning($"[InventoryManager] Cannot place item {item.itemName} at ({gridX}, {gridY})");
                return false;
            }
            
            Debug.Log($"[InventoryManager] 配置可能確認済み、GridManager.PlaceItem呼び出し開始");
            // 配置処理（GridManagerの状態を更新）
            if (gridManager != null)
            {
                gridManager.PlaceItem(gridX, gridY, item.sizeX, item.sizeY, item);
            }
            
            OnItemAdded?.Invoke(item, gridX, gridY);
            Debug.Log($"[InventoryManager] Item added: {item.itemName} at ({gridX}, {gridY})");
            return true;
        }
        
        /// <summary>
        /// アイテムを自動配置（空きスペースに配置）
        /// </summary>
        public bool TryAddItemAuto(ItemData item)
        {
            if (gridManager == null)
            {
                Debug.LogError("[InventoryManager] GridManager is null!");
                return false;
            }
            
            Debug.Log($"[InventoryManager] TryAddItemAuto開始: {item.itemName} (size: {item.sizeX}x{item.sizeY})");
            Debug.Log($"[InventoryManager] アンロック行数: {gridManager.GetUnlockedRows()}");
            
            // 空きスペースを検索
            for (int y = 0; y < gridManager.GetUnlockedRows(); y++)
            {
                for (int x = 0; x <= InventoryConstants.GRID_WIDTH - item.sizeX; x++)
                {
                    Debug.Log($"[InventoryManager] チェック位置 ({x}, {y})");
                    if (CanPlaceItem(item, x, y))
                    {
                        Debug.Log($"[InventoryManager] 配置可能位置発見: ({x}, {y})");
                        return AddItem(item, x, y);
                    }
                    else
                    {
                        Debug.Log($"[InventoryManager] 配置不可: ({x}, {y})");
                    }
                }
            }
            
            Debug.LogWarning($"[InventoryManager] No space available for item: {item.itemName} (size: {item.sizeX}x{item.sizeY})");
            return false;
        }
        
        /// <summary>
        /// アイテムを削除
        /// </summary>
        public void RemoveItem(int gridX, int gridY, ItemData item)
        {
            if (gridManager != null)
            {
                gridManager.RemoveItem(gridX, gridY, item.sizeX, item.sizeY);
            }
            
            OnItemRemoved?.Invoke(gridX, gridY);
            Debug.Log($"[InventoryManager] Item removed at ({gridX}, {gridY})");
        }
        
        /// <summary>
        /// アイテムを装備
        /// </summary>
        public void EquipItem(ItemData item)
        {
            if (!item.IsEquippable())
            {
                Debug.LogWarning($"[InventoryManager] Item {item.itemName} is not equippable");
                return;
            }
            
            OnItemEquipped?.Invoke(item);
            Debug.Log($"[InventoryManager] Item equipped: {item.itemName}");
        }
        
        /// <summary>
        /// グリッドを拡張
        /// </summary>
        public void ExpandGrid()
        {
            if (gridManager != null)
            {
                int newRows = gridManager.GetUnlockedRows() + 1;
                if (newRows <= InventoryConstants.GRID_HEIGHT)
                {
                    gridManager.UnlockRow(newRows);
                    OnGridExpanded?.Invoke(newRows);
                    Debug.Log($"[InventoryManager] Grid expanded to {newRows} rows");
                }
            }
        }
        
        /// <summary>
        /// インベントリを開く
        /// </summary>
        public void OpenInventory()
        {
            OnInventoryOpened?.Invoke();
            Debug.Log("[InventoryManager] Inventory opened");
        }
        
        /// <summary>
        /// インベントリを閉じる
        /// </summary>
        public void CloseInventory()
        {
            OnInventoryClosed?.Invoke();
            Debug.Log("[InventoryManager] Inventory closed");
        }
        
        // 仮の配置チェック(後でPlacementValidatorに移動)
        private bool CanPlaceItem(ItemData item, int gridX, int gridY)
        {
            Debug.Log($"[InventoryManager] CanPlaceItem呼び出し: {item.itemName} at ({gridX}, {gridY}) size {item.sizeX}x{item.sizeY}");
            
            if (gridManager == null)
            {
                Debug.LogError("[InventoryManager] GridManager is null in CanPlaceItem!");
                return false;
            }
            
            bool result = gridManager.CanPlaceItem(gridX, gridY, item.sizeX, item.sizeY);
            Debug.Log($"[InventoryManager] CanPlaceItem結果: {result} for {item.itemName} at ({gridX}, {gridY})");
            return result;
        }
        
        /// <summary>
        /// メモリリーク防止：静的インスタンスの安全な解除
        /// </summary>
        void OnDestroy()
        {
            if (instance == this)
            {
                // メモリリーク防止フレームワークから解除
                if (MemoryLeakPreventionFramework.Instance != null)
                {
                    MemoryLeakPreventionFramework.UnregisterStaticInstance(typeof(InventoryManager));
                }
                
                instance = null;
                Debug.Log("[InventoryManager] Static instance safely cleared");
            }
        }
    }
}
