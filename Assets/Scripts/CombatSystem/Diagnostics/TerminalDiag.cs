using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>[計装 2026-11-12] 端子の山札 (ADR-0014) の端子ごとの使われ方。
    ///
    /// <para><b>なぜ要るか。</b> 端子ごとの効き目を ITT で測る前に、 「弱い」と「BOT が使っていない」を分けるため。
    /// 撃破のボーナスの 4 種 (収穫機・研ぎ澄まし・戦利品箱・剥ぎ取り) と対象を取る 3 種 (写し取り・焼き切り・保持器) は、
    /// BOT の方策が手書きの規則なので、 使われているかを数で確かめる (#102)。</para>
    ///
    /// <para>AutoRunner がバッチ中にログを切るので、 静的カウンタで持ち、 ワーカーの応答に載せる (<see cref="GuardDiag"/> と同じ)。</para></summary>
    public static class TerminalDiag
    {
        /// <summary>列: [0] その端子が山札にあった戦闘数 / [1] 場に出たターン数 (手札 + 場に残る) / [2] 発動したターン数 /
        /// [3] 撃破のボーナスが入った回数 / [4] 写し取りの対象 / [5] 焼き切りの対象 / [6] 保持器の対象。</summary>
        public const int Width = 7;
        public const int InDeck = 0, OnBoard = 1, Fired = 2, KillBonus = 3, AsCopy = 4, AsBurn = 5, AsRetain = 6;

        public static readonly Dictionary<string, long[]> Rows = new Dictionary<string, long[]>();

        public static void Note(string id, int col)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!Rows.TryGetValue(id, out var r)) Rows[id] = r = new long[Width];
            r[col]++;
        }

        /// <summary>戦闘の始め: 山札にある端子を 1 種につき 1 回数える。</summary>
        public static void NoteBattle(List<string> runDeck)
        {
            if (runDeck == null) return;
            var seen = new HashSet<string>();
            foreach (var id in runDeck) if (seen.Add(id)) Note(id, InDeck);
        }

        /// <summary>ターンの結果: 場に出た端子・発動した端子・対象に選ばれた端子。</summary>
        public static void NoteTurn(List<string> board, TerminalOutcome o)
        {
            if (board == null) return;
            foreach (var id in board) Note(id, OnBoard);
            if (o == null) return;
            foreach (int s in o.fired) if (s >= 0 && s < board.Count) Note(board[s], Fired);
            if (o.copyTarget >= 0 && o.copyTarget < board.Count) Note(board[o.copyTarget], AsCopy);
            if (o.burnTarget >= 0 && o.burnTarget < board.Count) Note(board[o.burnTarget], AsBurn);
            if (o.retainTarget >= 0 && o.retainTarget < board.Count) Note(board[o.retainTarget], AsRetain);
        }

        public static void Reset() => Rows.Clear();
    }
}
