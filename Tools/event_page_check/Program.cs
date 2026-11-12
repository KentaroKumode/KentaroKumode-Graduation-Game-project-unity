// イベントの窓の頁割り (EventPaging・JpTextLayout・ModalJpMetrics) の検算。 Tools/event_page_check/run.py が Unity なしでコンパイルして回す。
//   画面と同じ関数 (Assets/Scripts/UI/Game2D/JpTextLayout.cs ほか) を、 焼いたドット字 (GameFont.DotPath の .bytes) の送り幅で回す。
//   1. 頁割りの規則 (段落ごとに 1 頁・長い段落は行で割る・最後の頁は選択肢に場所を空ける・行を捨てない)
//   2. 直す前 (2026-11-13 まで) の窓: 導入は 5 行・結果は 8 行で切り捨て、 選択肢は窓の高さ 266 に入る分だけ描いていた ── 溢れていた文と選択肢の一覧
//   3. 直した後の窓: 全イベントの導入・結果の文が全部どこかの頁に出る (切り捨て 0)・どの頁も窓が 266 (格子 270) に収まる・
//      選択肢が全部描ける (説明の 2 行が付いた時も)・選択肢の名前と説明 (選べない理由・使うターン) が切れない
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
        Adv = ch => map.TryGetValue(ch, out var a) ? a : cell / 2;      // Game2DJpFont.Advance と同じ (無い字は字の高さの半分)
        LineH = cell + 1;                                                // Game2DJpFont.LineH
        Console.WriteLine($"ドット字: 字の高さ {cell}・行送り {LineH}・{map.Count} 字・本文の幅 {ModalJpMetrics.BodyW(EventPaging.Width)} dot (窓の幅 {EventPaging.Width})");
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
        foreach (var para in JpTextLayout.Paragraphs(text)) l.AddRange(JpTextLayout.Lines(para, ModalJpMetrics.BodyW(EventPaging.Width), Adv));
        return l;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string root = args.Length > 0 ? args[0] : ".";
        LoadFont(root);
        Rules();

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
                    // 説明の文の最も長い形: 選べない理由のどれか + 使うターン + 補足中 (最短 99 ターン)
                    var reasons = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var sc in scenes) { sc(gm); var r = c.UnavailableReason(gm.Run); if (r != null) reasons.Add(r); }
                    var cands = new List<string> { EventChoice.CostText(null, c.TurnCost, c.TurnCost > 0 ? 99 : -1) };
                    foreach (var r in reasons) cands.Add(EventChoice.CostText(r, c.TurnCost, c.TurnCost > 0 ? 99 : -1));
                    foreach (var d in cands) if (!string.IsNullOrEmpty(d)) { ev.descs.Add(d); ev.anyDesc = true; }
                }
                evs.Add(ev);
            }
        GameLoop.GameManager.Instance = null;
        int bw = ModalJpMetrics.BodyW(EventPaging.Width);
        Console.WriteLine($"イベント {evs.Count}・選択肢 {evs.Sum(e => e.def.choices.Count)}・選択肢の最大 {evs.Max(e => e.def.choices.Count)}");

        // ===== 2. 直す前の窓 =====
        //   導入: 本文 5 行で切る (ScreenDef.bodyLines の既定)・「／」はそのまま字として出ていた・高さは中身の高さを 266 で切る・行は入る分だけ描く
        //   結果: 本文 8 行で切る
        var before = new List<(int over, string what)>();
        int oldCutRows = 0;
        foreach (var ev in evs)
        {
            var d = ev.def; int k = d.choices.Count;
            int n = JpTextLayout.Lines(d.flavor, bw, Adv).Count;          // 「／」を字のまま折り返す
            if (n > 5) before.Add((n - 5, $"{ev.file} 〈{d.name}〉 導入 {n} 行 → 5 行で切れる ({n - 5} 行が読めない・{d.flavor.Length} 字)"));
            foreach (bool det in ev.anyDesc ? new[] { false, true } : new[] { false })
            {
                int shown = Math.Min(5, n);
                int h = Math.Min(ModalJpMetrics.MaxH, ModalJpMetrics.HeightUncapped(LineH, n > 0, shown, k, 0, det));
                int fit = ModalJpMetrics.RowsThatFit(h, LineH, n > 0, shown, 0, det);
                if (fit < k)
                {
                    oldCutRows++;
                    before.Add((100 + k - fit, $"{ev.file} 〈{d.name}〉 選択肢 {k} のうち {fit} しか描けない{(det ? " (説明の 2 行が出ている時)" : "")}: 「{string.Join("」「", d.choices.Skip(fit).Select(c => c.text))}」が出ない"));
                    break;
                }
            }
            for (int i = 0; i < k; i++)
            {
                var c = d.choices[i]; int nb = BranchCount(c);
                for (int b = 0; b < Math.Max(1, nb); b++)
                {
                    int m = JpTextLayout.Lines(ResultText(c, nb > 0 ? b : -1), bw, Adv).Count;
                    if (m > 8) before.Add((m - 8, $"{ev.file} 〈{d.name}〉「{c.text}」{(nb > 0 ? "分岐" + (b + 1) : "")} 結果 {m} 行 → 8 行で切れる"));
                }
            }
        }
        Console.WriteLine($"[直す前] 切り捨てられる文・描かれない選択肢 {before.Count} 件 (うち選択肢が描かれないイベント {oldCutRows})。 上位 10 件:");
        foreach (var x in before.OrderByDescending(x => x.over).Take(10)) Console.WriteLine("  " + x.what);

        // ===== 3. 直した後の窓 =====
        int texts = 0, cut = 0, overH = 0, rowsCut = 0, nameCut = 0, descCut = 0, pagesMax = 0, hMax = 0, compact = 0;
        string hMaxWhat = "", pagesMaxWhat = "";
        foreach (var ev in evs)
        {
            var d = ev.def; int k = d.choices.Count;
            foreach (bool det in ev.anyDesc ? new[] { false, true } : new[] { false })
            {
                var plan = EventPaging.Intro(d.flavor, k, det, LineH, Adv);
                texts++;
                var got = plan.pages.SelectMany(x => x).ToList(); var want = AllLines(d.flavor);
                if (!got.SequenceEqual(want)) { cut++; Console.WriteLine($"  NG 切り捨て: 〈{d.name}〉 導入 {want.Count} 行のうち {got.Count} 行"); }
                for (int pg = 0; pg < plan.Count; pg++)
                {
                    int cap = plan.IsLast(pg) ? plan.lastLines : EventPaging.TextLines;
                    if (plan.Lines(pg) > cap) { cut++; Console.WriteLine($"  NG 頁の行数: 〈{d.name}〉 {pg + 1} 頁 {plan.Lines(pg)} 行 > {cap}"); }
                    if (!EventPaging.RowsFit(plan, pg, k, det, LineH)) { rowsCut++; Console.WriteLine($"  NG 選択肢が描けない: 〈{d.name}〉{(det ? " (説明あり)" : "")}"); }
                }
                if (!plan.fits || plan.height > ModalJpMetrics.MaxH) { overH++; Console.WriteLine($"  NG 窓の高さ: 〈{d.name}〉 {plan.height}"); }
                if (plan.rowH > 0) compact++;
                if (plan.height > hMax) { hMax = plan.height; hMaxWhat = $"〈{d.name}〉 導入 (選択肢 {k}{(det ? "・説明あり" : "")})"; }
                if (plan.Count > pagesMax) { pagesMax = plan.Count; pagesMaxWhat = $"〈{d.name}〉 導入"; }
            }
            // 選択肢の名前 (行の幅に Clip される: 行の幅 - 左右 6)・説明 (2 行まで)
            int nameW = EventPaging.Width - 2 * ModalJpMetrics.Inset - 12;
            foreach (var c in d.choices)
            {
                int w = c.text.Sum(ch => Adv(ch));
                if (w > nameW) { nameCut++; Console.WriteLine($"  NG 選択肢の名前が切れる: 〈{d.name}〉「{c.text}」 {w} > {nameW} dot"); }
            }
            foreach (var s in ev.descs)
            {
                int n = JpTextLayout.Lines(s, bw, Adv).Count;
                if (n > ModalJpMetrics.JDetail) { descCut++; Console.WriteLine($"  NG 説明が 2 行に収まらない: 〈{d.name}〉「{s}」 {n} 行"); }
            }
            for (int i = 0; i < k; i++)
            {
                var c = d.choices[i]; int nb = BranchCount(c);
                for (int b = 0; b < Math.Max(1, nb); b++)
                {
                    string text = ResultText(c, nb > 0 ? b : -1);
                    var plan = EventPaging.Result(text, LineH, Adv);
                    texts++;
                    if (!plan.pages.SelectMany(x => x).SequenceEqual(AllLines(text))) { cut++; Console.WriteLine($"  NG 切り捨て: 〈{d.name}〉「{c.text}」 結果"); }
                    foreach (var pgl in plan.pages) if (pgl.Count > EventPaging.TextLines) { cut++; Console.WriteLine($"  NG 頁の行数: 〈{d.name}〉「{c.text}」 結果"); }
                    if (!plan.fits || plan.height > ModalJpMetrics.MaxH) { overH++; Console.WriteLine($"  NG 窓の高さ: 〈{d.name}〉「{c.text}」 結果 {plan.height}"); }
                    if (plan.height > hMax) { hMax = plan.height; hMaxWhat = $"〈{d.name}〉「{c.text}」 結果"; }
                    if (plan.Count > pagesMax) { pagesMax = plan.Count; pagesMaxWhat = $"〈{d.name}〉「{c.text}」 結果"; }
                }
            }
        }
        // 選択肢の数の上限 (説明あり・行を詰めて・本文 0 行で 266 に入る数)
        int maxK = 0;
        for (int kk = 1; kk <= 12; kk++) if (EventPaging.Intro("", kk, true, LineH, Adv).fits) maxK = kk;
        Console.WriteLine($"[直した後] 文 {texts} 本 (導入は説明の有無の両方・結果は分岐ごと)・頁の最大 {pagesMax} ({pagesMaxWhat})・窓の高さの最大 {hMax} ({hMaxWhat})・行を詰めた導入 {compact}");
        Console.WriteLine($"[直した後] 今の窓に入る選択肢の数の上限 (説明の 2 行あり) = {maxK}");
        foreach (var ev in evs.Where(e => e.def.name.StartsWith("さびれた観測所")))
        {
            var plan = EventPaging.Intro(ev.def.flavor, ev.def.choices.Count, ev.anyDesc, LineH, Adv);
            Console.WriteLine($"  〈{ev.def.name}〉 導入 {ev.def.flavor.Length} 字 → {plan.Count} 頁 (行数 {string.Join("・", plan.pages.Select(x => x.Count))})・最後の頁の本文 {plan.lastLines} 行まで・選択肢 {ev.def.choices.Count} (説明{(ev.anyDesc ? "あり" : "なし")})・行の高さ {(plan.rowH > 0 ? plan.rowH : ModalJpMetrics.JRowH)}・窓の高さ {plan.height}");
        }
        Check(cut == 0, $"切り捨てられる文 {cut} 件 (全部の行がどこかの頁に出る・頁の行数が上限以下)");
        Check(overH == 0 && hMax <= ModalJpMetrics.MaxH, $"窓の高さが {ModalJpMetrics.MaxH} (格子 270) を超えるもの {overH} 件 (最大 {hMax})");
        Check(rowsCut == 0, $"描けない選択肢があるもの {rowsCut} 件 (説明の 2 行が出ている時も)");
        Check(nameCut == 0, $"名前が切れる選択肢 {nameCut} 件");
        Check(descCut == 0, $"2 行に収まらない説明 (選べない理由・使うターン・補足中) {descCut} 件");
        Check(maxK >= evs.Max(e => e.def.choices.Count), $"選択肢の数の上限 {maxK} ≧ 今の最大 {evs.Max(e => e.def.choices.Count)}");
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
