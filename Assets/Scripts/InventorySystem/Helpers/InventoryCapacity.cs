using GameLoop;

namespace InventorySystem.Helpers
{
    /// <summary>
    /// BOT 用のインベントリ容量チェッカ。
    /// 形状/配置は無視し、 セル合計のみで判定する (B案)。
    /// セル数規約: Weapon=9 (3×3), Passive=4 (2×2), Consumable=1 (1×1), Dice=0, Material=0
    /// 容量 = run.inventoryUnlockedRows × InventoryConstants.GRID_WIDTH (5)
    /// </summary>
    public static class InventoryCapacity
    {
        public const int CELLS_WEAPON     = 9;
        public const int CELLS_PASSIVE    = 4;
        public const int CELLS_CONSUMABLE = 1;

        /// <summary>itemId 1個分のセル数を返す (0 = ダイス/素材/不明)。</summary>
        public static int CellsOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            var db = ItemDatabase.Instance;
            if (db == null) return CellsOfByIdHeuristic(itemId);
            var def = db.GetItem(itemId);
            if (def == null) return CellsOfByIdHeuristic(itemId);

            // ItemCategory ベースの判定 (PassiveItem も Passive と同じ扱い)
            // 武器は装備スロット扱い(ダイスと同じ)＝グリッド枠を消費しない (0)。
            //  ・武器は1本しか持たない(進化はID差し替え。予備を抱えない)
            //  ・同じ装備スロットのダイスが既に0セル → 整合のため武器も0
            if (def.category == ItemCategory.Weapon) return 0;
            if (def.category == ItemCategory.Consumable) return CELLS_CONSUMABLE;
            if (def.category == ItemCategory.Passive || def.category == ItemCategory.PassiveItem) return CELLS_PASSIVE;
            // Dice / Material / その他は容量0
            return 0;
        }

        /// <summary>DB未参照時のフォールバック: ID prefix から推定。</summary>
        private static int CellsOfByIdHeuristic(string itemId)
        {
            if (itemId.StartsWith("cons_") || itemId.StartsWith("uniq_")) return CELLS_CONSUMABLE;
            if (itemId.StartsWith("dice_")) return 0;
            // 武器の prefix 列挙: 装備スロット扱い(ダイスと同じ) → 0セル
            if (itemId.StartsWith("sword_") || itemId.StartsWith("axe_") || itemId.StartsWith("dagger_")
                || itemId.StartsWith("shield_") || itemId.StartsWith("curse_") || itemId.StartsWith("invest_")
                || itemId.StartsWith("ryusen_") || itemId.StartsWith("deadend_") || itemId.StartsWith("holy_"))
                return 0;
            // それ以外は Passive 扱い
            return CELLS_PASSIVE;
        }

        /// <summary>現在使用中のセル合計 (パッシブ + 消費)。
        /// 武器/ダイスは装備スロット扱いで 0 セル (CellsOf が 0 を返す)＝グリッド枠を消費しない。
        /// よって equippedWeaponId を別途加算する必要はなく、 ownedPassiveItems 内の武器ID残骸も
        /// 自動的に 0 計上になる (二重計上の心配なし)。</summary>
        public static int UsedCells(RunState run)
        {
            if (run == null) return 0;
            int sum = 0;
            if (run.ownedPassiveItems != null)
                foreach (var id in run.ownedPassiveItems) sum += CellsOf(id); // 武器/ダイス=0, パッシブ=4
            if (run.ownedConsumables != null)
                foreach (var id in run.ownedConsumables) sum += CellsOf(id); // 消費=1
            return sum;
        }

        /// <summary>現在の容量 (= unlocked rows × 5)。</summary>
        public static int Capacity(RunState run)
        {
            int rows = run != null ? run.inventoryUnlockedRows : InventoryConstants.INITIAL_UNLOCKED_ROWS;
            return rows * InventoryConstants.GRID_WIDTH;
        }

        /// <summary>余りセル数。</summary>
        public static int FreeCells(RunState run) => System.Math.Max(0, Capacity(run) - UsedCells(run));

        /// <summary>itemId を追加可能か (セル合計 ≤ 容量)。</summary>
        public static bool CanAdd(RunState run, string itemId)
        {
            if (run == null) return false;
            int need = CellsOf(itemId);
            if (need <= 0) return true; // ダイス/素材は容量を消費しない → 常に可
            return UsedCells(run) + need <= Capacity(run);
        }

        /// <summary>次に1列拡張するためのコスト。 既に最大なら int.MaxValue。</summary>
        public static int NextExpansionCost(RunState run)
        {
            if (run == null) return int.MaxValue;
            int currentRows = run.inventoryUnlockedRows;
            int idx = currentRows - InventoryConstants.INITIAL_UNLOCKED_ROWS;
            if (currentRows >= InventoryConstants.MAX_UNLOCKED_ROWS) return int.MaxValue;
            if (idx < 0 || idx >= InventoryConstants.ExpansionCost.Length) return int.MaxValue;
            return InventoryConstants.ExpansionCost[idx];
        }

        public static bool CanExpand(RunState run) => NextExpansionCost(run) != int.MaxValue;
    }
}
