namespace UI.Game2D
{
    /// <summary>
    /// 戦闘の画面の入口 [持ち物] と、 敵の詳細の窓を開く範囲 (480×270 の格子の dot・左上が原点)。
    /// <para>2026-11-16 に旧い戦闘の HUD (液晶の外のパネル群: 予告の枠・ステータス・充電と激化の板・出目と差し込み口・操作・手札・役の板・秘宝の列) を消し、
    /// 戦闘の画面は液晶の中 (<see cref="Game2DBattleLcd"/>・寸法の正本は <see cref="BattleLcdLayout"/>) になった。 ここに残すのは、
    /// 検算 Tools/inventory_check が数える入口と、 その周りの部品の範囲だけ (値は BattleLcdLayout から作る・写しを持たない)。</para>
    /// <b>UnityEngine に依存しない</b> ── 検算がこのファイルと BattleLcdLayout.cs をそのままコンパイルして、 入口がほかの部品と重ならず、 液晶の安全に見える範囲の中にあることを数える。
    /// </summary>
    public static class BattleHudLayout
    {
        /// <summary>入口 [持ち物] の押せる範囲 (液晶の中の左上の字・<see cref="BattleLcdLayout.InvX"/>)。</summary>
        public static readonly InventoryLayout.Box Inv = new InventoryLayout.Box(BattleLcdLayout.InvX, BattleLcdLayout.InvY - 1, BattleLcdLayout.RelicX - BattleLcdLayout.InvX - 2, 14);
        /// <summary>敵の絵の範囲 (敵の詳細の窓を開く所・Game2DEnemyPanel)。</summary>
        public static readonly InventoryLayout.Box Enemy = new InventoryLayout.Box(190, 84, 100, 120);
        /// <summary>液晶の安全に見える範囲 (<see cref="BattleLcdLayout.SafeX0"/> ほか)。 入口はこの中に置く。</summary>
        public static readonly InventoryLayout.Box Safe = new InventoryLayout.Box(BattleLcdLayout.SafeX0, BattleLcdLayout.SafeY0,
            BattleLcdLayout.SafeX1 - BattleLcdLayout.SafeX0 + 1, BattleLcdLayout.SafeY1 - BattleLcdLayout.SafeY0 + 1);

        /// <summary>入口のキーの名前 (画面の字・ボタンには出さない)。</summary>
        public const string InvKeyName = "I";
        /// <summary>入口の字。</summary>
        public const string InvLabel = "持ち物";

        /// <summary>入口と重なってはいけない部品: 秘宝の列・山と捨て札の数 (右上・幅は多めに 80)・敵・手札の行・床の出目 (9 個)。</summary>
        public static InventoryLayout.Box[] Others => new[]
        {
            new InventoryLayout.Box(BattleLcdLayout.RelicX - 1, BattleLcdLayout.RelicY - 1, BattleLcdLayout.RelicRight - BattleLcdLayout.RelicX + 2, 18),
            new InventoryLayout.Box(BattleLcdLayout.DeckRight - 80, BattleLcdLayout.DeckY, 80, 13),
            Enemy,
            new InventoryLayout.Box(BattleLcdLayout.HandX0, BattleLcdLayout.HandY - BattleLcdLayout.Lift, BattleLcdLayout.HandX1 - BattleLcdLayout.HandX0, BattleLcdLayout.CardH + BattleLcdLayout.Lift + BattleLcdLayout.Arc),
            new InventoryLayout.Box(BattleLcdLayout.FloorX, BattleLcdLayout.FloorY - 2 * BattleLcdLayout.FloorRowStep, 3 * BattleLcdLayout.FloorStepX + BattleLcdLayout.FloorStepX / 2, 2 * BattleLcdLayout.FloorRowStep + 18 + BattleLcdLayout.FloorZig),
        };
    }
}
