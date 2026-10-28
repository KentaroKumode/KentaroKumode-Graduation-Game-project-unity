namespace MapSystem.FreeMove
{
    /// <summary>
    /// 航行マップの天候 (手番制・2026-10-18)。 層ごとに 1 つ。 参照実装は map-grid-prototype.html の <c>WEATHERS</c>。
    ///
    /// <para><b>前線</b>は「その現象が届く範囲」を表すだけで、 前線そのものに罰は無い。 左端から
    /// <see cref="FreeMapParams.FrontEvery"/> 手番ごとに <see cref="FreeMapParams.FrontStep"/> ずつ右へ進み、
    /// 前線の<b>内側の点</b>でだけ各天候の効果が起きる。</para>
    /// </summary>
    public enum WeatherKind
    {
        None = 0,
        /// <summary>前線の中では、 道で 2 歩以上離れた点が「?」に戻る (近づけば戻る・中身は引き直さない)。</summary>
        Fog,
        /// <summary>沈む静寂 (仮): 前線の中では痕跡が 2 倍長く残る。 物音が無くなったので作り直し中の仮の効果 (抽選には入れない)。</summary>
        Still,
        /// <summary>削る砂: 前線の中では 1 歩の物資が 1.5 倍。 痕跡は 2 倍早く消える。</summary>
        Sand,
        /// <summary>朱の雪: 前線の中では、 移動に 2 手番かかる (追跡者は 2 回動く)。</summary>
        Snow,
        /// <summary>落石のような雹: 前線の中では、 移動ごとに HP −1。</summary>
        Hail,
        /// <summary>崩れる地平: 前線が進むたび、 前線の中の道が 1 本崩れる (終点へは必ずつながったまま)。</summary>
        Crumble,
        /// <summary>沈み続ける湖面: 前線が進むたび、 前線の中の未使用の点が 1 つ沈む (終点へは必ずつながったまま)。</summary>
        Sink,
    }

    public static class WeatherInfo
    {
        public const string RngKey = "map.free.weather";

        public static string DisplayName(this WeatherKind w)
        {
            switch (w)
            {
                case WeatherKind.Fog: return "霧";
                case WeatherKind.Still: return "沈む静寂（仮）";
                case WeatherKind.Sand: return "削る砂";
                case WeatherKind.Snow: return "朱の雪";
                case WeatherKind.Hail: return "落石のような雹";
                case WeatherKind.Crumble: return "崩れる地平";
                case WeatherKind.Sink: return "沈み続ける湖面";
                default: return "なし";
            }
        }

        public static string Description(this WeatherKind w)
        {
            switch (w)
            {
                case WeatherKind.Fog: return "前線の中では、道で2歩以上離れた点が「?」に戻る（近づけば戻る・中身は引き直さない）";
                case WeatherKind.Still: return "前線の中では、痕跡が2倍長く残る（作り直し中の仮の効果）";
                case WeatherKind.Sand: return "前線の中では、1歩の物資が1.5倍。痕跡は2倍早く消える";
                case WeatherKind.Snow: return "前線の中では、移動に2手番かかる（追跡者は2回動く）";
                case WeatherKind.Hail: return "前線の中では、移動ごとにHP-1";
                case WeatherKind.Crumble: return "前線が進むたび、前線の中の道が1本崩れる（終点へは必ずつながったまま）";
                case WeatherKind.Sink: return "前線が進むたび、前線の中の未使用の点が1つ沈む（終点へは必ずつながったまま）";
                default: return "";
            }
        }

        /// <summary>その層で引ける天候。 1 層は穏やかに天候なし。 <see cref="WeatherKind.Still"/> は作り直し中なので抽選に入れない。</summary>
        public static WeatherKind[] PoolFor(int floor)
            => floor <= 1 || floor == FloorPlan.LinearFloor     // 5 層 (直線) は道が 1 本なので、 崩れる・沈む天候を入れない
                ? new[] { WeatherKind.None }
                : new[] { WeatherKind.Fog, WeatherKind.Sand, WeatherKind.Snow, WeatherKind.Hail,
                          WeatherKind.Crumble, WeatherKind.Sink };

        /// <summary>層の天候を引く (GameRng のキー <see cref="RngKey"/>・連番 = 層)。</summary>
        public static WeatherKind ForFloor(int floor)
        {
            var pool = PoolFor(floor);
            return pool[GameLoop.GameRng.Range(0, pool.Length, RngKey, floor)];
        }
    }
}
