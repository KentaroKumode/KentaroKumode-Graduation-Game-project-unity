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

        // ---- 4b. 博士の「止まらない時計」(2026-11-13): ヴェスカの各段に目盛り 0.80 を 1 つ足す ----
        //   段ごとの目盛りと 0.80 は BossParams (UnityEngine 依存でここではコンパイルしない) の写し。 BossParams.DownNotches(id, true) は
        //   ヴェスカの段でだけ BossDown.WithExtraNotch(表の値, VescaClockNotch) を返し、 ダウンを止めた口 (bossDown=False) では表の値が null。
        //   「時計あり」= 秘宝〈止まらない時計〉を持っている (VescaClock.Held = ownedPassiveItems・2026-11-13 にフラグから秘宝へ)。
        //   持ち方 (items.json・除外の集合) の検算は Tools/event_branch_check の「時計:」の行。
        {
            const float clock = 0.80f;
            var stages = new[] { ("p1", new[] { 0.60f }), ("p2", new[] { 0.50f }), ("p3", new[] { 0.40f }), ("最終", new[] { 0.50f }) };
            const int maxHp = 2000;
            foreach (var (name, baseN) in stages)
            {
                var withClock = BossDown.WithExtraNotch(baseN, clock);
                Check(withClock.Length == baseN.Length + 1 && withClock[0] == clock && withClock[1] == baseN[0] && baseN.Length == 1 && baseN[0] != clock,
                      $"時計あり {name}: 目盛りが 1 つ増え、 80% が先頭 (大きい順)・元の表は書き換えない");
                // 時計あり: 80% を下回ったターンの次のターンにダウン
                var a = new BossDownState(); a.Reset(withClock);
                bool before = a.NoteTurnEnd(BossDown.NotchHp(maxHp, clock) + 1, maxHp) == 0 && !a.BeginTurn();
                bool hit = a.NoteTurnEnd(BossDown.NotchHp(maxHp, clock) - 1, maxHp) == 1 && a.BeginTurn() && a.pending == 0;
                bool next = a.NoteTurnEnd(BossDown.NotchHp(maxHp, baseN[0]), maxHp) == 1 && a.BeginTurn();
                Check(before && hit && next, $"時計あり {name}: 80% の手前ではダウンせず・下回った次のターンにダウン・元の目盛りでももう一度ダウン");
                // 時計なし: 今までと同じ (80% を下回ってもダウンしない・元の目盛りで 1 回)
                var b = new BossDownState(); b.Reset(baseN);
                Check(b.NoteTurnEnd(BossDown.NotchHp(maxHp, clock) - 1, maxHp) == 0 && !b.BeginTurn()
                      && b.NoteTurnEnd(BossDown.NotchHp(maxHp, baseN[0]), maxHp) == 1 && b.BeginTurn() && b.notches.Length == 1,
                      $"時計なし {name}: 今までと同じ (80% では何も起きない)");
            }
            // bossDown=False (目盛りの表が null): 時計があっても何も足さない
            var off = BossDown.WithExtraNotch(null, clock);
            var c = new BossDownState(); c.Reset(off);
            Check(off == null && !c.Enabled && c.NoteTurnEnd(1, maxHp) == 0 && !c.BeginTurn(), "bossDown=False: 時計があっても目盛りは生えず、 ダウンしない");
            // 足す位置: 大きい順を保つ (途中・末尾)
            var mid = BossDown.WithExtraNotch(new[] { 0.9f, 0.5f }, clock);
            var tail = BossDown.WithExtraNotch(new[] { 0.9f, 0.85f }, clock);
            Check(mid.Length == 3 && mid[0] == 0.9f && mid[1] == clock && mid[2] == 0.5f && tail[2] == clock, "足す目盛りは大きい順の位置に入る");
        }

        // ---- 5. 端子の旧 id (対応表は 2026-11-14 に削除): 旧い名前は無い端子・山札から外れる ----
        {
            var deckOld = new List<string> { "攻撃", "保持器", "防御" };
            int dropped = TerminalCards.DropUnknownIds(deckOld);
            var deckNow = TerminalCards.StartingDeck(); deckNow.Add(TerminalIds.Retainer);
            int keptNow = deckNow.Count;
            Check(TerminalCards.Get("保持器") == null && TerminalCards.Get(TerminalIds.Retainer) != null
                  && dropped == 1 && deckOld.Count == 2 && deckOld[0] == "攻撃" && deckOld[1] == "防御"
                  && TerminalCards.DropUnknownIds(deckNow) == 0 && deckNow.Count == keptNow,
                  "旧 id「保持器」は無い端子 (解決しない)。 山札から外れ、 今の id だけの山札は変わらない");
        }

        // ---- 6. 〈剥ぎ取り〉は攻撃の端子 (2026-11-13・ユーザーの決定) ----
        //   撃破のボーナスを配る所 (CombatManager.FinishTerminalTurn / GrantTerminalKillBonus) は UnityEngine 依存でここでは動かせない。
        //   ここで見るのは、 その判定が読む値 (発動・攻撃の個数) が〈戦利品箱〉ほか 3 つと同じになること。
        {
            var sd = TerminalCards.Get(TerminalIds.Stripper);
            Check(sd != null && sd.kind == TerminalKind.Attack && sd.rare && !sd.disposable && sd.cond == TerminalCond.None
                  && sd.description.StartsWith("出目を攻撃に。") && sd.description.Contains(TerminalCards.StripperChoices + " つ増える"),
                  "〈剥ぎ取り〉: 攻撃の種類・レア・条件なし・説明文は「出目を攻撃に。…」");
            var b = new List<string> { TerminalIds.Stripper, TerminalIds.LootBox, TerminalIds.Block };
            var o1 = TerminalCards.Resolve(b, new[] { 0, -1, 2 }, new[] { 5, 3, 4 }, new TerminalEnv(), new TerminalBattleState());
            Check(o1.attackAdd == 5 && o1.attackDice == 1 && o1.supportDice == 0 && o1.kindOfDie[0] == DiceTerminal.Attack && o1.Fired(b, TerminalIds.Stripper),
                  $"挿すと出目が攻撃になる (攻撃 {o1.attackAdd}・攻撃の個数 {o1.attackDice})");
            var o2 = TerminalCards.Resolve(b, new[] { 1, -1, 2 }, new[] { 5, 3, 4 }, new TerminalEnv(), new TerminalBattleState());
            Check(o1.attackDice > 0 && o2.attackDice > 0 && o2.Fired(b, TerminalIds.LootBox),
                  "撃破のボーナスを主攻撃で配る条件 (発動・攻撃の個数 > 0) が〈戦利品箱〉と同じ");
            var o3 = TerminalCards.Resolve(b, new[] { -1, -1, 2 }, new[] { 5, 3, 4 }, new TerminalEnv(), new TerminalBattleState());
            Check(!o3.Fired(b, TerminalIds.Stripper) && o3.attackDice == 0, "挿さなければ発動しない (撃破のボーナスの印が立たない = 選択肢は増えない)");
            var bt = new List<string> { TerminalIds.Stripper, TerminalIds.Transformer };
            var o4 = TerminalCards.Resolve(bt, new[] { 0, 1 }, new[] { 4, 2 }, new TerminalEnv(), new TerminalBattleState());
            Check(o4.attackAdd == 8, $"〈変圧器〉が攻撃の端子として 2 倍にする (攻撃 {o4.attackAdd})");
            Check(TerminalCards.StripperChoices == 1, "倒すと増える選択肢の数は 1");
        }

        // ---- 7. 使い捨て (2026-11-14・作者の決定): 使った戦闘の間は山札に戻らず、 次の戦闘ではまた山札にある (〈応急箱〉も同じ) ----
        {
            GameLoop.GameHooks.HandCarry = HandCarryMode.Pick; GameLoop.GameRng.Reset();   // 既定の手札の規則 (上の項目が切り替えたまま)
            var once = TerminalCards.All.Where(d => d.disposable).Select(d => d.id).ToList();
            Check(TerminalCards.All.All(d => !TerminalCards.LeavesRunDeck(d)),
                  "既定では、 使ってランの山札から抜ける端子は無い (使い捨て " + once.Count + " 種: " + string.Join("・", once) + ")");
            var runDeck = new List<string> { TerminalIds.FirstAid, TerminalIds.Attack, TerminalIds.Block, TerminalIds.Attack, TerminalIds.Block, TerminalIds.Charge, TerminalIds.Attack };
            var dk = new TerminalDeckState();
            dk.BeginBattle(runDeck);
            bool used = false, cameBack = false;
            for (int t = 0; t < 12; t++)
            {
                dk.DrawTurn(TerminalCards.HandSize);
                if (used && dk.board.Contains(TerminalIds.FirstAid)) cameBack = true;
                int fa = dk.board.IndexOf(TerminalIds.FirstAid);
                var w = new int[] { -1, -1, -1 };
                if (fa >= 0 && !used) w[0] = fa;
                var o = TerminalCards.Resolve(dk.board, w, new[] { 3, 2, 1 }, new TerminalEnv { hp = 10, maxHp = 50 }, dk.state);
                if (fa >= 0 && !used) used = o.Fired(dk.board, TerminalIds.FirstAid) && o.heal == 3 * TerminalCards.FirstAidMul;
                dk.FinishTurn(o);
            }
            Check(used && !cameBack && dk.exiled.Contains(TerminalIds.FirstAid) && !dk.draw.Contains(TerminalIds.FirstAid) && !dk.discard.Contains(TerminalIds.FirstAid),
                  "〈応急箱〉: 使った戦闘の間は山札・捨て札・手札に戻らない");
            Check(runDeck.Count == 7 && runDeck.Contains(TerminalIds.FirstAid), "〈応急箱〉: ランの山札からは抜けない");
            dk.BeginBattle(runDeck);
            Check(dk.draw.Contains(TerminalIds.FirstAid) && dk.exiled.Count == 0, "〈応急箱〉: 次の戦闘ではまた山札にある");
            Check(!TerminalCards.Get(TerminalIds.FirstAid).description.Contains("抜ける"), "〈応急箱〉の説明文に「ランの山札から抜ける」が無い");
        }

        // ---- 8. 〈写し取り〉(2026-11-14): 写しは次のターンの手札に入る (引く 5 枚とは別・次のターンは 1 枚多い) ----
        {
            GameLoop.GameHooks.HandCarry = HandCarryMode.Pick; GameLoop.GameRng.Reset();   // 既定の手札の規則 (上の項目が切り替えたまま)
            var runDeck = new List<string> { TerminalIds.Copier, TerminalIds.Igniter, TerminalIds.Attack, TerminalIds.Block, TerminalIds.Charge,
                                             TerminalIds.Attack, TerminalIds.Block };
            var dk = new TerminalDeckState();
            dk.BeginBattle(runDeck);
            bool done = false;
            for (int t = 0; t < 20 && !done; t++)
            {
                dk.DrawTurn(TerminalCards.HandSize);
                int cp = dk.board.IndexOf(TerminalIds.Copier);
                int tg = dk.board.IndexOf(TerminalIds.Igniter);
                if (cp < 0 || tg < 0) { dk.FinishTurn(TerminalCards.Resolve(dk.board, new[] { -1, -1, -1 }, new[] { 1, 2, 3 }, new TerminalEnv(), dk.state)); continue; }
                var want = new int[dk.board.Count]; for (int i = 0; i < want.Length; i++) want[i] = -1; want[cp] = tg;
                var o = TerminalCards.Resolve(dk.board, new[] { cp, -1, -1 }, new[] { 4, 2, 3 }, new TerminalEnv(), dk.state, want);
                Check(o.Fired(dk.board, TerminalIds.Copier) && o.copyTarget == tg, "〈写し取り〉が選んだ対象 (〈定電圧〉) を写す");
                int discBefore = dk.discard.Count;
                dk.FinishTurn(o);
                Check(dk.discard.Count == discBefore && dk.copyNext == TerminalIds.Igniter, "写しは捨て札へ入らず、 次のターンの手札へ控える");
                dk.DrawTurn(TerminalCards.HandSize);
                Check(dk.hand.Count == TerminalCards.HandSize + 1 && dk.LastDrawnCount == TerminalCards.HandSize && dk.hand[dk.hand.Count - 1] == TerminalIds.Igniter
                      && dk.copyNext == null,
                      $"次のターンは 5 枚引いたうえで写しが 1 枚足される (手札 {dk.hand.Count} 枚・引いた {dk.LastDrawnCount} 枚・最後 〈{dk.hand[dk.hand.Count - 1]}〉)");
                int igBefore = dk.hand.Count(x => x == TerminalIds.Igniter);
                dk.FinishTurn(TerminalCards.Resolve(dk.board, new[] { -1, -1, -1 }, new[] { 1, 2, 3 }, new TerminalEnv(), dk.state));
                dk.DrawTurn(TerminalCards.HandSize);
                Check(dk.hand.Count == TerminalCards.HandSize, "使わなければ、 その次のターンはほかの手札と同じに捨て札へ (手札は 5 枚に戻る)");
                int total = dk.draw.Count(x => x == TerminalIds.Igniter) + dk.discard.Count(x => x == TerminalIds.Igniter) + dk.hand.Count(x => x == TerminalIds.Igniter);
                Check(total == 2 && igBefore >= 1, $"写しはこの戦闘の山札に残る (〈定電圧〉が山・捨て札・手札に {total} 枚)");
                dk.BeginBattle(runDeck);
                Check(dk.draw.Count(x => x == TerminalIds.Igniter) == 1 && dk.copyNext == null, "戦闘が終われば写しは消える (次の戦闘の山札は元のまま)");
                done = true;
            }
            Check(done, "〈写し取り〉と〈定電圧〉が同じ手札に来た");
            Check(!TerminalCards.Get(TerminalIds.Copier).description.Contains("山札に加える") && TerminalCards.Get(TerminalIds.Copier).description.Contains("次のターンの手札"),
                  "〈写し取り〉の説明文は「次のターンの手札に加える」");
        }

        // ---- 9. 〈焼き切り〉(2026-11-14・作者の決定): 出目 ×2 を攻撃に・空けた手札の端子を 1 枚この戦闘から外す・候補 0 では不発で消費しない ----
        {
            GameLoop.GameHooks.HandCarry = HandCarryMode.Pick; GameLoop.GameRng.Reset();
            var bd = TerminalCards.Get(TerminalIds.BurnOut);
            Check(bd != null && bd.kind == TerminalKind.Attack && bd.disposable && !bd.rare && TerminalCards.BurnOutMul == 2
                  && bd.description == "出目 ×2 を攻撃に。挿さずに空けた手札の端子を 1 枚、この戦闘から外す。",
                  "〈焼き切り〉: 攻撃の種類・使い捨て・コモン・説明文");
            var env = new TerminalEnv();
            // 挿すと出目 ×2 が攻撃に入り、 選んだ空けた端子が外れる
            var b = new List<string> { TerminalIds.BurnOut, TerminalIds.Charge, TerminalIds.Block };
            var o = TerminalCards.Resolve(b, new[] { 0, 2, -1 }, new[] { 5, 3, 1 }, env, new TerminalBattleState(), new[] { 1, -1, -1 });
            Check(o.Fired(b, TerminalIds.BurnOut) && o.attackAdd == 10 && o.attackDice == 1 && o.supportDice == 0 && o.burnTarget == 1 && o.kindOfDie[0] == DiceTerminal.Attack,
                  $"出目 5 → 攻撃 {o.attackAdd} (×2)・攻撃の個数 {o.attackDice}・外す 〈{(o.burnTarget >= 0 ? b[o.burnTarget] : "-")}〉");
            // 山札で: 外した端子と〈焼き切り〉はこの戦闘から外れ、 次の戦闘では両方戻る
            var dk = new TerminalDeckState();
            var runDeck = new List<string> { TerminalIds.BurnOut, TerminalIds.Charge, TerminalIds.Block };
            dk.BeginBattle(runDeck);
            dk.DrawTurn(TerminalCards.HandSize);
            int bs = dk.board.IndexOf(TerminalIds.BurnOut), cs = dk.board.IndexOf(TerminalIds.Charge), ks = dk.board.IndexOf(TerminalIds.Block);
            var want = new int[dk.board.Count]; for (int i = 0; i < want.Length; i++) want[i] = -1; want[bs] = cs;
            var od = TerminalCards.Resolve(dk.board, new[] { bs, ks, -1 }, new[] { 4, 2, 6 }, env, dk.state, want);
            dk.FinishTurn(od);
            Check(od.attackAdd == 8 && dk.exiled.Contains(TerminalIds.BurnOut) && dk.exiled.Contains(TerminalIds.Charge) && !dk.hand.Contains(TerminalIds.Charge),
                  "対象の端子と〈焼き切り〉がこの戦闘から外れる");
            dk.DrawTurn(TerminalCards.HandSize);
            Check(!dk.board.Contains(TerminalIds.Charge) && !dk.board.Contains(TerminalIds.BurnOut), "この戦闘の次のターンには出てこない");
            dk.BeginBattle(runDeck);
            Check(dk.draw.Count == 3 && dk.exiled.Count == 0, "次の戦闘では両方とも山札にある");
            // 候補 0: 外す端子が無いターンは不発 (出目を外す・攻撃にならない・使い捨てを消費しない)
            var b0 = new List<string> { TerminalIds.BurnOut, TerminalIds.Attack };
            var o0 = TerminalCards.Resolve(b0, new[] { 0, 1, -1 }, new[] { 5, 3, 1 }, env, new TerminalBattleState());
            Check(!o0.Fired(b0, TerminalIds.BurnOut) && o0.slotOfDie[0] == -1 && o0.attackAdd == 3 && o0.attackDice == 1 && o0.burnTarget == -1,
                  $"候補 0 では不発 (出目は外れる・攻撃は〈攻撃〉の 3 だけ = {o0.attackAdd})");
            var dk0 = new TerminalDeckState();
            dk0.BeginBattle(new List<string> { TerminalIds.BurnOut, TerminalIds.Attack });
            dk0.DrawTurn(TerminalCards.HandSize);
            int b0s = dk0.board.IndexOf(TerminalIds.BurnOut), a0s = dk0.board.IndexOf(TerminalIds.Attack);
            var w0 = new int[] { b0s, a0s, -1 };
            var od0 = TerminalCards.Resolve(dk0.board, w0, new[] { 5, 3, 1 }, env, dk0.state);
            dk0.FinishTurn(od0);
            Check(!dk0.exiled.Contains(TerminalIds.BurnOut) && dk0.exiled.Count == 0, "不発の時は使い捨てを消費しない (この戦闘から外れない)");
            // 常設・止まった端子は候補にならない (今の IsCandidate のまま)
            var bp = new List<string> { TerminalIds.Mains, TerminalIds.BurnOut };
            var op = TerminalCards.Resolve(bp, new[] { 1, -1, -1 }, new[] { 4, 1, 1 }, env, new TerminalBattleState());
            Check(!op.Fired(bp, TerminalIds.BurnOut) && op.attackAdd == 0, "常設 (〈主幹〉) しか空いていなければ不発");
            var stS = new TerminalBattleState { stopped = new[] { false, true } };
            var bst = new List<string> { TerminalIds.BurnOut, TerminalIds.Charge };
            var ost = TerminalCards.Resolve(bst, new[] { 0, -1, -1 }, new[] { 4, 1, 1 }, env, stS);
            Check(!ost.Fired(bst, TerminalIds.BurnOut), "止まった端子しか空いていなければ不発");
            // ほかの「倍にする」端子との重なり (出目 4 を〈焼き切り〉に・空けた〈充電〉を外す)
            int Atk(List<string> bb, int[] w, int[] dice) => TerminalCards.Resolve(bb, w, dice, env, new TerminalBattleState()).attackAdd;
            int alone = Atk(new List<string> { TerminalIds.BurnOut, TerminalIds.Charge }, new[] { 0, -1, -1 }, new[] { 4, 1, 1 });
            int tf = Atk(new List<string> { TerminalIds.BurnOut, TerminalIds.Transformer, TerminalIds.Charge }, new[] { 0, 1, -1 }, new[] { 4, 2, 1 });
            int db = Atk(new List<string> { TerminalIds.BurnOut, TerminalIds.Doubler, TerminalIds.Charge }, new[] { 0, 1, -1 }, new[] { 4, 6, 1 });
            int both = Atk(new List<string> { TerminalIds.BurnOut, TerminalIds.Doubler, TerminalIds.Transformer, TerminalIds.Charge }, new[] { 0, 1, 2 }, new[] { 4, 6, 2 });
            int gr = Atk(new List<string> { TerminalIds.BurnOut, TerminalIds.Grinder, TerminalIds.Charge }, new[] { 0, 1, -1 }, new[] { 4, 2, 1 });
            var dbo = TerminalCards.Resolve(new List<string> { TerminalIds.BurnOut, TerminalIds.Doubler, TerminalIds.Charge, TerminalIds.Block }, new[] { 0, 1, -1 }, new[] { 4, 6, 1 }, env, new TerminalBattleState());
            Check(alone == 8 && tf == 12 && db == 16 && both == 24 && gr == 12 && dbo.burnTarget == 2,
                  $"重なり (出目 4): 単独 {alone} / +〈変圧器〉 {tf} / +〈倍電圧〉 {db} (外すのは 1 枚 = 〈{(dbo.burnTarget >= 0 ? "充電" : "-")}〉) / +両方 {both} / +〈増圧〉(出目 2 を挿す・他の出目 +1 → (4+1)×2 + 2) {gr}");
        }

        // ---- 10. #141 BOT のダウンの狙いと残す印 (AutoTest.DownKeepRules は本物をコンパイル) ----
        {
            // 10a. 残した端子は次のターンに引く枚数を減らす (本体の DrawTurn: 手札が規定の枚数になるまでしか引かない)
            GameLoop.GameHooks.HandCarry = HandCarryMode.Pick; GameLoop.GameRng.Reset();
            var dk = new TerminalDeckState();
            var runDeck = new List<string>();
            for (int i = 0; i < 4; i++) { runDeck.Add(TerminalIds.Attack); runDeck.Add(TerminalIds.Block); runDeck.Add(TerminalIds.Charge); }
            dk.BeginBattle(runDeck);
            dk.DrawTurn(TerminalCards.HandSize);
            var o = TerminalCards.Resolve(dk.board, new[] { 0, -1, -1 }, new[] { 3, 1, 1 }, new TerminalEnv(), dk.state);
            var marks = new bool[dk.board.Count]; marks[1] = marks[2] = true;
            dk.keepNext.AddRange(TerminalDeckState.KeepFromMarks(dk.board, dk.permanent.Count, o, marks));
            dk.FinishTurn(o);
            dk.DrawTurn(TerminalCards.HandSize);
            Check(dk.LastKept.Count == 2 && dk.LastDrawnCount == TerminalCards.HandSize - 2 && dk.hand.Count == TerminalCards.HandSize,
                  $"残す印で 2 枚残すと、 次のターンに新しく引くのは {dk.LastDrawnCount} 枚 (手札 {dk.hand.Count} 枚)");

            // 10b. 待つ得の式 = シミュレーションの平均 (戦闘の終わりを周期の中で一様と見る)。 本体の周期の規則 (Escalation.IsHeavyTurn・ダウンで起点が戻る) の写し
            const int P = 3; const float H = 2.0f, W = 0.7f, B = 10f;
            float F(int c) => c > 0 && c % P == 0 ? H * B : W * B;      // 周期の c ターン目の攻撃
            foreach (int c1 in new[] { 1, 2, 3 })                         // 次のターン (T+1) の周期の位置
            {
                // 今割る: T+1 がダウン (0)・T+1+j は周期の j ターン目。 待つ: T+1 は c1・T+2 がダウン・T+2+j は j ターン目
                float Seq(bool hold, int j) => hold ? (j == 1 ? F(c1) : j == 2 ? 0f : F(j - 2)) : (j == 1 ? 0f : F(j - 1));
                double mean = 0;
                for (int nEnd = 2; nEnd <= P + 1; nEnd++)
                {
                    double a = 0, h = 0;
                    for (int j = 1; j <= nEnd; j++) { a += Seq(false, j); h += Seq(true, j); }
                    mean += a - h;
                }
                mean /= P;
                var cyc = new List<int>(); for (int j = 0; j < P; j++) cyc.Add((int)F(c1 + j));
                float g = AutoTest.DownKeepRules.HoldGain((int)F(c1), cyc, 1f);
                Check(Math.Abs(g - mean) < 1e-3, $"待つ得の式 (次のターンが周期の {c1} ターン目) {g:0.00} = シミュレーションの平均 {mean:0.00}");
            }
            var cy = new List<int> { 7, 20, 7 };
            Check(AutoTest.DownKeepRules.HoldGain(7, cy, 0.2f) > 0f && AutoTest.DownKeepRules.HoldGain(20, new List<int> { 20, 7, 7 }, 0.2f) < 0f
                  && Math.Abs(AutoTest.DownKeepRules.HoldGain(7, cy, 0.2f) - 0.2f * (34f / 3f - 7f)) < 1e-4,
                  "待つ得: 次のターンが溜めなら正・大技なら負・帳簿の割引を掛ける");
            Check(AutoTest.DownKeepRules.HoldEligible(true, 2, false, true, false, 760, 750, 30f)
                  && !AutoTest.DownKeepRules.HoldEligible(true, 1, false, true, false, 760, 750, 30f)      // 大技が次のターン = 今割れば重なる
                  && !AutoTest.DownKeepRules.HoldEligible(true, 2, false, false, false, 760, 750, 30f)     // 今割れる手が無い
                  && !AutoTest.DownKeepRules.HoldEligible(true, 2, false, true, true, 740, 750, 30f)       // この手は割る
                  && !AutoTest.DownKeepRules.HoldEligible(true, 2, false, true, false, 790, 750, 30f)      // 次のターンに割れる見込みが無い
                  && !AutoTest.DownKeepRules.HoldEligible(true, 2, true, true, false, 760, 750, 30f)       // 何もしなくても次はダウン
                  && !AutoTest.DownKeepRules.HoldEligible(false, 2, false, true, false, 760, 750, 30f),    // 大技の無い敵
                  "待つ手の条件 (大技が 2 ターン後・今割れる手がある・この手は割らない・次のターンに割れる見込み)");

            // 10c. 残す印 Planned
            Check(AutoTest.DownKeepRules.BlockSlotsNeeded(20, 3.5f, 3) == 3 && AutoTest.DownKeepRules.BlockSlotsNeeded(6, 3.5f, 3) == 2
                  && AutoTest.DownKeepRules.BlockSlotsNeeded(0, 3.5f, 3) == 0, "大技の守りに要る口の数 (攻撃 ÷ 出目の平均・出目の数まで)");
            AutoTest.DownKeepRules.KeepCard C(int slot, TerminalKind kind, float worth, bool condFree = true, bool before = false, int cap = 1)
                => new AutoTest.DownKeepRules.KeepCard { slot = slot, kind = kind, worth = worth, condFree = condFree, keptBeforeUnused = before, capacity = cap };
            var R = AutoTest.DownKeepRules.KeepReason.None;
            var cards = new List<AutoTest.DownKeepRules.KeepCard>
                { C(1, TerminalKind.Attack, 1f), C(2, TerminalKind.Attack, 3f), C(3, TerminalKind.Block, 2f), C(4, TerminalKind.Attack, 2f, before: true), C(5, TerminalKind.Support, 3f) };
            var r1 = AutoTest.DownKeepRules.PlanKeep(cards, 3, true, false, 1, 0, 0, 1.5f, true);
            Check(r1[0] == AutoTest.DownKeepRules.KeepReason.DownAttack && r1[1] == AutoTest.DownKeepRules.KeepReason.DownAttack && r1[2] == R && r1[3] == R && r1[4] == R,
                  "次がダウン: 攻撃の端子を常設と合わせて出目の数まで (点の高い順)・前に残して使わなかった端子とほかの種類は残さない");
            var r2 = AutoTest.DownKeepRules.PlanKeep(cards, 3, false, true, 0, 0, 2, 1.5f, true);
            Check(r2[2] == AutoTest.DownKeepRules.KeepReason.HeavyBlock && r2[4] == AutoTest.DownKeepRules.KeepReason.Leftover && r2[1] == AutoTest.DownKeepRules.KeepReason.Leftover
                  && r2[0] == R && r2[3] == R,
                  "次が大技: 防御の端子 (常設と合わせて要る口まで) + 出目を挿し切って余った、 平均より点の高い端子 (残す口は出目の数まで)");
            var r3 = AutoTest.DownKeepRules.PlanKeep(cards, 3, false, false, 0, 0, 0, 1.5f, false);
            Check(r3.All(x => x == R), "当てが無い (ダウンでも大技でもなく、 出目が余っていた) なら何も残さない");
            var many = new List<AutoTest.DownKeepRules.KeepCard>();
            for (int i = 0; i < 6; i++) many.Add(C(i, TerminalKind.Support, 3f));
            many.Add(C(6, TerminalKind.Attack, 3f, condFree: false));
            var r4 = AutoTest.DownKeepRules.PlanKeep(many, 3, false, false, 0, 0, 0, 1.5f, true);
            Check(r4.Count(x => x != R) == 3 && r4[6] == R, "余った端子を残すのは出目の数まで・条件のある端子は残さない");

            // 10d. 〈呼び戻し〉の当て
            var kinds = new List<TerminalKind> { TerminalKind.Attack, TerminalKind.Block, TerminalKind.Support, TerminalKind.Block };
            var worths = new List<float> { 2f, 1f, 3f, 1f };
            Check(AutoTest.DownKeepRules.RecallIndex(kinds, worths, null) == 2 && AutoTest.DownKeepRules.RecallIndex(kinds, worths, TerminalKind.Attack) == 0
                  && AutoTest.DownKeepRules.RecallIndex(kinds, worths, TerminalKind.Block) == 3
                  && AutoTest.DownKeepRules.RecallIndex(kinds, worths, TerminalKind.Charge) == 2
                  && AutoTest.DownKeepRules.RecallIndex(new List<TerminalKind>(), new List<float>(), TerminalKind.Attack) == -1,
                  "〈呼び戻し〉: 当ての種類の中で点の最大 (同点は後に捨てた方)・無ければ全体で・当てが無ければ今までと同じ");
        }

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
