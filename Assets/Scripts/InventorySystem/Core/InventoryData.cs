using System;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// インベントリのセーブデータ構造
    /// </summary>
    [Serializable]
    public class InventoryData
    {
        public int expandedRows;
        public List<SavedItem> items;
        
        public InventoryData()
        {
            expandedRows = InventoryConstants.INITIAL_UNLOCKED_ROWS;
            items = new List<SavedItem>();
        }
    }
    
    /// <summary>
    /// 保存されるアイテム情報
    /// </summary>
    [Serializable]
    public class SavedItem
    {
        public string itemId;
        public int gridX;
        public int gridY;
        public bool isEquipped;
        
        public SavedItem(string id, int x, int y, bool equipped)
        {
            itemId = id;
            gridX = x;
            gridY = y;
            isEquipped = equipped;
        }
    }
}
