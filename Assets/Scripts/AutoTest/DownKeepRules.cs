using System;
using System.Collections.Generic;
using CombatSystem;

namespace AutoTest
{
    /// <summary>
    /// BOT の「ボスのダウンの狙い方」と「手札の残す印」の判断のうち、 盤面を読まない純粋な部分 (#141・2026-11-14)。
    /// UnityEngine に依存しない (検算 <c>Tools/rules_check</c> が本物をコンパイルして確かめる)。 乱数は引かない。
    /// 盤面の読み出し (敵の攻撃の見込み・端子の点) は <c>AutoRunner.Terminals</c> が行い、 ここへ数で渡す。
    ///
    /// <para><b>ダウンの狙い (口 <c>AutoRunner.downAimPolicy</c>)</b>。 本体の規則 (<see cref="BossDown"/>・<c>CombatManager.BeginBossDownTurn</c>):
    /// ダウンしたターンの頭に大技の周期の起点がそのターンへ戻る (<c>Escalation.HeavyCycleOffset</c>)。 だから<b>どのターンのダウンでも</b>、
    /// その後の敵の攻撃は「ダウン (0) → 周期の 1 ターン目 → …」と同じ並びになる。 目盛りを今割る (ダウンは次のターン) か、
    /// 1 ターン待って割る (ダウンは 2 ターン後) かの違いは、 待つ方が「次のターンの攻撃を 1 回受けて、 その後の並びが 1 ターン後ろへずれる」ことだけ:
    /// 受ける攻撃の和 (待つ − 今割る) = 次のターンの攻撃 − 戦闘の終わりのターンの攻撃。 戦闘の終わりが周期のどこに来るかは決まっていないので、
    /// 終わりのターンの攻撃を周期の平均で見ると、 待つ得 = 周期の平均の攻撃 − 次のターンの攻撃 (次のターンが溜めなら正・大技なら負)。
    /// 終わり際の差なので、 HP の帳簿の先の割引 (<c>V3Flags.ledgerFutureShare</c>) を掛ける。 待つ間に逃す与ダメ (撃破が遅れる分) は、
    /// 帳簿の「撃破までの被ダメの見込み」が既に見ている (ここでは足さない)。</para>
    ///
    /// <para><b>残す印 (口 <c>AutoRunner.handKeepPolicy = Planned</c>)</b>。 本体の規則 (<see cref="TerminalDeckState.DrawTurn"/>):
    /// 残した端子は次のターンの手札の枚数に数える (手札が <see cref="TerminalCards.HandSize"/> 枚になるまでしか引かない) ── 1 枚残すと、 新しく引く端子が 1 枚減る。
    /// だから「次のターンに使う当て」がある端子だけを残す:
    /// <list type="number">
    /// <item><b>ダウンの攻撃</b>: 次のターンがダウン (攻撃してこない・受けるダメージ ×1.5) なら、 攻撃の種類の端子を、 常設の攻撃の端子と合わせて出目の数ぶんの口が埋まるまで (点の高い順)。</item>
    /// <item><b>大技の守り</b>: 次のターンが大技 (ダウンでない) なら、 防御の種類の端子を、 常設の防御と合わせて <see cref="BlockSlotsNeeded"/> の口が埋まるまで。</item>
    /// <item><b>出目が足りず余った端子</b>: このターンに出目を全部挿し切って、 それでも挿さなかった、 条件の無い端子のうち、 これから引ける端子の平均より点の高い物。</item>
    /// </list>
    /// 残す口の数 (容量の和) は次のターンの出目の数まで (それより多くは次のターンに挿し切れない)。 前のターンに残したのに使わなかった端子は、 もう残さない
    /// (同じ端子を毎ターン持ち越して手札を詰まらせない)。</para>
    /// </summary>
    public static class DownKeepRules
    {
        // =====================================================================
        //  ダウンの狙い
        // =====================================================================

        /// <summary>目盛りを今割らずに 1 ターン待つ得 (HP の単位・帳簿の物差し)。 = <paramref name="futureShare"/> × (周期の平均の攻撃 − 次のターンの攻撃)。
        /// <paramref name="cycleAtks"/> = 次のターンから 1 周期ぶんの敵の攻撃の見込み (本体の式から・BOT は写しを持たない)。 空なら 0。</summary>
        public static float HoldGain(int nextAtk, IReadOnlyList<int> cycleAtks, float futureShare)
        {
            if (cycleAtks == null || cycleAtks.Count == 0) return 0f;
            float avg = 0f;
            for (int i = 0; i < cycleAtks.Count; i++) avg += cycleAtks[i];
            avg /= cycleAtks.Count;
            return futureShare * (avg - nextAtk);
        }

        /// <summary>この挿し方を「1 ターン待つ手」として扱うか。
        /// <list type="bullet">
        /// <item>敵が大技を持ち、 大技が 2 ターン後 (<paramref name="turnsToHeavy"/> = 2: 待って次のターンに割ると、 ダウンが大技のターンに重なる)。</item>
        /// <item>何もしなくても次のターンがダウン (<paramref name="downNextAnyway"/>) なら判断は要らない。</item>
        /// <item>今のターンに目盛りを割れる挿し方が候補にある (<paramref name="someCandBreaks"/>) のに、 この挿し方は割らない。</item>
        /// <item>この挿し方の後の HP が次の目盛りの上にあり、 その差が 1 ターンの与ダメの見込み (<paramref name="perTurnDmg"/>) 以内 (次のターンに割れる見込みがある)。</item>
        /// </list></summary>
        public static bool HoldEligible(bool hasHeavy, int turnsToHeavy, bool downNextAnyway, bool someCandBreaks, bool candBreaks,
                                        int hpAfter, int nextNotchHp, float perTurnDmg)
            => hasHeavy && turnsToHeavy == 2 && !downNextAnyway && someCandBreaks && !candBreaks
            && nextNotchHp >= 0 && hpAfter > nextNotchHp && hpAfter - nextNotchHp <= perTurnDmg;

        // =====================================================================
        //  残す印
        // =====================================================================

        /// <summary>残した理由 (計装の区分にも使う)。</summary>
        public enum KeepReason { None = 0, DownAttack = 1, HeavyBlock = 2, Leftover = 3 }

        /// <summary>残せる端子 1 枚 (挿していない・止まっていない手札)。</summary>
        public struct KeepCard
        {
            public int slot;               // 場の添字
            public TerminalKind kind;
            public int capacity;           // 挿せる出目の数
            public float worth;            // 素の点 (AutoRunner.TargetWorth)
            public bool condFree;          // 出目の条件が無い
            public bool keptBeforeUnused;  // 前のターンに残したのに、 このターンも使わなかった
        }

        /// <summary>大技のターンに守りに要る口の数 = 次のターンの敵の攻撃 ÷ 出目の平均 (切り上げ)・出目の数まで。</summary>
        public static int BlockSlotsNeeded(int nextAtk, float meanFace, int dice)
        {
            if (nextAtk <= 0 || dice <= 0) return 0;
            int n = (int)Math.Ceiling(nextAtk / Math.Max(1f, meanFace));
            return Math.Min(dice, n);
        }

        /// <summary>残す端子を決める。 戻り値 = <paramref name="cands"/> と同じ並びの理由 (None = 残さない)。
        /// <paramref name="dice"/> = 次のターンの出目の数 / <paramref name="permAttackCap"/>・<paramref name="permBlockCap"/> = 常設 (毎ターン場にある) の攻撃・防御の口の数 /
        /// <paramref name="poolAvg"/> = これから引ける端子 (山と捨て札) の点の平均 / <paramref name="diceExhausted"/> = このターンに出目を全部挿した。</summary>
        public static KeepReason[] PlanKeep(IList<KeepCard> cands, int dice, bool downNext, bool heavyNext,
                                            int permAttackCap, int permBlockCap, int blockSlotsNeeded, float poolAvg, bool diceExhausted)
        {
            int n = cands?.Count ?? 0;
            var res = new KeepReason[n];
            if (n == 0 || dice <= 0) return res;
            // 点の高い順 (同点は場の左から)
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => { int c = cands[b].worth.CompareTo(cands[a].worth); return c != 0 ? c : cands[a].slot.CompareTo(cands[b].slot); });
            int budget = dice;   // 残す口の数の残り (次のターンに挿せる出目の数まで)
            bool Take(int i, KeepReason r)
            {
                var c = cands[i];
                int cap = Math.Max(1, c.capacity);
                if (res[i] != KeepReason.None || c.keptBeforeUnused || cap > budget) return false;
                res[i] = r; budget -= cap;
                return true;
            }
            if (downNext)
            {
                int have = permAttackCap;
                foreach (int i in order)
                {
                    if (have >= dice) break;
                    if (cands[i].kind != TerminalKind.Attack) continue;
                    if (Take(i, KeepReason.DownAttack)) have += Math.Max(1, cands[i].capacity);
                }
                return res;   // 次のターンは出目を攻撃へ回す当て ── ほかの種類は残さない
            }
            if (heavyNext)
            {
                int have = permBlockCap;
                foreach (int i in order)
                {
                    if (have >= blockSlotsNeeded) break;
                    if (cands[i].kind != TerminalKind.Block) continue;
                    if (Take(i, KeepReason.HeavyBlock)) have += Math.Max(1, cands[i].capacity);
                }
            }
            if (diceExhausted)
                foreach (int i in order)
                    if (cands[i].condFree && cands[i].worth > poolAvg) Take(i, KeepReason.Leftover);
            return res;
        }

        /// <summary>〈呼び戻し〉で捨て札から戻す 1 枚の添字。 <paramref name="prefer"/> の種類 (次のターンの当て) があればその中で点の最大、
        /// 無ければ全体で点の最大 (同点は後に捨てた方 = 添字の大きい方・<c>AutoRunner.RecallBest</c> と同じ)。 空なら −1。</summary>
        public static int RecallIndex(IReadOnlyList<TerminalKind> kinds, IReadOnlyList<float> worths, TerminalKind? prefer)
        {
            int n = kinds?.Count ?? 0;
            int Best(bool onlyPrefer)
            {
                int pick = -1; float best = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (onlyPrefer && kinds[i] != prefer.Value) continue;
                    if (pick < 0 || worths[i] >= best) { pick = i; best = worths[i]; }
                }
                return pick;
            }
            if (prefer.HasValue) { int p = Best(true); if (p >= 0) return p; }
            return Best(false);
        }
    }
}
