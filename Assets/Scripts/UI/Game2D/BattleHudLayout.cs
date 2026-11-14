namespace UI.Game2D
{
    /// <summary>
    /// 戦闘の画面 (<see cref="Game2DBattleHud"/>) の部品の場所 (480×270 の格子の dot・左上が原点・入りきった時の位置)。 2026-11-13。
    /// <para>2026-11-15 (作者の決定「敵 HP は上のバーじゃなくて敵の下あたりに表示できるし、 激化も充電も左下に移植できると思う」・
    /// 「敵 HP はゲーム内 GUI にしよう」): <b>上の帯を無くした</b> (敵の頭上 = 天与の剣の日食・審判官の日輪などの絵を隠していた)。
    /// 敵の HP は液晶の中の敵の足もとの下 (<see cref="EnemyHpDx"/> ほか・Game2DBattleFx が描く)、 充電と激化は左下の <see cref="Gauge"/> へ。
    /// 左の列を組み替えた: 出目 (Ports) を入口の下へ上げ、 ステータス → 充電と激化 → 操作 (位置と大きさは変えない) の順。 役の板は右上 (予告の枠の上) へ。</para>
    /// <b>UnityEngine に依存しない</b> ── 検算 Tools/inventory_check がこのファイルをそのままコンパイルして、 持ち物の入口 (<see cref="Inv"/>) が
    /// ほかの部品と重ならず 480×270 の中にあることを数える。 Game2DBattleHud はここの値から RectInt を作る (写しを持たない)。
    /// </summary>
    public static class BattleHudLayout
    {
        /// <summary>予告の枠 (右)。</summary>
        public static readonly InventoryLayout.Box Right = new InventoryLayout.Box(370, 60, 108, 120);
        /// <summary>自分のステータス (会心・攻撃・防御/貫通)。 2026-11-15: (2,162,108,48) → 出目の下へ上げた。</summary>
        public static readonly InventoryLayout.Box Stat = new InventoryLayout.Box(2, 122, 108, 46);
        /// <summary>充電 (10 個のランプ) と激化 (「あと N」と 5 個のランプ)。 2026-11-15 に上の帯から移した。 左の列 (108) より広く、 液晶の左下の端に 34 dot かかる
        /// (ランプ 10 個 = 100 dot と「充電」の字が 1 行に入る幅)。</summary>
        public static readonly InventoryLayout.Box Gauge = new InventoryLayout.Box(2, 170, 142, 40);
        /// <summary>成立した役 (役があるターンだけ)。 2026-11-15: 左上 (2,20) → 右上 (予告の枠の上・上の帯が無くなって空いた所)。</summary>
        public static readonly InventoryLayout.Box Role = new InventoryLayout.Box(386, 20, 92, 37);
        /// <summary>出目と差し込み口 (出目 3 個の時の幅。 4 個目からは列を右へ足す・<see cref="PortsWidth"/>)。 2026-11-15: y 60 → 20 (入口の下)。</summary>
        public static readonly InventoryLayout.Box Ports = new InventoryLayout.Box(2, 20, 108, 98);
        /// <summary>振り直す・おまかせ / 道具・逃げる・決める。</summary>
        public static readonly InventoryLayout.Box Ctl = new InventoryLayout.Box(2, 214, 108, 52);
        /// <summary>手札の端子。</summary>
        public static readonly InventoryLayout.Box Hand = new InventoryLayout.Box(318, 184, 160, 80);
        /// <summary>敵の絵の範囲 (敵の詳細の窓を開く所・Game2DEnemyPanel.EnemyArea と同じ値)。</summary>
        public static readonly InventoryLayout.Box Enemy = new InventoryLayout.Box(190, 84, 100, 120);

        /// <summary>持ち物の画面の入口 [持ち物] (2026-11-13)。 左上の角 (出目の上)。 キーは <see cref="InvKeyName"/>。</summary>
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
            Right, Stat, Gauge, Role, new InventoryLayout.Box(Ports.x, Ports.y, PortsWidth(9), Ports.h), Ctl, Hand, Enemy,
        };

        // ── 敵の HP (液晶の中・Game2DBattleFx が描く・2026-11-15) ──
        //   敵の足もと (BattleMotion.FloorY = 196) のすぐ下に、 棒 (枠 1 dot + 中身) と、 その下の中央に「今/最大」の数字。
        //   足もとの下で液晶が見えている所は x 170〜290 の y 〜218 (その下は資源バーの物資・シールドの板 y 219〜・x 290〜 は丸い窓の台 y 205〜) なので、 その中に収める。
        /// <summary>棒の位置 (敵の体の中心 x と足もとの y からのずれ) と大きさ (dot)。 中身は (w − 2)×(h − 2)。 y 199〜204。</summary>
        public const int EnemyHpDx = -50, EnemyHpDy = 3, EnemyHpW = 100, EnemyHpH = 6;
        /// <summary>数字の上端 (棒の上端からのずれ・棒の中央にそろえる)。 y 206〜217 (影は 218 まで)。</summary>
        public const int EnemyHpTextDy = EnemyHpH + 1;
    }
}
