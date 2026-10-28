namespace MapSystem.FreeMove
{
    /// <summary>
    /// 層の構成 (2026-09-28)。 正本は docs/GAME.md §4-1。
    ///
    /// <para>このクラスの <c>floor</c> 引数は<b>深さ</b> (旧い層番号 1/3/5/6/7/8)。 プレイヤーに見せる層は 1〜5 (2026-10-20・<see cref="DepthOf"/> / <see cref="OrdinalOf"/>)。
    /// 深さは 2 層と 4 層を飛ばす: 1 → 3 → 5 → 6 → 7 → 8。
    /// 番号を詰めないのは、 敵 (enemies.json の floor)・ボス ID・層タイトル・§13-5 の目標・
    /// 学習ファイルが深さ基準で書かれているから ── 詰めるとすべてが 1 つずつずれる。</para>
    ///
    /// <para>8 層 (Null Point) は<b>内部でボス戦だけの層</b>として残す。 7 層の〈門〉を処理したら
    /// マップを挟まずヴェスカ戦へ入る (GameManager.EnterFloor が 8 層だけ即座にボスを起動する)。</para>
    /// </summary>
    public static class FloorPlan
    {
        public const int FirstFloor = 1;
        public const int LastFloor = 7;

        /// <summary>直線の 1 本道の層 (5 層 = 深さ 7・2026-10-20)。 〈門〉とヴェスカ戦を 1 つの層へ統合した最終層。</summary>
        public const int LinearFloor = 7;

        /// <summary>プレイヤーに見せる層の数 (2026-10-20)。 深さ 1/3/5/6/7 が 1〜5 層 (旧 8 層のヴェスカ戦は 5 層の終点のボスへ統合した)。</summary>
        public const int LastOrdinal = 5;
        private static readonly int[] DepthByOrdinal = { 0, 1, 3, 5, 6, 7 };

        /// <summary>層の番号 (1〜5) → 深さ (1/3/5/6/7)。 範囲外は両端へ丸める。</summary>
        public static int DepthOf(int ordinal)
        {
            return DepthByOrdinal[ordinal < 1 ? 1 : ordinal > LastOrdinal ? LastOrdinal : ordinal];
        }

        /// <summary>深さ (1/3/5/6/7) → 層の番号 (1〜5)。 存在しない深さ (2/4) は直前の層。</summary>
        public static int OrdinalOf(int depth)
        {
            if (depth >= 7) return 5;
            if (depth >= 6) return 4;
            if (depth >= 5) return 3;
            if (depth >= 3) return 2;
            return 1;
        }

        /// <summary>存在する層か (2・4 層は削除済み)。</summary>
        public static bool Exists(int floor)
            => floor >= FirstFloor && floor <= LastFloor && floor != 2 && floor != 4;

        /// <summary>次の層。 最終層なら同じ値を返す。</summary>
        public static int Next(int floor)
        {
            if (floor >= LastFloor) return LastFloor;
            int f = floor + 1;
            while (f < LastFloor && !Exists(f)) f++;
            return f;
        }

        /// <summary>層ボスの居る層 (全層。 最終層の終点はヴェスカ)。</summary>
        public static bool HasBoss(int floor) => Exists(floor);

        /// <summary>航行マップを持つ層 (1/3/5/6/7)。</summary>
        public static bool IsFreeMap(int floor) => Exists(floor);

        /// <summary>追跡者の居る層。 §23-21 の決定は「2 層から」だったが、 2 層の削除に伴い 3 層から。 直線の最終層には居ない。</summary>
        public static bool HasWanderer(int floor) => IsFreeMap(floor) && floor >= 3 && floor != LinearFloor;

        /// <summary>自由移動の層の終点の種別 (すべてボス。 〈門〉は 5 層の途中のイベントになった)。</summary>
        public static TileType GoalType(int floor) => TileType.Boss;
    }
}
