namespace MapSystem.FreeMove
{
    /// <summary>嵐の時計の種類 (2026-11-13)。 既定は <see cref="Instability"/> (取るほど不安定になる)。
    /// <see cref="Turns"/> は手番で進む嵐 (ブランチ storm-boss の作り・測定の口 GameHooks.StormClock)。</summary>
    public enum StormClock
    {
        /// <summary>不安定化ポイントの累計で進む (既定)。</summary>
        Instability = 0,
        /// <summary>航行の手番で進む (静かな時間 10 → フェーズ 1 (10) → 2 (10) → 3 (5) → 4・2026-11-13 の storm-boss の作り)。</summary>
        Turns = 1,
    }

    /// <summary>不安定化ポイントが溜まった理由 (<see cref="Instability.PointsOf"/> の表の行)。 ログと記録に名前で残す。</summary>
    public enum InstabilitySource
    {
        /// <summary>宝の点を開けた・縄張りの宝を取った。</summary>
        Treasure,
        /// <summary>秘宝を 1 つ得た (イベント・戦闘の報酬・番人の報酬)。</summary>
        Relic,
        /// <summary>エリートを倒した。</summary>
        Elite,
        /// <summary>追跡者 (番人) を倒した。</summary>
        Pursuer,
        /// <summary>通常の戦闘 (使い魔・イベントの戦闘も) に勝った。</summary>
        Battle,
        /// <summary>イベントで何かを得た (秘宝のほか)。</summary>
        EventGain,
        /// <summary>戦闘の中でエスカレーションの段が上がった (1 段ごと)。</summary>
        Escalation,
        /// <summary>イベントの「ターン+N」(N ごと)。</summary>
        EventTurns,
        /// <summary>崩れ始めた後の 1 歩・待つ・偵察 (手番ごと)。</summary>
        Walk,
        /// <summary>イベントの「嵐を遠ざける / 近づける+N」(符号つき)。</summary>
        Shift,
    }

    /// <summary>
    /// 不安定化 (2026-11-13・GAME.md §5-12)。 <b>大穴から何かを取るたびに不安定になる</b> ── 嵐を進めるのは手番ではなく、 この累計。
    ///
    /// <para><b>「取る」の表はここ 1 か所</b> (<see cref="PointsOf"/>)。 本体 (GameManager) は理由と量を渡すだけで、 数を持たない。
    /// 理由は効果の種類・点の種別・戦闘の種類で決める (id の綴りで判定しない)。</para>
    ///
    /// <para>累計 → 段 → 輪は、 累計と層の定数だけで決まる純粋な関数 (<see cref="Storm.RingAt(FreeMapLayout, int, Storm.Schedule, out Vec2, out double)"/>
    /// に <see cref="TimeOf"/> と <see cref="Schedule"/> を渡す)。 中断セーブに足す状態は、 累計と理由ごとの内訳 (<see cref="Parts"/>) だけ。</para>
    /// </summary>
    public static class Instability
    {
        /// <summary>内訳の列: 取る (宝・秘宝・戦闘・イベントの得) / エスカレーション / 歩く (1 歩・待つ・偵察) / イベントのターン / 遠ざける・近づける (正味)。</summary>
        public const int PartTake = 0, PartEscalation = 1, PartWalk = 2, PartEventTurns = 3, PartShift = 4, Parts = 5;

        /// <summary>理由 <paramref name="src"/> で <paramref name="n"/> 回ぶん溜まるポイント。 歩く・イベントのターン・遠ざける / 近づける は n がそのまま量 (符号つき)。</summary>
        public static int PointsOf(InstabilitySource src, int n = 1)
        {
            switch (src)
            {
                case InstabilitySource.Treasure:
                case InstabilitySource.Relic:      return FreeMapParams.InstabilityTake * n;
                case InstabilitySource.Elite:
                case InstabilitySource.Pursuer:    return FreeMapParams.InstabilityKill * n;
                case InstabilitySource.Battle:
                case InstabilitySource.EventGain:  return FreeMapParams.InstabilityWin * n;
                case InstabilitySource.Escalation: return FreeMapParams.InstabilityEscalation * n;
                case InstabilitySource.Walk:       return FreeMapParams.InstabilityWalk * n;
                case InstabilitySource.EventTurns:
                case InstabilitySource.Shift:      return n;
            }
            return 0;
        }

        /// <summary>理由 → 内訳の列。</summary>
        public static int PartOf(InstabilitySource src)
        {
            switch (src)
            {
                case InstabilitySource.Escalation: return PartEscalation;
                case InstabilitySource.Walk:       return PartWalk;
                case InstabilitySource.EventTurns: return PartEventTurns;
                case InstabilitySource.Shift:      return PartShift;
                default:                           return PartTake;
            }
        }

        /// <summary>理由の短い名前 (ログ・結果の行)。</summary>
        public static string NameOf(InstabilitySource src)
        {
            switch (src)
            {
                case InstabilitySource.Treasure:   return "宝";
                case InstabilitySource.Relic:      return "秘宝";
                case InstabilitySource.Elite:      return "エリート";
                case InstabilitySource.Pursuer:    return "番人";
                case InstabilitySource.Battle:     return "戦闘";
                case InstabilitySource.EventGain:  return "イベントの得";
                case InstabilitySource.Escalation: return "激化";
                case InstabilitySource.EventTurns: return "イベントのターン";
                case InstabilitySource.Walk:       return "歩く";
                case InstabilitySource.Shift:      return "嵐を動かす";
            }
            return src.ToString();
        }

        /// <summary>イベント 1 回 (選んだ効果・勝利後の効果のそれぞれ) で溜まるポイント: 秘宝 1 つにつき <see cref="FreeMapParams.InstabilityTake"/>。
        /// 秘宝が無く、 ほかの得が 1 つ以上起きた時だけ <see cref="FreeMapParams.InstabilityWin"/> (重ねない)。 <paramref name="otherAlreadyCounted"/> = 同じイベントで既に +1 (か秘宝) を数えた。</summary>
        public static int EventPoints(int relics, bool otherGain, bool otherAlreadyCounted)
            => relics > 0 ? PointsOf(InstabilitySource.Relic, relics)
             : otherGain && !otherAlreadyCounted ? PointsOf(InstabilitySource.EventGain) : 0;

        /// <summary>戦闘の中でエスカレーションの段が <paramref name="rises"/> 段上がった時のポイント。 <b>ボス戦では溜めない</b> (ボスの点に入ったら層は終わりへ向かうので)。</summary>
        public static int EscalationPoints(int rises, bool bossFight) => bossFight || rises <= 0 ? 0 : PointsOf(InstabilitySource.Escalation, rises);

        /// <summary>層に入った時の累計 (深さ 6 = 画面の第 4 層は <see cref="FreeMapParams.InstabilityDepth6Start"/>・ほかは 0)。</summary>
        public static int StartOf(int depth) => depth == 6 ? FreeMapParams.InstabilityDepth6Start : 0;

        /// <summary>累計の段の長さ (フェーズ 1 = 15・2 = 15・3 = 5・境目の差から出す)。 輪の補間に使う (<see cref="Storm.RingAt(FreeMapLayout, int, Storm.Schedule, out Vec2, out double)"/>)。</summary>
        public static Storm.Schedule Schedule => new Storm.Schedule(
            FreeMapParams.InstabilityPhase2At - FreeMapParams.InstabilityPhase1At,
            FreeMapParams.InstabilityPhase3At - FreeMapParams.InstabilityPhase2At,
            FreeMapParams.InstabilityPhase4At - FreeMapParams.InstabilityPhase3At);

        /// <summary>累計 → 嵐の時計 (崩れ始めてからのポイント・負 = 崩れ始めるまでの残り)。</summary>
        public static int TimeOf(int points) => points - FreeMapParams.InstabilityPhase1At;

        /// <summary>累計 → 段 (0 = 静か / 1〜4)。</summary>
        public static int PhaseOf(int points) => Storm.PhaseOf(TimeOf(points), Schedule);

        /// <summary>崩れ始めたか (累計が最初の境目以上)。 ここから先は歩くだけでも溜まる。</summary>
        public static bool Collapsing(int points) => points >= FreeMapParams.InstabilityPhase1At;

        /// <summary>着いた点の種別で溜まるポイントの見積もり (地図の見積もり用)。 <paramref name="atLeast"/> = それより多くなり得る
        /// (戦闘のエスカレーション・報酬の秘宝・イベントの選び方)。 店・休憩・始点・ボス・交換 は 0。</summary>
        public static int ArrivalPoints(TileType t, out bool atLeast)
        {
            atLeast = false;
            switch (t)
            {
                case TileType.Treasure:    return PointsOf(InstabilitySource.Treasure);
                case TileType.EliteBattle: atLeast = true; return PointsOf(InstabilitySource.Elite);
                case TileType.Battle:      atLeast = true; return PointsOf(InstabilitySource.Battle);
                case TileType.Event:       atLeast = true; return 0;
            }
            return 0;
        }
    }
}
