using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// [非推奨] PlacementValidator.TryFindPlacement に統合済み
    /// 既存シーン参照の互換性のため残存。新規利用禁止。
    /// </summary>
    [System.Obsolete("PlacementValidator.TryFindPlacement を使用してください")]
    public class AutoPlacementManager : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private PlacementValidator validator;
        
        void Start()
        {
            if (validator == null)
                validator = FindObjectOfType<PlacementValidator>();
        }
        
        /// <summary>
        /// PlacementValidator.TryFindPlacement に委譲
        /// </summary>
        public bool TryAutoPlace(CompleteItemData item, out int outX, out int outY)
        {
            outX = -1;
            outY = -1;
            if (item == null || validator == null) return false;
            
            var gridManager = FindObjectOfType<GridManager>();
            int unlockedRows = gridManager != null ? gridManager.GetUnlockedRows() : InventoryConstants.GRID_HEIGHT;
            return validator.TryFindPlacement(item, unlockedRows, out outX, out outY);
        }
        
        /// <summary>
        /// TryAutoPlace に委譲
        /// </summary>
        public bool TryFindOptimalPlacement(CompleteItemData item, out int outX, out int outY)
        {
            return TryAutoPlace(item, out outX, out outY);
        }
    }
}
