using System;
using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// 液晶のドット字の折り返しと頁割り (2026-11-13・イベントの頁送り)。 <b>UnityEngine に依存しない</b> ── 検算
    /// (Tools/event_page_check) がこのファイルをそのままコンパイルして、 画面と同じ規則で数える。
    /// 字の送り幅は呼ぶ側が渡す (画面は <see cref="Game2DJpFont.Advance"/>・検算は焼いた .bytes を読む)。
    /// </summary>
    public static class JpTextLayout
    {
        /// <summary>頁の区切り (全角の斜線)。 イベントの文 (event_list*.txt の導入・選んだ後の文) で、 段落の切れ目 = 頁の切れ目。
        /// 画面には出さない。 区切りの前後の空白は落とす。</summary>
        public const char PageBreak = '／';

        /// <summary>
        /// 幅 maxW に収まるよう折り返す (改行文字でも改行・2026-11-17 に 1 字ずつの簡易版から作り直し)。 <see cref="Game2DJpFont.Lines"/> の中身。
        /// 設定の「字間」で 1 行に入る字数が変わるので、 書き手の手の改行では守れない ── 折る位置は表示の側が決める。 決まり:
        /// <list type="number">
        /// <item><b>文節で折る</b>: 折ってよいのは文節の切れ目 (BudouX の日本語モデル・<see cref="JpBreakModel"/>) と空白の後だけ。 「映ってい／る。」のような語の途中では折らない</item>
        /// <item><b>禁則</b>: 行頭に句読点・閉じ括弧・小書きの仮名・長音・中黒などを置かない (<see cref="NoStart"/>)。 行末に開き括弧を置かない (<see cref="NoEnd"/>)。
        ///   ぶら下げはしない (幅 maxW を超えないことを保つ) ── 収まらない時は前の字ごと次の行へ追い出す</item>
        /// <item>文節 1 つが 1 行より長い時だけ、 禁則を守って字の間で折る (それも無理なら字の間で折る)</item>
        /// <item><b>短すぎる最後の行を防ぐ</b>: 段落の最後の行が句読点を除いて <see cref="WidowChars"/> 字以下なら、 前の行の最後の文節を下ろす (下ろした行が幅に収まる時だけ)</item>
        /// </list>
        /// 字は 1 つも捨てない (折った所の行頭の空白 (半角・全角) だけは次の行へ持ち越さない)。
        /// </summary>
        public static List<string> Lines(string s, int maxW, Func<char, int> advance)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            var hard = s.Split('\n');
            for (int k = 0; k < hard.Length; k++)
            {
                string seg = hard[k];
                if (seg.Length == 0) { if (k < hard.Length - 1) lines.Add(""); continue; }
                WrapSegment(seg, maxW, advance, lines);
            }
            return lines;
        }

        /// <summary>最後の行がこの字数以下 (句読点・閉じ括弧などの <see cref="NoStart"/> を数えない) なら、 短すぎる行として前の行から文節を下ろす。</summary>
        public const int WidowChars = 2;

        /// <summary>行頭に置かない字 (句読点・閉じ括弧・小書きの仮名・長音・中黒・三点リーダー・ダッシュ・感嘆と疑問など)。</summary>
        public static bool NoStart(char c) => NoStartChars.IndexOf(c) >= 0;
        private const string NoStartChars = "、。，．,.:;：；!?！？)]}）］｝〕〉》」』】〙〗〟’”｠»ゝゞヽヾーァィゥェォッャュョヮヵヶぁぃぅぇぉっゃゅょゎゕゖㇰㇱㇲㇳㇴㇵㇶㇷㇸㇹㇺㇻㇼㇽㇾㇿ々〻‐゠–〜～・…‥―—ｰ％%";

        /// <summary>行末に置かない字 (開き括弧)。</summary>
        public static bool NoEnd(char c) => NoEndChars.IndexOf(c) >= 0;
        private const string NoEndChars = "([{（［｛〔〈《「『【〘〖〝‘“｟«";

        private static bool IsSpace(char c) => c == ' ' || c == '　';
        private static bool IsAlnum(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '０' && c <= '９');

        /// <summary>s[i-1] と s[i] の間で、 字の間の折り返しとして許されるか (禁則と、 英数字の語・同じ記号の連なりを割らない)。</summary>
        private static bool CharBreakOk(string s, int i)
        {
            char a = s[i - 1], b = s[i];
            if (NoStart(b) || NoEnd(a)) return false;
            if (IsAlnum(a) && IsAlnum(b)) return false;
            return true;
        }

        /// <summary>改行を含まない 1 段を折る。</summary>
        private static void WrapSegment(string s, int maxW, Func<char, int> advance, List<string> lines)
        {
            int n = s.Length;
            var adv = new int[n];
            for (int i = 0; i < n; i++) adv[i] = advance(s[i]);
            // phrase[i] = s[i-1] と s[i] の間が文節の切れ目 (かつ禁則を守る)。 charOk[i] = 字の間として折ってよい
            var phrase = new bool[n + 1];
            var charOk = new bool[n + 1];
            for (int i = 1; i < n; i++)
            {
                charOk[i] = CharBreakOk(s, i);
                bool boundary = IsSpace(s[i - 1]) || (!IsSpace(s[i]) && PhraseBoundary(s, i));
                phrase[i] = boundary && charOk[i];
            }
            var starts = new List<int>();
            int start = 0;
            while (start < n)
            {
                starts.Add(start);
                int w = 0, e = start;
                while (e < n && (e == start || w + adv[e] <= maxW)) { w += adv[e]; e++; }
                if (e >= n) break;
                int b = -1;
                for (int j = e; j > start; j--) if (phrase[j]) { b = j; break; }
                if (b < 0) for (int j = e; j > start; j--) if (charOk[j]) { b = j; break; }
                if (b < 0) b = e;
                start = b;
                while (start < n && IsSpace(s[start])) start++;
            }
            // 短すぎる最後の行: 前の行の最後の文節を下ろす (下ろした行が幅に収まる時だけ)
            int m = starts.Count;
            if (m >= 2)
            {
                int ps = starts[m - 2], ls = starts[m - 1];
                int real = 0;
                for (int i = ls; i < n; i++) if (!NoStart(s[i]) && !IsSpace(s[i])) real++;
                if (real <= WidowChars)
                {
                    int tail = 0; for (int i = ls; i < n; i++) tail += adv[i];
                    for (int j = ls - 1; j > ps; j--)
                    {
                        tail += adv[j];
                        if (tail > maxW) break;
                        if (phrase[j] && !IsSpace(s[j])) { starts[m - 1] = j; break; }
                    }
                }
            }
            for (int k = 0; k < starts.Count; k++)
            {
                int a = starts[k], z = k + 1 < starts.Count ? starts[k + 1] : n;
                // 次の行の頭で飛ばした空白は前の行にも付けない (前の行の末尾の空白を落とす)
                while (z > a && k + 1 < starts.Count && IsSpace(s[z - 1])) z--;
                lines.Add(s.Substring(a, z - a));
            }
        }

        // ===== 文節の切れ目 (BudouX・https://github.com/google/budoux v0.7.0・Apache License 2.0) =====
        // budoux/parser.py の Parser.parse と同じ点数: 前後 3 字の単字・2 字・3 字の特徴の重みの和が base_score (全重みの和の -1/2) を超えたら切れ目。
        // 整数で比べるため 2 倍して持つ (JpBreakModel.BaseScore2)。

        private static Dictionary<char, int>[] _uw;
        private static Dictionary<int, int>[] _bw;
        private static Dictionary<long, int>[] _tw;

        private static void EnsureModel()
        {
            if (_uw != null) return;
            var uw = new Dictionary<char, int>[6];
            string[] uk = { JpBreakModel.UW1Keys, JpBreakModel.UW2Keys, JpBreakModel.UW3Keys, JpBreakModel.UW4Keys, JpBreakModel.UW5Keys, JpBreakModel.UW6Keys };
            int[][] uv = { JpBreakModel.UW1Vals, JpBreakModel.UW2Vals, JpBreakModel.UW3Vals, JpBreakModel.UW4Vals, JpBreakModel.UW5Vals, JpBreakModel.UW6Vals };
            for (int f = 0; f < 6; f++) { uw[f] = new Dictionary<char, int>(); for (int i = 0; i < uv[f].Length; i++) uw[f][uk[f][i]] = uv[f][i]; }
            var bw = new Dictionary<int, int>[3];
            string[] bk = { JpBreakModel.BW1Keys, JpBreakModel.BW2Keys, JpBreakModel.BW3Keys };
            int[][] bv = { JpBreakModel.BW1Vals, JpBreakModel.BW2Vals, JpBreakModel.BW3Vals };
            for (int f = 0; f < 3; f++) { bw[f] = new Dictionary<int, int>(); for (int i = 0; i < bv[f].Length; i++) bw[f][Key2(bk[f][2 * i], bk[f][2 * i + 1])] = bv[f][i]; }
            var tw = new Dictionary<long, int>[4];
            string[] tk = { JpBreakModel.TW1Keys, JpBreakModel.TW2Keys, JpBreakModel.TW3Keys, JpBreakModel.TW4Keys };
            int[][] tv = { JpBreakModel.TW1Vals, JpBreakModel.TW2Vals, JpBreakModel.TW3Vals, JpBreakModel.TW4Vals };
            for (int f = 0; f < 4; f++) { tw[f] = new Dictionary<long, int>(); for (int i = 0; i < tv[f].Length; i++) tw[f][Key3(tk[f][3 * i], tk[f][3 * i + 1], tk[f][3 * i + 2])] = tv[f][i]; }
            _bw = bw; _tw = tw; _uw = uw;
        }

        private static int Key2(char a, char b) => (a << 16) | b;
        private static long Key3(char a, char b, char c) => ((long)a << 32) | ((long)b << 16) | c;
        private static int U(int f, char c) => _uw[f].TryGetValue(c, out var v) ? v : 0;
        private static int B(int f, char a, char b) => _bw[f].TryGetValue(Key2(a, b), out var v) ? v : 0;
        private static int T(int f, char a, char b, char c) => _tw[f].TryGetValue(Key3(a, b, c), out var v) ? v : 0;

        /// <summary>BudouX で s[i-1] と s[i] の間が文節の切れ目か (i ≧ 1)。</summary>
        public static bool PhraseBoundary(string s, int i)
        {
            EnsureModel();
            int n = s.Length, sc = 0;
            if (i > 2) sc += U(0, s[i - 3]);
            if (i > 1) sc += U(1, s[i - 2]);
            sc += U(2, s[i - 1]);
            sc += U(3, s[i]);
            if (i + 1 < n) sc += U(4, s[i + 1]);
            if (i + 2 < n) sc += U(5, s[i + 2]);
            if (i > 1) sc += B(0, s[i - 2], s[i - 1]);
            sc += B(1, s[i - 1], s[i]);
            if (i + 1 < n) sc += B(2, s[i], s[i + 1]);
            if (i > 2) sc += T(0, s[i - 3], s[i - 2], s[i - 1]);
            if (i > 1) sc += T(1, s[i - 2], s[i - 1], s[i]);
            if (i + 1 < n) sc += T(2, s[i - 1], s[i], s[i + 1]);
            if (i + 2 < n) sc += T(3, s[i], s[i + 1], s[i + 2]);
            return 2 * sc + JpBreakModel.BaseScore2 > 0;
        }

        /// <summary>文を <see cref="PageBreak"/> で段落に分ける (前後の空白・改行を落とし、 空の段落は捨てる)。</summary>
        public static List<string> Paragraphs(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var raw in text.Split(PageBreak))
            {
                string p = raw.Trim(' ', '　', '\n', '\r', '\t');
                if (p.Length > 0) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// 頁に割る。 段落 (<see cref="PageBreak"/> の区切り) 1 つが 1 頁で、 1 頁 <paramref name="perPage"/> 行を超える段落は行の切れ目で次の頁へ送る
        /// (文の途中で切れてよいが、 行の途中では切らない)。 最後の頁は <paramref name="lastPage"/> 行まで (選択肢に場所を空ける):
        /// 最後の頁が超える時は、 その頁の終わりの <paramref name="lastPage"/> 行だけを最後の頁に残し、 前を 1 頁にする。
        /// <paramref name="lastPage"/> が 0 なら、 本文の無い頁を最後に足す。 文が空でも 1 頁 (行 0) を返す。
        /// <b>どの行も捨てない</b> ── 全頁の行を並べると、 段落ごとに折り返した行の全部になる。
        /// </summary>
        public static List<List<string>> Pages(string text, int maxW, int perPage, int lastPage, Func<char, int> advance)
        {
            if (perPage < 1) perPage = 1;
            if (lastPage > perPage) lastPage = perPage;
            if (lastPage < 0) lastPage = 0;
            var pages = new List<List<string>>();
            foreach (var para in Paragraphs(text))
            {
                var lines = Lines(para, maxW, advance);
                for (int i = 0; i < lines.Count; i += perPage)
                    pages.Add(lines.GetRange(i, Math.Min(perPage, lines.Count - i)));
            }
            if (pages.Count == 0) { pages.Add(new List<string>()); return pages; }
            var last = pages[pages.Count - 1];
            if (last.Count > lastPage)
            {
                if (lastPage == 0) pages.Add(new List<string>());
                else
                {
                    int head = last.Count - lastPage;
                    pages[pages.Count - 1] = last.GetRange(0, head);
                    pages.Add(last.GetRange(head, lastPage));
                }
            }
            return pages;
        }
    }
}
