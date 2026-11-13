namespace MapSystem.FreeMove
{
    /// <summary>
    /// 深さ 6 (画面の第 4 層・灰燼の王の層) の〈灰に埋もれた基地〉を、 いつ出すかの規則 (2026-11-13・docs/GAME.md §4-2)。
    /// 本体 (GameManager) と検算 (Tools/freemap_check) が同じ式を使うように、 UnityEngine に依存しない形でここに置く。
    ///
    /// <para><b>深さ 6 で最初に踏んだイベントマス</b>が、 その点の通常のイベントの抽選の代わりに基地になる (点は決め打ちしない ── どのイベントマスでもよい)。
    /// 2 つ目以降のイベントマスは通常の抽選。 イベントマスを 1 つも踏まずに終点 (ボス) に着いた時は、 ボス戦の前に基地を出し、 閉じたらそのままボス戦へ入る (安全弁)。
    /// 基地は 1 ランに 1 回だけ (seen = このランで基地の選択を済ませた)。</para>
    /// <para>中身 (はじめて / 灰燼の王を倒したことがある) はランの外の記録で変わる ── 選ぶのは本体 (<c>GameLoop.AshBaseEvent</c>)。</para>
    /// </summary>
    public static class AshBase
    {
        /// <summary>基地のある深さ (画面の第 4 層)。</summary>
        public const int Depth = 6;

        /// <summary>イベントマスを起動した時 (または終点の安全弁で呼んだ時)、 抽選の代わりに基地を出すか。
        /// <paramref name="suppressEvents"/> = イベントなしの測定 (基地も安全弁も出さない)。 <paramref name="seen"/> = このランで基地を済ませた。
        /// <paramref name="hasResolve"/> / <paramref name="hasTruth"/> = 〈決意〉以上 / 〈真理〉を持っているか
        /// (〈真理〉を持って入った層 = イベントなしの測定の配り物 では出さない)。</summary>
        public static bool Forces(int depth, bool suppressEvents, bool seen, bool hasResolve, bool hasTruth)
            => depth == Depth && !suppressEvents && !seen && hasResolve && !hasTruth;
    }
}
