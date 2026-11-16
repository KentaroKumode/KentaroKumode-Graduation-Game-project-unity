using System.Collections.Generic;

namespace EventSystem.TimedEffects
{
    /// <summary>
    /// イベントの「悪い行い」の罰の時限デバフ〈感傷〉〈罪悪感〉〈無力感〉(2026-11-16・作者の決定・docs/GAME.md §17) の規則と数値の正本。
    /// UnityEngine に依存しない (検算 Tools/rules_check が本物をコンパイルして確かめる)。
    ///
    /// <para>3 つとも「次の N 戦闘」続く (<see cref="TimedEffectTrigger.OnRoll"/> の扱い: 戦闘の終わり (FinishCombat → TimedEffectManager.OnCombatEnd) に 1 つ減る。
    /// 勝っても・負けても・逃げても・イベントの戦闘でも同じく 1 つ減る)。</para>
    /// <list type="bullet">
    /// <item>〈感傷〉: 自分の出目が全部 −1 (下限 1)。 次の 3 戦闘 (2026-11-16 に 1 → 3)。 掛けるのは <c>Effects.SentimentEffect</c> (振り直しの後・配線の前)。</item>
    /// <item>〈罪悪感〉: <b>防御の端子に挿した出目だけ</b> −2 (下限 0)。 攻撃・充電・補助に挿した出目は変えない。 次の 2 戦闘。
    ///   掛けるのは端子の解決 (<c>CombatSystem.TerminalCards.Resolve</c>) の 1 か所 ── 盤面 (<c>TerminalEnv.blockDieCut</c>) に載せるので、
    ///   本体・画面の見込み・BOT の配線の評価が同じ値を読む。</item>
    /// <item>〈無力感〉: 振り直しの費用に充電 +1 (毎回)。 無料の振り直し (〈特別・対〉) には足さない。 次の 2 戦闘。
    ///   費用は <see cref="RerollCost"/> の 1 か所 (本体の RerollPhase・画面の表示・BOT の方策が読む)。</item>
    /// </list>
    /// </summary>
    public static class TimedDebuffRule
    {
        /// <summary>〈感傷〉の名前 (event_list.txt の「時限デバフ[感傷]」と一致させる)。</summary>
        public const string SentimentName = "感傷";
        /// <summary>〈感傷〉を受けた時に持つ数 (= 続く戦闘の数・2026-11-16 に 1 → 3)。</summary>
        public const int SentimentCharges = 3;
        /// <summary>〈感傷〉で出目から引く量 (下限 1)。</summary>
        public const int SentimentDieCut = 1;
        /// <summary>〈感傷〉の画面の説明。</summary>
        public const string SentimentDescription = "自分の出目が全部 −1。";

        /// <summary>〈罪悪感〉の名前 (event_list.txt の「時限デバフ[罪悪感]」と一致させる)。</summary>
        public const string GuiltName = "罪悪感";
        /// <summary>〈罪悪感〉を受けた時に持つ数 (= 続く戦闘の数)。</summary>
        public const int GuiltCharges = 2;
        /// <summary>〈罪悪感〉で防御に挿した出目から引く量 (下限 0)。</summary>
        public const int GuiltBlockDieCut = 2;
        /// <summary>〈罪悪感〉の画面の説明。</summary>
        public const string GuiltDescription = "防御に挿した出目 −2。";

        /// <summary>〈無力感〉の名前 (event_list.txt の「時限デバフ[無力感]」と一致させる)。</summary>
        public const string HelplessName = "無力感";
        /// <summary>〈無力感〉を受けた時に持つ数 (= 続く戦闘の数)。</summary>
        public const int HelplessCharges = 2;
        /// <summary>〈無力感〉で振り直し 1 回の費用に足す充電。</summary>
        public const int HelplessRerollSurcharge = 1;
        /// <summary>〈無力感〉の画面の説明。</summary>
        public const string HelplessDescription = "振り直しの費用 +1。";

        /// <summary>時限デバフの表 (id → 残り) にこの id が 1 つ以上あるか。</summary>
        public static bool Has(IDictionary<string, int> debuffs, string id)
            => debuffs != null && id != null && debuffs.TryGetValue(id, out int n) && n > 0;

        /// <summary>〈感傷〉を掛けた出目 (下限 1)。</summary>
        public static int SentimentDie(int v) => System.Math.Max(1, v - SentimentDieCut);

        /// <summary>防御に挿した出目から引く量 (〈罪悪感〉を持っていれば <see cref="GuiltBlockDieCut"/>・無ければ 0)。</summary>
        public static int BlockDieCut(IDictionary<string, int> debuffs) => Has(debuffs, GuiltName) ? GuiltBlockDieCut : 0;

        /// <summary>防御に挿した出目 1 つに引く量を掛ける (下限 0)。 cut ≦ 0 なら素通し。</summary>
        public static int CutBlockDie(int v, int cut) => cut > 0 ? System.Math.Max(0, v - cut) : v;

        /// <summary>振り直しの費用に足す充電 (〈無力感〉を持っていれば <see cref="HelplessRerollSurcharge"/>・無ければ 0)。</summary>
        public static int RerollSurcharge(IDictionary<string, int> debuffs) => Has(debuffs, HelplessName) ? HelplessRerollSurcharge : 0;

        /// <summary>振り直し 1 回の費用 (充電)。 = 振る個数 × この戦闘で何回目か + 上乗せ (〈無力感〉)。 無料の振り直し (〈特別・対〉) は 0 (上乗せも足さない)。
        /// 本体 (CombatManager.RerollPhase) の支払いと画面の表示・BOT の方策 (AutoRunner.MutualRerollPolicy) が読む 1 か所。</summary>
        public static int RerollCost(int diceCount, int nth, bool free, int surcharge)
            => free ? 0 : System.Math.Max(0, diceCount) * System.Math.Max(1, nth) + System.Math.Max(0, surcharge);

        /// <summary>画面に出す説明 (3 つのどれかなら・ほかは null)。</summary>
        public static string DescriptionOf(string id)
            => id == SentimentName ? SentimentDescription
             : id == GuiltName ? GuiltDescription
             : id == HelplessName ? HelplessDescription : null;
    }
}
