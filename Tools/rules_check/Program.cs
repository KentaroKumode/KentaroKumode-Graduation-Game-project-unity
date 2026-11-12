// 戦闘の規則 (手札の持ち越し・ボスのダウン) の検算。 python Tools/rules_check/run.py
using System;
using System.Collections.Generic;
using System.Linq;
using CombatSystem;

static class T
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }

    /// <summary>2026-11-13 より前の DrawTurn の捨て方 (旧い規則の写し・SplitHand の旧い側と一致するかを見る)。</summary>
    static void OldSplit(List<string> hand0, List<string> board, int perm, bool[] stopped, string retained, List<string> keep, List<string> disc)
    {
        var hand = new List<string>(hand0);
        var held = new List<string>();
        if (stopped != null) for (int s = perm; s < board.Count && s < stopped.Length; s++) if (stopped[s]) held.Add(board[s]);
        foreach (var id in held) if (hand.Remove(id)) keep.Add(id);
        bool kept = false;
        foreach (var id in hand) { if (!kept && retained != null && id == retained) { kept = true; continue; } disc.Add(id); }
        if (kept) keep.Add(retained);
    }

    static TerminalOutcome Fired(params int[] slots) { var o = new TerminalOutcome(); o.fired.AddRange(slots); return o; }

    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var names = new[] { TerminalIds.Attack, TerminalIds.Block, TerminalIds.Charge, TerminalIds.Igniter, TerminalIds.Detonator, TerminalIds.Retainer, TerminalIds.Fuse };

        // ---- 1. 旧い規則: SplitHand(carry=false) は旧い DrawTurn と全件一致 ----
        var rnd = new Random(7); int same = 0, N = 20000;
        for (int t = 0; t < N; t++)
        {
            int perm = rnd.Next(2); var board = new List<string>();
            for (int i = 0; i < perm; i++) board.Add(TerminalIds.Mains);
            int h = 1 + rnd.Next(7);
            for (int i = 0; i < h; i++) board.Add(names[rnd.Next(names.Length)]);
            var hand = board.Skip(perm).ToList();
            if (rnd.Next(3) == 0 && hand.Count > 0) hand.RemoveAt(rnd.Next(hand.Count));   // FinishTurn が使い捨てを外した
            bool[] st = rnd.Next(2) == 0 ? null : Enumerable.Range(0, board.Count).Select(_ => rnd.Next(4) == 0).ToArray();
            string ret = rnd.Next(2) == 0 ? null : names[rnd.Next(names.Length)];
            var k1 = new List<string>(); var d1 = new List<string>(); var k2 = new List<string>(); var d2 = new List<string>();
            OldSplit(hand, board, perm, st, ret, k1, d1);
            TerminalDeckState.SplitHand(hand, board, perm, st, ret, new List<string> { names[0] }, HandCarryMode.Off, k2, d2);
            if (k1.SequenceEqual(k2) && d1.SequenceEqual(d2)) same++;
        }
        Check(same == N, $"旧い規則の分け方が旧い DrawTurn と一致 ({same}/{N})");

        // ---- 2. 持ち越し: 挿した端子だけが捨て札へ・残りは順のまま ----
        {
            var board = new List<string> { TerminalIds.Mains, TerminalIds.Attack, TerminalIds.Block, TerminalIds.Attack, TerminalIds.Charge, TerminalIds.Fuse };
            var used = TerminalDeckState.UsedFromHand(board, 1, Fired(0, 1, 4));   // 常設 (0) は数えない
            Check(used.SequenceEqual(new[] { TerminalIds.Attack, TerminalIds.Charge }), "挿した端子の数え方 (常設は除く)");
            var hand = board.Skip(1).ToList(); var k = new List<string>(); var d = new List<string>();
            TerminalDeckState.SplitHand(hand, board, 1, null, TerminalIds.Fuse, used, HandCarryMode.All, k, d);
            Check(k.SequenceEqual(new[] { TerminalIds.Block, TerminalIds.Attack, TerminalIds.Fuse }) && d.SequenceEqual(new[] { TerminalIds.Attack, TerminalIds.Charge }),
                  "持ち越し: 挿さなかった 3 枚が残り、 挿した 2 枚が捨て札へ");
        }

        // ---- 3. 本体の山札で回す ----
        foreach (bool carry in new[] { false, true })
        {
            GameLoop.GameHooks.HandCarry = carry ? HandCarryMode.All : HandCarryMode.Off; GameLoop.GameRng.Reset();
            var dk = new TerminalDeckState();
            var deck = new List<string> { "攻撃", "攻撃", "防御", "防御", "充電", "定電圧", "ヒューズ", "起爆筒", "検波器", "蓄電池" };
            dk.BeginBattle(deck);
            bool ok = true; int carriedSum = 0;
            for (int turn = 1; turn <= 12; turn++)
            {
                dk.DrawTurn(TerminalCards.HandSize);
                if (dk.hand.Count != TerminalCards.HandSize) ok = false;
                int total = dk.draw.Count + dk.discard.Count + dk.hand.Count + dk.permanent.Count + dk.exiled.Count;
                if (total != deck.Count) ok = false;
                carriedSum += dk.LastCarriedCount;
                // 手札の先頭 2 枚を挿したことにする
                dk.FinishTurn(Fired(dk.permanent.Count, dk.permanent.Count + 1));
            }
            Check(ok, (carry ? "持ち越し" : "旧い規則") + ": 毎ターン手札 5 枚・端子の総数は変わらない");
            Check(carry ? carriedSum == 11 * 3 : carriedSum == 0, (carry ? "持ち越し" : "旧い規則") + $": 持ち越した枚数の合計 {carriedSum} (持ち越しは 2 ターン目から毎ターン 3 枚)");
        }
        {
            GameLoop.GameHooks.HandCarry = HandCarryMode.All;
            var dk = new TerminalDeckState();
            dk.BeginBattle(new List<string> { "攻撃", "攻撃", "防御", "防御", "充電", "定電圧", "ヒューズ", "蓄電池" });
            dk.DrawTurn(TerminalCards.HandSize + 2);   // 〈配電盤〉の分で 7 枚
            dk.FinishTurn(Fired(0));
            dk.DrawTurn(TerminalCards.HandSize);
            Check(dk.hand.Count == 6 && dk.LastDrawnCount == 0, $"規定の枚数を超えて残った手札は捨てない・引かない (手札 {dk.hand.Count}・引いた {dk.LastDrawnCount})");
            var rb = new List<string> { TerminalIds.Retainer, TerminalIds.Attack };
            var ro = TerminalCards.Resolve(rb, new[] { 0 }, new[] { 4 }, new TerminalEnv(), new TerminalBattleState());
            Check(ro.retainTarget == -1 && ro.recall && ro.blockAdd == 4, "持ち越しの規則の〈呼び戻し〉: 場の端子は対象に取らず、 出目をブロックに入れて捨て札から戻す");
            // 戻す 1 枚: 選んだ添字 / 選ばなければ最後に捨てた 1 枚 / 空なら効果なし
            var disc = new List<string> { "攻撃", "定電圧", "防御" };
            Check(TerminalDeckState.RecallPick(disc, 1) == "定電圧" && TerminalDeckState.RecallPick(disc, -1) == "防御" && TerminalDeckState.RecallPick(disc, 9) == "防御"
                  && TerminalDeckState.RecallPick(new List<string>(), 0) == null, "〈呼び戻し〉の戻す 1 枚の選び方");
            {
                var dk2 = new TerminalDeckState();
                dk2.BeginBattle(new List<string> { "攻撃", "攻撃", "防御", "防御", "充電", "定電圧", "ヒューズ", "蓄電池", "検波器", "治癒", "接地" });
                dk2.DrawTurn(TerminalCards.HandSize);
                dk2.FinishTurn(Fired(0, 1, 2));
                dk2.DrawTurn(TerminalCards.HandSize);          // 3 枚が捨て札へ
                string want = dk2.discard[0];
                int discBefore = dk2.discard.Count;
                dk2.recallNext = TerminalDeckState.RecallPick(dk2.discard, 0);
                dk2.FinishTurn(Fired(0));
                dk2.DrawTurn(TerminalCards.HandSize);
                Check(dk2.hand.Count == TerminalCards.HandSize && dk2.hand.Contains(want) && dk2.LastDrawnCount == 0 && dk2.discard.Count == discBefore,
                      $"戻した端子は手札の枚数に数える (引いた {dk2.LastDrawnCount} 枚・捨て札 {discBefore}→{dk2.discard.Count})");
                var keep = new List<string> { "a", "b", "c", "d", "e" }; var d3 = new List<string> { "x" };
                Check(!TerminalDeckState.ApplyRecall("x", keep, d3, 5) && d3.Count == 1 && keep.Count == 5, "手札が規定の枚数なら戻さない (上限を超えない)");
            }
            GameLoop.GameHooks.HandCarry = HandCarryMode.Off;
            Check(TerminalCards.Resolve(rb, new[] { 0 }, new[] { 4 }, new TerminalEnv(), new TerminalBattleState()).retainTarget == 1,
                  "旧い規則では〈呼び戻し〉が空けた端子を残す");
            GameLoop.GameHooks.HandCarry = HandCarryMode.All;
        }

        // ---- 3b. 手札の規則 Pick (残す印) ----
        {
            // 何も残さない Pick は Off と同じ: 同じ山札を同じ順で回し、 毎ターンの手札・捨て札・乱数の回数が一致する
            string Run(HandCarryMode mode, bool markFirstUnplugged, out int shuffles, out int keptTotal)
            {
                GameLoop.GameHooks.HandCarry = mode; GameLoop.GameRng.Reset();
                var dk = new TerminalDeckState();
                var deck = new List<string> { "攻撃", "攻撃", "防御", "防御", "充電", "定電圧", "ヒューズ", "起爆筒", "検波器", "蓄電池", "治癒" };
                dk.BeginBattle(deck);
                var log = new System.Text.StringBuilder(); keptTotal = 0; bool ok = true;
                for (int turn = 1; turn <= 15; turn++)
                {
                    dk.DrawTurn(TerminalCards.HandSize);
                    if (dk.hand.Count != TerminalCards.HandSize) ok = false;
                    if (dk.draw.Count + dk.discard.Count + dk.hand.Count + dk.permanent.Count + dk.exiled.Count != deck.Count) ok = false;
                    keptTotal += dk.LastKept.Count;
                    log.Append(string.Join(",", dk.hand)).Append('/').Append(string.Join(",", dk.discard)).Append(';');
                    var o = Fired(dk.permanent.Count, dk.permanent.Count + 1);   // 手札の先頭 2 枚を挿した
                    if (markFirstUnplugged)
                    {
                        var marks = new bool[dk.board.Count]; marks[dk.permanent.Count + 2] = true;
                        dk.keepNext.AddRange(TerminalDeckState.KeepFromMarks(dk.board, dk.permanent.Count, o, marks));
                    }
                    dk.FinishTurn(o);
                }
                shuffles = GameLoop.GameRng.Count("terminal.shuffle");
                return ok ? log.ToString() : "NG";
            }
            string a = Run(HandCarryMode.Off, false, out int shA, out _);
            string b = Run(HandCarryMode.Pick, false, out int shB, out _);
            Check(a != "NG" && a == b && shA == shB, $"Pick で何も残さない時は Off と同じ (手札・捨て札・切る乱数 {shA}/{shB} 回)");
            string c = Run(HandCarryMode.Pick, true, out _, out int kept);
            Check(c != "NG" && kept == 14, $"Pick: 印を付けた端子だけ残る (2 ターン目から毎ターン 1 枚・計 {kept})・手札 5 枚と端子の総数は保たれる");
            // 挿した端子・止まった端子には印が付いても残さない
            var bd = new List<string> { "攻撃", "防御", "充電" };
            var o2 = Fired(0); o2.stopped = new[] { false, true, false };
            var km = TerminalDeckState.KeepFromMarks(bd, 0, o2, new[] { true, true, true });
            Check(km.Count == 1 && km[0] == "充電", "挿した端子・止まった端子には残す印が効かない");
            // 〈呼び戻し〉と重なった時: 印で残した分が先・手札が規定の枚数に満ちたら戻さない
            GameLoop.GameHooks.HandCarry = HandCarryMode.Pick;
            var keep = new List<string> { "攻撃", "防御", "充電", "定電圧" }; var disc = new List<string> { "ヒューズ", "蓄電池" };
            bool r1 = TerminalDeckState.ApplyRecall("蓄電池", keep, disc, 5);
            bool r2 = TerminalDeckState.ApplyRecall("ヒューズ", keep, disc, 5);
            Check(r1 && !r2 && keep.Count == 5 && disc.Count == 1, "印で残した分の後に〈呼び戻し〉・手札は規定の枚数を超えない");
            GameLoop.GameHooks.HandCarry = HandCarryMode.All;
        }

        // ---- 4. ボスのダウン ----
        {
            var n = new[] { 0.75f, 0.5f, 0.25f };
            Check(BossDown.NotchHp(1000, 0.75f) == 750 && BossDown.NotchHp(505, 0.66f) == 333, "目盛りの HP (切り捨て)");
            var st = new BossDownState(); st.Reset(n);
            Check(st.NoteTurnEnd(800, 1000) == 0 && !st.BeginTurn(), "目盛りの手前ではダウンしない");
            Check(st.NoteTurnEnd(750, 1000) == 1 && st.BeginTurn() && st.pending == 0, "ちょうど目盛りで割れ、 次のターンがダウン");
            Check(st.NoteTurnEnd(700, 1000) == 0 && !st.BeginTurn(), "割った目盛りはもう数えない");
            Check(st.NoteTurnEnd(200, 1000) == 2 && st.BeginTurn() && st.BeginTurn() && !st.BeginTurn(), "1 ターンで 2 つ割ると 2 ターン続けてダウン");
            var s2 = new BossDownState(); s2.Reset(n);
            s2.NoteTurnEnd(740, 1000); s2.BeginTurn();
            s2.NoteTurnEnd(490, 1000);
            Check(s2.BeginTurn(), "ダウン中に次の目盛りを割ると、 続けてもう 1 ターン");
            Check(s2.NoteTurnEnd(0, 1000) == 0, "倒れたターンは数えない");
            Check(BossDown.NextTurnIsDown(n, 1000, 0, 0, 750) && !BossDown.NextTurnIsDown(n, 1000, 0, 0, 751) && BossDown.NextTurnIsDown(n, 1000, 1, 1, 740),
                  "次のターンがダウンかの判定 (BOT と本体の共通の関数)");
            Check(BossDown.ApplyTaken(10, true) == 15 && BossDown.ApplyTaken(7, true) == 10 && BossDown.ApplyTaken(7, false) == 7 && BossDown.ApplyTaken(0, true) == 0,
                  "受けるダメージ ×1.5 (切り捨て)");
            var s3 = new BossDownState(); s3.Reset(null);
            Check(!s3.Enabled && s3.NoteTurnEnd(1, 1000) == 0 && !s3.BeginTurn(), "目盛りの無い敵はダウンしない");
        }

        // ---- 5. 端子の旧 id (〈保持器〉→〈呼び戻し〉) ----
        {
            var deckOld = new List<string> { "攻撃", "保持器", "防御" };
            TerminalCards.NormalizeIds(deckOld);
            Check(TerminalCards.Get("保持器")?.id == TerminalIds.Retainer && TerminalIds.Retainer == "呼び戻し" && deckOld[1] == "呼び戻し" && deckOld[0] == "攻撃",
                  "旧 id「保持器」は〈呼び戻し〉に解決し、 山札の名前も揃う");
        }

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
