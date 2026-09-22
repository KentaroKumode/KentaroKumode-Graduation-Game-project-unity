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
        public const int INITIAL_UNLOCKED_ROWS = 4; // 初期4列(20マス)。 UI グリッドの初期表示行数

        // [削除 2026-09-11] ExpansionCost / MAX_UNLOCKED_ROWS ── ショップのインベントリ拡張。
        //   容量制限が 2026-07-29 に撤廃され、 拡張枠も 2026-09-03 に陳列から撤去されたため、
        //   参照元が無くなった。 UI グリッドの行数上限は GRID_HEIGHT が持つ。

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
