// イベントの窓の頁割り (EventPaging・JpTextLayout・ModalJpMetrics) の検算。 Tools/event_page_check/run.py が Unity なしでコンパイルして回す。
//   画面と同じ関数 (Assets/Scripts/UI/Game2D/JpTextLayout.cs ほか) を、 焼いたドット字 (GameFont.DotPath の .bytes) の送り幅で回す。
//   1. 頁割りの規則 (段落ごとに 1 頁・長い段落は行で割る・最後の頁は選択肢に場所を空ける・行を捨てない)
//   2. 画面の配置 (2026-11-16・EventLayout): 本文 8 行が本文の板に収まる・板と列が画面に収まる・画面が使う字がドット字にある
//   3. 全イベント: 導入・結果の文が全部どこかの頁に出る (切り捨て 0・1 頁 8 行まで)・選択肢の列 (箱 + 説明の板) が画面に収まる・
//      箱の下の説明 (選べない理由・補足中の見込み) が 2 行に収まる。 名前が札と当たって「…」で切れる選択肢の数と一覧を出す
//   4. 背景の絵の対応表 (Resources/Game2DBackdrops/event_backdrops.txt): 全イベントに対応がある・表の名前が全部実在する・表の絵が全部ある (480×270 の PNG)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EventSystem;
using UI.Game2D;

static class Program
{
    static int fails;
    static Func<char, int> Adv;
    static int LineH;
    static Dictionary<int, int> Glyphs;

    static void Check(bool ok, string what)
    {
        if (!ok) { fails++; Console.WriteLine("NG  " + what); }
        else Console.WriteLine("ok  " + what);
    }

    // ===== 字の送り幅 (焼いたドット字・Tools/glyph_check.py と同じ読み方) =====
    static void LoadFont(string root)
    {
        var src = File.ReadAllText(Path.Combine(root, "Assets/Scripts/UI/GameFont.cs"));
        var m = Regex.Match(src, "\\bDotPath\\s*=\\s*\"([^\"]*)\"");
        var b = File.ReadAllBytes(Path.Combine(root, "Assets/Resources", m.Groups[1].Value + ".bytes"));
        if (b.Length < 9 || b[0] != 'M' || b[1] != 'J' || b[2] != 'F' || b[3] != '2') throw new Exception("ドット字の形式が違う");
        int cell = b[4], n = BitConverter.ToInt32(b, 5), o = 9;
        var map = new Dictionary<int, int>();
        for (int i = 0; i < n && o + 5 + 2 * cell <= b.Length; i++) { map[(int)BitConverter.ToUInt32(b, o)] = b[o + 4]; o += 5 + 2 * cell; }
        Glyphs = map;
        Adv = ch => map.TryGetValue(ch, out var a) ? a : cell / 2;      // Game2DJpFont.Advance と同じ (無い字は字の高さの半分)
        LineH = cell + 1;                                                // Game2DJpFont.LineH
        Console.WriteLine($"ドット字: 字の高さ {cell}・行送り {LineH}・{map.Count} 字・本文の幅 {EventLayout.BodyW} dot (本文の板 {EventLayout.PlateW}×{EventLayout.PlateH})・選択肢の列の幅 {EventLayout.ColW}");
    }

    // ===== 1. 規則 =====
    static void Rules()
    {
        Func<char, int> a10 = ch => 10;   // 1 字 10 dot・幅 30 = 1 行 3 字
        var p = JpTextLayout.Pages("一二三四五六七八九／十", 30, 2, 2, a10);
        Check(p.Count == 3 && p[0].Count == 2 && p[1].Count == 1 && p[2].Count == 1 && p[2][0] == "十", "段落ごとに頁・長い段落は行で割る (3 字 × 3 行 → 2 行 + 1 行・次の段落は新しい頁)");
        var q = JpTextLayout.Pages("一二三四五六七八九", 30, 3, 1, a10);
        Check(q.Count == 2 && q[0].Count == 2 && q[1].Count == 1 && q[1][0] == "七八九", "最後の頁は lastPage 行まで (溢れる行は前の頁へ)");
        var z = JpTextLayout.Pages("一二三", 30, 3, 0, a10);
        Check(z.Count == 2 && z[1].Count == 0, "lastPage = 0 なら本文の無い頁を最後に足す");
        var e = JpTextLayout.Pages("", 30, 3, 3, a10);
        Check(e.Count == 1 && e[0].Count == 0, "空の文は 1 頁 (行 0)");
        var t = JpTextLayout.Pages(" 一二 ／ 　／三 ", 30, 3, 3, a10);
        Check(t.Count == 2 && t[0][0] == "一二" && t[1][0] == "三", "区切りの前後の空白を落とし、 空の段落は捨てる");
        Check(!string.Join("", JpTextLayout.Pages("一／二", 30, 3, 3, a10).SelectMany(x => x)).Contains('／'), "区切りの「／」は画面に出さない");
    }

    // ===== 状況の総当たり (選べない理由の文を全部集める・Tools/event_branch_check と同じ場面) =====
    static IEnumerable<Action<GameLoop.GameManager>> Scenes()
    {
        foreach (bool rich in new[] { true, false })
            foreach (bool trade in new[] { true, false })
                foreach (bool forge in new[] { true, false })
                    foreach (int mapKind in new[] { 0, 1, 2, 3, 4 })
                        yield return gm =>
                        {
                            gm.Run.coins = rich ? 9999 : 0; gm.Run.provision = rich ? 9999 : 0; gm.Run.weaponMaterials = rich ? 9999 : 0;
                            gm.FreeMap = mapKind == 0 ? null : new MapSystem.FreeMove.FreeMapSim
                            {
                                FoeAlive = mapKind == 1 || mapKind == 3,
                                Weather = mapKind >= 3 ? MapSystem.FreeMove.WeatherKind.Hail : MapSystem.FreeMove.WeatherKind.None,
                                FrontArrived = mapKind == 4,
                            };
                            GameLoop.GameManager.TradeOk = trade; GameLoop.GameManager.ForgeCost = forge ? 3 : int.MaxValue;
                        };
    }

    sealed class Ev { public string file; public EventDefinition def; public List<string> descs = new List<string>(); public bool anyDesc; }

    static int BranchCount(EventChoice c)
    {
        foreach (var e in c.effects) if (e.type == EventEffectType.Probability && e.branches != null && e.branches.Count >= 2) return e.branches.Count;
        return 0;
    }

    /// <summary>結果の行の見積り (EventEffectExecutor は効果 1 つにつき 1 行・確率分岐は「確率分岐: N% 抽選」の 1 行 + 引いた分岐の効果)。
    /// 中身は長めの品名で埋める (行数の見積りなので、 1 行に収まる長さの代表)。</summary>
    static List<string> LogLines(EventChoice c, int branch)
    {
        var list = new List<string>();
        void Add(EventEffect e)
        {
            if (e.type == EventEffectType.Probability)
            {
                list.Add("確率分岐: 50% 抽選");
                if (e.branches != null && e.branches.Count > 0) foreach (var x in e.branches[Math.Max(0, Math.Min(branch, e.branches.Count - 1))]) Add(x);
                return;
            }
            if (e.type == EventEffectType.None) return;
            list.Add(e.type + ": 〈長めの品名の代表〉 (現在 999/999)");
        }
        foreach (var e in c.effects) Add(e);
        return list;
    }

    static string ResultText(EventChoice c, int branch)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var l in LogLines(c, branch)) sb.Append("・").Append(l).Append('\n');
        var after = c.PostFlavorFor(branch);
        if (!string.IsNullOrEmpty(after)) sb.Append(after);
        var s = sb.ToString().TrimEnd('\n');
        return s.Length == 0 ? "……" : s;
    }

    static List<string> AllLines(string text)
    {
        var l = new List<string>();
        foreach (var para in JpTextLayout.Paragraphs(text)) l.AddRange(JpTextLayout.Lines(para, EventLayout.BodyW, Adv));
        return l;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string root = args.Length > 0 ? args[0] : ".";
        LoadFont(root);
        Rules();

        // ===== 2. 画面の配置 =====
        Check(EventLayout.BodyLinesFit(LineH) >= EventPaging.TextLines, $"本文 {EventPaging.TextLines} 行が本文の板に収まる (入る行数 {EventLayout.BodyLinesFit(LineH)})");
        Check(EventLayout.PlateX >= EventLayout.SafeL && EventLayout.PlateY >= LcdSafeArea.T && EventLayout.PlateY + EventLayout.PlateH <= LcdSafeArea.B
              && EventLayout.PlateX + EventLayout.PlateW <= EventLayout.ColX,
              $"本文の板がイベントの余白 (x {EventLayout.SafeL}〜{EventLayout.SafeR}・y {LcdSafeArea.T}〜{LcdSafeArea.B}・左右は液晶の安全域 {LcdSafeArea.L} より狭いイベントだけの値) に収まり、 選択肢の列と重ならない (板 x {EventLayout.PlateX} y {EventLayout.PlateY} {EventLayout.PlateW}×{EventLayout.PlateH})");
        Check(EventLayout.ColX + EventLayout.ColW <= EventLayout.SafeR, $"選択肢の列がイベントの余白の右に収まる (列 x {EventLayout.ColX} 幅 {EventLayout.ColW})");
        string need = "…〜0123456789G素材物資手放す次へ前選ぶ補足されている。最短ターンで捕まる中";
        var missing = need.Where(ch => !Glyphs.ContainsKey(ch)).Distinct().ToList();
        Check(missing.Count == 0, "画面が使う字がドット字にある" + (missing.Count > 0 ? " (無い字: " + string.Join("", missing) + ")" : ""));

        var gm = new GameLoop.GameManager { Run = new GameLoop.RunState() };
        GameLoop.GameManager.Instance = gm;
        var scenes = Scenes().ToList();
        var evs = new List<Ev>();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "Assets/Resources/Events"), "*.txt").OrderBy(x => x, StringComparer.Ordinal))
            foreach (var def in EventParser.ParseAll(File.ReadAllText(path)))
            {
                var ev = new Ev { file = Path.GetFileName(path), def = def };
                foreach (var c in def.choices)
                {
                    // 箱の下の説明の全部の形: 選べない理由 (無し・どれか) × 補足中の見込み (無し・N ターンで捕まる・最短 99 ターン)
                    var reasons = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var sc in scenes) { sc(gm); var r = c.UnavailableReason(gm.Run); if (r != null) reasons.Add(r); }
                    var whys = new List<string> { null }; whys.AddRange(reasons);
                    foreach (var why in whys)
                        foreach (int eta in new[] { -1, 1, 99 })
                        {
                            var d = c.NoteText(why, eta);
                            if (!string.IsNullOrEmpty(d)) { ev.descs.Add(d); ev.anyDesc = true; }
                        }
                }
                evs.Add(ev);
            }
        GameLoop.GameManager.Instance = null;
        int choicesAll = evs.Sum(e => e.def.choices.Count);
        Console.WriteLine($"イベント {evs.Count}・選択肢 {choicesAll}・選択肢の最大 {evs.Max(e => e.def.choices.Count)}");

        // ===== 3. 全イベント =====
        int texts = 0, cut = 0, colOver = 0, noteOver = 0, nameCut = 0, titleCut = 0, pagesMax = 0, multi = 0;
        string pagesMaxWhat = "";
        var cutNames = new List<string>();
        var pageCounts = new List<string>();
        foreach (var ev in evs)
        {
            var d = ev.def; int k = d.choices.Count;
            var plan = EventPaging.Intro(d.flavor, Adv);
            texts++;
            pageCounts.Add(d.name + "\t" + plan.Count);
            if (plan.Count > 1) multi++;
            if (!plan.pages.SelectMany(x => x).SequenceEqual(AllLines(d.flavor))) { cut++; Console.WriteLine($"  NG 切り捨て: 〈{d.name}〉 導入"); }
            foreach (var pg in plan.pages) if (pg.Count > EventPaging.TextLines) { cut++; Console.WriteLine($"  NG 頁の行数: 〈{d.name}〉 {pg.Count} 行"); }
            if (plan.Count > pagesMax) { pagesMax = plan.Count; pagesMaxWhat = $"〈{d.name}〉 導入"; }
            // 題 (頁の位置「9/9」の分を空けて)。 切れても「…」で出る
            int titleRoom = EventLayout.BodyW - (plan.Count > 1 ? EventLayout.Width(plan.Count + "/" + plan.Count, Adv) + 8 : 0);
            if (EventLayout.Width(d.name, Adv) > titleRoom) { titleCut++; Console.WriteLine($"  題が切れる: 〈{d.name}〉 {EventLayout.Width(d.name, Adv)} > {titleRoom} dot"); }
            // 選択肢の列 (箱 k 個 + 説明の板の最大)
            int noteMax = 0;
            foreach (var s in ev.descs)
            {
                int n = JpTextLayout.Lines(s, EventLayout.NoteW, Adv).Count;
                if (n > EventLayout.NoteLines) { noteOver++; Console.WriteLine($"  NG 説明が {EventLayout.NoteLines} 行に収まらない: 〈{d.name}〉「{s.Replace("\n", "／")}」 {n} 行"); }
                noteMax = Math.Max(noteMax, Math.Min(n, EventLayout.NoteLines));
            }
            if (!EventLayout.ColumnFits(k, EventLayout.NoteH(noteMax, LineH))) { colOver++; Console.WriteLine($"  NG 選択肢の列が画面に収まらない: 〈{d.name}〉 選択肢 {k}・説明 {noteMax} 行"); }
            // 名前が札と当たって切れるか
            foreach (var c in d.choices)
            {
                var chips = c.CostChips();
                int room = EventLayout.NameRoom(EventLayout.ChipsW(chips, Adv)), w = EventLayout.Width(c.text, Adv);
                if (w > room)
                {
                    nameCut++;
                    cutNames.Add($"〈{d.name}〉「{c.text}」 {w} > {room} dot (札: {(chips.Count == 0 ? "なし" : string.Join(" ", chips.Select(x => (x.turns ? "砂" : "") + x.text)))})");
                }
            }
            for (int i = 0; i < k; i++)
            {
                var c = d.choices[i]; int nb = BranchCount(c);
                for (int b = 0; b < Math.Max(1, nb); b++)
                {
                    string text = ResultText(c, nb > 0 ? b : -1);
                    var rp = EventPaging.Result(text, Adv);
                    texts++;
                    if (!rp.pages.SelectMany(x => x).SequenceEqual(AllLines(text))) { cut++; Console.WriteLine($"  NG 切り捨て: 〈{d.name}〉「{c.text}」 結果"); }
                    foreach (var pgl in rp.pages) if (pgl.Count > EventPaging.TextLines) { cut++; Console.WriteLine($"  NG 頁の行数: 〈{d.name}〉「{c.text}」 結果"); }
                    if (rp.Count > pagesMax) { pagesMax = rp.Count; pagesMaxWhat = $"〈{d.name}〉「{c.text}」 結果"; }
                }
            }
        }
        if (args.Length > 1) File.WriteAllLines(args[1], pageCounts);      // 導入の頁数の一覧 (配置を変えた時の比べ用)
        Console.WriteLine($"文 {texts} 本 (導入・結果は分岐ごと)・頁の最大 {pagesMax} ({pagesMaxWhat})・導入が 2 頁以上のイベント {multi}");
        foreach (var ev in evs.Where(e => e.def.name.StartsWith("さびれた観測所")))
        {
            var plan = EventPaging.Intro(ev.def.flavor, Adv);
            Console.WriteLine($"  〈{ev.def.name}〉 導入 {ev.def.flavor.Length} 字 → {plan.Count} 頁 (行数 {string.Join("・", plan.pages.Select(x => x.Count))})・選択肢 {ev.def.choices.Count}");
        }
        int kMax = evs.Max(e => e.def.choices.Count), noteHMax = EventLayout.NoteH(EventLayout.NoteLines, LineH);
        Console.WriteLine($"選択肢の列: 最多 {kMax} つ + 説明 {EventLayout.NoteLines} 行 = 高さ {EventLayout.ColumnH(kMax, noteHMax)}・上端 {EventLayout.ColumnTop(kMax, noteHMax)}");
        Console.WriteLine($"名前が「…」で切れる選択肢 {nameCut} / {choicesAll}:");
        foreach (var x in cutNames) Console.WriteLine("  " + x);
        Console.WriteLine($"題が切れるイベント {titleCut}");
        // ===== 4. 背景の絵の対応表 (2026-11-16・EventBackdropTable) =====
        //   全イベントに対応がある・表の名前が全部実在する (改名で黙って層の絵へ落ちない)・表の絵が全部ある (480×270 の PNG)
        {
            string dir = Path.Combine(root, "Assets/Resources/Game2DBackdrops");
            var bad = new List<string>();
            var table = EventBackdropTable.Parse(File.ReadAllText(Path.Combine(root, "Assets/Resources", EventBackdropTable.ResourcePath + ".txt")), bad);
            var names = new HashSet<string>(evs.Select(e => e.def.name));
            var noArt = evs.Where(e => !table.ContainsKey(e.def.name)).Select(e => e.def.name).Distinct().ToList();
            var ghost = table.Keys.Where(k => !names.Contains(k)).ToList();
            var artBad = new List<string>();
            foreach (var art in table.Values.Distinct())
            {
                string f = Path.Combine(dir, art + ".png.bytes");
                if (!File.Exists(f)) { artBad.Add(art + " (無い)"); continue; }
                var b = File.ReadAllBytes(f);
                bool png = b.Length > 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G';
                int w = png ? (b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19]) : 0, h = png ? (b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23]) : 0;
                if (!png || w != 480 || h != 270) artBad.Add($"{art} (PNG {png}・{w}×{h})");
            }
            Console.WriteLine($"背景の絵の対応表: {table.Count} 行・絵 {table.Values.Distinct().Count()} 種類");
            foreach (var x in noArt) Console.WriteLine("  対応が無いイベント (層の絵になる): " + x);
            foreach (var x in ghost) Console.WriteLine("  表にあるのに存在しないイベント名: " + x);
            foreach (var x in artBad) Console.WriteLine("  絵が無い・大きさが違う: " + x);
            foreach (var x in bad) Console.WriteLine("  読めない行: " + x);
            Check(bad.Count == 0, $"背景の対応表の読めない行 {bad.Count} 件");
            Check(noArt.Count == 0, $"背景の対応が無いイベント {noArt.Count} 件");
            Check(ghost.Count == 0, $"背景の対応表にあるのに存在しないイベント名 {ghost.Count} 件");
            Check(artBad.Count == 0, $"背景の対応表の絵が無い・480×270 の PNG でない {artBad.Count} 件");
        }
        Check(cut == 0, $"切り捨てられる文 {cut} 件 (全部の行がどこかの頁に出る・1 頁 {EventPaging.TextLines} 行まで)");
        Check(colOver == 0 && EventLayout.ColumnFits(kMax, noteHMax), $"選択肢の列 (箱 + 説明の板) が画面に収まらないもの {colOver} 件");
        Check(noteOver == 0, $"{EventLayout.NoteLines} 行に収まらない説明 (選べない理由・補足中の見込み) {noteOver} 件");
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
