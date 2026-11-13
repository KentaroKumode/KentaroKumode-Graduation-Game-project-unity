// 持ち物の画面 (InventoryLayout・ItemCardInfo・JpTextLayout・ModalJpMetrics) の検算。 Tools/inventory_check/run.py が Unity なしでコンパイルして回す。
//   画面と同じ関数 (Assets/Scripts/UI/Game2D/Screens/InventoryLayout.cs ほか) を、 焼いたドット字 (GameFont.DotPath の .bytes) の送り幅で回す。
//   1. 窓の場所: 窓・升目の欄・詳細の欄・足元・ボタンが 480×270 の中に収まり、 互いに重ならない
//   2. 品の数 0・1・12・60・200 (区切りへの配り方を 3 通り): どの送りの位置でも、 升目と見出しが升目の欄の中・升目どうしが (強調の枠を含めて) 重ならない・
//      全部の品に、 矢印と送りで届く・吹き出しが窓の中 (= 480×270 の中) で升目に重ならない
//   3. items.json の全件: 詳細の欄 (説明文 + フレーバー文・売値と印は最長の形) が、 欄の幅で折り返して頁に割っても字を 1 つも落とさない・
//      吹き出し (品名 + 説明文) が窓の高さに入る
//   5. 戦闘の画面の入口 [持ち物] (BattleHudLayout.Inv): 480×270 の中で、 ほかの部品 (予告の枠・上の帯・ステータス・役・出目・操作・手札・敵) に重ならず、 字が入る
//   4. アイコンの無い品 (Resources/Icons/Items/<id>.png が無い品) の仮の字: 升目の内側に収まり、 アイコンの無い品どうしで (字・レア度) が重ならない
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UI.Game2D;
using Box = UI.Game2D.InventoryLayout.Box;

static class Program
{
    static int fails;
    static Func<char, int> Adv;
    static int LineH;
    static HashSet<int> Have = new HashSet<int>();

    static void Check(bool ok, string what)
    {
        if (!ok) { fails++; Console.WriteLine("NG  " + what); }
        else Console.WriteLine("ok  " + what);
    }

    static void LoadFont(string root)
    {
        var src = File.ReadAllText(Path.Combine(root, "Assets/Scripts/UI/GameFont.cs"));
        var m = Regex.Match(src, "\\bDotPath\\s*=\\s*\"([^\"]*)\"");
        var b = File.ReadAllBytes(Path.Combine(root, "Assets/Resources", m.Groups[1].Value + ".bytes"));
        if (b.Length < 9 || b[0] != 'M' || b[1] != 'J' || b[2] != 'F' || b[3] != '2') throw new Exception("ドット字の形式が違う");
        int cell = b[4], n = BitConverter.ToInt32(b, 5), o = 9;
        var map = new Dictionary<int, int>();
        for (int i = 0; i < n && o + 5 + 2 * cell <= b.Length; i++) { map[(int)BitConverter.ToUInt32(b, o)] = b[o + 4]; o += 5 + 2 * cell; }
        Adv = ch => map.TryGetValue(ch, out var a) ? a : cell / 2;      // Game2DJpFont.Advance と同じ
        LineH = cell + 1;
        Have = new HashSet<int>(map.Keys);
        Console.WriteLine($"ドット字: 字の高さ {cell}・行送り {LineH}・{map.Count} 字");
    }

    static int Width(string s) => s.Sum(ch => Adv(ch));

    static Box Shift(Box b) => new Box(b.x + InventoryLayout.WindowX, b.y + InventoryLayout.WindowY, b.w, b.h);

    // ===== 1. 窓の場所 =====
    static void Frame()
    {
        var screen = new Box(0, 0, InventoryLayout.ScreenW, InventoryLayout.ScreenH);
        var win = new Box(InventoryLayout.WindowX, InventoryLayout.WindowY, InventoryLayout.W, InventoryLayout.H);
        var c = InventoryLayout.Content(LineH); var g = InventoryLayout.Grid(LineH); var d = InventoryLayout.Detail(LineH);
        int btnY = InventoryLayout.H - ModalJpMetrics.Inset - ModalJpMetrics.BtnH, footY = btnY - 4 - LineH;
        var foot = new Box(ModalJpMetrics.Inset, footY, InventoryLayout.W - 2 * ModalJpMetrics.Inset, LineH);
        var btn = new Box(ModalJpMetrics.Inset, btnY, InventoryLayout.W - 2 * ModalJpMetrics.Inset, ModalJpMetrics.BtnH);
        int cols = InventoryLayout.Cols(LineH);
        Console.WriteLine($"窓 {win}・中身 {c}・升目の欄 {g} ({cols} 列・升目 {InventoryLayout.Cell}・間隔 {InventoryLayout.Pitch})・詳細の欄 {d} (1 頁 {InventoryLayout.DetailPerPage(LineH)} 行・幅 {InventoryLayout.DetailTextW} dot)・足元 {foot}・ボタン {btn}");
        Check(win.Inside(screen), $"窓 {win} が 480×270 の中");
        Check(Shift(g).Inside(win) && Shift(d).Inside(win) && Shift(foot).Inside(win) && Shift(btn).Inside(win), "升目の欄・詳細の欄・足元・ボタンが窓の中");
        Check(!g.Overlaps(d) && !g.Overlaps(foot) && !d.Overlaps(foot) && !foot.Overlaps(btn) && !g.Overlaps(btn) && !d.Overlaps(btn), "升目の欄・詳細の欄・足元・ボタンが重ならない");
        Check(1 + (cols - 1) * InventoryLayout.Pitch + InventoryLayout.Cell + 1 <= g.w - InventoryLayout.BarW, $"1 段 {cols} 列の升目 (強調の枠を含む) が送りの棒の手前に収まる");
        // 見出しの字 (区切りの名前 + 数・数は 4 桁まで見る) が欄に入る
        int headMax = ItemCardInfo.KindNames.Max(k => Width(k + " 9999"));
        Check(headMax + 8 <= g.w - InventoryLayout.BarW, $"見出しの字の最大 {headMax} dot が升目の欄 (幅 {g.w - InventoryLayout.BarW}) に入る");
        // 足元の案内・ボタン (使う・ステータス・閉じる) の字が足元・ボタンの段に入る (Game2DModal.RedrawJp と同じ幅の式)
        string footer = "矢印で選ぶ  Space で詳細を送る  Esc で閉じる";
        Check(Width(footer) <= foot.w - 4, $"足元の案内 {Width(footer)} dot ≦ {foot.w - 4}");
        int bt = new[] { "使う", "ステータス", "閉じる" }.Sum(s => Math.Max(52, Width(s) + 20) + 5) - 5;
        Check(bt <= btn.w, $"ボタン 3 つの幅 {bt} ≦ {btn.w}");
        // 題の右の副題 (HP・物資・所持金の最長の形) が題の行に入る (Game2DModal は Clip するが、 切れないこと)
        string sub = "HP 9999/9999  物資 999/999  99999G";
        int titleW = Width("持ち物");
        Check(titleW + 12 + Width(sub) <= c.w, $"題と副題 {titleW + 12 + Width(sub)} dot ≦ {c.w}");
    }

    // ===== 5. 戦闘の画面の入口 =====
    static void BattleEntry()
    {
        var screen = new Box(0, 0, InventoryLayout.ScreenW, InventoryLayout.ScreenH);
        var inv = BattleHudLayout.Inv;
        var hit = BattleHudLayout.Others.Where(o => inv.Overlaps(o)).ToList();
        Console.WriteLine($"戦闘の画面の入口 [{BattleHudLayout.InvLabel}] {inv} (キー {BattleHudLayout.InvKeyName})");
        Check(inv.Inside(screen), $"戦闘の画面の入口 {inv} が 480×270 の中");
        Check(hit.Count == 0, "戦闘の画面の入口が、 予告の枠・上の帯・ステータス・役・出目 (9 個の幅)・操作・手札・敵に重ならない" + (hit.Count > 0 ? ": " + string.Join(" ", hit) : ""));
        Check(Width(BattleHudLayout.InvLabel) + 6 <= inv.w && LineH <= inv.h + 1, $"入口の字「{BattleHudLayout.InvLabel}」{Width(BattleHudLayout.InvLabel)} dot が入口 ({inv.w}×{inv.h}) に入る");
    }

    // ===== 2. 品の数 =====
    static List<int> Spread(int n, int how)
    {
        var s = new int[ItemCardInfo.KindCount];
        if (how == 0) s[0] = n;                                 // 全部秘宝
        else if (how == 1) for (int i = 0; i < n; i++) s[i % s.Length]++;   // 均等
        else { s[0] = n * 6 / 10; s[1] = Math.Min(n - s[0], 2); s[2] = (n - s[0] - s[1]) / 2; s[3] = n - s[0] - s[1] - s[2]; }  // 実際に近い (秘宝が多い・武器 2)
        return s.ToList();
    }

    static void Counts(string longestTipItem, ItemCardInfo tipCard)
    {
        var g = InventoryLayout.Grid(LineH); var win = new Box(0, 0, InventoryLayout.W, InventoryLayout.H);
        var screen = new Box(0, 0, InventoryLayout.ScreenW, InventoryLayout.ScreenH);
        int cols = InventoryLayout.Cols(LineH);
        var tl = tipCard.TooltipLines(InventoryLayout.TipTextW, Adv).Select(x => x.text).ToList();
        InventoryLayout.TipSize(tl, Adv, LineH, out int tw, out int th);
        int bad = 0, unreachable = 0, tipBad = 0, cases = 0, keyBad = 0;
        foreach (int n in new[] { 0, 1, 12, 60, 200 })
            for (int how = 0; how < 3; how++)
            {
                var counts = Spread(n, how); var lines = InventoryLayout.BuildLines(counts, cols);
                int max = InventoryLayout.MaxTop(lines, g.h, LineH);
                int total = counts.Sum();
                if (n == 0 && lines.Count != 0) { bad++; Console.WriteLine("  NG 0 個で行がある"); }
                for (int top = 0; top <= max; top++)
                {
                    cases++;
                    var boxes = InventoryLayout.CellBoxes(lines, top, g, LineH);
                    // 見出しも含めて、 見えている行が欄に入る
                    int vis = InventoryLayout.VisibleCount(lines, top, g.h, LineH), y = 0;
                    for (int i = top; i < top + vis; i++) y += lines[i].Height(LineH);
                    if (y > g.h) { bad++; Console.WriteLine($"  NG 行が欄の下へはみ出す: n={n} 配り方 {how} top={top} 高さ {y} > {g.h}"); }
                    foreach (var kv in boxes)
                    {
                        var ring = new Box(kv.Value.x - 1, kv.Value.y - 1, kv.Value.w + 2, kv.Value.h + 2);
                        if (!ring.Inside(new Box(g.x, g.y, g.w - InventoryLayout.BarW, g.h))) { bad++; Console.WriteLine($"  NG 升目が欄の外: n={n} {how} top={top} #{kv.Key} {ring}"); }
                        // 吹き出し (いちばん大きい物) が窓の中・升目に重ならない
                        var tip = InventoryLayout.TipBox(kv.Value, tw, th);
                        if (!tip.Inside(InventoryLayout.TipBounds) || !Shift(tip).Inside(screen) || tip.Overlaps(kv.Value)) { tipBad++; if (tipBad < 5) Console.WriteLine($"  NG 吹き出し {tip} (升目 {kv.Value})"); }
                    }
                    for (int a = 0; a < boxes.Count; a++)
                        for (int b = a + 1; b < boxes.Count; b++)
                        {
                            var ra = new Box(boxes[a].Value.x - 1, boxes[a].Value.y - 1, boxes[a].Value.w + 2, boxes[a].Value.h + 2);
                            var rb = new Box(boxes[b].Value.x - 1, boxes[b].Value.y - 1, boxes[b].Value.w + 2, boxes[b].Value.h + 2);
                            if (ra.Overlaps(rb)) { bad++; Console.WriteLine($"  NG 升目が重なる: #{boxes[a].Key} #{boxes[b].Key}"); }
                        }
                }
                // どの品にも届く: フォーカスを当てて見えるようにすると、 その升目が見えている
                int t = 0;
                for (int f = 0; f < total; f++)
                {
                    t = InventoryLayout.EnsureVisible(lines, t, f, g.h, LineH);
                    if (!InventoryLayout.CellBoxes(lines, t, g, LineH).Any(kv => kv.Key == f)) { unreachable++; if (unreachable < 5) Console.WriteLine($"  NG 届かない: n={n} {how} #{f}"); }
                }
                // 矢印: → を押し続けると最後まで・↓ を押し続けると最後の段まで・範囲の外へ出ない
                if (total > 0)
                {
                    int f = -1, steps = 0; while (steps++ < total + 2) { int nf = InventoryLayout.MoveFocus(lines, f, 1, 0, total); if (nf == f) break; f = nf; }
                    if (f != total - 1) { keyBad++; Console.WriteLine($"  NG → で最後に届かない n={n} {how}: {f}"); }
                    f = 0; steps = 0; while (steps++ < total + 2) { int nf = InventoryLayout.MoveFocus(lines, f, 0, 1, total); if (nf == f) break; f = nf; }
                    int lastLine = lines.Count - 1;
                    if (InventoryLayout.LineOf(lines, f) != lastLine) { keyBad++; Console.WriteLine($"  NG ↓ で最後の段に届かない n={n} {how}"); }
                    for (int x = 0; x < total; x++)
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        { int nf = InventoryLayout.MoveFocus(lines, x, dx, dy, total); if (nf < 0 || nf >= total) keyBad++; }
                }
                else if (InventoryLayout.MoveFocus(lines, -1, 1, 0, 0) != -1) keyBad++;
                Console.WriteLine($"  品 {n,3} ({(how == 0 ? "全部秘宝" : how == 1 ? "均等" : "実際に近い")}): 行 {lines.Count}・送りの位置 {max + 1} 通り");
            }
        Check(bad == 0, $"品の数 0/1/12/60/200 × 配り方 3 × 送りの位置 {cases} 通りで、 升目・見出しが升目の欄の中・升目どうしが重ならない (NG {bad})");
        Check(unreachable == 0, $"どの品にもフォーカスと送りで届く (NG {unreachable})");
        Check(keyBad == 0, $"矢印のフォーカスが端で止まり、 → ↓ で最後まで届く (NG {keyBad})");
        Check(tipBad == 0, $"いちばん大きい吹き出し ({longestTipItem}・{tw}×{th}) が、 どの升目に出しても窓 (= 480×270) の中で、 升目に重ならない (NG {tipBad})");
    }

    // ===== 3. items.json の全件 =====
    sealed class Item { public string id, name, category, rarity, description, flavor, axis, family; public int tier; }

    static List<Item> LoadItems(string root)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/Data/InventorySystem/items.json")));
        string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int I(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        var l = new List<Item>();
        foreach (var e in doc.RootElement.GetProperty("items").EnumerateArray())
            l.Add(new Item { id = S(e, "id"), name = S(e, "name") ?? S(e, "id"), category = S(e, "category"), rarity = S(e, "rarity"), description = S(e, "description"),
                             flavor = S(e, "flavorText"), axis = S(e, "axis"), family = S(e, "family"), tier = I(e, "tier") });
        return l;
    }

    static int RarityOf(string r) => r == "BRONZE" ? 0 : r == "SILVER" ? 1 : r == "GOLD" ? 2 : r == "LEGENDARY" ? 3 : r == "MYTHIC" ? 4 : -1;

    static string NoSpace(string s) => new string((s ?? "").Where(ch => ch != ' ' && ch != '　' && ch != '\n' && ch != '\r').ToArray());

    static ItemCardInfo Card(Item it, int count = 999)
        => ItemCardInfo.ForItem(it.id, it.name, it.category, RarityOf(it.rarity), it.description, it.flavor, it.axis, it.family, it.tier,
                                it.category == "Weapon" ? null : "999〜999G", "預かり物 (手放せない)", count, Adv);

    static ItemCardInfo AllItems(List<Item> items, out string longestTip)
    {
        int perPage = InventoryLayout.DetailPerPage(LineH), dw = InventoryLayout.DetailTextW;
        int lost = 0, wide = 0, pagesMax = 0, tipBad = 0, tipHMax = 0; string pagesWhat = "", tipWhat = ""; ItemCardInfo tipCard = null;
        var missing = new SortedSet<char>();
        foreach (var it in items)
        {
            var c = Card(it); c.block = "この戦闘ではもう使えない (9 個まで)";
            var lines = c.DetailLines(dw, Adv);
            var pages = InventoryLayout.Pages(lines, perPage, l => l.text.Length == 0);
            var got = NoSpace(string.Concat(pages.SelectMany(p => p).Select(l => l.text)));
            var want = NoSpace(c.name + string.Join("", c.MetaParts()) + c.mark + "今は使えない:" + c.block + c.description + c.flavor);
            if (got != want) { lost++; Console.WriteLine($"  NG 字が落ちる: {it.id}\n     want {want}\n     got  {got}"); }
            foreach (var p in pages) { if (p.Count > perPage) lost++; foreach (var l in p) if (Width(l.text) > dw) { wide++; Console.WriteLine($"  NG 幅を超える行: {it.id} 「{l.text}」"); } }
            if (pages.Count > pagesMax) { pagesMax = pages.Count; pagesWhat = it.id + " (" + lines.Count + " 行)"; }
            var tl = c.TooltipLines(InventoryLayout.TipTextW, Adv).Select(x => x.text).ToList();
            InventoryLayout.TipSize(tl, Adv, LineH, out int tw, out int th);
            if (th > InventoryLayout.TipBounds.h || tw > InventoryLayout.TipMaxW || tl.Any(s => Width(s) > InventoryLayout.TipTextW)) { tipBad++; Console.WriteLine($"  NG 吹き出しが入らない: {it.id} {tw}×{th}"); }
            if (th > tipHMax || tipCard == null) { tipHMax = th; tipWhat = it.id; tipCard = c; }
            foreach (var ch in c.name + c.description + c.flavor) if (ch > ' ' && !Have.Contains(ch)) missing.Add(ch);
        }
        Console.WriteLine($"items.json {items.Count} 品・詳細の欄の頁の最大 {pagesMax} ({pagesWhat})・吹き出しの高さの最大 {tipHMax} ({tipWhat})");
        if (missing.Count > 0) Console.WriteLine($"  (情報) ドット字に無い字 {missing.Count}: {string.Concat(missing)} ── items.json の文の字。 この画面で新しく足した字ではない");
        Check(lost == 0 && wide == 0, $"全 {items.Count} 品の詳細 (品名・付記・印・説明文・フレーバー文) が、 幅 {dw} dot で折り返して 1 頁 {perPage} 行に割っても字を落とさない (NG {lost + wide})");
        Check(tipBad == 0, $"全 {items.Count} 品の吹き出し (品名 ×999 + 説明文) が幅 {InventoryLayout.TipMaxW}・窓の高さに入る (NG {tipBad})");
        longestTip = tipWhat;
        return tipCard;
    }

    // ===== 4. アイコンの無い品の仮の字 =====
    static void Placeholders(string root, List<Item> items)
    {
        string dir = Path.Combine(root, "Assets/Resources/Icons/Items");
        var noIcon = items.Where(it => !File.Exists(Path.Combine(dir, it.id + ".png"))).ToList();
        var seen = new Dictionary<string, string>(); int bad = 0;
        foreach (var it in noIcon)
        {
            var c = Card(it, 1);
            string key = c.glyph + "/" + c.rarity;
            int w = Width(c.glyph);
            if (string.IsNullOrEmpty(c.glyph) || w > InventoryLayout.CellInner) { bad++; Console.WriteLine($"  NG 仮の字が升目に入らない: {it.id} 「{c.glyph}」 {w} dot"); }
            if (c.glyph.Any(ch => !Have.Contains(ch))) { bad++; Console.WriteLine($"  NG 仮の字がドット字に無い: {it.id} 「{c.glyph}」"); }
            if (seen.TryGetValue(key, out var other)) { bad++; Console.WriteLine($"  NG 仮の見た目が重なる: {it.id} と {other} (字「{c.glyph}」・レア度 {ItemCardInfo.RarityName(c.rarity)})"); }
            else seen[key] = it.id;
        }
        Console.WriteLine($"アイコンの無い品 {noIcon.Count}: " + string.Join(" ", noIcon.Select(it => it.id + "「" + Card(it, 1).glyph + "」")));
        // 強化素材「素」・出目パーツの目 1〜9・数 (2〜999) も升目に入る。 1000 以上は升目に数を出さない (印だけ・詳細の欄に個数)
        int extra = 0;
        foreach (var g in new[] { "素" }.Concat(Enumerable.Range(1, 9).Select(i => i.ToString()))) if (Width(g) > InventoryLayout.CellInner || g.Any(ch => !Have.Contains(ch))) extra++;
        int fitMax = 1;
        for (int n = 2; n <= 100000; n++)
        {
            var lab = ItemCardInfo.CountLabel(n, InventoryLayout.CellInner, Adv);
            if (lab != null) { fitMax = n; if (Width(lab) > InventoryLayout.CellInner) extra++; }
        }
        if (fitMax < 99) extra++;
        Console.WriteLine($"升目に数を出せるのは {fitMax} まで (それより多いと升目は印だけ・詳細の欄に「個数」)");
        Check(bad == 0, $"アイコンの無い {noIcon.Count} 品の仮の字が升目の内側 ({InventoryLayout.CellInner} dot) に入り、 (字・レア度) が互いに重ならない (NG {bad})");
        Check(extra == 0, "強化素材・出目パーツの字と、 升目に出す数 (2〜99 は必ず出す) が升目の内側に入る (NG " + extra + ")");
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string root = args.Length > 0 ? args[0] : ".";
        LoadFont(root);
        Frame();
        BattleEntry();
        var items = LoadItems(root);
        var tipCard = AllItems(items, out string tipWhat);
        Counts(tipWhat, tipCard);
        Placeholders(root, items);
        // 画面で足した字 (見出し・案内・付記・印・空の時の字) がドット字にある
        string ui = string.Concat(ItemCardInfo.KindNames) + "持ち物ステータス使う閉じる矢印で選ぶSpace詳細を送るEsc升目を押すと、ここに詳しく出る。何も持っていない。"
                  + "軸家系段個数売値手放せない預かり物今は使えない:強化素材休憩で武器を強化する時に払う。銅銀金伝説剣斧短剣盾・×〜G()1/2持ち物へ"
                  + "戦闘中は [道具] から使う";
        var miss = ui.Where(ch => ch > ' ' && !Have.Contains(ch)).Distinct().ToList();
        Check(miss.Count == 0, "画面で足した字がドット字にある" + (miss.Count > 0 ? ": " + string.Concat(miss) : ""));
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
