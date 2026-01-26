namespace InventorySystem
{
    /// <summary>
    /// インベントリシステムの定数定義
    /// </summary>
    public static class InventoryConstants
    {
        // グリッドサイズ
        public const int GRID_WIDTH = 5;
        public const int GRID_HEIGHT = 8;
        public const int INITIAL_UNLOCKED_ROWS = GRID_HEIGHT; // 全グリッド開放
        
        // アイテムサイズ制限
        public const int MAX_ITEM_SIZE = 4;
        
        // UI設定
        public const float DOUBLE_CLICK_TIME = 0.5f;
        public const float TOOLTIP_DELAY = 0.5f;
        
        // アニメーション時間
        public const float UNLOCK_SHAKE_DURATION = 1.0f;
        public const float UNLOCK_FLASH_DURATION = 0.3f;
    }
}
