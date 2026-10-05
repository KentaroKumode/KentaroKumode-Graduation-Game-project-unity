namespace MapSystem.FreeMove
{
    /// <summary>
    /// 層の構成 (2026-09-28)。 正本は docs/GAME.md §4-1。
    ///
    /// <para><b>内部の層番号は深さのまま</b>で、 2 層と 4 層を飛ばす: 1 → 3 → 5 →(Λ)→ 6 → 7 → 8。
    /// 番号を詰めないのは、 敵 (enemies.json の floor)・ボス ID・層タイトル・§13-5 の目標・
    /// 学習ファイルが深さ基準で書かれているから ── 詰めるとすべてが 1 つずつずれる。</para>
    ///
    /// <para>8 層 (Null Point) は<b>内部でボス戦だけの層</b>として残す。 7 層の〈門〉を処理したら
    /// マップを挟まずヴェスカ戦へ入る (GameManager.EnterFloor が 8 層だけ即座にボスを起動する)。</para>
    /// </summary>
    public static class FloorPlan
    {
        public const int FirstFloor = 1;
        public const int LastFloor = 8;

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

        /// <summary>層ボスの居る層。 7 層の終点は〈門〉でボスは居ない。</summary>
        public static bool HasBoss(int floor) => Exists(floor) && floor != 7;

        /// <summary>自由移動のマップを持つ層 (1/3/5/6/7)。 8 層はボス戦だけ・Λ は環状線のまま。</summary>
        public static bool IsFreeMap(int floor) => Exists(floor) && floor != 8;

        /// <summary>徘徊エネミーの居る層。 §23-21 の決定は「2 層から」だったが、 2 層の削除に伴い 3 層から。</summary>
        public static bool HasWanderer(int floor) => IsFreeMap(floor) && floor >= 3;

        /// <summary>徘徊エネミーの連戦の数。 3 層 2 戦・5 層以降 3 戦。</summary>
        public static int GauntletFights(int floor) => floor >= 5 ? 3 : 2;

        /// <summary>連戦の中身を引く層 ＝ 次の層 (7 層は 7 層自身)。 エリートの抽選に渡す。</summary>
        public static int GauntletEnemyFloor(int floor) => floor >= 7 ? 7 : Next(floor);

        /// <summary>自由移動の層の終点の種別。 ボス層はボス、 7 層は〈門〉。</summary>
        public static TileType GoalType(int floor) => HasBoss(floor) ? TileType.Boss : TileType.Gate;
    }
}
