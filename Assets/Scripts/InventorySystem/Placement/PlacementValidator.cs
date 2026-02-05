using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム配置の可否を判定
    /// </summary>
    public class PlacementValidator : MonoBehaviour
    {
        [SerializeField] private GridManager gridManager;
        
        void Start()
        {
            if (gridManager == null)
            {
                gridManager = FindObjectOfType<GridManager>();
            }
        }
        
        /// <summary>
        /// 指定位置にアイテムを配置可能か判定
        /// </summary>
        public bool CanPlaceItem(CompleteItemData item, int gridX, int gridY, out string reason)
        {
            reason = "";
            
            // 範囲チェック
            if (gridX < 0 || gridY < 0)
            {
                reason = "範囲外";
                return false;
            }
            
            if (gridX + item.size.x > InventoryConstants.GRID_WIDTH)
            {
                reason = "右端を超えています";
                return false;
            }
            
            if (gridY + item.size.y > InventoryConstants.GRID_HEIGHT)
            {
                reason = "下端を超えています";
                return false;
            }
            
            // ロック状態チェック
            for (int y = gridY; y < gridY + item.size.y; y++)
            {
                for (int x = gridX; x < gridX + item.size.x; x++)
                {
                    GridCell cell = gridManager.GetCell(x, y);
                    if (cell != null && cell.IsLocked)
                    {
                        reason = "ロック中のマス";
                        return false;
                    }
                }
            }
            
            // GridManagerの占有チェックを使用
            if (gridManager != null)
            {
                if (!gridManager.CanPlaceItem(gridX, gridY, item.size.x, item.size.y))
                {
                    reason = "配置不可エリア";
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// 移動時の配置可否判定（元の位置を考慮）
        /// </summary>
        public bool CanMoveItem(CompleteItemData item, int fromX, int fromY, int toX, int toY, out string reason)
        {
            reason = "";
            
            // 同じ位置なら移動不要
            if (fromX == toX && fromY == toY)
            {
                reason = "同じ位置";
                return false;
            }
            
            // 基本的な範囲チェック
            if (toX < 0 || toY < 0)
            {
                reason = "範囲外";
                return false;
            }
            
            if (toX + item.sizeX > InventoryConstants.GRID_WIDTH)
            {
                reason = "右端を超えています";
                return false;
            }
            
            if (toY + item.sizeY > InventoryConstants.GRID_HEIGHT)
            {
                reason = "下端を超えています";
                return false;
            }
            
            // 移動先のセルチェック（元の位置は除外）
            for (int y = toY; y < toY + item.sizeY; y++)
            {
                for (int x = toX; x < toX + item.sizeX; x++)
                {
                    // 元の占有範囲内なら無視
                    if (x >= fromX && x < fromX + item.sizeX && 
                        y >= fromY && y < fromY + item.sizeY)
                    {
                        continue;
                    }
                    
                    GridCell cell = gridManager.GetCell(x, y);
                    if (cell == null)
                    {
                        reason = $"セルが存在しません ({x}, {y})";
                        return false;
                    }
                    
                    if (cell.IsLocked)
                    {
                        reason = "ロック中のマス";
                        return false;
                    }
                    
                    if (cell.IsOccupied)
                    {
                        reason = "他のアイテムと重複";
                        return false;
                    }
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// 配置不可の理由となるセルを取得
        /// </summary>
        public void GetInvalidCells(CompleteItemData item, int gridX, int gridY, out bool[] invalidCells)
        {
            int cellCount = item.size.x * item.size.y;
            invalidCells = new bool[cellCount];
            
            int index = 0;
            for (int y = gridY; y < gridY + item.size.y; y++)
            {
                for (int x = gridX; x < gridX + item.size.x; x++)
                {
                    bool isInvalid = false;
                    
                    // 範囲外
                    if (x < 0 || x >= InventoryConstants.GRID_WIDTH || 
                        y < 0 || y >= InventoryConstants.GRID_HEIGHT)
                    {
                        isInvalid = true;
                    }
                    else
                    {
                        // ロック状態
                        GridCell cell = gridManager.GetCell(x, y);
                        if (cell != null && cell.IsLocked)
                        {
                            isInvalid = true;
                        }
                    }
                    
                    invalidCells[index] = isInvalid;
                    index++;
                }
            }
        }
    }
}
