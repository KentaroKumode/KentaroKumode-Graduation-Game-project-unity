using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテムの自動配置ロジック
    /// </summary>
    public class AutoPlacementManager : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private PlacementValidator validator;
        
        void Start()
        {
            if (gridManager == null)
                gridManager = FindObjectOfType<GridManager>();
            
            if (validator == null)
                validator = FindObjectOfType<PlacementValidator>();
        }
        
        /// <summary>
        /// アイテムを自動配置
        /// </summary>
        public bool TryAutoPlace(CompleteItemData item, out int outX, out int outY)
        {
            outX = -1;
            outY = -1;
            
            if (item == null)
            {
                Debug.LogWarning("[AutoPlacementManager] Item is null");
                return false;
            }
            
            // 左上から順に空きスロットを探す
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    if (validator.CanPlaceItem(item, x, y, out string reason))
                    {
                        outX = x;
                        outY = y;
                        Debug.Log($"[AutoPlacementManager] Found placement at ({x}, {y})");
                        return true;
                    }
                }
            }
            
            Debug.LogWarning($"[AutoPlacementManager] No space found for item: {item.displayName}");
            return false;
        }
        
        /// <summary>
        /// 最適な配置場所を探す（サイズに合わせて）
        /// </summary>
        public bool TryFindOptimalPlacement(CompleteItemData item, out int outX, out int outY)
        {
            outX = -1;
            outY = -1;
            
            // まずは通常の自動配置を試す
            if (TryAutoPlace(item, out outX, out outY))
            {
                return true;
            }
            
            // TODO: より高度な配置アルゴリズム
            // - 空きスペースの断片化を最小化
            // - 同じカテゴリーをまとめて配置
            
            return false;
        }
    }
}
