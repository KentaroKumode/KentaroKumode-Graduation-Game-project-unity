namespace EventSystem.TimedEffects
{
    /// <summary>直近の戦闘の報酬に掛かった時限効果 (2026-11-13・〈徒労〉〈開眼〉)。</summary>
    public enum BattleBoon { None, Toil, Insight }

    /// <summary>[測定 2026-11-14] 〈徒労〉の形 (GameHooks.ToilMode・AutoRunner.toilMode)。 既定 <see cref="Stacks"/> = 今の形。</summary>
    public enum ToilMode
    {
        /// <summary>今の形 (既定): 3 つ持ち、 通常の戦闘に勝つたびに 1 つ減り、 その戦闘では何も得られない (ゴールド・ドロップ・強化素材・物資 0)。</summary>
        Stacks,
        /// <summary>別の形 (測定用・採否は未決): 深さ 6 (画面の第 4 層) にいる間ずっと続き (勝っても減らない)、 層を出たら消える。
        /// 通常の戦闘は品を一切落とさない (ゴールド・強化素材・物資は出る)。 宝箱 (宝の点・縄張りの宝) は確率で空になる。</summary>
        LayerLong,
    }

    /// <summary>
    /// 時限デバフ〈徒労〉/ 時限バフ〈開眼〉(2026-11-13・docs/GAME.md §17) の規則と数値の正本。 UnityEngine に依存しない
    /// (検算 Tools/event_branch_check が本物をコンパイルして確かめる)。
    ///
    /// <para><b>通常の戦闘</b> (地図の戦闘マスの戦闘で、 エリート・ボス・追跡者・使い魔・イベントの戦闘・偽の商人・値下げ交渉でない) <b>に勝った時だけ</b>、
    /// 持っている方を 1 つ減らし (<see cref="TimedEffectManager.OnNormalBattleWon"/>)、 減らす前に持っていた方をその戦闘の報酬に掛ける。
    /// 〈徒労〉= 報酬が全部 0 (ゴールド・ドロップ・強化素材・物資・強奪のドロップ)。 〈開眼〉= ゴールド・ドロップ・強化素材が <see cref="InsightMultiplier"/> 倍
    /// (ドロップは抽選を <see cref="InsightMultiplier"/> 回引く・物資は増やさない)。 両方ある時は〈徒労〉が勝つ (両方減る)。
    /// 逃げた・負けた戦闘と、 通常でない戦闘は減らさず、 報酬も普通に出る。 層を出ても残る。</para>
    /// <para>[測定 2026-11-14] 〈徒労〉には測定用の別の形 (<see cref="ToilMode.LayerLong"/>・GameHooks.ToilMode) がある。 形を見る版の関数 (引数 mode) は、
    /// mode = <see cref="ToilMode.Stacks"/> (既定) なら形を見ない版と同じ値を返す (検算 Tools/event_branch_check の「徒労 (既定の形 Stacks)」)。</para>
    /// <para>呼ぶのは GameManager.TakeNormalBattleBoon (判定の 1 か所) と、 その結果を読む報酬の各所 (素材・強奪・ゴールド・物資・ドロップ)。</para>
    /// </summary>
    public static class BattleBoonRule
    {
        /// <summary>〈徒労〉の名前 (event_list.txt の「時限デバフ[徒労]」と一致させる)。</summary>
        public const string ToilName = "徒労";
        /// <summary>〈徒労〉を受けた時に持つ数 (= 報われない通常の戦闘の数)。</summary>
        public const int ToilCharges = 3;
        /// <summary>〈徒労〉の画面の説明 (作者の確定文・2026-11-13)。</summary>
        public const string ToilDescription = "この後の通常の戦闘三回は、勝っても何も得られない。";

        /// <summary>〈徒労〉の別の形 (<see cref="ToilMode.LayerLong"/>) の画面の説明 (仮の文・2026-11-14)。</summary>
        public const string ToilLayerDescription = "この層の間、通常の戦闘は品を落とさず、宝箱は空のことがある。";
        /// <summary>〈徒労〉の別の形で宝箱が空になる確率 (%) の既定 (GameHooks.ToilChestEmptyPct)。</summary>
        public const int DefaultChestEmptyPct = 50;

        /// <summary>〈開眼〉の名前 (event_list.txt の「時限バフ[開眼]」と一致させる)。</summary>
        public const string InsightName = "開眼";
        /// <summary>〈開眼〉を得た時に持つ数。</summary>
        public const int InsightCharges = 3;
        /// <summary>〈開眼〉の倍率 (ゴールド・ドロップ・強化素材)。</summary>
        public const int InsightMultiplier = 2;
        /// <summary>〈開眼〉の画面の説明 (作者の確定文・2026-11-13)。</summary>
        public const string InsightDescription = "この後の通常の戦闘三回は、得られる物が二倍になる。";

        /// <summary>この戦闘の報酬に掛かる時限効果 (= 1 つ減らすか)。 <paramref name="normal"/> = 通常の戦闘か。
        /// <paramref name="toilLeft"/> / <paramref name="insightLeft"/> = 減らす前の残り。</summary>
        public static BattleBoon Decide(bool won, bool fled, bool normal, int toilLeft, int insightLeft)
        {
            if (!won || fled || !normal) return BattleBoon.None;
            if (toilLeft > 0) return BattleBoon.Toil;
            if (insightLeft > 0) return BattleBoon.Insight;
            return BattleBoon.None;
        }

        /// <summary>報酬の量 (ゴールド・強化素材) に掛ける: 〈徒労〉0 /〈開眼〉×<see cref="InsightMultiplier"/> / 掛からなければそのまま。</summary>
        public static int Scale(BattleBoon b, int amount)
            => b == BattleBoon.Toil ? 0 : b == BattleBoon.Insight ? amount * InsightMultiplier : amount;

        /// <summary>ドロップの抽選を引く回数 (〈開眼〉は <see cref="InsightMultiplier"/> 回・ほかは 1 回。 〈徒労〉も 1 回引いて乱数の列を変えない)。</summary>
        public static int DropRolls(BattleBoon b) => b == BattleBoon.Insight ? InsightMultiplier : 1;

        /// <summary>当たったドロップを配るか (〈徒労〉は配らない)。</summary>
        public static bool GrantsDrops(BattleBoon b) => b != BattleBoon.Toil;

        /// <summary>物資を配るか (〈徒労〉は配らない・〈開眼〉は増やさない = 普通に配る)。</summary>
        public static bool GrantsProvision(BattleBoon b) => b != BattleBoon.Toil;

        /// <summary>画面に出す説明 (名前・残りと並べる)。 〈徒労〉〈開眼〉だけ。 無ければ null。</summary>
        public static string DescriptionOf(string id)
            => id == ToilName ? ToilDescription : id == InsightName ? InsightDescription : null;

        // ── 〈徒労〉の形ごとの違い (2026-11-14・測定用)。 mode = Stacks では上の規則と 1 つも変わらない ──

        /// <summary>報酬の量 (ゴールド・強化素材) に掛ける (形を見る版)。 別の形の〈徒労〉はゴールド・強化素材を止めない (品だけ止める)。</summary>
        public static int Scale(BattleBoon b, int amount, ToilMode mode)
            => b == BattleBoon.Toil && mode == ToilMode.LayerLong ? amount : Scale(b, amount);

        /// <summary>物資を配るか (形を見る版)。 別の形の〈徒労〉は物資を止めない。</summary>
        public static bool GrantsProvision(BattleBoon b, ToilMode mode)
            => b == BattleBoon.Toil && mode == ToilMode.LayerLong ? true : GrantsProvision(b);

        /// <summary>通常の戦闘に勝った時に〈徒労〉を 1 つ減らすか (今の形だけ・別の形は層にいる間ずっと続く)。</summary>
        public static bool ToilCountsDown(ToilMode mode) => mode == ToilMode.Stacks;

        /// <summary>層を出た時に〈徒労〉を消すか (別の形だけ)。</summary>
        public static bool ToilClearsOnLeavingLayer(ToilMode mode) => mode == ToilMode.LayerLong;

        /// <summary>宝箱が空か: 別の形で〈徒労〉を持っている時だけ、 <paramref name="roll01"/> (0〜1 の一様乱数) × 100 &lt; <paramref name="emptyPct"/> なら空。</summary>
        public static bool ChestEmpty(ToilMode mode, bool hasToil, double roll01, int emptyPct)
            => mode == ToilMode.LayerLong && hasToil && roll01 * 100.0 < emptyPct;

        /// <summary>画面に出す説明 (形を見る版)。 別の形の〈徒労〉は層の間の説明に切り替える。</summary>
        public static string DescriptionOf(string id, ToilMode mode)
            => id == ToilName && mode == ToilMode.LayerLong ? ToilLayerDescription : DescriptionOf(id);

        /// <summary>画面に残りの数を出すか (別の形の〈徒労〉は減らないので出さない)。</summary>
        public static bool ShowsCharges(string id, ToilMode mode) => !(id == ToilName && mode == ToilMode.LayerLong);
    }
}
