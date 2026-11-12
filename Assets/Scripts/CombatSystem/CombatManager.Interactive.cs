using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>人が操作するターンが止まっている判断の種類。</summary>
    public enum TurnPauseKind
    {
        /// <summary>振り直すか (全ダイス・充電を払う)。 <see cref="CombatManager.AnswerReroll"/> で答える。</summary>
        Reroll,
        /// <summary>配線 (出目 → 場の端子)。 <see cref="CombatManager.AnswerWiringSlots"/> で答える。</summary>
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
        /// <summary>リロールの判断のとき: 振り直すと払う充電。</summary>
        public int rerollCost;

        /// <summary>端子の山札 (ADR-0014): 場の端子 (常設 + 手札) の名前。 振り直しの判断の時も配線の時も入っている
        /// (手札は振り直しでは変わらない・規則 6)。 書き換えないこと。</summary>
        public List<string> board;
        /// <summary>端子の見積もりに要る盤面 (画面が挿し方の結果を先に見せるのに使う)。 振り直しの判断の時は、 予告の近似の敵の攻撃で組む。</summary>
        public TerminalEnv terminalEnv;
        /// <summary>端子の戦闘中の状態 (溜まり・連続)。 見積もりは Clone した写しに掛けること。</summary>
        public TerminalBattleState terminalState;
        /// <summary>挑戦カード〈猫の手〉が先に挿した 1 組 (出目の添字 → 場の添字)。 配線の判断の時だけ入る・−1 = 無し。
        /// 答えの挿し方にこの組が無くても、 本体が入れ直す (画面はこの組を外せないように見せる)。</summary>
        public int catDie = -1, catSlot = -1;

        // ===== 答え (CombatManager が読む) =====
        internal bool reroll;
        internal int[] slots;
        /// <summary>〈写し取り〉〈焼き切り〉〈保持器〉の対象 (添字 = その端子の場の位置・値 = 対象の場の位置・−1 = 選ばない)。 null あり。</summary>
        internal int[] targets;
        /// <summary>挑戦カード〈金で殴る〉(#6) で払う金額 (G)。 0 = 払わない。 画面はまだこの答えを出さない (#6 の残り)。</summary>
        internal int goldSpend;

        internal void Set(TurnPauseKind k, int[] dice, int[] faceIdx, MutualTurnTelegraph t)
        {
            kind = k; playerDice = dice; playerFaceIdx = faceIdx; tele = t;
            rerollCost = 0; reroll = false; slots = null; targets = null; goldSpend = 0; board = null; terminalEnv = default; terminalState = null;
            catDie = catSlot = -1;
        }
    }

    /// <summary>CombatManager: 人が操作するターン。 <see cref="ExecuteTurn"/> (方策が決める) と同じ本体を、 判断の所で止めながら進める。
    ///
    /// <para>使い方: <see cref="BeginInteractiveTurn"/> → <see cref="PendingDecision"/> を読んで画面に出す →
    /// <see cref="AnswerReroll"/> / <see cref="AnswerWiringSlots"/> で答える → 判断が無くなったらターンが解決し、
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

        /// <summary>端子の山札 (ADR-0014) の配線に答える。 出目ごとの挿し先 (場の端子の添字・−1 = 挿さない)。 規則に合わない挿し方は挿さなかった扱いになる。
        /// targets = 〈写し取り〉〈焼き切り〉〈保持器〉の対象 (添字 = その端子の場の位置・値 = 対象の場の位置)。 null / −1 / 候補でない対象は左端の候補。
        /// goldSpend = 挑戦カード〈金で殴る〉で払う金額 (G・攻撃するターンだけ所持金まで払う・札が無ければ無視)。</summary>
        public void AnswerWiringSlots(int[] slots, int[] targets = null, int goldSpend = 0)
        {
            if (_activeTurn == null || _pause.kind != TurnPauseKind.Wiring) return;
            _pause.slots = slots; _pause.targets = targets; _pause.goldSpend = goldSpend;
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
