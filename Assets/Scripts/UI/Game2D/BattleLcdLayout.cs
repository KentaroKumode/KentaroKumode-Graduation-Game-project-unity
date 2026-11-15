namespace UI.Game2D
{
    /// <summary>
    /// 液晶の中に描く戦闘の画面 (<see cref="Game2DBattleLcd"/>・2026-11-16 の大規模改修 第 1 段) の場所と大きさ (480×270 の dot・左上が原点)。
    /// 見本は Captures/ui_layout/scene/still.png (描いた道具 = wt/hb の Tools/pixelfx/lab/ui_scene_mock.py の <c>cpos</c>・<c>FLOOR</c>・<c>EB</c>・<c>MB</c>・<c>ORB</c>・<c>card()</c>)。
    /// 見本は敵の足もとが y 188 だが、 ゲームの敵 (BattleMotion.FloorY = 204) に合わせて、 床の出目と棒を下げ、 札の行を 2 dot 下げた。
    /// <para><b>カードの寸法はここの 1 か所だけ</b> (枠の絵は未決 ── 候補は Captures/ui_layout/card_frames/v2/・64×58 に変わる可能性がある)。
    /// 枠の絵を差し替える時は <see cref="CardW"/>〜<see cref="SockY"/> を変え、 描き方は <see cref="Game2DBattleLcd"/> の DrawCardFrame だけを差し替える。</para>
    /// <b>UnityEngine に依存しない</b> (数だけ)。
    /// </summary>
    public static class BattleLcdLayout
    {
        // ── カード (仮の枠: 見本と同じ単純な枠をコードで描く) ──
        /// <summary>カードの大きさ。</summary>
        public const int CardW = 64, CardH = 52;
        /// <summary>名前の帯の高さ (上から)。</summary>
        public const int BandH = 14;
        /// <summary>絵の窓 (カードの左上から): 位置と大きさ。</summary>
        public const int ArtX = 1, ArtY = 14, ArtW = 62, ArtH = 36;
        /// <summary>出目の差し口 (カードの左上から・左下に置く)。 中に 18×18 の出目 (<see cref="Game2DDiceRoll.Face"/>) が入る。 2 個挿せる札は右へ <see cref="SockStep"/> ずらして 2 つ目を置く (仮)。</summary>
        public const int SockX = 1, SockY = 31, SockSize = 20, SockStep = 21;
        /// <summary>挿したカードを持ち上げる量 (dot)。</summary>
        public const int Lift = 5;
        /// <summary>手札の並び: 左端・右端 (この間に並べる)・基準の送り・行の上端 (弧の分を足す)。</summary>
        public const int HandX0 = 96, HandX1 = 426, HandStep = 66, HandY = 216;
        /// <summary>弧 (端ほど下げる) の最大。 5 枚の時に見本の [2, 1, 0, 1, 2]。</summary>
        public const int Arc = 2;
        /// <summary>解決の間に手札を引っ込める距離 (下へ)。</summary>
        public const int HandHide = 64;
        /// <summary>札の上の印 (「残す」「停止」など) の字の上端 (カードの上端からのずれ)。</summary>
        public const int LabelDy = -13;

        // ── 床の出目 ──
        /// <summary>1 個目の出目の左上と送り。 3 個で 1 段・4 個目からは上の段へ (仮)。</summary>
        public const int FloorX = 106, FloorY = 162, FloorStepX = 24, FloorZig = 6, FloorRowStep = 26, FloorPerRow = 3;

        // ── 敵の棒 (足もと) ──
        /// <summary>棒 (枠 1 dot + 中身 5 dot) の左端と上端 (敵の体の中心・足もとからのずれ) と幅。</summary>
        public const int EBarDx = -50, EBarDy = 0, EBarW = 102, EBarH = 7;
        /// <summary>数字の左端 (棒の右端から) と上端 (棒の上端から)。</summary>
        public const int ENumGap = 5, ENumDy = -4;

        // ── 左下: 自分 ──
        /// <summary>自分の HP の棒 (左端・右端・中身の上端)。</summary>
        public const int MBarX0 = 8, MBarX1 = 90, MBarY = 257;
        /// <summary>HP の数字の行の上端。</summary>
        public const int MNumY = 242;
        /// <summary>充電の玉の中心と半径・横の「振り直す」の字の左端。</summary>
        public const int OrbX = 22, OrbY = 228, OrbR = 10, RerollTextX = 36;
        /// <summary>シールドの行・与ダメの補正の行 (どちらも出す物がある時だけ)。</summary>
        public const int ShieldY = 205, ModY = 192;

        // ── 右 ──
        /// <summary>「決める」の字の中心 x と上端・下線の y と幅。</summary>
        public const int GoCx = 452, GoY = 238, GoLineY = 254, GoLineW = 40;
        /// <summary>右の列の小さな操作 (おまかせ・道具・逃げる・仮置き) の右端と 1 行目の上端と送り。</summary>
        public const int SideRight = 476, SideY = 176, SideStep = 15;
        /// <summary>山と捨て札の枚数 (右上・右端と上端)。</summary>
        public const int DeckRight = 476, DeckY = 3;

        // ── 左上 ──
        /// <summary>[持ち物] の字の左上 (仮置き) と、 秘宝の列の左端・上端・送り・右の止め。</summary>
        public const int InvX = 4, InvY = 3, RelicX = 44, RelicY = 2, RelicPitch = 18, RelicRight = 396;

        // ── 敵の頭上 ──
        /// <summary>予告の行 (Game2DBattleFx.DrawMarks と同じ行・足もとから) と、 その上の前触れの行。</summary>
        public const int HeadIntentDy = -138, HeadNextDy = -152;
        /// <summary>激化の段の印 (小さな四角) の大きさ・送り・最大の段。</summary>
        public const int PipSize = 4, PipStep = 6, PipMax = 5;

        // ── 説明の帯 ──
        public const int TipMaxW = 260, TipLinesMax = 7;

        /// <summary>カード i (m 枚中) の左上 x と、 弧の分の下げ。 5 枚の時に見本の cpos (96 + 66i) と同じ。 枚数が多い時は送りを詰める (重ねる・仮)。</summary>
        public static void CardPos(int i, int m, out int x, out int arcDy)
        {
            if (m <= 0) { x = HandX0; arcDy = 0; return; }
            int avail = HandX1 - HandX0;
            int step = m > 1 ? System.Math.Min(HandStep, (avail - CardW) / (m - 1)) : HandStep;
            int total = CardW + (m - 1) * step, x0 = HandX0 + (avail - total) / 2;
            x = x0 + i * step;
            float half = (m - 1) / 2f;
            arcDy = half <= 0f ? 0 : (int)System.Math.Round(Arc * System.Math.Abs(i - half) / half);
        }

        /// <summary>床の出目 i (n 個中) の左上。</summary>
        public static void FloorPos(int i, out int x, out int y)
        {
            int row = i / FloorPerRow, col = i % FloorPerRow;
            x = FloorX + col * FloorStepX + (row % 2) * (FloorStepX / 2);
            y = FloorY - row * FloorRowStep + ((col & 1) == 1 ? FloorZig : 0);
        }
    }
}
