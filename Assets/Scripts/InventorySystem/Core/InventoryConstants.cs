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
        public const int INITIAL_UNLOCKED_ROWS = 4; // 初期4列(20マス)、 ショップで拡張可

        // インベントリ拡張コスト (1列ずつ追加)
        // 4列→5列=5G, 5→6=8G, 6→7=12G, 7→8=17G
        public static readonly int[] ExpansionCost = { 5, 8, 12, 17 };
        public const int MAX_UNLOCKED_ROWS = 8;

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
