// 確率分岐ごとの文 (EventBranchText) の検算。 Tools/event_branch_check/run.py が Unity なしでコンパイルして回す。
//   1. 切り分けの規則 (分岐 2 つ・3 つ・区切りなし・数が合わない・確率分岐なしで区切った)
//   2. Assets/Resources/Events/*.txt の全選択肢を、 解析 (EventParser) と同じ切り方で読み、
//      区切った文の数が分岐の数と合わない行を挙げる (合わない行は、 ゲームでは警告して先頭の文を使う)
//   3. 選べない選択肢の規則 (2026-11-13・EventEffectRoles / EventChoice.UnavailableReason): 暗くするのは払えない時と、
//      得になる効果が何ひとつ起きない時だけ。 全イベントを本物の解析 (EventParser) で読み、 状況を総当たりして
//      「暗くなり得る選択肢」の一覧と理由を出し、 全部が同時に暗くなり得るイベントが 0 件であることを見る
//   4. 〈さびれた観測所〉3 本の選択肢と効果の並び・博士の時計が秘宝として items.json と除外の集合に入っているか (2026-11-13)
//   5. 手放せない品 (確信の 3 品・預かり物・ItemIds.Undroppable): 売却・取り替え・偽の商人のどの経路でも残るか・
//      確信を売ってしまったランで深さ 5 のボスを倒すと〈決意〉が足されるか (2026-11-13・Ids.cs と ConvictionSystem.cs は本物をコンパイル)
//   6. 売値 0G の品 (灯火・レイピア・items.json の noSellValue): 売値の規則 (SellPriceRule は本物をコンパイル)・BOT は売らない・人は売れる
//   7. 出る層 (2026-11-13): 条件は今ある深さ (1・3・5・6・7) だけで書く・深さごとの抽選のプールの数・連鎖の順 (前の段 ≦ 後の段)・
//      確信の連鎖は深さ 1〜5・深さ 7 (一本道・追跡者と天候なし) に出るイベントは選べる選択肢がある
//   8. 〈灰に埋もれた基地〉2 本 (選択肢の数・効果・版の選び方・〈真理〉は効果で立つ) と〈徒労〉〈開眼〉の規則 (2026-11-13・AshBaseEvent / BattleBoonRule は本物をコンパイル)
//      〈徒労〉の戦闘のデバフ (2026-11-14・与えるダメージ −N%・既定 0・怠惰と同じ枠・王を倒すと消える)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using EventSystem;

static class Program
{
    static int fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { fails++; Console.WriteLine("NG  " + what); }
        else Console.WriteLine("ok  " + what);
    }

    static void Rules()
    {
        string first, problem;

        var two = EventBranchText.Split("当たり||外れ", 2, out first, out problem);
        Check(two != null && two.Count == 2 && two[0] == "当たり" && two[1] == "外れ" && first == "当たり" && problem == null, "分岐 2 つ");
        Check(EventBranchText.For(two, 1, first) == "外れ", "分岐 2 つ: 2 番目を引いたら 2 番目の文");
        Check(EventBranchText.For(two, -1, first) == "当たり", "分岐 2 つ: 引いていない (-1) なら先頭の文");

        var three = EventBranchText.Split(" 一 || 二 ||三 ", 3, out first, out problem);
        Check(three != null && three.Count == 3 && three[0] == "一" && three[1] == "二" && three[2] == "三" && problem == null, "分岐 3 つ (前後の空白は落とす)");

        var none = EventBranchText.Split("どの分岐でも同じ文", 2, out first, out problem);
        Check(none == null && first == "どの分岐でも同じ文" && problem == null, "区切りなし: null・全分岐で同じ文");
        Check(EventBranchText.For(none, 1, first) == "どの分岐でも同じ文", "区切りなし: どの分岐を引いても同じ文");

        var bad = EventBranchText.Split("一||二", 3, out first, out problem);
        Check(bad == null && first == "一" && problem != null, "数が合わない (文 2・分岐 3): null・先頭の文・警告あり");

        var bad2 = EventBranchText.Split("一||二||三", 2, out first, out problem);
        Check(bad2 == null && first == "一" && problem != null, "数が合わない (文 3・分岐 2)");

        var noProb = EventBranchText.Split("一||二", 0, out first, out problem);
        Check(noProb == null && first == "一" && problem != null, "確率分岐なしで区切った: 先頭の文・警告あり");

        var empty = EventBranchText.Split("一||", 2, out first, out problem);
        Check(empty != null && empty.Count == 2 && empty[1] == "", "空の分岐は空文字 (記録では沈黙)");

        Check(EventBranchText.Split(null, 2, out first, out problem) == null && first == null && problem == null, "null はそのまま");
    }

    // EventParser と同じ切り方 (@@ → 最初と最後の '-' → 「N%で」が 2 回以上なら確率分岐)
    static readonly Regex prob = new Regex(@"(\d+)%で");

    static void Scan(string dir)
    {
        int choices = 0, probChoices = 0, split = 0;
        foreach (var path in Directory.GetFiles(dir, "*.txt"))
        {
            int ln = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                ln++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.Contains("━━") || line.Contains("===")) continue;
                int colons = 0; foreach (var ch in line) if (ch == ':') colons++;
                if (colons < 3) continue;
                // フラグ:[..] / パッシブ:[..] の内部コロンを読み飛ばす (EventParser.ParseLine と同じ)
                int p1 = line.IndexOf(':');
                int from = p1 + 1;
                if (Regex.IsMatch(line.Substring(from), @"^(フラグ|パッシブ):\[")) from = line.IndexOf(':', from) + 1;
                int p2 = line.IndexOf(':', from);
                int p3 = line.IndexOf(':', p2 + 1);
                string name = line.Substring(0, p1);
                foreach (var part in line.Substring(p3 + 1).Split('/'))
                {
                    string s = part.Trim();
                    if (s.Length == 0) continue;
                    string digest = null;
                    int at = s.IndexOf("@@", StringComparison.Ordinal);
                    if (at >= 0) { digest = s.Substring(at + 2).Trim(); s = s.Substring(0, at); }
                    int a = s.IndexOf('-'), b = s.LastIndexOf('-');
                    if (a < 0 || b <= a) continue;
                    choices++;
                    string label = s.Substring(0, a).Trim();
                    string result = s.Substring(a + 1, b - a - 1).Trim();
                    string after = s.Substring(b + 1).Trim();
                    int n = prob.Matches(result).Count;
                    if (n < 2) n = 0; else probChoices++;
                    string f, p;
                    var x = EventBranchText.Split(after, n, out f, out p);
                    var y = EventBranchText.Split(digest, n, out f, out string q);
                    if (x != null || y != null) split++;
                    string where = $"{Path.GetFileName(path)}:{ln} 〈{name}〉「{label}」";
                    if (p != null) { fails++; Console.WriteLine($"NG  {where} 選んだ後の文: {p}"); }
                    if (q != null) { fails++; Console.WriteLine($"NG  {where} 記録の文: {q}"); }
                }
            }
        }
        Console.WriteLine($"選択肢 {choices}・確率分岐 {probChoices}・分岐ごとに分けた文あり {split}");
    }

    // ===== 3. 選べない選択肢の規則 =====

    static EventEffect E(EventEffectType t, int amount = 0, bool post = false) => new EventEffect { type = t, amount = amount, postCombat = post };

    static void RoleRules()
    {
        // 向き: BOT の値付け (EventChoiceScorer) の符号と同じ
        Check(EventEffectRoles.RoleOf(E(EventEffectType.PullPursuer, 2)) == EventEffectRole.Cost, "向き: 追跡者を呼ぶ = 代償");
        Check(EventEffectRoles.RoleOf(E(EventEffectType.ShiftFront, 2)) == EventEffectRole.Cost, "向き: 前線を近づける (正) = 代償");
        Check(EventEffectRoles.RoleOf(E(EventEffectType.ShiftFront, -2)) == EventEffectRole.Benefit, "向き: 前線を遠ざける (負) = 得");
        Check(EventEffectRoles.RoleOf(E(EventEffectType.ShakeOffPursuer)) == EventEffectRole.Benefit
              && EventEffectRoles.RoleOf(E(EventEffectType.PushPursuer, 3)) == EventEffectRole.Benefit
              && EventEffectRoles.RoleOf(E(EventEffectType.Shortcut, 2)) == EventEffectRole.Benefit, "向き: 撒く・遠ざける・近道 = 得");
        Check(EventEffectRoles.RoleOf(E(EventEffectType.HpDelta, 25)) == EventEffectRole.Benefit && EventEffectRoles.RoleOf(E(EventEffectType.HpDelta, -8)) == EventEffectRole.Cost,
              "向き: HP+N = 得・HP-N = 代償");
        Check(EventEffectRoles.RoleOf(E(EventEffectType.TurnsPass, 2)) == EventEffectRole.Cost && EventEffectRoles.RoleOf(E(EventEffectType.None)) == EventEffectRole.Neutral,
              "向き: ターン+N = 代償・なし = どちらでもない");
        var prob = new EventEffect { type = EventEffectType.Probability, branches = new List<List<EventEffect>> { new List<EventEffect> { E(EventEffectType.HpDelta, -8) }, new List<EventEffect> { E(EventEffectType.MaterialDelta, 1) } } };
        Check(EventEffectRoles.RoleOf(prob) == EventEffectRole.Benefit, "向き: 確率分岐はどれかの分岐に得があれば得");

        var noFoe = new EventEffectSituation { navKnown = true, map = true, foe = false, weather = false, canTrade = true, canForge = true };
        var heal = new List<EventEffect> { E(EventEffectType.TurnsPass, 2), E(EventEffectType.HpDelta, 25), E(EventEffectType.PullPursuer, 2) };
        Check(EventEffectRoles.NothingGainedReason(heal, noFoe) == null, "規則: 起きないのが代償 (呼ぶ) だけなら選べる (HP+25・追跡者を呼ぶ・追跡者なし)");
        var shake = new List<EventEffect> { E(EventEffectType.TurnsPass, 1), E(EventEffectType.ShakeOffPursuer) };
        Check(EventEffectRoles.NothingGainedReason(shake, noFoe) == "この層に追跡者は居ない", "規則: 得 (撒く) が何も起きないなら選べない");
        var some = new List<EventEffect> { E(EventEffectType.ShakeOffPursuer), E(EventEffectType.GoldDelta, 5) };
        Check(EventEffectRoles.NothingGainedReason(some, noFoe) == null, "規則: 得の一部 (撒く) だけが起きないなら選べる");
        Check(EventEffectRoles.NothingGainedReason(new List<EventEffect>(), noFoe) == null, "規則: 効果なし (立ち去る) は選べる");
        Check(EventEffectRoles.NothingGainedReason(new List<EventEffect> { E(EventEffectType.HpDelta, -5), E(EventEffectType.ShiftFront, 2) }, noFoe) == null,
              "規則: 得を持たない (代償だけ) なら選べる");
        var notKnown = new EventEffectSituation { navKnown = false, canTrade = true, canForge = true };
        Check(EventEffectRoles.NothingGainedReason(shake, notKnown) == null, "規則: 航行を判定できない (本体のランでない) なら航行の効果は起きるとみなす");
        var front = new EventEffectSituation { navKnown = true, map = true, foe = true, weather = true, frontArrived = false, canTrade = true, canForge = true };
        Check(EventEffectRoles.NotHappeningReason(E(EventEffectType.ShiftFront, -2), front) == "前線はまだ来ていない"
              && EventEffectRoles.NotHappeningReason(E(EventEffectType.ShiftFront, 2), front) == null, "規則: まだ来ていない前線は遠ざけられない・近づけるのは起きる");
        // 嵐 (2026-11-13): 「前線を遠ざける/近づける」は嵐の時計を動かす。 静かな時間でも遠ざけられる (まだ来ていないで選べなくしない)・嵐の無い層は「この層に嵐は無い」
        var quiet = new EventEffectSituation { navKnown = true, map = true, foe = true, weather = false, frontArrived = false, stormRules = true, storm = true, canTrade = true, canForge = true };
        var noStorm = new EventEffectSituation { navKnown = true, map = true, foe = false, weather = false, stormRules = true, storm = false, canTrade = true, canForge = true };
        Check(EventEffectRoles.NotHappeningReason(E(EventEffectType.ShiftFront, -2), quiet) == null
              && EventEffectRoles.NotHappeningReason(E(EventEffectType.ShiftFront, 2), quiet) == null
              && EventEffectRoles.NotHappeningReason(E(EventEffectType.ShiftFront, -2), noStorm) == "この層に嵐は無い"
              && EventEffectRoles.NothingGainedReason(new List<EventEffect> { E(EventEffectType.ShiftFront, -2) }, noStorm) == "この層に嵐は無い"
              && EventEffectRoles.NothingGainedReason(new List<EventEffect> { E(EventEffectType.ShiftFront, -2) }, quiet) == null,
              "規則 (嵐): 嵐のある層は静かな時間でも遠ざけられる・嵐の無い層は「この層に嵐は無い」で選べない");
    }

    sealed class Scene
    {
        public bool rich, map, foe, weather, front, trade, forge;
        public override string ToString()
            => (rich ? "" : "手持ち 0・") + (!map ? "地図なし" : (foe ? "追跡者あり" : "追跡者なし") + (weather ? (front ? "・前線あり" : "・前線まだ") : "・天候なし"))
               + (trade ? "" : "・差し出せる秘宝なし") + (forge ? "" : "・武器が終点");
    }

    static IEnumerable<Scene> Scenes()
    {
        foreach (bool rich in new[] { true, false })
            foreach (bool trade in new[] { true, false })
                foreach (bool forge in new[] { true, false })
                {
                    yield return new Scene { rich = rich, map = false, trade = trade, forge = forge };
                    foreach (bool foe in new[] { true, false })
                        foreach (bool weather in new[] { true, false })
                            foreach (bool front in weather ? new[] { true, false } : new[] { false })
                                yield return new Scene { rich = rich, map = true, foe = foe, weather = weather, front = front, trade = trade, forge = forge };
                }
    }

    static void Apply(Scene sc, GameLoop.GameManager gm)
    {
        gm.Run.coins = sc.rich ? 9999 : 0;
        gm.Run.provision = sc.rich ? 9999 : 0;
        gm.Run.weaponMaterials = sc.rich ? 9999 : 0;
        gm.FreeMap = sc.map ? new MapSystem.FreeMove.FreeMapSim { FoeAlive = sc.foe, Weather = sc.weather ? MapSystem.FreeMove.WeatherKind.Hail : MapSystem.FreeMove.WeatherKind.None, FrontArrived = sc.front } : null;
        GameLoop.GameManager.TradeOk = sc.trade;
        GameLoop.GameManager.ForgeCost = sc.forge ? 3 : int.MaxValue;
    }

    static void Selectability(string dir)
    {
        var gm = new GameLoop.GameManager { Run = new GameLoop.RunState() };
        GameLoop.GameManager.Instance = gm;
        var scenes = new List<Scene>(Scenes());
        int events = 0, choices = 0, darkable = 0, allDark = 0;
        var list = new List<string>();
        var allDarkList = new List<string>();
        bool campfire = false, cart = false, campfireOk = true, cartOk = true;
        int warn0 = UnityEngine.Debug.Warnings;
        foreach (var path in Directory.GetFiles(dir, "*.txt"))
            foreach (var ev in EventSystem.EventParser.ParseAll(File.ReadAllText(path)))
            {
                events++;
                for (int i = 0; i < ev.choices.Count; i++)
                {
                    choices++;
                    var reasons = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var sc in scenes)
                    {
                        Apply(sc, gm);
                        string r = ev.choices[i].UnavailableReason(gm.Run);
                        if (r != null) reasons.Add(r);
                    }
                    if (reasons.Count > 0) { darkable++; list.Add($"  〈{ev.name}〉「{ev.choices[i].text}」: {string.Join(" / ", reasons)}"); }
                }
                // 同じ状況で全部が暗くなるか (状況は層によらず総当たり ── 実際より厳しめに数える)
                foreach (var sc in scenes)
                {
                    Apply(sc, gm);
                    bool any = false;
                    foreach (var c in ev.choices) if (c.IsAvailable(gm.Run)) { any = true; break; }
                    if (!any) { allDark++; allDarkList.Add($"  全部が暗い: 〈{ev.name}〉({sc})"); break; }
                }
                // 作者の決定の例: 〈焚き火の跡〉「火にあたって体を休める」・〈置き去りの荷車〉「荷を全部ひっくり返す」は追跡者なしの層で選べる
                foreach (var c in ev.choices)
                {
                    bool isCamp = ev.name == "焚き火の跡" && c.text == "火にあたって体を休める";
                    bool isCart = ev.name == "置き去りの荷車" && c.text == "荷を全部ひっくり返す";
                    if (!isCamp && !isCart) continue;
                    if (isCamp) campfire = true; else cart = true;
                    foreach (var sc in scenes)
                    {
                        if (!sc.rich || !sc.map || sc.foe) continue;
                        Apply(sc, gm);
                        if (!c.IsAvailable(gm.Run)) { if (isCamp) campfireOk = false; else cartOk = false; }
                    }
                }
            }
        GameLoop.GameManager.Instance = null;
        Check(UnityEngine.Debug.Warnings == warn0, $"選べるか: 全イベントを解析の警告なしで読めた (警告 {UnityEngine.Debug.Warnings - warn0})");
        Console.WriteLine($"[選べるか] 暗くなり得る選択肢 {darkable} / {choices} (イベント {events}・状況 {scenes.Count} 通りの総当たり):");
        foreach (var l in list) Console.WriteLine(l);
        Check(campfire && campfireOk, "選べるか: 〈焚き火の跡〉「火にあたって体を休める」は追跡者なしの層で選べる (代償の「追跡者を呼ぶ」が起きないだけ)");
        Check(cart && cartOk, "選べるか: 〈置き去りの荷車〉「荷を全部ひっくり返す」は追跡者なしの層で選べる");
        foreach (var l in allDarkList) Console.WriteLine(l);
        Check(allDark == 0, $"選べるか: 全部の選択肢が同時に暗くなり得るイベント {allDark} 件 (安全弁 EventEncounter.IsChoiceSelectable は残す)");
    }

    /// <summary>〈灰に埋もれた基地〉2 本 (2026-11-13・docs/GAME.md §4-2 / §17): 召喚専用・一度のみ・深さ 6・選択肢の数 (はじめて 1・再訪 2) と効果・
    /// 時限効果の名前が本体の定数 (BattleBoonRule) と一致・版の選び方 (AshBaseEvent は本物をコンパイル)・〈真理〉は効果「真理を得る」で立つ。</summary>
    static void AshBase(string dir)
    {
        var all = new List<EventDefinition>();
        foreach (var f in Directory.GetFiles(dir, "*.txt")) all.AddRange(EventSystem.EventParser.ParseAll(File.ReadAllText(f)));
        EventDefinition Find(string name) => all.Find(e => e.name == name);
        var first = Find(GameLoop.AshBaseEvent.FirstName);
        var revisit = Find(GameLoop.AshBaseEvent.RevisitName);
        Check(first != null && revisit != null, $"基地: 2 本とも定義がある (「{GameLoop.AshBaseEvent.FirstName}」「{GameLoop.AshBaseEvent.RevisitName}」)");
        if (first == null || revisit == null) return;
        foreach (var ev in new[] { first, revisit })
            Check(ev.condition.summonOnly && ev.condition.onceOnly, $"基地: 〈{ev.name}〉は召喚専用・一度のみ (通常の抽選に出ない・1 ランに 1 回)");
        string Fx(EventChoice c) => string.Join(",", c.effects.ConvertAll(e => e.type + (string.IsNullOrEmpty(e.param) ? "" : "[" + e.param + "]")));
        const string toil = "TimedDebuff[" + EventSystem.TimedEffects.BattleBoonRule.ToilName + "]";
        const string ready = "TimedBuff[" + EventSystem.TimedEffects.BattleBoonRule.InsightName + "]";
        Check(first.choices.Count == 1 && Fx(first.choices[0]) == toil,
              $"基地 (はじめて): 選択肢 1 つ・〈徒労〉を受けるだけ・手記を開く選択肢は無い ({first.choices.Count} つ: {string.Join(" / ", first.choices.ConvertAll(Fx))})");
        int open = revisit.choices.FindIndex(c => c.Has(EventEffectType.GainTruth));
        int other = open == 0 ? 1 : 0;
        Check(revisit.choices.Count == 2 && open >= 0 && Fx(revisit.choices[open]) == "GainTruth," + ready && Fx(revisit.choices[other]) == toil,
              $"基地 (再訪): 選択肢 2 つ・「{(open >= 0 ? revisit.choices[open].text : "?")}」= 真理を得る +〈開眼〉(〈徒労〉なし) / もう 1 つ = 〈徒労〉 ({string.Join(" / ", revisit.choices.ConvertAll(Fx))})");
        Check(!first.choices.Exists(c => c.Has(EventEffectType.GainTruth)), "基地 (はじめて): 〈真理〉を得る選択肢は無い");
        // 選べるか (代償だけの選択肢も選べる・暗くならない)
        var run = new GameLoop.RunState();
        bool selectable = true;
        foreach (var ev in new[] { first, revisit }) foreach (var c in ev.choices) selectable &= c.IsAvailable(run);
        Check(selectable, "基地: どの選択肢も選べる (〈徒労〉だけの選択肢も暗くならない)");
        Check(EventEffectRoles.RoleOf(revisit.choices[open].effects[0]) == EventEffectRole.Benefit, "基地: 「真理を得る」は得として扱う");

        // 版の選び方 (本物の AshBaseEvent): 測定の口 → ランの外の記録
        var rec = MetaProgression.MetaProgressManager.Instance.State;
        GameLoop.GameHooks.AshKingBeatenEver = null; rec.ashKingBeaten = false;
        bool a1 = GameLoop.AshBaseEvent.NameFor() == GameLoop.AshBaseEvent.FirstName;
        GameLoop.AshBaseEvent.NoteAshKingBeaten();
        bool a2 = rec.ashKingBeaten && GameLoop.AshBaseEvent.NameFor() == GameLoop.AshBaseEvent.RevisitName;
        GameLoop.GameHooks.AshKingBeatenEver = false;
        bool a3 = GameLoop.AshBaseEvent.NameFor() == GameLoop.AshBaseEvent.FirstName;
        rec.ashKingBeaten = false; GameLoop.GameHooks.AshKingBeatenEver = true;
        bool a4 = GameLoop.AshBaseEvent.NameFor() == GameLoop.AshBaseEvent.RevisitName;
        GameLoop.GameHooks.AshKingBeatenEver = null;
        Check(a1 && a2 && a3 && a4, $"基地: 版の選び方 (記録なし → はじめて / 灰燼の王を倒すと記録が立ち再訪 / 測定の口 False → はじめて・True → 再訪) ({a1}/{a2}/{a3}/{a4})");
        // 済んだか (どちらの版でも)
        EventSystem.EventDatabase.Lookup = n => Find(n);
        var r2 = new GameLoop.RunState();
        bool s0 = !GameLoop.AshBaseEvent.Seen(r2);
        r2.seenOnceEvents.Add(first.id); bool s1 = GameLoop.AshBaseEvent.Seen(r2);
        var r3 = new GameLoop.RunState(); r3.seenOnceEvents.Add(revisit.id); bool s2 = GameLoop.AshBaseEvent.Seen(r3);
        EventSystem.EventDatabase.Lookup = null;
        Check(s0 && s1 && s2 && GameLoop.AshBaseEvent.IsBase(first) && GameLoop.AshBaseEvent.IsBase(revisit), "基地: 済んだかはどちらの版の一度のみの記録でも立つ");
        // 〈真理〉は効果が立てる (本物の ConvictionSystem): 〈決意〉→〈真理〉・資格
        var t = new GameLoop.RunState { convictionStage = 2, layer6Unlocked = true };
        t.ownedPassiveItems.Add(GameLoop.ConvictionSystem.IdResolve);
        Check(GameLoop.ConvictionSystem.RevealTruthInLayer6(t) && t.layer7Unlocked && t.ownedPassiveItems[0] == GameLoop.ConvictionSystem.IdTruth,
              "基地: 「真理を得る」(ConvictionSystem.RevealTruthInLayer6) で〈決意〉が〈真理〉に変わり深さ 7 の資格が立つ");
        // 本体の名指しが効果で判定しているか (原文)
        string root = Path.GetFullPath(Path.Combine(dir, "../../.."));
        string evs = File.ReadAllText(Path.Combine(root, "Assets/Scripts/GameLoop/GameManager.Events.cs"));
        string exe = File.ReadAllText(Path.Combine(root, "Assets/Scripts/EventSystem/EventEffectExecutor.cs"));
        Check(!evs.Contains("index == 0") && exe.Contains("case EventEffectType.GainTruth:") && exe.Contains("ConvictionSystem.RevealTruthInLayer6(run)"),
              "基地: 〈真理〉は選択肢の並び (index == 0) でなく効果「真理を得る」(EventEffectExecutor) が立てる");
    }

    /// <summary>時限デバフ〈徒労〉/ 時限バフ〈開眼〉(2026-11-13): 規則 (BattleBoonRule は本物をコンパイル) を戦闘の列で回す。
    /// 減らし方は TimedEffectManager.ApplyDictByTrigger (decrement) と同じ「残り −1・0 で消える」を写す (本体は UnityEngine に依存するので原文で口を確かめる)。</summary>
    static void ToilInsight(string root)
    {
        const string T = EventSystem.TimedEffects.BattleBoonRule.ToilName, R = EventSystem.TimedEffects.BattleBoonRule.InsightName;
        Check(EventSystem.TimedEffects.BattleBoonRule.ToilCharges == 3 && EventSystem.TimedEffects.BattleBoonRule.InsightCharges == 3
              && EventSystem.TimedEffects.BattleBoonRule.InsightMultiplier == 2, "徒労・開眼: 置き値 (3 つ・3 つ・2 倍) は定数");
        // 戦闘の列: 種類 (N = 通常 / E = エリート / B = ボス / P = 追跡者 / F = 逃げた通常 / L = 負けた通常)
        (int coins, int mats, int drops, int prov, int left) Fight(Dictionary<string, int> deb, Dictionary<string, int> buf, char kind, double[] rolls, ref int ri)
        {
            bool normal = kind == 'N' || kind == 'F' || kind == 'L';
            bool won = kind != 'L', fled = kind == 'F';
            int Left(Dictionary<string, int> d, string id) => d.TryGetValue(id, out int n) ? n : 0;
            var b = EventSystem.TimedEffects.BattleBoonRule.Decide(won, fled, normal, Left(deb, T), Left(buf, R));
            if (b != EventSystem.TimedEffects.BattleBoon.None)
                foreach (var (d, id) in new[] { (deb, T), (buf, R) })
                    if (d.TryGetValue(id, out int n) && n > 0) { d[id] = n - 1; if (d[id] <= 0) d.Remove(id); }   // TimedEffectManager の減らし方
            if (!won || fled) return (0, 0, 0, 0, Left(deb, T) + Left(buf, R));
            int baseMat = kind == 'E' ? 2 : 1, baseCoins = 10, baseProv = 1;
            int drops = 0;
            for (int k = 0; k < EventSystem.TimedEffects.BattleBoonRule.DropRolls(b); k++) if (rolls[ri++ % rolls.Length] < 0.5) drops++;
            if (!EventSystem.TimedEffects.BattleBoonRule.GrantsDrops(b)) drops = 0;
            return (EventSystem.TimedEffects.BattleBoonRule.Scale(b, baseCoins), EventSystem.TimedEffects.BattleBoonRule.Scale(b, baseMat), drops,
                    EventSystem.TimedEffects.BattleBoonRule.GrantsProvision(b) ? baseProv : 0, Left(deb, T) + Left(buf, R));
        }
        var rollsAllHit = new[] { 0.1 };
        // 〈徒労〉: エリート・ボス・追跡者・逃げた・負けたは減らさず報酬も出る / 通常 3 勝で晴れる
        {
            var deb = new Dictionary<string, int> { [T] = EventSystem.TimedEffects.BattleBoonRule.ToilCharges }; var buf = new Dictionary<string, int>();
            int ri = 0; var log = new List<string>(); bool ok = true;
            foreach (char k in "EBPFLNNEN" + "N")
            {
                var r = Fight(deb, buf, k, rollsAllHit, ref ri);
                log.Add($"{k}:{r.coins}G/{r.mats}素/{r.drops}落/{r.prov}物/残{r.left}");
            }
            // 期待: E,B,P は報酬あり・残り 3 / F,L は報酬 0 (逃げ・負け) 残り 3 / N,N は 0・残り 2,1 / E は報酬あり 残り 1 / N は 0 残り 0 / 最後の N は報酬あり
            string got = string.Join(" ", log);
            string want = "E:10G/2素/1落/1物/残3 B:10G/1素/1落/1物/残3 P:10G/1素/1落/1物/残3 F:0G/0素/0落/0物/残3 L:0G/0素/0落/0物/残3 "
                        + "N:0G/0素/0落/0物/残2 N:0G/0素/0落/0物/残1 E:10G/2素/1落/1物/残1 N:0G/0素/0落/0物/残0 N:10G/1素/1落/1物/残0";
            Check(got == want, "徒労 (旧い形 Stacks・形を見ない版): 通常の戦闘の勝ちだけで 1 つ減り、 その戦闘は何も得られない (ゴールド・素材・ドロップ・物資 0)・エリート/ボス/追跡者/逃げた/負けたは減らない・3 勝で晴れる"
                               + (got == want ? "" : $"\n    実際 {got}\n    期待 {want}"));
        }
        // 〈開眼〉: 通常の戦闘でゴールド・素材 2 倍・ドロップは 2 回引く・物資は増やさない / 3 勝で消える / 〈徒労〉と両方なら〈徒労〉が勝つ
        {
            var deb = new Dictionary<string, int>(); var buf = new Dictionary<string, int> { [R] = EventSystem.TimedEffects.BattleBoonRule.InsightCharges };
            int ri = 0; var log = new List<string>();
            foreach (char k in "NENNN") { var r = Fight(deb, buf, k, rollsAllHit, ref ri); log.Add($"{k}:{r.coins}G/{r.mats}素/{r.drops}落/{r.prov}物/残{r.left}"); }
            string got = string.Join(" ", log);
            string want = "N:20G/2素/2落/1物/残2 E:10G/2素/1落/1物/残2 N:20G/2素/2落/1物/残1 N:20G/2素/2落/1物/残0 N:10G/1素/1落/1物/残0";
            Check(got == want, "開眼: 通常の戦闘の勝ちでゴールド・素材 2 倍・ドロップは抽選 2 回・物資はそのまま・エリートには掛からない・3 勝で消える"
                               + (got == want ? "" : $"\n    実際 {got}\n    期待 {want}"));
            var both = EventSystem.TimedEffects.BattleBoonRule.Decide(true, false, true, 1, 1);
            Check(both == EventSystem.TimedEffects.BattleBoon.Toil, "徒労・開眼: 両方ある時は〈徒労〉 (何も得られない)");
        }
        // 本体の口 (原文): 判定は 1 か所・既存の時限効果の減らし方に乗る・登録と数
        string Src(string p) => File.ReadAllText(Path.Combine(root, p));
        string rw = Src("Assets/Scripts/GameLoop/GameManager.Rewards.cs"), cb = Src("Assets/Scripts/GameLoop/GameManager.Combat.cs");
        string reg = Src("Assets/Scripts/EventSystem/TimedEffects/TimedEffectRegistry.cs"), fx = Src("Assets/Scripts/EventSystem/TimedEffects/Effects/BattleRewardEffects.cs");
        Check(rw.Contains("BattleBoonRule.Decide(") && rw.Contains("TimedEffectManager.OnNormalBattleWon(Run)") && cb.Contains("TakeNormalBattleBoon(result);")
              && System.Text.RegularExpressions.Regex.Matches(rw + cb, @"BattleBoonRule\.Decide\(").Count == 1,
              "徒労・開眼: 判定は GameManager.TakeNormalBattleBoon の 1 か所 (戦闘の終わりに呼ぶ)・減らすのは TimedEffectManager.OnNormalBattleWon");
        Check(reg.Contains("new Effects.ToilDebuffEffect()") && reg.Contains("new Effects.InsightBuffEffect()")
              && reg.Contains("BattleBoonRule.ToilCharges") && reg.Contains("BattleBoonRule.InsightCharges")
              && System.Text.RegularExpressions.Regex.Matches(fx, @"TimedEffectTrigger\.NormalBattleWon").Count == 2,
              "徒労・開眼: 時限効果として登録・持つ数は定数・どちらも「通常の戦闘に勝った時」で減る");
        Check(rw.Contains("ScaleByBattleBoon(coins)") && rw.Contains("BattleBoonRule.GrantsProvision(LastBattleBoon, GameHooks.ToilMode)") && rw.Contains("BattleBoonRule.DropRolls(LastBattleBoon)")
              && cb.Contains("ScaleByBattleBoon(elite ? 2 : 1)") && cb.Contains("BattleBoonRule.GrantsDrops(LastBattleBoon)")
              && rw.Contains("BattleBoonRule.Scale(LastBattleBoon, amount, GameHooks.ToilMode)"),
              "徒労・開眼: 報酬の各所 (ゴールド・物資・ドロップ・強化素材・強奪) が判定の結果を読む (量と物資は〈徒労〉の形も見る)");
        ToilLayerLong(root);
        ToilUntilKing(root);
        ToilCombat(root);
    }

    /// <summary>〈徒労〉の戦闘のデバフ (2026-11-14・GameHooks.ToilDamageDownPct・既定 50・測定の口 toilDamageDownPct で 0 にすれば切れる): 持っている間、 すべての戦闘で与えるダメージ −N%。
    /// 枠は大罪:怠惰と同じ (重なったら厳しい方)・持っていない時と N = 0 の時は今の式と同じ・灰燼の王を倒すと消える。</summary>
    static void ToilCombat(string root)
    {
        const string T = EventSystem.TimedEffects.BattleBoonRule.ToilName, R = EventSystem.TimedEffects.BattleBoonRule.InsightName;
        var U = EventSystem.TimedEffects.ToilMode.UntilKing;
        int Pct(bool has, int n) => EventSystem.TimedEffects.BattleBoonRule.ToilDamageDownPct(has, n);
        int Cut(int d, int sloth, int toil) => EventSystem.TimedEffects.BattleBoonRule.ApplyOutgoingCut(d, EventSystem.TimedEffects.BattleBoonRule.OutgoingCutPct(sloth, toil));
        Check(EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct == 50, "徒労 (戦闘): 既定は 50 (与えるダメージ −50%・2026-11-14 作者の決定)");
        Check(Cut(100, 0, Pct(true, EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct)) == 50 && Cut(100, 30, Pct(true, EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct)) == 50
              && Cut(100, 0, Pct(false, EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct)) == 100,
              "徒労 (戦闘): 既定のまま持っている間は 100 → 50 (怠惰 30 と重なっても厳しい方の 50)・持っていなければ 100");
        // 持っていない時・N = 0 の時は、 今の式 (怠惰なし = 素通し / 怠惰あり = d × (100 − 30) / 100) と全部同じ
        {
            bool same = true;
            for (int d = -3; d <= 600; d++)
                foreach (int n in new[] { 0, 15, 30, 45 })
                {
                    same &= Cut(d, 0, Pct(false, n)) == d && Cut(d, 0, Pct(true, 0)) == d;
                    int oldSloth = d > 0 ? d * (100 - 30) / 100 : d;
                    same &= Cut(d, 30, Pct(false, n)) == oldSloth && Cut(d, 30, Pct(true, 0)) == oldSloth;
                }
            Check(same, "徒労 (戦闘): 持っていない時と N = 0 の時は与ダメージが変わらない (怠惰あり・なしとも今の式と同じ)");
        }
        // 持っている時は −N% (切り捨て)・怠惰と重なったら厳しい方 1 つ (足さない・掛けない)
        {
            bool ok = true;
            foreach (int n in new[] { 15, 30, 45 })
                for (int d = 1; d <= 600; d++)
                {
                    ok &= Cut(d, 0, Pct(true, n)) == d * (100 - n) / 100;
                    ok &= Cut(d, 30, Pct(true, n)) == d * (100 - Math.Max(30, n)) / 100;
                }
            ok &= Cut(100, 0, Pct(true, 15)) == 85 && Cut(100, 0, Pct(true, 45)) == 55 && Cut(100, 30, Pct(true, 15)) == 70 && Cut(100, 30, Pct(true, 45)) == 55;
            ok &= Pct(true, 150) == 100 && Pct(true, -5) == 0 && Cut(100, 0, Pct(true, 150)) == 0 && Cut(0, 0, 45) == 0;
            Check(ok, "徒労 (戦闘): 持っている間は与えるダメージ −N% (切り捨て)・怠惰と重なったら厳しい方 (100 → 15%:85 / 45%:55 / 怠惰 30 と 15%:70 / 怠惰 30 と 45%:55)");
        }
        // 戦闘の列 (既定の形 UntilKing・N = 30・主攻撃 100): N = 通常 / E = エリート / B = ボス / P = 追跡者 / K = 灰燼の王を倒す (その戦闘は削られる・倒した後に消える)
        {
            var deb = new Dictionary<string, int> { [T] = EventSystem.TimedEffects.BattleBoonRule.ToilCharges };
            int Left(string id) => deb.TryGetValue(id, out int v) ? v : 0;
            var log = new List<string>();
            foreach (char k in "NEBPKNEB")
            {
                int dealt = Cut(100, 0, Pct(Left(T) > 0, 30));
                if (k == 'K' && EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnKingBeaten(U)) deb.Remove(T);
                log.Add($"{k}:{dealt}");
            }
            string got = string.Join(" ", log), want = "N:70 E:70 B:70 P:70 K:70 N:100 E:100 B:100";
            Check(got == want, "徒労 (戦闘): 通常・エリート・ボス・追跡者・灰燼の王の戦闘で −30%・王を倒すと消えて元に戻る" + (got == want ? "" : $"\n    実際 {got}\n    期待 {want}"));
        }
        // 説明: N > 0 なら「与えるダメージ −N%。」を後ろに付ける・0 なら今の文のまま・〈開眼〉は変わらない
        Check(EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(T, U, 0) == EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(T, U)
              && EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(T, U, 15) == EventSystem.TimedEffects.BattleBoonRule.ToilUntilKingDescription + "与えるダメージ −15%。"
              && EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(R, U, 15) == EventSystem.TimedEffects.BattleBoonRule.InsightDescription
              && EventSystem.TimedEffects.BattleBoonRule.DescriptionOf("啓示", U, 15) == null,
              "徒労 (戦闘): 説明に「与えるダメージ −N%。」(N は値から・0 なら句を出さない)・〈開眼〉には付けない");
        // 本体の口 (原文): 枠は 1 つの関数 (旧 ApplySlothCut を改名)・敵の HP を減らす口がそれを通る・口の既定と戻し・BOT の見積もりも同じ関数
        string Src(string p) => File.ReadAllText(Path.Combine(root, p));
        string meta = Src("Assets/Scripts/MetaProgression/MetaDebuffApplicator.cs"), dmg = Src("Assets/Scripts/CombatSystem/CombatManager.Damage.cs");
        string diag = Src("Assets/Scripts/CombatSystem/Diagnostics/HpLossDiag.cs"), kw = Src("Assets/Scripts/ItemSystem/PassiveSkills/Keywords.cs");
        string arc = Src("Assets/Scripts/AutoTest/AutoRunner.Combat.cs"), hooks = Src("Assets/Scripts/GameLoop/GameHooks.cs");
        string st = Src("Assets/Scripts/AutoTest/AutoRunner.Step.cs"), tr = Src("Assets/Scripts/AutoTest/AutoRunner.Tracking.cs"), ar = Src("Assets/Scripts/AutoTest/AutoRunner.cs");
        int stale = 0;
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
            if (File.ReadAllText(f).Contains("ApplySlothCut(")) stale++;
        Check(stale == 0 && meta.Contains("BattleBoonRule.ApplyOutgoingCut(dmg, GetOutgoingCutPct())") && meta.Contains("BattleBoonRule.OutgoingCutPct(GetSlothDamageCutPct(), GetToilDamageCutPct())"),
              "徒労 (戦闘): 枠は MetaDebuffApplicator.ApplyOutgoingCut の 1 つ (怠惰と〈徒労〉の厳しい方・旧 ApplySlothCut の呼び出しは残っていない)");
        Check(dmg.Contains("ApplyOutgoingCut(totalDmg)") && diag.Contains("if (channel != \"通常攻撃\") dmg = MetaProgression.MetaDebuffApplicator.ApplyOutgoingCut(dmg);")
              && kw.Contains("ApplyOutgoingCut(dmg)") && Regex.Matches(arc, @"ApplyOutgoingCut\(").Count == 2,
              "徒労 (戦闘): 主攻撃 (ApplyWinDamageModifiers)・主攻撃以外 (HpLossDiag.Lose)・品の直接 (Kw.DamageEnemy)・BOT の主攻撃の見積もり 2 か所が同じ枠を通る");
        Check(hooks.Contains("public static int ToilDamageDownPct = EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct;")
              && ar.Contains("public int toilDamageDownPct = EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct;")
              && st.Contains("GameLoop.GameHooks.ToilDamageDownPct = System.Math.Max(0, toilDamageDownPct);")
              && tr.Contains("GameLoop.GameHooks.ToilDamageDownPct = EventSystem.TimedEffects.BattleBoonRule.DefaultToilDamageDownPct;"),
              "徒労 (戦闘): 本体と BOT の既定は同じ定数 (50)・毎ランの始めに口の値を入れ・ラン終了で既定へ戻す");
    }

    /// <summary>〈徒労〉の既定の形 (toilMode=UntilKing・2026-11-14・作者の決定「徒労は王を倒すまで永続化」): 勝っても減らず、 通常の戦闘は何も得られない (中身は旧い形 Stacks と同じ)・
    /// エリート/ボス/追跡者/逃げた/負けたは対象外・宝箱は空にしない・残りの数は出さない・灰燼の王を倒すと消える (念のため深さ 7 に入っても消える)。</summary>
    static void ToilUntilKing(string root)
    {
        const string T = EventSystem.TimedEffects.BattleBoonRule.ToilName, R = EventSystem.TimedEffects.BattleBoonRule.InsightName;
        var U = EventSystem.TimedEffects.ToilMode.UntilKing;
        // 戦闘の列: N = 通常 / E = エリート / B = ボス (王でない) / P = 追跡者 / F = 逃げた通常 / L = 負けた通常 / X = 層を出る (深さ 5→6) / K = 灰燼の王を倒す / D = 深さ 7 に入る
        string Run(string seq)
        {
            var deb = new Dictionary<string, int> { [T] = EventSystem.TimedEffects.BattleBoonRule.ToilCharges };
            int Left(string id) => deb.TryGetValue(id, out int n) ? n : 0;
            var log = new List<string>();
            foreach (char k in seq)
            {
                if (k == 'X') { if (EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnLeavingLayer(U) || EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnEnteringDepth(U, 6)) deb.Remove(T); log.Add($"X:残{Left(T)}"); continue; }
                if (k == 'K') { if (EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnKingBeaten(U)) deb.Remove(T); log.Add($"K:残{Left(T)}"); continue; }
                if (k == 'D') { if (EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnEnteringDepth(U, 7)) deb.Remove(T); log.Add($"D:残{Left(T)}"); continue; }
                bool normal = k == 'N' || k == 'F' || k == 'L', won = k != 'L', fled = k == 'F';
                var b = EventSystem.TimedEffects.BattleBoonRule.Decide(won, fled, normal, Left(T), 0);
                if (b == EventSystem.TimedEffects.BattleBoon.Toil && EventSystem.TimedEffects.BattleBoonRule.ToilCountsDown(U)) { deb[T]--; if (deb[T] <= 0) deb.Remove(T); }
                if (!won || fled) { log.Add($"{k}:0G/0素/0落/0物/残{Left(T)}"); continue; }
                int drops = EventSystem.TimedEffects.BattleBoonRule.DropRolls(b);
                if (!EventSystem.TimedEffects.BattleBoonRule.GrantsDrops(b)) drops = 0;
                log.Add($"{k}:{EventSystem.TimedEffects.BattleBoonRule.Scale(b, 10, U)}G/{EventSystem.TimedEffects.BattleBoonRule.Scale(b, k == 'E' ? 2 : 1, U)}素/{drops}落/"
                        + $"{(EventSystem.TimedEffects.BattleBoonRule.GrantsProvision(b, U) ? 1 : 0)}物/残{Left(T)}");
            }
            return string.Join(" ", log);
        }
        {
            string got = Run("EBPFLNNNNNXNKN");
            string want = "E:10G/2素/1落/1物/残3 B:10G/1素/1落/1物/残3 P:10G/1素/1落/1物/残3 F:0G/0素/0落/0物/残3 L:0G/0素/0落/0物/残3 "
                        + "N:0G/0素/0落/0物/残3 N:0G/0素/0落/0物/残3 N:0G/0素/0落/0物/残3 N:0G/0素/0落/0物/残3 N:0G/0素/0落/0物/残3 X:残3 N:0G/0素/0落/0物/残3 K:残0 N:10G/1素/1落/1物/残0";
            Check(got == want, "徒労 (既定の形 UntilKing): 通常の戦闘は何も得られない・エリート/ボス/追跡者は対象外・何勝しても減らない・層を出ても残る・灰燼の王を倒すと消える"
                               + (got == want ? "" : $"\n    実際 {got}\n    期待 {want}"));
            string got2 = Run("NNDN");
            Check(got2 == "N:0G/0素/0落/0物/残3 N:0G/0素/0落/0物/残3 D:残0 N:10G/1素/1落/1物/残0", "徒労 (既定の形): 念のため深さ 7 に入った時にも消える (" + got2 + ")");
        }
        {
            bool chest = true;
            for (int i = 0; i < 100; i++) chest &= !EventSystem.TimedEffects.BattleBoonRule.ChestEmpty(U, true, i / 100.0, 100);
            Check(chest, "徒労 (既定の形): 宝箱は空にしない");
            Check(EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(T, U) == EventSystem.TimedEffects.BattleBoonRule.ToilUntilKingDescription
                  && EventSystem.TimedEffects.BattleBoonRule.ToilUntilKingDescription == "灰燼の王を倒すまで、通常の戦闘は勝っても何も得られない。"
                  && EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(R, U) == EventSystem.TimedEffects.BattleBoonRule.InsightDescription
                  && !EventSystem.TimedEffects.BattleBoonRule.ShowsCharges(T, U) && EventSystem.TimedEffects.BattleBoonRule.ShowsCharges(R, U),
                  "徒労 (既定の形): 説明は「灰燼の王を倒すまで…」・残りの数は出さない (〈開眼〉は変わらない)");
            Check(!EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnKingBeaten(EventSystem.TimedEffects.ToilMode.Stacks)
                  && !EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnKingBeaten(EventSystem.TimedEffects.ToilMode.LayerLong)
                  && !EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnEnteringDepth(EventSystem.TimedEffects.ToilMode.Stacks, 7)
                  && !EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnEnteringDepth(EventSystem.TimedEffects.ToilMode.LayerLong, 7)
                  && !EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnEnteringDepth(U, 6),
                  "徒労: 王を倒して消える・深さ 7 で消えるのは既定の形だけ (Stacks・LayerLong は今までどおり)");
        }
        // 本体の口 (原文): 既定は UntilKing (本体の口・BOT の口・ラン終了の戻し)・王を倒した所で消す・深さ 7 に入った所で消す・報酬の画面の 1 行は残りを出さない
        string Src(string p) => File.ReadAllText(Path.Combine(root, p));
        string hooks = Src("Assets/Scripts/GameLoop/GameHooks.cs"), ar = Src("Assets/Scripts/AutoTest/AutoRunner.cs"), tr = Src("Assets/Scripts/AutoTest/AutoRunner.Tracking.cs");
        string rw = Src("Assets/Scripts/GameLoop/GameManager.Rewards.cs"), fl = Src("Assets/Scripts/GameLoop/GameManager.Floor.cs"), ui = Src("Assets/Scripts/UI/Game2D/Screens/Game2DPhaseScreens.cs");
        Check(hooks.Contains("ToilMode ToilMode = EventSystem.TimedEffects.ToilMode.UntilKing;") && ar.Contains("toilMode = EventSystem.TimedEffects.ToilMode.UntilKing;")
              && tr.Contains("GameHooks.ToilMode = EventSystem.TimedEffects.ToilMode.UntilKing;"),
              "徒労: 既定の形は UntilKing (本体の口・BOT の口・ラン終了の戻し)");
        int king = rw.IndexOf("AshBaseEvent.NoteAshKingBeaten();"), clear = rw.IndexOf("BattleBoonRule.ToilClearsOnKingBeaten(GameHooks.ToilMode)");
        Check(king > 0 && clear > king && clear - king < 600, "徒労 (既定の形): 灰燼の王を倒した記録を立てる所 (NoteAshKingBeaten) の直後で消す");
        int adv = fl.IndexOf("if (!Run.AdvanceFloor())"), depth = fl.IndexOf("BattleBoonRule.ToilClearsOnEnteringDepth(GameHooks.ToilMode, Run.Depth)");
        Check(adv > 0 && depth > adv, "徒労 (既定の形): 層を進めた後 (新しい深さ) で、 深さ 7 以降なら消す");
        Check(rw.Contains("GameHooks.ToilMode == EventSystem.TimedEffects.ToilMode.UntilKing") && ui.Contains("GameHooks.ToilMode != EventSystem.TimedEffects.ToilMode.LayerLong"),
              "徒労 (既定の形): 報酬の画面の 1 行は「何も得られなかった」(残りを出さない)・ゴールドの行を置き換える");
    }

    /// <summary>〈徒労〉の別の形 (toilMode=LayerLong・2026-11-14・測定用): 規則 (BattleBoonRule は本物をコンパイル) と、 旧い形 (Stacks) が形を見ない版の規則と変わらないこと。</summary>
    static void ToilLayerLong(string root)
    {
        const string T = EventSystem.TimedEffects.BattleBoonRule.ToilName, R = EventSystem.TimedEffects.BattleBoonRule.InsightName;
        var S = EventSystem.TimedEffects.ToilMode.Stacks; var L = EventSystem.TimedEffects.ToilMode.LayerLong;
        // 既定の形 (Stacks) は形を見ない版と全部一致する (= main の規則のまま)
        {
            bool same = true;
            foreach (var b in new[] { EventSystem.TimedEffects.BattleBoon.None, EventSystem.TimedEffects.BattleBoon.Toil, EventSystem.TimedEffects.BattleBoon.Insight })
                foreach (int amt in new[] { 0, 1, 2, 10, 37 })
                    same &= EventSystem.TimedEffects.BattleBoonRule.Scale(b, amt, S) == EventSystem.TimedEffects.BattleBoonRule.Scale(b, amt)
                          && EventSystem.TimedEffects.BattleBoonRule.GrantsProvision(b, S) == EventSystem.TimedEffects.BattleBoonRule.GrantsProvision(b);
            foreach (var id in new[] { T, R, "啓示", "" })
                same &= EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(id, S) == EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(id)
                      && EventSystem.TimedEffects.BattleBoonRule.ShowsCharges(id, S);
            same &= EventSystem.TimedEffects.BattleBoonRule.ToilCountsDown(S) && !EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnLeavingLayer(S);
            for (int i = 0; i < 100; i++) same &= !EventSystem.TimedEffects.BattleBoonRule.ChestEmpty(S, true, i / 100.0, 100);
            Check(same, "徒労 (旧い形 Stacks): 量・物資・説明・残りの表示・減り方・層を出ても残る・宝箱は空にならない ── 形を見ない版 (今の規則) と全部同じ");
            Check(default(EventSystem.TimedEffects.ToilMode) == S, "徒労: enum の 0 は Stacks (値の並びは変えない・既定の形は口の初期値 UntilKing)");
        }
        // 別の形 (LayerLong) の戦闘の列: 種類 (N = 通常 / E = エリート / B = ボス / P = 追跡者 / F = 逃げた通常 / L = 負けた通常 / X = 層を出る)
        {
            var deb = new Dictionary<string, int> { [T] = EventSystem.TimedEffects.BattleBoonRule.ToilCharges };
            int Left(string id) => deb.TryGetValue(id, out int n) ? n : 0;
            var log = new List<string>();
            foreach (char k in "EBPFLNNNNNXN")
            {
                if (k == 'X') { if (EventSystem.TimedEffects.BattleBoonRule.ToilClearsOnLeavingLayer(L)) deb.Remove(T); log.Add($"X:残{Left(T)}"); continue; }
                bool normal = k == 'N' || k == 'F' || k == 'L', won = k != 'L', fled = k == 'F';
                var b = EventSystem.TimedEffects.BattleBoonRule.Decide(won, fled, normal, Left(T), 0);
                if (b == EventSystem.TimedEffects.BattleBoon.Toil && EventSystem.TimedEffects.BattleBoonRule.ToilCountsDown(L)) { deb[T]--; if (deb[T] <= 0) deb.Remove(T); }
                if (!won || fled) { log.Add($"{k}:0G/0素/0落/0物/残{Left(T)}"); continue; }
                int drops = EventSystem.TimedEffects.BattleBoonRule.DropRolls(b);   // 抽選は全部当たる
                if (!EventSystem.TimedEffects.BattleBoonRule.GrantsDrops(b)) drops = 0;
                log.Add($"{k}:{EventSystem.TimedEffects.BattleBoonRule.Scale(b, 10, L)}G/{EventSystem.TimedEffects.BattleBoonRule.Scale(b, k == 'E' ? 2 : 1, L)}素/{drops}落/"
                        + $"{(EventSystem.TimedEffects.BattleBoonRule.GrantsProvision(b, L) ? 1 : 0)}物/残{Left(T)}");
            }
            string got = string.Join(" ", log);
            string want = "E:10G/2素/1落/1物/残3 B:10G/1素/1落/1物/残3 P:10G/1素/1落/1物/残3 F:0G/0素/0落/0物/残3 L:0G/0素/0落/0物/残3 "
                        + "N:10G/1素/0落/1物/残3 N:10G/1素/0落/1物/残3 N:10G/1素/0落/1物/残3 N:10G/1素/0落/1物/残3 N:10G/1素/0落/1物/残3 X:残0 N:10G/1素/1落/1物/残0";
            Check(got == want, "徒労 (別の形 LayerLong): 通常の戦闘は品が出ない・ゴールド/素材/物資は出る・エリート/ボス/追跡者は対象外・何勝しても減らない・層を出たら消える"
                               + (got == want ? "" : $"\n    実際 {got}\n    期待 {want}"));
        }
        // 宝箱: 別の形で〈徒労〉を持っている時だけ、 確率で空
        {
            int Empties(EventSystem.TimedEffects.ToilMode m, bool has, int pct)
            { int n = 0; for (int i = 0; i < 1000; i++) if (EventSystem.TimedEffects.BattleBoonRule.ChestEmpty(m, has, i / 1000.0, pct)) n++; return n; }
            Check(Empties(L, true, 50) == 500 && Empties(L, true, 0) == 0 && Empties(L, true, 100) == 1000 && Empties(L, true, 25) == 250
                  && Empties(L, false, 100) == 0 && Empties(S, true, 100) == 0 && EventSystem.TimedEffects.BattleBoonRule.DefaultChestEmptyPct == 50,
                  "徒労 (別の形): 宝箱は toilChestEmptyPct % で空 (既定 50)・〈徒労〉が無ければ空にならない・旧い形では空にならない");
            Check(EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(T, L) == EventSystem.TimedEffects.BattleBoonRule.ToilLayerDescription
                  && EventSystem.TimedEffects.BattleBoonRule.DescriptionOf(R, L) == EventSystem.TimedEffects.BattleBoonRule.InsightDescription
                  && !EventSystem.TimedEffects.BattleBoonRule.ShowsCharges(T, L) && EventSystem.TimedEffects.BattleBoonRule.ShowsCharges(R, L),
                  "徒労 (別の形): 説明は層の間の文・残りの数は出さない (〈開眼〉は変わらない)");
        }
        // 本体の口 (原文): 宝箱の空は専用の乱数キーで、 今の形・〈徒労〉なしでは引かない / 宝の点と縄張りの宝の両方 / 層を出たら消す / 減らさない
        string Src(string p) => File.ReadAllText(Path.Combine(root, p));
        string rw = Src("Assets/Scripts/GameLoop/GameManager.Rewards.cs"), fm = Src("Assets/Scripts/GameLoop/GameManager.FreeMap.cs"), fl = Src("Assets/Scripts/GameLoop/GameManager.Floor.cs");
        const string Key = "\"toil.chestEmpty\"";
        int keyUses = 0;
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string t = File.ReadAllText(f);
            for (int i = t.IndexOf(Key); i >= 0; i = t.IndexOf(Key, i + 1)) keyUses++;
        }
        int guard = rw.IndexOf("if (mode != EventSystem.TimedEffects.ToilMode.LayerLong || !hasToil) return false;"), draw = rw.IndexOf("GameRng.Value(" + Key + ")");
        Check(keyUses == 1 && guard > 0 && draw > guard,
              "徒労 (別の形): 宝箱の空の抽選は専用キー toil.chestEmpty の 1 か所・LayerLong 以外の形と〈徒労〉なしでは引かない");
        Check(rw.Contains("ToilEmptiesChest() ? ") && fm.Contains("if (ToilEmptiesChest())"),
              "徒労 (別の形): 宝の点 (OpenTreasure) と縄張りの宝 (TakeTerritoryTreasure) の両方で空になり得る");
        Check(fl.Contains("BattleBoonRule.ToilClearsOnLeavingLayer(GameHooks.ToilMode)") && rw.Contains("BattleBoonRule.ToilCountsDown(GameHooks.ToilMode)"),
              "徒労 (別の形): 層を出たら消す (ConfirmFloorClear)・通常の戦闘に勝っても減らさない (TakeNormalBattleBoon)");
    }

    // ===== 7. 出る層 (2026-11-13・docs/GAME.md §17「出る層」) =====

    static readonly int[] Depths = { 1, 3, 5, 6, 7 };   // 今ある深さ (MapSystem.FreeMove.FloorPlan.Exists の 0 以外・深さ 0 にはイベントの点が無い)
    static bool DepthExists(int d) => Array.IndexOf(Depths, d) >= 0;
    static bool Covers(EventDefinition ev, int d) => ev.condition.MatchesFloor(d);
    static string Range(EventDefinition ev) => ev.condition.floorMin == 1 && ev.condition.floorMax == 99 ? "全"
        : ev.condition.floorMin == ev.condition.floorMax ? $"{ev.condition.floorMin}層" : $"{ev.condition.floorMin}-{ev.condition.floorMax}層";

    static void IEnumerableChoicesOf(EventDefinition ev, Action<EventEffect> f)
    {
        foreach (var c in ev.choices)
            foreach (var e in c.effects)
            {
                f(e);
                if (e.branches != null) foreach (var br in e.branches) if (br != null) foreach (var x in br) f(x);
            }
    }

    static void Layers(string dir)
    {
        var all = new List<EventDefinition>();
        foreach (var path in Directory.GetFiles(dir, "*.txt"))
            all.AddRange(EventSystem.EventParser.ParseAll(File.ReadAllText(path)));

        // 1. 書いた深さ: 「全」以外は、 範囲の両端が今ある深さで、 7 より上を書かない
        var bad = new List<string>();
        foreach (var ev in all)
        {
            var c = ev.condition;
            if (c.floorMin == 1 && c.floorMax == 99) continue;
            if (!DepthExists(c.floorMin) || !DepthExists(c.floorMax) || c.floorMin > c.floorMax) bad.Add($"〈{ev.name}〉{Range(ev)}");
        }
        Check(bad.Count == 0, "出る層: 条件は今ある深さ (1・3・5・6・7) だけで書かれている" + (bad.Count > 0 ? " ── " + string.Join(" / ", bad) : ""));

        // 2. 深さごとの抽選のプール (召喚専用を除く)。 「条件なし」= フラグ・秘宝の持ち物を要らない (ふだんの抽選に並ぶ)
        var draw = all.FindAll(e => !e.condition.summonOnly);
        int lo = int.MaxValue, hi = 0;
        Console.WriteLine($"[出る層] 抽選に出るイベント {draw.Count} (召喚専用 {all.Count - draw.Count} を除く)。 深さごとのプール:");
        foreach (int d in Depths)
        {
            var pool = draw.FindAll(e => Covers(e, d));
            var plain = pool.FindAll(e => e.condition.requiredFlags.Count == 0 && e.condition.requiredPassiveItems.Count == 0);
            var own = plain.FindAll(e => Range(e) != "全");
            lo = Math.Min(lo, plain.Count); hi = Math.Max(hi, plain.Count);
            Console.WriteLine($"  深さ {d} (画面の第 {Array.IndexOf(Depths, d) + 1} 層): {pool.Count} (条件なし {plain.Count}・うち「全」以外 {own.Count}: {string.Join("・", own.ConvertAll(e => e.name))})");
        }
        Check(lo * 4 >= hi * 3, $"出る層: 深さごとの条件なしのプールが大きく偏らない (最小 {lo} ≧ 最大 {hi} の 3/4)");
        foreach (int d in new[] { 6, 7 })
        {
            int own = draw.FindAll(e => Covers(e, d) && Range(e) != "全" && e.condition.requiredFlags.Count == 0 && e.condition.requiredPassiveItems.Count == 0).Count;
            Check(own >= 5, $"出る層: 深さ {d} に「全」以外のイベントが 5 件以上ある ({own})");
        }

        // 3. 連鎖の順: フラグを要るイベントは、 そのフラグを渡すイベントより浅く始まらず、 浅く終わらない (前の段 ≦ 後の段)
        int links = 0; var order = new List<string>();
        foreach (var ev in draw)
            foreach (var flag in ev.condition.requiredFlags)
                foreach (var g in all)
                {
                    bool gives = false;
                    IEnumerableChoicesOf(g, e => { if (e.type == EventEffectType.GainFlag && e.param == flag) gives = true; });
                    if (!gives) continue;
                    links++;
                    var a = g.condition; var b = ev.condition;
                    bool ok = a.floorMin <= b.floorMin && a.floorMax <= b.floorMax;
                    order.Add($"  {(ok ? "ok" : "NG")} 〈{g.name}〉{Range(g)} →[{flag}]→ 〈{ev.name}〉{Range(ev)}");
                    if (!ok) fails++;
                }
        Console.WriteLine($"[出る層] 連鎖 {links} 本:");
        foreach (var l in order) Console.WriteLine(l);
        Check(links >= 4, $"出る層: 連鎖を見つけた ({links} 本・確信 2 本と犬 2 本)");

        // 4. 確信の連鎖 (石板 → 啓示 → 災厄の予兆) は深さ 1・3・5 のどこでも出て、 深さ 5 (画面の第 3 層) のボスまでに終わる (§4-2)
        foreach (var n in new[] { "預言者の石板", "啓示", "災厄の予兆" })
        {
            var ev = all.Find(e => e.name == n);
            Check(ev != null && ev.condition.floorMin == 1 && ev.condition.floorMax == 5, $"出る層: 確信の連鎖〈{n}〉は深さ 1〜5 ({(ev == null ? "無い" : Range(ev))})");
        }

        // 5. 深さ 7 (画面の第 5 層) は一本道: 地図はあるが追跡者が居ず、 嵐も旧い前線の天候も無い (Storm.HasStorm・Weather.PoolFor)。
        //    そこに出るイベントは選べる選択肢を 1 つ以上残す。 嵐の作り (本体の既定・GameHooks.StormEnabled) と旧い前線の作りの両方で見る
        var gm = new GameLoop.GameManager { Run = new GameLoop.RunState() };
        GameLoop.GameManager.Instance = gm;
        var noGain = new List<string>(); var dark = new List<string>();
        foreach (var ev in draw)
        {
            if (!Covers(ev, 7)) continue;
            foreach (bool stormRules in new[] { true, false })
            foreach (bool rich in new[] { true, false })
            {
                GameLoop.GameHooks.StormEnabled = stormRules;
                Apply(new Scene { rich = rich, map = true, foe = false, weather = false, trade = true, forge = true }, gm);
                gm.FreeMap.StormMode = false;
                int ok = 0, gain = 0;
                foreach (var c in ev.choices)
                {
                    if (!c.IsAvailable(gm.Run)) continue;
                    ok++;
                    foreach (var e in c.effects) if (EventEffectRoles.RoleOf(e) == EventEffectRole.Benefit) { gain++; break; }
                }
                string how = (stormRules ? "嵐の作り" : "前線の作り") + "・" + (rich ? "手持ち十分" : "手持ち 0");
                if (ok == 0) dark.Add($"〈{ev.name}〉({how})");
                if (rich && gain == 0) noGain.Add($"〈{ev.name}〉({how})");
            }
        }
        GameLoop.GameHooks.StormEnabled = false;
        GameLoop.GameManager.Instance = null;
        Check(dark.Count == 0, "出る層: 深さ 7 に出るイベントは選べる選択肢が 1 つ以上ある (追跡者なし・嵐も前線も無し)" + (dark.Count > 0 ? " ── " + string.Join(" / ", dark) : ""));
        Check(noGain.Count == 0, "出る層: 深さ 7 に出るイベントは、 手持ちが十分なら得のある選択肢を選べる" + (noGain.Count > 0 ? " ── " + string.Join(" / ", noGain) : ""));
    }

    /// <summary>〈さびれた観測所〉3 本 (2026-11-13 作り直し): 選択肢の数 (4・4・5) と効果の並び。 時計の 5 つ目は時計だけ (ふだんの荷は無い)・得として扱う。</summary>
    static void Observatory(string dir)
    {
        var want = new Dictionary<string, string[]>
        {
            ["さびれた観測所"] = new[] { "MaterialDelta,GainPassiveItem,GoldDelta", "MaxHpDelta,HpFullHeal,GainConsumableItem,GainConsumableItem",
                                       "GainPassiveItem,GainPassiveItem,GainPassiveItem,GoldDelta", "ObservatoryTakeCopy,MaterialDelta,ProvisionDelta" },
            ["さびれた観測所・再訪"] = new[] { "MaterialDelta,GainPassiveItem,GoldDelta", "MaxHpDelta,HpFullHeal,GainConsumableItem,GainConsumableItem",
                                       "GainPassiveItem,GainPassiveItem,GainPassiveItem,GoldDelta", "ObservatoryTakeCopy,MaterialDelta,ProvisionDelta" },
            ["さびれた観測所・再訪・写し"] = new[] { "MaterialDelta,GainPassiveItem,GoldDelta", "MaxHpDelta,HpFullHeal,GainConsumableItem,GainConsumableItem",
                                       "GainPassiveItem,GainPassiveItem,GainPassiveItem,GoldDelta", "ObservatoryTakeCopy,MaterialDelta,ProvisionDelta", "VescaClockTake" },
        };
        var labels = new Dictionary<string, string>
        {
            ["さびれた観測所"] = "向こうへ行けるのか聞く", ["さびれた観測所・再訪"] = "自分も渡れるのか聞く", ["さびれた観測所・再訪・写し"] = "時計を預かる",
        };
        var seen = new HashSet<string>();
        string path = Path.Combine(dir, "event_list_observatory.txt");
        foreach (var ev in EventSystem.EventParser.ParseAll(File.ReadAllText(path)))
        {
            if (!want.TryGetValue(ev.name, out var w)) continue;
            seen.Add(ev.name);
            bool ok = ev.choices.Count == w.Length;
            for (int i = 0; ok && i < w.Length; i++)
            {
                var got = string.Join(",", ev.choices[i].effects.ConvertAll(e => e.type.ToString()));
                if (got != w[i]) { ok = false; Console.WriteLine($"  〈{ev.name}〉{i + 1} つ目: {got} (期待 {w[i]})"); }
            }
            string last = ev.choices.Count > 0 ? ev.choices[ev.choices.Count - 1].text : "";
            Check(ok && last == labels[ev.name], $"観測所: 〈{ev.name}〉 選択肢 {ev.choices.Count} つ・効果の並びが元のまま・最後は「{last}」");
            if (ev.name == "さびれた観測所・再訪・写し" && ev.choices.Count == 5)
                Check(EventSystem.EventEffectRoles.RoleOf(ev.choices[4].effects[0]) == EventSystem.EventEffectRole.Benefit,
                      "観測所: 〈止まらない時計を預かる〉は得として扱う (選べる)");
        }
        Check(seen.Count == 3, $"観測所: 3 本とも読めた ({seen.Count})");
    }

    /// <summary>博士の時計は秘宝 (2026-11-13): items.json に同名の品があり、 発火する物を持たず、 配布の山・預かり物・進行用の品の 3 つに入っているか。
    /// 本体の型 (ItemIds・EventOnlyItemFilter) は UnityEngine に依存するのでコンパイルせず、 定数と集合の中身を原文で確かめる。</summary>
    static void ClockRelic(string root)
    {
        const string id = "止まらない時計";   // GameLoop.ItemIds.VescaClock の写し (下で原文と突き合わせる)
        string ids = File.ReadAllText(Path.Combine(root, "Assets/Scripts/GameLoop/Ids.cs"));
        Check(ids.Contains($"public const string VescaClock = \"{id}\";"), "時計: ItemIds.VescaClock の値 = 「止まらない時計」");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/Data/InventorySystem/items.json")));
        System.Text.Json.JsonElement item = default; int n = 0;
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
            if (it.GetProperty("id").GetString() == id) { item = it; n++; }
        Check(n == 1, $"時計: items.json に 1 品だけある ({n})");
        if (n != 1) return;
        string Str(string k) => item.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        bool unique = item.TryGetProperty("unique", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.True;
        Check(Str("name") == id && Str("category") == "Passive" && unique, "時計: id = 表示名・秘宝 (Passive)・unique");
        Check(Str("skill") == null && Str("axis") == null && !item.TryGetProperty("skills", out _) && !item.TryGetProperty("stats", out _),
              "時計: 発火する物を持たない (skill・skills・stats・axis なし ── 偏重にも数えられない)");
        Check(!string.IsNullOrEmpty(Str("description")) && !string.IsNullOrEmpty(Str("flavorText")), "時計: 説明とフレーバーがある");

        string filter = File.ReadAllText(Path.Combine(root, "Assets/Scripts/ItemSystem/Shop/EventOnlyItemFilter.cs"));
        Check(filter.Contains("GameLoop.ItemIds.VescaClock,"), "時計: 配布の山から外す (EventOnlyItemFilter.ExcludedIds)");
        int prog = ids.IndexOf("ProgressionItems =", StringComparison.Ordinal);
        int progEnd = prog < 0 ? -1 : ids.IndexOf("};", prog, StringComparison.Ordinal);
        Check(prog >= 0 && progEnd > prog && Regex.IsMatch(ids.Substring(prog, progEnd - prog), @"^\s*VescaClock,", RegexOptions.Multiline),
              "時計: 進行用の品 (ItemIds.ProgressionItems: 〈門〉・大罪・BOT の学習から外す)");
        Check(Regex.IsMatch(ids, @"Entrusted\s*=\s*new System\.Collections\.Generic\.HashSet<string> \{ VescaClock \}"),
              "時計: 預かり物 (ItemIds.Entrusted: 図鑑は手に入れるまで出さない・手放せない品 Undroppable にも入る ── 下の KeepConviction)");
        string clock = File.ReadAllText(Path.Combine(root, "Assets/Scripts/GameLoop/VescaClock.cs"));
        Check(clock.Contains("ownedPassiveItems.Contains(ItemIds.VescaClock)") && clock.Contains("PassiveAddHelper.AddPassiveItem(run, ItemIds.VescaClock)")
              && !clock.Contains("ownedFlags.Add"), "時計: 持っているか = 秘宝の所持 (ownedFlags は使わない)");
        Check(!clock.Contains("VescaLine") && !File.ReadAllText(Path.Combine(root, "Assets/Scripts/CombatSystem/CombatManager.Down.cs")).Contains("[ヴェスカ]"),
              "時計: ヴェスカの台詞は置かない (作者の決定)");
    }

    /// <summary>手放せない品 (2026-11-13・ItemIds.Undroppable): 確信の 3 品と預かり物は売却・取り替え・偽の商人のどの経路でも持ち物に残る。
    /// 判定 (ItemIds.CanSell / CanTradeAway / CanBeLost) は本物をコンパイルして見る。 経路 (ShopManager・GameManager・画面) は
    /// UnityEngine に深く依存するのでコンパイルせず、 その判定を呼んでいることと、 集合を直接読む所が Ids.cs の外に無いことを原文で見る。
    /// 確信の書き換え (ConvictionSystem) は本物をコンパイルし、 確信を売ってしまったランで深さ 5 のボス撃破の後に〈決意〉が足されるかを見る。</summary>
    static void KeepConviction(string root)
    {
        string[] keep = { GameLoop.ConvictionSystem.IdConviction, GameLoop.ConvictionSystem.IdResolve, GameLoop.ConvictionSystem.IdTruth, GameLoop.ItemIds.VescaClock };
        foreach (var id in keep)
            Check(!GameLoop.ItemIds.CanSell(id) && !GameLoop.ItemIds.CanTradeAway(id) && !GameLoop.ItemIds.CanBeLost(id)
                  && GameLoop.ItemIds.RefuseSellText(id) != null && !GameLoop.ItemIds.CanOfferAtGate(id) && !GameLoop.ItemIds.CanBeTakenBySin(id),
                  $"手放せない: 〈{id}〉は売れない・取り替えに出さない・失わない・〈門〉と大罪の対象外");
        foreach (var id in new[] { "ちいさな灯火", "シュヴァリエのレイピア", "合札" })
            Check(GameLoop.ItemIds.CanSell(id) && GameLoop.ItemIds.CanTradeAway(id) && GameLoop.ItemIds.CanBeLost(id),
                  $"手放せる: 〈{id}〉は売れる (灯火・レイピアは売値 0G ── 下の ZeroSellPrice)");
        Check(GameLoop.ItemIds.RefuseSellText(GameLoop.ItemIds.VescaClock) == "預かった品は売れない"
              && GameLoop.ItemIds.RefuseSellText(GameLoop.ConvictionSystem.IdConviction) == "これは手放せない",
              "手放せない: 断りの文は集合ごと (時計 = 預かった品は売れない・確信 = これは手放せない)");
        Check(GameLoop.ItemIds.HiddenInCodexUntilOwned(GameLoop.ItemIds.VescaClock)
              && !GameLoop.ItemIds.HiddenInCodexUntilOwned(GameLoop.ConvictionSystem.IdConviction)
              && !GameLoop.ItemIds.HiddenInCodexUntilOwned(GameLoop.ConvictionSystem.IdTruth),
              "図鑑: 枠を隠すのは預かり物 (時計) だけ・確信の 3 品は隠さない");
        bool subset = true;
        foreach (var id in GameLoop.ItemIds.Undroppable) subset &= GameLoop.ItemIds.ProgressionItems.Contains(id);
        Check(subset, "手放せない品は全部、 進行用の品 (〈門〉・大罪・学習から外す) にも入っている");
        Check(!GameLoop.ItemIds.CountsAsTakenRelic(GameLoop.ConvictionSystem.IdConviction) && !GameLoop.ItemIds.CountsAsTakenRelic(GameLoop.ConvictionSystem.IdResolve)
              && !GameLoop.ItemIds.CountsAsTakenRelic(GameLoop.ConvictionSystem.IdTruth) && GameLoop.ItemIds.CountsAsTakenRelic(GameLoop.ItemIds.VescaClock),
              "不安定化: 進行の資格の品 (確信の 3 品) は「秘宝を得た」に数えない・預かり物の時計は数える (2026-11-13)");
        {
            string exe = File.ReadAllText(Path.Combine(root, "Assets/Scripts/EventSystem/EventEffectExecutor.cs"));
            string ins = File.ReadAllText(Path.Combine(root, "Assets/Scripts/GameLoop/GameManager.Instability.cs"));
            int gt = exe.IndexOf("case EventEffectType.GainTruth:", StringComparison.Ordinal);
            Check(Regex.Matches(exe, @"CountsAsTakenRelic\(").Count == 2 && ins.Contains("if (!ItemIds.CountsAsTakenRelic(itemId)) return 0;")
                  && gt > 0 && !exe.Substring(gt, Math.Min(400, exe.Length - gt)).Contains("relicsGained"),
                  "不安定化: イベントの秘宝獲得 (名指し・品の指定)・報酬の秘宝が進行の資格の品を除く・「真理を得る」は数えない");
        }

        // 経路が判定を呼んでいるか (原文)
        string Src(string p) => File.ReadAllText(Path.Combine(root, p));
        string shop = Src("Assets/Scripts/ItemSystem/Shop/ShopManager.cs");
        int sell = shop.IndexOf("public bool TrySell(", StringComparison.Ordinal);
        int refuse = sell < 0 ? -1 : shop.IndexOf("!GameLoop.ItemIds.CanSell(id)", sell, StringComparison.Ordinal);
        int remove = sell < 0 ? -1 : shop.IndexOf("PassiveAddHelper.RemoveAt(run, listIndex)", sell, StringComparison.Ordinal);
        Check(refuse > sell && remove > refuse, "売却: ShopManager.TrySell は外す前に ItemIds.CanSell で断る (人・BOT・キー操作の共通の口)");
        Check(Src("Assets/Scripts/UI/Game2D/Screens/Game2DPhaseScreens.cs").Contains("ItemIds.CanSell(run.ownedPassiveItems[i])"), "売る画面: ItemIds.CanSell で並べない");
        string tiles = Src("Assets/Scripts/GameLoop/GameManager.Tiles.cs");
        Check(tiles.Contains("if (!ItemIds.CanTradeAway(owned[i])) continue;"), "取り替え: LowestTierPassiveIndex (交換マス・イベント) は ItemIds.CanTradeAway で外す");
        Check(tiles.Contains("if (ItemIds.CanBeLost(owned[i])) cands.Add(i);"), "偽の商人: LoseRandomPassiveItem は ItemIds.CanBeLost の品からだけ選ぶ");
        Check(Src("Assets/Scripts/AutoTest/AutoRunner.Shop.cs").Contains("if (!GameLoop.ItemIds.CanSell(id)) continue;"), "BOT の売却 (SellOneScore0): 売れない品を飛ばして次の品へ");
        var direct = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Replace('\\', '/').EndsWith("GameLoop/Ids.cs", StringComparison.Ordinal)) continue;
            foreach (var line in File.ReadAllLines(f))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//")) continue;
                if (Regex.IsMatch(code, @"\b(Entrusted|Undroppable|ProgressionItems)\.Contains\(")) direct.Add(Path.GetFileName(f) + ": " + code);
            }
        }
        Check(direct.Count == 0, "集合を直接読む所が Ids.cs の外に無い (経路は判定メソッドを呼ぶ)" + (direct.Count > 0 ? " ── " + string.Join(" / ", direct) : ""));

        // 確信の書き換え (本物の ConvictionSystem)
        var run = new GameLoop.RunState { convictionStage = 1 };
        run.seenPassiveItemIds.Add(GameLoop.ConvictionSystem.IdConviction);   // 確信は手に入れてから売った (古い中断セーブ)
        GameLoop.ConvictionSystem.OnEliteDefeated(run);
        Check(run.convictionStage == 2 && run.ownedPassiveItems.Count == 0, "エリート撃破: 確信の品が無くても段が 1 以上なら段 +1 (品は足さない)");
        Check(GameLoop.ConvictionSystem.PromoteForLayer6(run) && run.layer6Unlocked && run.ownedPassiveItems.Contains(GameLoop.ConvictionSystem.IdResolve),
              "深さ 5 のボス撃破: 確信を売ったランでも〈決意〉が持ち物に足される");
        Check(GameLoop.ConvictionSystem.RevealTruthInLayer6(run) && run.layer7Unlocked
              && run.ownedPassiveItems.Count == 1 && run.ownedPassiveItems[0] == GameLoop.ConvictionSystem.IdTruth,
              "灰に埋もれた基地 (真理を得る): 〈決意〉が〈真理〉に書き換わる (1 品のまま)");
        var held = new GameLoop.RunState { convictionStage = 1 };
        held.ownedPassiveItems.Add(GameLoop.ConvictionSystem.IdConviction);
        int adds = ItemSystem.Helpers.PassiveAddHelper.Adds;
        GameLoop.ConvictionSystem.PromoteForLayer6(held);
        Check(held.ownedPassiveItems.Count == 1 && held.ownedPassiveItems[0] == GameLoop.ConvictionSystem.IdResolve && ItemSystem.Helpers.PassiveAddHelper.Adds == adds,
              "深さ 5 のボス撃破: 確信を持っていれば書き換えるだけ (足さない)");
        var none = new GameLoop.RunState();
        GameLoop.ConvictionSystem.OnEliteDefeated(none);
        Check(!GameLoop.ConvictionSystem.PromoteForLayer6(none) && none.convictionStage == 0 && none.ownedPassiveItems.Count == 0,
              "災厄の予兆を受けていないラン: エリートでも段は 0・深さ 5 のボスで資格も品も得ない");
    }

    /// <summary>売値 0G の品 (2026-11-13・作者の決定): 〈ちいさな灯火〉〈シュヴァリエのレイピア〉は売値 0G (items.json の noSellValue)・
    /// 人は売れる (ItemIds.CanSell は true のまま)・BOT の売却 (SellOneScore0) は候補から外す。 規則 (SellPriceRule) は本物をコンパイル、
    /// 経路 (ShopManager・AutoRunner・画面) は UnityEngine に依存するので原文で見る。</summary>
    static void ZeroSellPrice(string root)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/Data/InventorySystem/items.json")));
        var zero = new List<string>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            bool flag = it.TryGetProperty("noSellValue", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
            // 幅は最悪 (0〜0 = basePrice の無い品) で見る: それでも 0G になるのは noSellValue の品だけ
            ItemSystem.Shop.SellPriceRule.Range(flag, 0, 0, out _, out int max);
            if (max <= 0) zero.Add(it.GetProperty("id").GetString());
        }
        zero.Sort(StringComparer.Ordinal);
        var want = new List<string> { "シュヴァリエのレイピア", "ちいさな灯火" }; want.Sort(StringComparer.Ordinal);
        Check(string.Join("・", zero) == string.Join("・", want), "売値 0G: 灯火とレイピアだけ (" + string.Join("・", zero) + ")");
        ItemSystem.Shop.SellPriceRule.Range(false, 0, 0, out int lo0, out int hi0);
        ItemSystem.Shop.SellPriceRule.Range(false, 5, 8, out int lo1, out int hi1);
        Check(lo0 == 1 && hi0 == 1 && lo1 == 5 && hi1 == 8, "売値: basePrice の無い品は 1G・ある品は basePrice の幅のまま");
        ItemSystem.Shop.SellPriceRule.Range(true, 5, 8, out int lo2, out int hi2);
        Check(lo2 == 0 && hi2 == 0, "売値: noSellValue の品は basePrice があっても 0G (買値は basePrice のまま)");
        foreach (var id in want) Check(GameLoop.ItemIds.CanSell(id), $"売値 0G: 〈{id}〉も人は売れる (CanSell に入れない)");

        string Src(string p) => File.ReadAllText(Path.Combine(root, p));
        string bot = Src("Assets/Scripts/AutoTest/AutoRunner.Shop.cs");
        int fn = bot.IndexOf("bool SellOneScore0()", StringComparison.Ordinal);
        int skip = fn < 0 ? -1 : bot.IndexOf("if (ItemSystem.Shop.ShopManager.SellsForNothing(id)) continue;", fn, StringComparison.Ordinal);
        int sell = fn < 0 ? -1 : bot.IndexOf("BotSelling.TrySellFromBot(run, i)", fn, StringComparison.Ordinal);
        Check(skip > fn && sell > skip, "BOT の売却 (SellOneScore0): 売値 0G の品を売る前に候補から外す");
        string shop = Src("Assets/Scripts/ItemSystem/Shop/ShopManager.cs");
        Check(shop.Contains("SellPriceRule.Range(data.noSellValue,") && shop.Contains("int price = ResolveSellPrice(id);") && !shop.Contains("Mathf.Max(1, Mathf.RoundToInt("),
              "売却: TrySell は SellRange (SellPriceRule) の値をそのまま入れる (0G を 1G に繰り上げない)");
        Check(Src("Assets/Scripts/UI/Game2D/Screens/Game2DPhaseScreens.cs").Contains("price = ShopManager.SellPriceLabel(id)"), "売る画面: 売値は SellPriceLabel (売却と同じ SellRange)");
        Check(Src("Assets/Scripts/ItemSystem/Items/ItemDatabase.cs").Contains("item.noSellValue = jsonItem.noSellValue;"), "読み込み: ItemDatabase が noSellValue を品へ写す");
        // 品の売値の幅 (sellPrice) を直接読む所が ShopManager.SellRange の外に無い (noSellValue を見落とさないため)
        var direct = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(f);
            if (name == "ItemDatabase.cs" || name == "ItemDataV2.cs") continue;   // 作る所と型の定義
            foreach (var line in File.ReadAllLines(f))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//") || code.StartsWith("///")) continue;
                if (Regex.IsMatch(code, @"\.sellPrice\b(?!"")") && !code.Contains("SellPriceRule.Range(data.noSellValue,")) direct.Add(name + ": " + code);
            }
        }
        Check(direct.Count == 0, "売値の幅を直接読む所が SellRange の外に無い" + (direct.Count > 0 ? " ── " + string.Join(" / ", direct) : ""));
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Rules();
        string dir = args.Length > 0 ? args[0] : "Assets/Resources/Events";
        Scan(dir);
        RoleRules();
        Selectability(dir);
        Observatory(dir);
        AshBase(dir);
        ToilInsight(Path.GetFullPath(Path.Combine(dir, "../../..")));
        Layers(dir);
        ClockRelic(Path.GetFullPath(Path.Combine(dir, "../../..")));   // dir = <root>/Assets/Resources/Events
        KeepConviction(Path.GetFullPath(Path.Combine(dir, "../../..")));
        ZeroSellPrice(Path.GetFullPath(Path.Combine(dir, "../../..")));
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
