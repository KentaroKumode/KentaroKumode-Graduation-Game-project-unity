namespace UI.Game2D
{
    /// <summary>
    /// 液晶の安全域 (2026-11-16・dot・480×270 の格子)。 字・板・押す物は、 この内側に収める。 背景の絵は画面いっぱいでよい。
    /// <para>液晶の湾曲の外形を絵に合わせる形 (Game2D/LcdScreen の <c>_CurveFit</c> = 1・2026-11-16 作者の決定・既定) では<b>縁の欠けは無い</b> (絵は 1 dot も画面の外へ出ない)。
    /// この値は四隅の楔 (画面の背景色・横 13 × 縦 7 dot ほど) と見た目の余白のため。 決めたのは旧い合わせ方 (f = 0・縁の中央で左右 14.7・上下 8.3 dot が外へ出た) の頃で、
    /// 結末の画面の配置は作者がこの値で承認しているので変えない。</para> <b>UnityEngine に依存しない</b> (検算 Tools/event_page_check がそのままコンパイルする)。
    /// 値は結末の画面 (<see cref="Game2DEndingScreen"/>) が実機で決めた余白 (左右 16・上下 9)。 イベントの画面 (<see cref="EventLayout"/>) も同じ値を使う。
    /// </summary>
    public static class LcdSafeArea
    {
        /// <summary>左端・右端・上端・下端 (この範囲の内側に収める)。</summary>
        public const int L = 16, R = 464, T = 9, B = 259;
    }
}
