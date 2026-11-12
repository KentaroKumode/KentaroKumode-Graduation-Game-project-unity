// 確率分岐ごとの文 (EventBranchText) の検算。 Tools/event_branch_check/run.py が Unity なしでコンパイルして回す。
//   1. 切り分けの規則 (分岐 2 つ・3 つ・区切りなし・数が合わない・確率分岐なしで区切った)
//   2. Assets/Resources/Events/*.txt の全選択肢を、 解析 (EventParser) と同じ切り方で読み、
//      区切った文の数が分岐の数と合わない行を挙げる (合わない行は、 ゲームでは警告して先頭の文を使う)
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

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Rules();
        Scan(args.Length > 0 ? args[0] : "Assets/Resources/Events");
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} 件 NG");
        return fails == 0 ? 0 : 1;
    }
}
