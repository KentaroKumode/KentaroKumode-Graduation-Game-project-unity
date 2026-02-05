using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム配置のインジケーター表示
    /// </summary>
    public class PlacementIndicator : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private PlacementValidator validator;
        
        [Header("色設定")]
        [SerializeField] private Color validColor = new Color(0.3f, 0.8f, 0.3f, 0.6f);
        [SerializeField] private Color invalidColor = new Color(0.8f, 0.2f, 0.2f, 0.6f);
        
        private bool isShowing = false;
        
        void Start()
        {
            if (gridManager == null)
                gridManager = FindObjectOfType<GridManager>();
            
            if (validator == null)
                validator = FindObjectOfType<PlacementValidator>();
        }
        
        /// <summary>
        /// インジケーターを表示
        /// </summary>
        public void ShowIndicator(CompleteItemData item, int gridX, int gridY)
        {
            if (gridManager == null || validator == null) return;
            
            // 前回のハイライトをクリア
            gridManager.ClearAllHighlights();
            
            // 配置可能かチェック
            bool canPlace = validator.CanPlaceItem(item, gridX, gridY, out string reason);
            
            if (canPlace)
            {
                // 全体を緑でハイライト
                gridManager.HighlightCells(gridX, gridY, item.size.x, item.size.y, true);
            }
            else
            {
                // 配置不可の部分を赤でハイライト
                validator.GetInvalidCells(item, gridX, gridY, out bool[] invalidCells);
                
                int index = 0;
                for (int y = gridY; y < gridY + item.size.y; y++)
                {
                    for (int x = gridX; x < gridX + item.size.x; x++)
                    {
                        GridCell cell = gridManager.GetCell(x, y);
                        if (cell != null)
                        {
                            if (invalidCells[index])
                            {
                                cell.HighlightInvalid();
                            }
                            else
                            {
                                cell.HighlightValid();
                            }
                        }
                        index++;
                    }
                }
            }
            
            isShowing = true;
        }
        
        /// <summary>
        /// インジケーターを非表示
        /// </summary>
        public void HideIndicator()
        {
            if (!isShowing) return;
            
            if (gridManager != null)
            {
                gridManager.ClearAllHighlights();
            }
            
            isShowing = false;
        }
    }
}
