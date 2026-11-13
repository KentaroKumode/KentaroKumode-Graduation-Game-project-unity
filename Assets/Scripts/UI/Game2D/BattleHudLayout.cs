namespace UI.Game2D
{
    /// <summary>
    /// 戦闘の画面 (<see cref="Game2DBattleHud"/>) の部品の場所 (480×270 の格子の dot・左上が原点・入りきった時の位置)。 2026-11-13。
    /// <b>UnityEngine に依存しない</b> ── 検算 Tools/inventory_check がこのファイルをそのままコンパイルして、 持ち物の入口 (<see cref="Inv"/>) が
    /// ほかの部品と重ならず 480×270 の中にあることを数える。 Game2DBattleHud はここの値から RectInt を作る (写しを持たない)。
    /// </summary>
    public static class BattleHudLayout
    {
        /// <summary>予告の枠 (右)。</summary>
        public static readonly InventoryLayout.Box Right = new InventoryLayout.Box(370, 60, 108, 120);
        /// <summary>上の帯。</summary>
        public static readonly InventoryLayout.Box Top = new InventoryLayout.Box(96, 2, 288, 56);
        /// <summary>自分のステータス (会心・攻撃・防御/貫通)。</summary>
        public static readonly InventoryLayout.Box Stat = new InventoryLayout.Box(2, 162, 108, 48);
        /// <summary>成立した役 (役があるターンだけ)。</summary>
        public static readonly InventoryLayout.Box Role = new InventoryLayout.Box(2, 20, 92, 37);
        /// <summary>出目と差し込み口 (出目 3 個の時の幅。 4 個目からは列を右へ足す・<see cref="PortsWidth"/>)。</summary>
        public static readonly InventoryLayout.Box Ports = new InventoryLayout.Box(2, 60, 108, 98);
        /// <summary>振り直す・おまかせ / 道具・逃げる・決める。</summary>
        public static readonly InventoryLayout.Box Ctl = new InventoryLayout.Box(2, 214, 108, 52);
        /// <summary>手札の端子。</summary>
        public static readonly InventoryLayout.Box Hand = new InventoryLayout.Box(318, 184, 160, 80);
        /// <summary>敵の絵の範囲 (敵の詳細の窓を開く所・Game2DEnemyPanel.EnemyArea と同じ値)。</summary>
        public static readonly InventoryLayout.Box Enemy = new InventoryLayout.Box(190, 84, 100, 120);

        /// <summary>持ち物の画面の入口 [持ち物] (2026-11-13)。 役の板 (<see cref="Role"/>) の上の空き・上の帯 (<see cref="Top"/>) の左。 キーは <see cref="InvKeyName"/>。</summary>
        public static readonly InventoryLayout.Box Inv = new InventoryLayout.Box(2, 2, 56, 17);
        /// <summary>入口のキーの名前 (画面の字・ボタンには出さない)。</summary>
        public const string InvKeyName = "I";
        /// <summary>入口の字。</summary>
        public const string InvLabel = "持ち物";

        /// <summary>差し込み口の列の幅と、 1 列の出目の数 (Game2DBattleHud と同じ)。</summary>
        public const int PortRows = 3, PortColW = 76;
        /// <summary>出目 n 個の時の Ports の幅。</summary>
        public static int PortsWidth(int n) => Ports.w + (System.Math.Max(1, (n + PortRows - 1) / PortRows) - 1) * PortColW;

        /// <summary>入口と重なってはいけない部品 (出目は 9 個の時の幅で見る)。</summary>
        public static InventoryLayout.Box[] Others => new[]
        {
            Right, Top, Stat, Role, new InventoryLayout.Box(Ports.x, Ports.y, PortsWidth(9), Ports.h), Ctl, Hand, Enemy,
        };
    }
}
