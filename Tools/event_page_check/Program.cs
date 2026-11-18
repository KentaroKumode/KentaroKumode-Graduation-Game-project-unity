// イベントの窓の頁割り (EventPaging・JpTextLayout・ModalJpMetrics) の検算。 Tools/event_page_check/run.py が Unity なしでコンパイルして回す。
//   画面と同じ関数 (Assets/Scripts/UI/Game2D/JpTextLayout.cs ほか) を、 焼いたドット字 (GameFont.DotPath の .bytes) の送り幅で回す。
//   1. 頁割りの規則 (段落ごとに 1 頁・長い段落は行で割る・最後の頁は選択肢に場所を空ける・行を捨てない)
//   2. 画面の配置 (2026-11-16・EventLayout): 本文 8 行が本文の板に収まる・板と列が画面に収まる・画面が使う字がドット字にある
//   3. 全イベント: 導入・結果の文が全部どこかの頁に出る (切り捨て 0・1 頁 8 行まで)・選択肢の列 (箱 + 説明の板) が画面に収まる・
//      箱の下の説明 (選べない理由・捕捉中の見込み) が 2 行に収まる。 名前が札と当たって「…」で切れる選択肢の数と一覧を出す
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
        // 設定の行間・字間 (2026-11-16・GameSettings.LineGap / CharGap): 環境変数 EVPC_GAPS="行間,字間" で与える (run.py が 0,0 と最大の 2 回回す)
        int lineGap = 0, charGap = 0;
        var gaps = Environment.GetEnvironmentVariable("EVPC_GAPS");
        if (!string.IsNullOrEmpty(gaps)) { var sp = gaps.Split(','); lineGap = int.Parse(sp[0]); charGap = int.Parse(sp[1]); }
        Adv = ch => (map.TryGetValue(ch, out var a) ? a : cell / 2) + charGap;      // Game2DJpFont.Advance と同じ (無い字は字の高さの半分・字間を足す)
        LineH = cell + 1 + lineGap;                                                  // Game2DJpFont.LineH (行間を足す)
        Console.WriteLine($"[設定] 行間 +{lineGap}・字間 +{charGap}");
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
        // 折り返しの決まり (2026-11-17): 語の途中で折らない・禁則・短すぎる最後の行を防ぐ
        var w1 = JpTextLayout.Lines("足もとの水たまりに、自分のものではない顔が映っている。", 250, a10);
        Check(w1.Count == 2 && w1[1] == "映っている。", "語の途中で折らない (1 行 25 字の幅: 「映ってい／る。」でなく「映っている。」を次の行へ) → " + string.Join("／", w1));
        var w2 = JpTextLayout.Lines("一二三四五六七八九。", 90, a10);
        Check(w2.All(l => l.Length == 0 || !JpTextLayout.NoStart(l[0])), "行頭に句点を置かない (追い出し) → " + string.Join("／", w2));
        var w3 = JpTextLayout.Lines("一二三四五六七八「九十」", 90, a10);
        Check(w3.All(l => l.Length == 0 || !JpTextLayout.NoEnd(l[l.Length - 1])), "行末に開き括弧を置かない → " + string.Join("／", w3));
        var w4 = JpTextLayout.Lines("あ\n\nい\n", 90, a10);
        Check(w4.Count == 3 && w4[1] == "" && w4[2] == "い", "改行文字の扱いは前と同じ (空行は残し、 末尾の改行は行にしない)");
        var w6 = JpTextLayout.Lines("そっとあなたへ差し出す", 80, a10);
        Check(w6.All(l => l.Length == 0 || l[0] != 'へ'), "ひらがな同士の間では折らない (行頭に「へ」を置かない) → " + string.Join("／", w6));
        var w7 = JpTextLayout.Lines("隊商付きの売り子らしく", 60, a10);
        Check(!w7.Any(l => l.EndsWith("売り")), "割らない語の中で折らない (「売り／子」) → " + string.Join("／", w7));
        var w8 = JpTextLayout.Lines("顔をまともに見た。「私の勘定にはない」", 120, a10);
        Check(!w8.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, "「.{0,3}$")), "開き括弧の後ろ 3 字以下で行を終えない → " + string.Join("／", w8));
        var w9 = JpTextLayout.Lines("ゆっくりとあなたはそれをひろいあげる", 90, a10);
        Check(w9.Take(w9.Count - 1).All(l => l.EndsWith("は") || l.EndsWith("を") || l.EndsWith("へ")), "ひらがな同士でも「を」「へ」「は」の後では折れる → " + string.Join("／", w9));
        var w10 = JpTextLayout.Lines("一二三四五六七——八九十", 80, a10);
        Check(!w10.Any(l => l.EndsWith("—") && !l.EndsWith("——")) && w10.Any(l => l.StartsWith("——")), "「——」は 2 字の間で割らず、 行頭に来てよい → " + string.Join("／", w10));
        Check(JpTextLayout.DialogueBreaks("男は言った。「行け」。そして去った。") == "男は言った。\n「行け」。\nそして去った。", "文の頭の台詞の前後で改行する (「」。」は「。」の後)");
        Check(JpTextLayout.DialogueBreaks("机には「誰も帰らなかった」とだけある。") == "机には「誰も帰らなかった」とだけある。", "文の途中の「」には触らない");
        Check(JpTextLayout.DialogueBreaks("「行け」と男は言った。") == "「行け」と男は言った。", "「」と」の後では改行しない");
        Check(JpTextLayout.DialogueBreaks("「一」「二」") == "「一」\n「二」", "続く台詞の間で改行する");
        var pd = JpTextLayout.Pages("一二三。「四五六七八九」", 40, 2, 2, a10);
        Check(pd.Count == 2 && pd[0].Count == 1 && pd[1][0].StartsWith("「"), "頁の後ろ半分に台詞の境目があればそこで頁を切る → " + string.Join(" | ", pd.Select(x => string.Join("／", x))));
        var p9 = JpTextLayout.Pages("一二三四五六七八九", 10, 8, 8, a10);
        Check(p9.Count == 2 && p9[0].Count == 7 && p9[1].Count == 2, "段落が 1 行だけはみ出す時は前の頁から 1 行下ろす (8 + 1 → 7 + 2)");
        var w5 = JpTextLayout.Lines("あいうえおかきくけこさしすせそ", 50, a10);
        Check(string.Concat(w5) == "あいうえおかきくけこさしすせそ" && w5.All(l => l.Length <= 5), "文節が 1 行より長ければ字の間で折る (字を捨てない・幅を超えない)");
    }

    // ===== 折れ方の監査 (2026-11-17): 本文の板の幅で、 段 (改行文字で区切った 1 まとまり) ごとに折って、 悪い折れ方を数える =====
    static int auditKinsoku, auditOver, auditWidow, auditMid, auditLines, auditLonePage;
    static readonly List<string> auditList = new List<string>();
    static void Audit(string label, string text, int maxW)
    {
        // 段落が次の頁へ 1 行だけはみ出して、 1 行だけの頁になる (2026-11-17・前の頁から 1 行下ろす決まりで 0 になるはず)
        int per = EventPaging.LinesPerPage(LineH);
        foreach (var para in JpTextLayout.Paragraphs(text))
        {
            var pp = JpTextLayout.Pages(para, maxW, per, per, Adv);
            if (pp.Count > 1 && pp[pp.Count - 1].Count == 1) { auditLonePage++; auditList.Add($"1 行だけの頁: {label}「{pp[pp.Count - 1][0]}」"); }
        }
        foreach (var para in JpTextLayout.Paragraphs(text))
            foreach (var seg in JpTextLayout.DialogueBreaks(para).Split('\n'))
            {
                if (seg.Length == 0) continue;
                var ls = JpTextLayout.Lines(seg, maxW, Adv);
                var dump = Environment.GetEnvironmentVariable("EVPC_DUMP");      // 折れ方を目で見る時: 書き出し先 (追記)
                if (!string.IsNullOrEmpty(dump)) File.AppendAllText(dump, label + "\n" + string.Join("\n", ls.Select(x => "  |" + x + "|")) + "\n");
                int pos = 0;
                for (int k = 0; k < ls.Count; k++)
                {
                    auditLines++;
                    var l = ls[k];
                    int at = seg.IndexOf(l, pos, StringComparison.Ordinal);
                    int w = l.Sum(c => Adv(c));
                    if (w > maxW) { auditOver++; auditList.Add($"幅を超える: {label}「{l}」 {w} > {maxW}"); }
                    if (k > 0)
                    {
                        if (l.Length > 0 && JpTextLayout.NoStart(l[0])) { auditKinsoku++; auditList.Add($"行頭禁則: {label}「{ls[k - 1]}／{l}」"); }
                        var prev = ls[k - 1];
                        if (prev.Length > 0 && JpTextLayout.NoEnd(prev[prev.Length - 1])) { auditKinsoku++; auditList.Add($"行末禁則: {label}「{prev}／{l}」"); }
                        bool afterSpace = at > 0 && (seg[at - 1] == ' ' || seg[at - 1] == '　');
                        if (!afterSpace && at > 0 && !JpTextLayout.BreakAllowed(seg, at) && "」』）】〕〉》".IndexOf(seg[at - 1]) < 0 && !(JpTextLayout.NoSplit(seg[at]) && seg[at - 1] != seg[at])) { auditMid++; auditList.Add($"語の途中: {label}「{ls[k - 1]}／{l}」"); }
                    }
                    pos = at + l.Length;
                }
                if (ls.Count >= 2)
                {
                    var last = ls[ls.Count - 1];
                    int real = last.Count(c => !JpTextLayout.NoStart(c) && c != ' ' && c != '　');
                    if (real <= JpTextLayout.WidowChars) { auditWidow++; auditList.Add($"短い最後の行: {label}「{ls[ls.Count - 2]}／{last}」"); }
                }
            }
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

    /// <summary>頁割りを目で見る時の書き出し (環境変数 EVPC_PAGES = 書き出し先・追記)。</summary>
    static void DumpPages(string label, EventPaging.Plan plan)
    {
        var f = Environment.GetEnvironmentVariable("EVPC_PAGES");
        if (string.IsNullOrEmpty(f)) return;
        var sb = new System.Text.StringBuilder("■" + label + "\n");
        for (int i = 0; i < plan.Count; i++) { foreach (var l in plan.pages[i]) sb.Append("    ").Append(l).Append('\n'); sb.Append("    ── ").Append(i + 1).Append('/').Append(plan.Count).Append('\n'); }
        File.AppendAllText(f, sb.ToString());
    }

    static List<string> AllLines(string text)
    {
        var l = new List<string>();
        foreach (var para in JpTextLayout.Paragraphs(text)) l.AddRange(JpTextLayout.Lines(JpTextLayout.DialogueBreaks(para), EventLayout.BodyW, Adv));
        return l;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string root = args.Length > 0 ? args[0] : ".";
        LoadFont(root);
        Rules();

        // ===== 2. 画面の配置 =====
        Check(EventLayout.BodyLinesFit(LineH) >= EventPaging.LinesPerPage(LineH), $"1 頁の本文 {EventPaging.LinesPerPage(LineH)} 行が本文の板に収まる (入る行数 {EventLayout.BodyLinesFit(LineH)}・標準は {EventPaging.TextLines} 行)");
        Check(EventLayout.PlateX >= EventLayout.SafeL && EventLayout.PlateY >= LcdSafeArea.T && EventLayout.PlateY + EventLayout.PlateH <= LcdSafeArea.B
              && EventLayout.PlateX + EventLayout.PlateW <= EventLayout.ColX,
              $"本文の板がイベントの余白 (x {EventLayout.SafeL}〜{EventLayout.SafeR}・y {LcdSafeArea.T}〜{LcdSafeArea.B}・左右は液晶の安全域 {LcdSafeArea.L} より狭いイベントだけの値) に収まり、 選択肢の列と重ならない (板 x {EventLayout.PlateX} y {EventLayout.PlateY} {EventLayout.PlateW}×{EventLayout.PlateH})");
        Check(EventLayout.ColX + EventLayout.ColW <= EventLayout.SafeR, $"選択肢の列がイベントの余白の右に収まる (列 x {EventLayout.ColX} 幅 {EventLayout.ColW})");
        // 上の帯 (2026-11-16): 最も長い値で横に収まる・選択肢の列 (最多 5 つ + 説明 2 行) と重ならない・本文の板と重ならない
        {
            var longest = new List<EventLayout.BarItem>
            {
                new EventLayout.BarItem { label = "HP", value = "999/999" }, new EventLayout.BarItem { label = "物資", value = "10" },
                new EventLayout.BarItem { value = "999G" }, new EventLayout.BarItem { label = "素材", value = "99" },
                new EventLayout.BarItem { label = "ターン", value = "999" }, new EventLayout.BarItem { label = "捕捉中", value = "あと99", warn = true },
            };
            int bw = EventLayout.BarContentW(longest, Adv);
            Check(bw <= EventLayout.BarW, $"上の帯に最も長い値 (HP 999/999・物資 10・999G・素材 99・ターン 999・捕捉中 あと99) が収まる ({bw} ≦ {EventLayout.BarW} dot・帯 x {EventLayout.BarX}〜{EventLayout.BarX + EventLayout.BarW} y {EventLayout.BarY}〜{EventLayout.BarY + EventLayout.BarH})");
            int colTop = EventLayout.ColumnTop(5, EventLayout.NoteH(EventLayout.NoteLines, LineH));
            Check(colTop >= EventLayout.BarY + EventLayout.BarH, $"選択肢の列 (5 つ + 説明 2 行・上端 {colTop}) が上の帯 (下端 {EventLayout.BarY + EventLayout.BarH}) と重ならない");
            Check(EventLayout.PlateY >= EventLayout.BarY + EventLayout.BarH, "本文の板が上の帯と重ならない");
        }
        string need = "…〜0123456789G素材物資手放す次へ前選ぶ捕捉されている。最短ターンで捕まる中HP/あと";
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
                    // 箱の下の説明の全部の形: 選べない理由 (無し・どれか) × 捕捉中の見込み (無し・N ターンで捕まる・最短 99 ターン)
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
            var plan = EventPaging.Intro(d.flavor, Adv, LineH);
            Audit($"〈{d.name}〉 導入", d.flavor, EventLayout.BodyW);
            DumpPages($"〈{d.name}〉 導入", plan);
            texts++;
            pageCounts.Add(d.name + "\t" + plan.Count);
            if (plan.Count > 1) multi++;
            if (!plan.pages.SelectMany(x => x).SequenceEqual(AllLines(d.flavor))) { cut++; Console.WriteLine($"  NG 切り捨て: 〈{d.name}〉 導入"); }
            foreach (var pg in plan.pages) if (pg.Count > EventPaging.LinesPerPage(LineH)) { cut++; Console.WriteLine($"  NG 頁の行数: 〈{d.name}〉 {pg.Count} 行"); }
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
                    var rp = EventPaging.Result(text, Adv, LineH);
                    DumpPages($"〈{d.name}〉「{c.text}」 結果{(nb > 0 ? " 分岐" + (b + 1) : "")}", rp);
                    var after = c.PostFlavorFor(nb > 0 ? b : -1);
                    if (!string.IsNullOrEmpty(after)) Audit($"〈{d.name}〉「{c.text}」 結果{(nb > 0 ? " 分岐" + (b + 1) : "")}", after, EventLayout.BodyW);
                    texts++;
                    if (!rp.pages.SelectMany(x => x).SequenceEqual(AllLines(text))) { cut++; Console.WriteLine($"  NG 切り捨て: 〈{d.name}〉「{c.text}」 結果"); }
                    foreach (var pgl in rp.pages) if (pgl.Count > EventPaging.LinesPerPage(LineH)) { cut++; Console.WriteLine($"  NG 頁の行数: 〈{d.name}〉「{c.text}」 結果"); }
                    if (rp.Count > pagesMax) { pagesMax = rp.Count; pagesMaxWhat = $"〈{d.name}〉「{c.text}」 結果"; }
                }
            }
        }
        if (args.Length > 1) File.WriteAllLines(args[1], pageCounts);      // 導入の頁数の一覧 (配置を変えた時の比べ用)
        Console.WriteLine($"文 {texts} 本 (導入・結果は分岐ごと)・頁の最大 {pagesMax} ({pagesMaxWhat})・導入が 2 頁以上のイベント {multi}");
        foreach (var ev in evs.Where(e => e.def.name.StartsWith("さびれた観測所")))
        {
            var plan = EventPaging.Intro(ev.def.flavor, Adv, LineH);
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
        Console.WriteLine($"折れ方の監査 (導入・選んだ後の文・{auditLines} 行): 禁則 {auditKinsoku}・幅超え {auditOver}・語の途中 {auditMid}・短い最後の行 {auditWidow}・1 行だけの頁 {auditLonePage}");
        foreach (var x in auditList) Console.WriteLine("  " + x);
        Check(auditKinsoku == 0, $"禁則を破る折れ方 {auditKinsoku} 件");
        Check(auditOver == 0, $"本文の板の幅を超える行 {auditOver} 件");
        Check(auditLonePage == 0, $"段落が 1 行だけはみ出した頁 {auditLonePage} 件");
        Check(cut == 0, $"切り捨てられる文 {cut} 件 (全部の行がどこかの頁に出る・1 頁 {EventPaging.LinesPerPage(LineH)} 行まで)");
        Check(colOver == 0 && EventLayout.ColumnFits(kMax, noteHMax), $"選択肢の列 (箱 + 説明の板) が画面に収まらないもの {colOver} 件");
        Check(noteOver == 0, $"{EventLayout.NoteLines} 行に収まらない説明 (選べない理由・捕捉中の見込み) {noteOver} 件");
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
