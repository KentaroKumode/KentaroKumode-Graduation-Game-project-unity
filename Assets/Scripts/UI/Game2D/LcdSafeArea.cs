namespace UI.Game2D
{
    /// <summary>
    /// 液晶の安全域 (2026-11-16・dot・480×270 の格子)。 液晶は樽型に湾曲している (Game2D/LcdScreen の _Curve) ので、 端の近くの絵は画面の外へ押し出されて欠ける。
    /// 字・板・押す物は、 この内側に収める。 背景の絵は画面いっぱいでよい。 <b>UnityEngine に依存しない</b> (検算 Tools/event_page_check がそのままコンパイルする)。
    /// 値は結末の画面 (<see cref="Game2DEndingScreen"/>) が実機で決めた余白 (左右 16・上下 9)。 イベントの画面 (<see cref="EventLayout"/>) も同じ値を使う。
    /// </summary>
    public static class LcdSafeArea
    {
        /// <summary>左端・右端・上端・下端 (この範囲の内側に収める)。</summary>
        public const int L = 16, R = 464, T = 9, B = 259;
    }
}
