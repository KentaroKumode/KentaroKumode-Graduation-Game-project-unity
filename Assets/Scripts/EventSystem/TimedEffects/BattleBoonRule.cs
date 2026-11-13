namespace EventSystem.TimedEffects
{
    /// <summary>直近の戦闘の報酬に掛かった時限効果 (2026-11-13・〈徒労〉〈開眼〉)。</summary>
    public enum BattleBoon { None, Toil, Insight }

    /// <summary>
    /// 時限デバフ〈徒労〉/ 時限バフ〈開眼〉(2026-11-13・docs/GAME.md §17) の規則と数値の正本。 UnityEngine に依存しない
    /// (検算 Tools/event_branch_check が本物をコンパイルして確かめる)。
    ///
    /// <para><b>通常の戦闘</b> (地図の戦闘マスの戦闘で、 エリート・ボス・追跡者・使い魔・イベントの戦闘・偽の商人・値下げ交渉でない) <b>に勝った時だけ</b>、
    /// 持っている方を 1 つ減らし (<see cref="TimedEffectManager.OnNormalBattleWon"/>)、 減らす前に持っていた方をその戦闘の報酬に掛ける。
    /// 〈徒労〉= 報酬が全部 0 (ゴールド・ドロップ・強化素材・物資・強奪のドロップ)。 〈開眼〉= ゴールド・ドロップ・強化素材が <see cref="InsightMultiplier"/> 倍
    /// (ドロップは抽選を <see cref="InsightMultiplier"/> 回引く・物資は増やさない)。 両方ある時は〈徒労〉が勝つ (両方減る)。
    /// 逃げた・負けた戦闘と、 通常でない戦闘は減らさず、 報酬も普通に出る。 層を出ても残る。</para>
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
    }
}
