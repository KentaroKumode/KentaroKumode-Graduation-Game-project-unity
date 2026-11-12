using System;
using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// イベントの窓の頁割り (2026-11-13)。 導入 (フレーバー) と結果 (結果の行 + 選んだ後の文) を、 窓の幅と行数で頁に割り、
    /// 窓の高さを決める。 <b>見せ方だけ</b> ── 進行 (乱数・効果・BOT の経路) には触らない。 <b>UnityEngine に依存しない</b>
    /// (検算 Tools/event_page_check がこのファイルをそのままコンパイルして、 全イベントで「切り捨てられる文 0・窓が 270 に収まる」を見る)。
    ///
    /// <para><b>決まり</b> (docs/GAME.md §17「イベントの窓」):
    /// <list type="bullet">
    /// <item>導入の「／」(<see cref="JpTextLayout.PageBreak"/>) は段落の区切り = 頁の区切り。 1 段落 = 1 頁。 1 頁は <see cref="TextLines"/> 行まで。 超える段落は行の切れ目で次の頁へ送る</item>
    /// <item>選択肢は最後の頁だけに出す。 最後の頁は選択肢と詳細 (選んだ行の説明 2 行) に場所を空けて、 本文を <see cref="Plan.lastLines"/> 行までにする
    ///   (溢れる行は 1 つ前の頁へ)。 本文が <see cref="MinLastLines"/> 行も残らない数の選択肢の時と、 最後の段落が最後の頁に入りきらない時は、
    ///   行の高さを <see cref="CompactRowH"/> に詰める (詰めた方が本文の行が多く残る時だけ)</item>
    /// <item>結果の文も同じ規則で割る (選択肢は無い)</item>
    /// <item>窓の高さは全頁で同じ (一番高い頁に合わせる・頁送りで窓が動かない)</item>
    /// </list></para>
    /// </summary>
    public static class EventPaging
    {
        /// <summary>イベントの窓の幅 (dot)。</summary>
        public const int Width = 420;
        /// <summary>本文だけの頁の行数。</summary>
        public const int TextLines = 8;
        /// <summary>最後の頁に残したい本文の行数の下限。 これを下回る時は行の高さを詰める。</summary>
        public const int MinLastLines = 3;
        /// <summary>選択肢が多い時の行の高さ (既定は <see cref="ModalJpMetrics.JRowH"/>)。</summary>
        public const int CompactRowH = 16;

        public sealed class Plan
        {
            /// <summary>頁ごとの行 (折り返し済み)。 最低 1 頁。 最後の頁に選択肢を出す。</summary>
            public List<List<string>> pages;
            /// <summary>選択肢の行の高さ (0 = 既定)。</summary>
            public int rowH;
            /// <summary>窓の高さ (全頁で同じ)。</summary>
            public int height;
            /// <summary>最後の頁の本文の行数の上限。</summary>
            public int lastLines;
            /// <summary>窓の高さの上限 (<see cref="ModalJpMetrics.MaxH"/>) に全部が収まるか。 false は選択肢が多すぎる (今の窓では 7 つまで)。</summary>
            public bool fits;
            public int Count => pages.Count;
            public bool IsLast(int page) => page >= pages.Count - 1;
            /// <summary>頁 page の本文 (行を改行でつないだもの)。 null は本文なし。</summary>
            public string Body(int page) { var p = pages[Clamp(page)]; return p.Count == 0 ? null : string.Join("\n", p); }
            public int Lines(int page) => pages[Clamp(page)].Count;
            /// <summary>頁の位置 (「2/4」)。 1 頁しかない時は null。</summary>
            public string Mark(int page) => pages.Count > 1 ? (Clamp(page) + 1) + "/" + pages.Count : null;
            public int Clamp(int page) => page < 0 ? 0 : page >= pages.Count ? pages.Count - 1 : page;
        }

        /// <summary>導入の頁割り。 choices = 選択肢の数・detail = どれかの選択肢に説明 (代償・選べない理由) が付くか。</summary>
        public static Plan Intro(string flavor, int choices, bool detail, int lineH, Func<char, int> advance)
        {
            int rowH = 0, last = LastLines(choices, 0, detail, lineH);
            // 行を詰めるのは、 既定の高さだと最後の頁の本文が MinLastLines 行も残らない時と、 最後の段落が最後の頁に入りきらず割れる時 (1 行だけの頁を作らないため)
            var whole = JpTextLayout.Pages(flavor, ModalJpMetrics.BodyW(Width), TextLines, TextLines, advance);
            int tail = whole[whole.Count - 1].Count;
            if (choices > 0 && (last < MinLastLines || tail > last))
            {
                int c = LastLines(choices, CompactRowH, detail, lineH);
                if (c > last) { rowH = CompactRowH; last = c; }
            }
            var plan = new Plan { rowH = rowH, fits = last >= 0, lastLines = Math.Max(0, last) };
            plan.pages = JpTextLayout.Pages(flavor, ModalJpMetrics.BodyW(Width), TextLines, plan.lastLines, advance);
            plan.height = Height(plan, choices, detail, lineH);
            return plan;
        }

        /// <summary>結果 (結果の行と選んだ後の文) の頁割り。</summary>
        public static Plan Result(string text, int lineH, Func<char, int> advance)
        {
            var plan = new Plan { rowH = 0, fits = true, lastLines = TextLines };
            plan.pages = JpTextLayout.Pages(text, ModalJpMetrics.BodyW(Width), TextLines, TextLines, advance);
            plan.height = Height(plan, 0, false, lineH);
            plan.fits = plan.height <= ModalJpMetrics.MaxH;
            return plan;
        }

        /// <summary>頁 page の窓の中身 (本文の行数・選択肢) で、 窓の高さ h に選択肢が全部描けるか (検算用)。</summary>
        public static bool RowsFit(Plan plan, int page, int choices, bool detail, int lineH)
        {
            if (!plan.IsLast(page) || choices == 0) return true;
            int n = plan.Lines(page);
            return ModalJpMetrics.RowsThatFit(plan.height, lineH, n > 0, n, plan.rowH, detail) >= choices;
        }

        /// <summary>最後の頁に置ける本文の行数 (<see cref="TextLines"/> まで)。 本文 0 行でも選択肢が入らなければ -1。</summary>
        private static int LastLines(int choices, int rowH, bool detail, int lineH)
        {
            for (int n = TextLines; n >= 0; n--)
            {
                int h = ModalJpMetrics.HeightUncapped(lineH, n > 0, n, choices, rowH, detail && choices > 0);
                if (h <= ModalJpMetrics.MaxH && (choices == 0 || ModalJpMetrics.RowsThatFit(h, lineH, n > 0, n, rowH, detail) >= choices)) return n;
            }
            return -1;
        }

        private static int Height(Plan plan, int choices, bool detail, int lineH)
        {
            int h = 0;
            for (int i = 0; i < plan.pages.Count; i++)
            {
                int n = plan.pages[i].Count; bool last = i == plan.pages.Count - 1;
                int hi = last ? ModalJpMetrics.HeightUncapped(lineH, n > 0, n, choices, plan.rowH, detail && choices > 0)
                              : ModalJpMetrics.HeightUncapped(lineH, n > 0, n, 0, 0, false);
                if (hi > h) h = hi;
            }
            return h > ModalJpMetrics.MaxH ? ModalJpMetrics.MaxH : h;
        }
    }
}
