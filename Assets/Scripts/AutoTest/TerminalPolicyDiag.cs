using System.Collections.Generic;

namespace AutoTest
{
    /// <summary>[計装 2026-11-13・#106] BOT の端子の方策の中身 (報酬で付けた点・配線で挿さなかった理由)。 <b>数えるだけ</b> ── 乱数を引かず、 判断を変えない。
    ///
    /// <para>本体の <see cref="CombatSystem.TerminalDiag"/> は「何が起きたか」(提示・取得・場に出た・発動)、 こちらは「BOT がどう値付けしたか」。
    /// 静的カウンタで持ち、 ワーカーの応答 (termPolicyNames / termPolicy) に載せる。</para>
    ///
    /// <para>列 (端子ごと):
    /// <list type="bullet">
    /// <item>報酬 [0〜4] / 店 [5〜9]: BOT が候補として点を付けた回数 / 選んだ回数 / 選んだ時の点の合計 / 選ばなかった時の点の合計 /
    /// 候補の中で最良だったが足切り (<c>RewardRejectBelow</c> + 山札の枚数) に届かず見送った回数。
    /// 店は 1 来店で買うたびに残りの棚へ選び直すので、 「候補にした回数」は来店の数より多い。 店は所持金の取り置きを割る棚を候補に入れない。</item>
    /// <item>配線 [10〜22]: 実際の配線の判断 (Optimal・Super・素朴版を除く・振り直しの値付けと〈猫の手〉の評価を除く) で、 その端子が場にあり止まっていない回数 [10] を、
    /// 1 つだけに分ける: [16] 選んだ手で発動 / [17] 選んだ手で挿したが不発 (c) / [11] 発動させられる手が無い (a・対象を取る端子は「対象が取れる手」が無い) /
    /// [12] 発動させられる手はあったが選ばなかった (b)。 (b) のうち [14] 最良と同点 / [15] 差が 500 以上 (生死がらみ) / 残りの差の合計 [13]。
    /// [18] 発動のうち、 ミス (Optimal の次点) で選んだ手でだけ発動した回数。 [22] (a) のうち、 対象を取らずに発動する手はあった回数 (対象を取る端子だけ)。
    /// [19] 未来の与ダメ 1 の価値 (DeckDamageWorth) の合計 / [20] 先の HP 1 の価値 (DeckFutureHpWorth) の合計 / [21] この戦闘であと引く回数の見込み (写し取り・焼き切りの値付けの draws) の合計 ── どれも [10] で割ると平均。</item>
    /// </list></para></summary>
    public static class TerminalPolicyDiag
    {
        public const int Width = 23;
        public const int RwCons = 0, RwChosen = 1, RwScChosen = 2, RwScNot = 3, RwBelow = 4;
        public const int ShopOffset = 5;
        public const int HandTurns = 10, NoFeasible = 11, NotChosen = 12, GapSum = 13, Tied = 14, Forced = 15,
                         ChosenFire = 16, PlugNoFire = 17, MistakeFire = 18, DwSum = 19, FwSum = 20, DrawsSum = 21, FiresNoTarget = 22;

        public static readonly Dictionary<string, double[]> Rows = new Dictionary<string, double[]>();

        public static double[] Row(string id)
        {
            if (!Rows.TryGetValue(id, out var r)) Rows[id] = r = new double[Width];
            return r;
        }

        /// <summary>報酬 / 店の 1 回の選択: 候補ごとの点・選んだ添字 (負 = 見送り)・最良の添字。</summary>
        public static void NoteReward(IReadOnlyList<string> ids, float[] scores, int chosen, int best, bool shop)
        {
            if (ids == null || scores == null) return;
            int o = shop ? ShopOffset : 0;
            for (int i = 0; i < ids.Count && i < scores.Length; i++)
            {
                if (float.IsNegativeInfinity(scores[i])) continue;   // 定義の無い端子 (点を付けていない)
                var r = Row(ids[i]);
                r[o + RwCons]++;
                if (i == chosen) { r[o + RwChosen]++; r[o + RwScChosen] += scores[i]; }
                else r[o + RwScNot] += scores[i];
                if (chosen < 0 && i == best) r[o + RwBelow]++;
            }
        }

        public static void Reset() => Rows.Clear();
    }
}
