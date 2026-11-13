using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>[計装 2026-11-12] 端子の山札 (ADR-0014) の端子ごとの使われ方。
    ///
    /// <para><b>なぜ要るか。</b> 端子ごとの効き目を ITT で測る前に、 「弱い」と「BOT が使っていない」を分けるため。
    /// 撃破のボーナスの 4 種 (収穫機・研ぎ澄まし・戦利品箱・剥ぎ取り) と対象を取る 3 種 (写し取り・焼き切り・呼び戻し) は、
    /// BOT の方策が手書きの規則なので、 使われているかを数で確かめる (#102)。</para>
    ///
    /// <para>[計装 2026-11-13・#106] 報酬で提示された回数・取った回数・ランの終わりの山札・山札から抜けた回数と、
    /// 場に出たターンの内訳 (止まっていた / 挿さなかった / 挿したが不発 / 発動) を足した。 <b>数えるだけ</b> ── 乱数を引かず、 判断を変えない。</para>
    ///
    /// <para>AutoRunner がバッチ中にログを切るので、 静的カウンタで持ち、 ワーカーの応答に載せる (<see cref="GuardDiag"/> と同じ)。</para></summary>
    public static class TerminalDiag
    {
        /// <summary>列: [0] その端子が山札にあった戦闘数 / [1] 場に出たターン数 (手札 + 場に残る) / [2] 発動したターン数 /
        /// [3] 撃破のボーナスが入った回数 / [4] 写し取りの対象 / [5] 焼き切りの対象 / [6] 呼び戻しの対象 /
        /// [7] 旧い条件 (攻撃の端子に挿したターンの、 主攻撃までの撃破だけ) では入らない撃破 (#117: 攻撃なしのターン・ターン終了時の撃破)。
        /// [7] は旧い条件の腕 (GameHooks.TerminalKillBonusNeedsAttack) でも数える (= その腕で取りこぼした回数)。 新しい条件では [3] に含まれる。
        /// <para>[#106] 場に出たターン [1] の内訳 (1 枚 1 ターンごとに 1 つだけに入る・[1] = [9] + [10] + [11] + [2]):
        /// [9] 止まっていた (ヴェスカ〈停滞する時間〉) / [10] 出目を 1 つも挿さなかった / [11] 挿したが発動しなかった (条件・充電で外れた) / [2] 発動した。
        /// [8] 引いた回数 (ターンの始めに山から新しく引いた枚数・持ち越しで残った分は数えない・常設も引いた時に 1)。</para>
        /// <para>[#106] 報酬: [12〜15] 提示された回数 (通常 / エリート / ボス / 店) / [16〜19] 取った回数 (同じ順・店は買った回数) /
        /// [20] 提示された場面で何も取られなかった回数 (報酬の見送り・店で端子を 1 枚も買わなかった来店) /
        /// [21] ランの終わりに山札に 1 枚以上あったラン数 / [22] ランの終わりの山札の枚数の合計 /
        /// [23] 店で抜かれた / [24] 休憩で抜かれた / [25] 使ってランの山札から抜けた (〈応急箱〉・測定の口 terminalFirstAidLeavesRunDeck の時だけ・既定では 0)。</para></summary>
        public const int Width = 26;
        public const int InDeck = 0, OnBoard = 1, Fired = 2, KillBonus = 3, AsCopy = 4, AsBurn = 5, AsRetain = 6, KillNewOnly = 7;
        public const int Drawn = 8, Stopped = 9, NotPlugged = 10, PluggedNoFire = 11;
        public const int OfferedBase = 12, TakenBase = 16, AllPassed = 20, RunsInDeck = 21, EndCopies = 22, RemovedShop = 23, RemovedRest = 24, LeftRunDeck = 25;

        /// <summary>報酬の場面 (列 [12〜19] の並び・<see cref="Scenes"/> の並び)。</summary>
        public const int SceneNormal = 0, SceneElite = 1, SceneBoss = 2, SceneShop = 3, SceneCount = 4;

        public static readonly Dictionary<string, long[]> Rows = new Dictionary<string, long[]>();

        /// <summary>[#106] 場面ごと: [s × 2] 提示した回数 (報酬の提示・店の来店) / [s × 2 + 1] 何も取らなかった回数。 [8] = 終わったラン数。</summary>
        public static readonly long[] Scenes = new long[SceneCount * 2 + 1];

        /// <summary>[#106] 「提示されたのに取られなかった端子 → 同じ提示で代わりに取られた端子」の組の回数 (鍵 = 取られなかった + "\t" + 取った)。
        /// 何も取らなかった提示は数えない ([20] の方)。</summary>
        public static readonly Dictionary<string, long> Pairs = new Dictionary<string, long>();

        public static void Note(string id, int col)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!Rows.TryGetValue(id, out var r)) Rows[id] = r = new long[Width];
            r[col]++;
        }

        private static void Add(string id, int col, long n)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!Rows.TryGetValue(id, out var r)) Rows[id] = r = new long[Width];
            r[col] += n;
        }

        /// <summary>戦闘の始め: 山札にある端子を 1 種につき 1 回数える。</summary>
        public static void NoteBattle(List<string> runDeck)
        {
            if (runDeck == null) return;
            var seen = new HashSet<string>();
            foreach (var id in runDeck) if (seen.Add(id)) Note(id, InDeck);
        }

        /// <summary>[#106] ターンの始めに山から新しく引いた端子。</summary>
        public static void NoteDrawn(List<string> drawn)
        {
            if (drawn == null) return;
            foreach (var id in drawn) Note(id, Drawn);
        }

        /// <summary>ターンの結果: 場に出た端子・発動した端子・対象に選ばれた端子。
        /// [#106] <paramref name="wanted"/> = 方策 / 画面が返した挿し方 (Resolve が直す前・〈猫の手〉込み)。 null なら内訳は数えない。</summary>
        public static void NoteTurn(List<string> board, TerminalOutcome o, int[] wanted = null)
        {
            if (board == null) return;
            foreach (var id in board) Note(id, OnBoard);
            if (o == null) return;
            foreach (int s in o.fired) if (s >= 0 && s < board.Count) Note(board[s], Fired);
            if (o.copyTarget >= 0 && o.copyTarget < board.Count) Note(board[o.copyTarget], AsCopy);
            if (o.burnTarget >= 0 && o.burnTarget < board.Count) Note(board[o.burnTarget], AsBurn);
            if (o.retainTarget >= 0 && o.retainTarget < board.Count) Note(board[o.retainTarget], AsRetain);
            if (wanted == null) return;
            for (int s = 0; s < board.Count; s++)
            {
                if (o.fired.Contains(s)) continue;
                if (o.stopped != null && s < o.stopped.Length && o.stopped[s]) { Note(board[s], Stopped); continue; }
                bool plugged = false;
                foreach (int w in wanted) if (w == s) { plugged = true; break; }
                Note(board[s], plugged ? PluggedNoFire : NotPlugged);
            }
        }

        /// <summary>旧い条件では入らない撃破 (列 [7]) を、 発動していた撃破のボーナスの端子ごとに数える。</summary>
        public static void NoteKillNewOnly(bool harvester, bool whet, bool loot, bool stripper)
        {
            if (harvester) Note(TerminalIds.Harvester, KillNewOnly);
            if (whet) Note(TerminalIds.Whet, KillNewOnly);
            if (loot) Note(TerminalIds.LootBox, KillNewOnly);
            if (stripper) Note(TerminalIds.Stripper, KillNewOnly);
        }

        // ── [#106] 報酬と店 ──
        //   戦闘後の報酬は 提示 (NoteOffer) → 選んだ (NoteResolve) の順で必ず閉じる。
        //   店の棚は来店ごとに提示 (NoteShopShelf) → 買った (NoteShopBuy) を溜め、 次の提示・ランの終わりで閉じる (FlushShop)。

        private static int _pendingScene = -1;
        private static readonly List<string> _shopOffered = new List<string>();
        private static readonly List<string> _shopBought = new List<string>();
        private static bool _shopOpen;

        /// <summary>戦闘後の報酬を提示した (場面 = <see cref="SceneNormal"/> など)。</summary>
        public static void NoteOffer(int scene, List<string> offer)
        {
            FlushShop();
            if (offer == null || offer.Count == 0 || scene < 0 || scene >= SceneCount) { _pendingScene = -1; return; }
            _pendingScene = scene;
            Scenes[scene * 2]++;
            foreach (var id in offer) Note(id, OfferedBase + scene);
        }

        /// <summary>提示した報酬から <paramref name="index"/> 番目を取った (負 = 見送り)。 NoteOffer の後に 1 回。</summary>
        public static void NoteResolve(List<string> offer, int index)
        {
            int scene = _pendingScene; _pendingScene = -1;
            if (scene < 0 || offer == null) return;
            if (index >= 0 && index < offer.Count)
            {
                string taken = offer[index];
                Note(taken, TakenBase + scene);
                for (int i = 0; i < offer.Count; i++)
                    if (i != index && offer[i] != taken) NotePair(offer[i], taken);
            }
            else
            {
                Scenes[scene * 2 + 1]++;
                foreach (var id in offer) Note(id, AllPassed);
            }
        }

        /// <summary>店の端子の棚を作った (来店 1 回)。</summary>
        public static void NoteShopShelf(List<string> shelf)
        {
            FlushShop();
            if (shelf == null || shelf.Count == 0) return;
            _shopOpen = true;
            Scenes[SceneShop * 2]++;
            _shopOffered.AddRange(shelf);
            foreach (var id in shelf) Note(id, OfferedBase + SceneShop);
        }

        /// <summary>店で端子を買った。</summary>
        public static void NoteShopBuy(string id)
        {
            Note(id, TakenBase + SceneShop);
            if (_shopOpen) _shopBought.Add(id);
        }

        /// <summary>開いている来店を閉じる (買わなかった端子 × 買った端子の組・1 枚も買わなかった来店)。</summary>
        public static void FlushShop()
        {
            if (!_shopOpen) return;
            _shopOpen = false;
            if (_shopBought.Count == 0)
            {
                Scenes[SceneShop * 2 + 1]++;
                foreach (var id in _shopOffered) Note(id, AllPassed);
            }
            else
            {
                var left = new List<string>(_shopOffered);
                foreach (var b in _shopBought) left.Remove(b);
                foreach (var n in left)
                    foreach (var b in _shopBought)
                        if (n != b) NotePair(n, b);
            }
            _shopOffered.Clear(); _shopBought.Clear();
        }

        private static void NotePair(string notTaken, string taken)
        {
            string k = notTaken + "\t" + taken;
            Pairs.TryGetValue(k, out long c); Pairs[k] = c + 1;
        }

        /// <summary>山札から抜いた (店 <see cref="RemovedShop"/> / 休憩 <see cref="RemovedRest"/> / 使って抜けた <see cref="LeftRunDeck"/>)。</summary>
        public static void NoteRemoved(string id, int col) => Note(id, col);

        /// <summary>ランの終わり (RunState.EndRun): 山札の中身。</summary>
        public static void NoteRunEnd(List<string> deck)
        {
            FlushShop();
            _pendingScene = -1;
            Scenes[SceneCount * 2]++;
            if (deck == null) return;
            var cnt = new Dictionary<string, int>();
            foreach (var id in deck) { if (string.IsNullOrEmpty(id)) continue; cnt.TryGetValue(id, out int c); cnt[id] = c + 1; }
            foreach (var kv in cnt) { Note(kv.Key, RunsInDeck); Add(kv.Key, EndCopies, kv.Value); }
        }

        public static void Reset()
        {
            Rows.Clear(); Pairs.Clear();
            System.Array.Clear(Scenes, 0, Scenes.Length);
            _pendingScene = -1; _shopOpen = false; _shopOffered.Clear(); _shopBought.Clear();
        }
    }
}
