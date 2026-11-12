namespace UI.Game2D
{
    /// <summary>
    /// 日本語の窓 (<see cref="Game2DModal"/> の jp の窓) の寸法の式 (2026-11-13)。 窓の高さ (AutoHeightJp) と、 描く時に行が何行入るか (RedrawJp) を
    /// <b>この 1 か所</b>で決める ── 検算 (Tools/event_page_check) もこのファイルをそのままコンパイルして同じ式で数える。
    /// <b>UnityEngine に依存しない</b>。 単位は 480×270 の格子の dot (Game2DLayout)。 字の行送りは呼ぶ側が渡す (<see cref="Game2DJpFont.LineH"/>)。
    ///
    /// <para>窓の中は上から: 題 (20) と線 (4) → 本文 (行 × 行送り + 4) → 行 (行の高さ + 間) → 詳細 (選んだ行の説明 2 行 + 6) →
    /// 足元 (4 + 1 行 + 4) → ボタン (22) → 下の余白。 窓の高さは <see cref="MaxH"/> まで (格子の高さ 270 から上下 2 dot)。</para>
    /// </summary>
    public static class ModalJpMetrics
    {
        public const int Inset = 16, TitleH = 20, RuleH = 4, JRowH = 20, JGap = 2, JDetail = 2, BtnH = 22;
        /// <summary>窓の高さの上限 (格子の高さ 270・上下 2 dot)。 これを超える中身は、 行が描かれずに落ちる。</summary>
        public const int MaxH = 266;

        /// <summary>本文を折り返す幅 (窓の幅 w)。</summary>
        public static int BodyW(int w) => w - 2 * Inset - 4;
        public static int RowHOf(int rowH) => rowH > 0 ? rowH : JRowH;
        /// <summary>行の間: 既定の高さより低い行は 1 dot、 それ以外は 2 dot。</summary>
        public static int GapOf(int rowH) => rowH > 0 && rowH < JRowH ? 1 : JGap;
        public static int BodyH(bool hasBody, int lines, int lineH) => hasBody ? lines * lineH + 4 : 0;
        public static int DetailH(bool detail, int lineH) => detail ? JDetail * lineH + 6 : 0;

        /// <summary>中身がちょうど収まる窓の高さ (上限で切らない)。 rows = 行の段数 (列で割った後)。</summary>
        public static int HeightUncapped(int lineH, bool hasBody, int bodyLines, int rows, int rowH, bool detail)
            => Inset + TitleH + RuleH + BodyH(hasBody, bodyLines, lineH) + rows * (RowHOf(rowH) + GapOf(rowH)) + DetailH(detail, lineH)
               + 4 + lineH + 4 + BtnH + Inset;

        /// <summary>窓の高さ h の時に描ける行の段数 (RedrawJp と同じ式: 本文の下から、 詳細の 2 dot 上まで)。</summary>
        public static int RowsThatFit(int h, int lineH, bool hasBody, int bodyLines, int rowH, bool detail)
        {
            int y = Inset - 4 + TitleH + RuleH + BodyH(hasBody, bodyLines, lineH);
            int btnY = h - Inset - BtnH, footY = btnY - 4 - lineH, rowsBottom = footY - DetailH(detail, lineH) - 2;
            int rh = RowHOf(rowH), g = GapOf(rowH);
            int n = (rowsBottom - y + g) / (rh + g);
            return n < 1 ? 1 : n;
        }
    }
}
