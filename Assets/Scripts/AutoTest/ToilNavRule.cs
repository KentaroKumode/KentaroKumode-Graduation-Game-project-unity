using EventSystem.TimedEffects;

namespace AutoTest
{
    /// <summary>
    /// 〈徒労〉を持っている間の BOT の航行の値付け (2026-11-14・口 <c>AutoRunner.navToilAware</c>)。 UnityEngine に依存しない
    /// (検算 Tools/event_branch_check が本物をコンパイルして確かめる)。
    /// <para>〈徒労〉が報酬を止めるのは<b>通常の戦闘</b>だけ (GameManager.IsNormalBattleNow: 戦闘マスで、 エリート・ボス・追跡者・イベントの戦闘でない)。
    /// 止めるかどうかは本体の規則 <see cref="BattleBoonRule.Scale(BattleBoon, int, ToilMode)"/> から読む (別の形 LayerLong はゴールドを止めない = 値付けを変えない)。</para>
    /// <para>BOT はこの値が 0 より大きい間、 通常の戦闘の点の報酬の見込みを 0 にする (被ダメ・危険の見込みは残す = 正味で損)。
    /// 乱数は引かない。 口が切れている・〈徒労〉を持っていない時は 0 = 今までと同じ値付け。</para>
    /// </summary>
    public static class ToilNavRule
    {
        /// <summary>通常の戦闘の報酬が止まっている〈徒労〉の残り。 0 = 止まっていない
        /// (口 <paramref name="aware"/> が切れている・持っていない・形が報酬のゴールドを止めない)。</summary>
        public static int RewardlessLeft(bool aware, int toilLeft, ToilMode mode)
            => aware && toilLeft > 0 && BattleBoonRule.Scale(BattleBoon.Toil, 1, mode) == 0 ? toilLeft : 0;

        /// <summary>通常の戦闘に 1 つ勝った後の残り (旧い形 Stacks だけ減る・既定の形 UntilKing は減らない)。</summary>
        public static int AfterNormalWin(int left, ToilMode mode)
            => left > 0 && BattleBoonRule.ToilCountsDown(mode) ? left - 1 : left;
    }
}
