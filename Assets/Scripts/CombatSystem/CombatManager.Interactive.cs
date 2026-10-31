using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>人が操作するターンが止まっている判断の種類。</summary>
    public enum TurnPauseKind
    {
        /// <summary>振り直すか (全ダイス・充電を払う)。 <see cref="CombatManager.AnswerReroll"/> で答える。</summary>
        Reroll,
        /// <summary>配線 (ダイス → 端子)。 <see cref="CombatManager.AnswerWiring"/> で答える。</summary>
        Wiring,
    }

    /// <summary>人が操作するターンの「止まっている所」。 画面はこれを読んで出目・予告を出し、 答えを CombatManager へ返す。
    /// 中の配列はターンの途中の値そのもの (コピーではない) なので、 画面からは書き換えないこと。</summary>
    public sealed class TurnPause
    {
        public TurnPauseKind kind;
        /// <summary>今の出目 (添字 = ダイス番号)。</summary>
        public int[] playerDice;
        /// <summary>どの面に止まったか (出目パーツの判定用。 −1 = 面から降りている)。 null あり。</summary>
        public int[] playerFaceIdx;
        /// <summary>予告。 リロール中は敵ダイスの素の合計で組んだ近似 (配線の時に確定値へ組み直される)。</summary>
        public MutualTurnTelegraph tele;
        /// <summary>今の配線 (前ターンのまま・未配置は攻撃)。 null あり。</summary>
        public DiceTerminal[] currentWiring;
        /// <summary>リロールの判断のとき: 振り直すと払う充電。</summary>
        public int rerollCost;

        // ===== 答え (CombatManager が読む) =====
        internal bool reroll;
        internal DiceTerminal[] wiring;

        internal void Set(TurnPauseKind k, int[] dice, int[] faceIdx, MutualTurnTelegraph t, DiceTerminal[] wired)
        {
            kind = k; playerDice = dice; playerFaceIdx = faceIdx; tele = t; currentWiring = wired;
            rerollCost = 0; reroll = false; wiring = null;
        }
    }

    /// <summary>CombatManager: 人が操作するターン。 <see cref="ExecuteTurn"/> (方策が決める) と同じ本体を、 判断の所で止めながら進める。
    ///
    /// <para>使い方: <see cref="BeginInteractiveTurn"/> → <see cref="PendingDecision"/> を読んで画面に出す →
    /// <see cref="AnswerReroll"/> / <see cref="AnswerWiring"/> で答える → 判断が無くなったらターンが解決し、
    /// <see cref="OnTurnEnd"/> が鳴る (結果は <see cref="LastInteractiveResult"/>)。</para></summary>
    public partial class CombatManager
    {
        /// <summary>止まる側の経路か。 イテレーターの中だけが読む (方策の経路では常に false = 一度も止まらない)。</summary>
        private bool _interactiveTurn;
        private IEnumerator<TurnPause> _activeTurn;
        private readonly TurnPause _pause = new TurnPause();
        /// <summary>ターンの本体が最後に書く結果。</summary>
        private TurnResult _stepResult;

        /// <summary>人の判断待ちで止まっているか。</summary>
        public bool IsAwaitingDecision => _activeTurn != null;

        /// <summary>止まっている判断 (無ければ null)。</summary>
        public TurnPause PendingDecision => _activeTurn != null ? _pause : null;

        /// <summary>人が操作したターンの直近の結果。</summary>
        public TurnResult? LastInteractiveResult { get; private set; }

        /// <summary>人が操作するターンを始める。 最初の判断で止まれば true (判断なしで解決したターンなら false)。</summary>
        public bool BeginInteractiveTurn()
        {
            if (!isCombatActive) { Debug.LogError("[CombatManager] No active combat!"); return false; }
            if (_activeTurn != null) return true;
            _interactiveTurn = true;
            _activeTurn = ExecuteTurnMutualSteps();
            return Advance();
        }

        /// <summary>リロールの判断に答える。 true = 振り直す (払えなければ振らずに配線へ進む)。</summary>
        public void AnswerReroll(bool reroll)
        {
            if (_activeTurn == null || _pause.kind != TurnPauseKind.Reroll) return;
            _pause.reroll = reroll;
            Advance();
        }

        /// <summary>配線の判断に答える。 長さが合わなければ今の配線のまま。 接続できない端子は攻撃へ落ちる。</summary>
        public void AnswerWiring(DiceTerminal[] wiring)
        {
            if (_activeTurn == null || _pause.kind != TurnPauseKind.Wiring) return;
            _pause.wiring = wiring;
            Advance();
        }

        /// <summary>次の判断まで進める。 止まれば true、 ターンが解決したら false。</summary>
        private bool Advance()
        {
            if (_activeTurn.MoveNext()) return true;
            _activeTurn = null;
            _interactiveTurn = false;
            LastInteractiveResult = _stepResult;
            return false;
        }

        private static int[] AllDiceIndex(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            return a;
        }
    }
}
