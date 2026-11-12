// 確率分岐ごとの文 (EventBranchText) の検算。 Tools/event_branch_check/run.py が Unity なしでコンパイルして回す。
//   1. 切り分けの規則 (分岐 2 つ・3 つ・区切りなし・数が合わない・確率分岐なしで区切った)
//   2. Assets/Resources/Events/*.txt の全選択肢を、 解析 (EventParser) と同じ切り方で読み、
//      区切った文の数が分岐の数と合わない行を挙げる (合わない行は、 ゲームでは警告して先頭の文を使う)
//   3. 選べない選択肢の規則 (2026-11-13・EventEffectRoles / EventChoice.UnavailableReason): 暗くするのは払えない時と、
//      得になる効果が何ひとつ起きない時だけ。 全イベントを本物の解析 (EventParser) で読み、 状況を総当たりして
//      「暗くなり得る選択肢」の一覧と理由を出し、 全部が同時に暗くなり得るイベントが 0 件であることを見る
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

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Rules();
        string dir = args.Length > 0 ? args[0] : "Assets/Resources/Events";
        Scan(dir);
        RoleRules();
        Selectability(dir);
        Observatory(dir);
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
