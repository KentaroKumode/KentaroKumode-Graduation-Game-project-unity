namespace UI.Game2D
{
    /// <summary>
    /// 2D 画面の格子の大きさ。 座標は <b>480×270 の格子（dot）</b>で、 左上が原点・y は下向き。 実画面では 1 dot ＝ 整数画素の倍率
    /// （<see cref="Game2DScreen"/>）。
    /// <para>旧い配置表 (docs/design/gui-full-layout.html の NAV / BAT の写し・GOLD の入れ物・魔石のレーダーなど) は、 どこからも読まれていなかったので
    /// 2026-11-24 に旧い筐体の部品と一緒に消した。 航行の計器の配置は <see cref="Game2DNavExtras"/>・戦闘は <see cref="BattleLcdLayout"/>。</para>
    /// </summary>
    public static class Game2DLayout
    {
        public const int Width = 480, Height = 270;
    }
}
