namespace MapSystem.FreeMove
{
    /// <summary>
    /// 深さ 6 (画面の第 4 層・灰燼の王の層) の「裂け目の記録」を、 いつ固定で出すかの規則 (2026-11-13・docs/GAME.md §4-2)。
    /// 本体 (GameManager) と検算 (Tools/freemap_check) が同じ式を使うように、 UnityEngine に依存しない形でここに置く。
    ///
    /// <para><b>今の作り</b>: 終点 (ボス) に道で隣り合う点は全部イベントマス (<see cref="FreeMapLayout.riftGate"/>)。 そのどれかを最初に踏んだ時に固定で起き、
    /// 済んだ後 (最後の頁を開いた / 手記を閉じた) に別の隣り合う点を踏むと、 通常のイベントマスとして抽選から 1 つ出る。
    /// 終点へは必ず隣り合う点を通るので、 終点に着いた時に未発生なら (地図の作りの穴・安全弁) ボス戦の前に出す。</para>
    /// <para><b>旧い作り</b> (<c>riftRecordAtBoss = False</c>): 層の 3 分の 1 あたりの固定の点 1 つ (<see cref="FreeMapLayout.fixedEvent"/>)。 踏まずにボスへ行けた。</para>
    /// </summary>
    public static class RiftGate
    {
        /// <summary>「裂け目の記録」のある深さ (画面の第 4 層)。</summary>
        public const int Depth = 6;

        /// <summary>イベントマスを起動した時 (または終点の安全弁で呼んだ時)、 抽選の代わりに「裂け目の記録」を出すか。
        /// <paramref name="freeMap"/> は点が航行マップの点か (点が無い時は false)。 <paramref name="seen"/> はこのランで選択を済ませたか。
        /// <paramref name="hasResolve"/> / <paramref name="hasTruth"/> は〈決意〉以上 / 〈真理〉を持っているか。</summary>
        public static bool ForcesRecord(int depth, bool freeMap, bool isFixedEvent, bool isRiftGate, bool atGoalValve,
                                        bool seen, bool hasResolve, bool hasTruth)
            => depth == Depth
               && (!freeMap || isFixedEvent || ((isRiftGate || atGoalValve) && !seen))
               && hasResolve && !hasTruth;

        /// <summary>終点 (ボス) に着いた時、 ボス戦の前に「裂け目の記録」を出す安全弁が要るか。 今の作りの層 (隣り合う点がある層) だけで働く。
        /// 終点へは必ず隣り合う点を通るので、 働いたら地図の作りに穴がある (検算で 0 を確かめる)。</summary>
        public static bool ValveNeeded(int depth, bool suppressEvents, FreeMapLayout layout, bool seen, bool hasResolve, bool hasTruth)
            => depth == Depth && !suppressEvents && layout != null && layout.riftGate.Count > 0
               && hasResolve && !hasTruth && !seen;
    }
}
