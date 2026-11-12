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

        /// <summary>幅 maxW に収まるよう 1 字ずつ折り返す (改行文字でも改行)。 行頭の句読点は前の行へ寄せない簡易版。
        /// 折り返した所の空白 (半角・全角) は次の行の頭に持ち越さない。 <see cref="Game2DJpFont.Lines"/> の中身。</summary>
        public static List<string> Lines(string s, int maxW, Func<char, int> advance)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            var cur = new System.Text.StringBuilder(); int w = 0;
            foreach (var ch in s)
            {
                if (ch == '\n') { lines.Add(cur.ToString()); cur.Clear(); w = 0; continue; }
                int a = advance(ch);
                if (w + a > maxW && cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); w = 0; if (ch == ' ' || ch == '　') continue; }
                cur.Append(ch); w += a;
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            return lines;
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
