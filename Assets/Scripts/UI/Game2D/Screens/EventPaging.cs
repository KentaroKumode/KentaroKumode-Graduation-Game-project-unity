using System;
using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// イベントの画面の頁割り (2026-11-13・2026-11-16 に本文の板の配置 <see cref="EventLayout"/> へ合わせて作り直し)。 導入 (フレーバー) と
    /// 結果 (結果の行 + 選んだ後の文) を、 本文の板の幅 (<see cref="EventLayout.BodyW"/>) と行数で頁に割る。 <b>見せ方だけ</b> ── 進行 (乱数・効果・BOT の経路) には触らない。
    /// <b>UnityEngine に依存しない</b> (検算 Tools/event_page_check がこのファイルをそのままコンパイルして、 全イベントで「切り捨てられる文 0」を見る)。
    ///
    /// <para><b>決まり</b> (docs/GAME.md §17「イベントの画面」):
    /// <list type="bullet">
    /// <item>導入の「／」(<see cref="JpTextLayout.PageBreak"/>) は段落の区切り = 頁の区切り。 1 段落 = 1 頁。 1 頁は <see cref="TextLines"/> 行まで。 超える段落は行の切れ目で次の頁へ送る</item>
    /// <item>選択肢は最後の頁だけに出すが、 本文の板の外 (右の列) なので、 最後の頁も本文を <see cref="TextLines"/> 行まで使う
    ///   (2026-11-16 まで窓 1 枚の時は、 最後の頁の本文を選択肢に譲っていた)</item>
    /// <item>結果の文も同じ規則で割る</item>
    /// </list></para>
    /// </summary>
    public static class EventPaging
    {
        /// <summary>1 頁の本文の行数の上限 (標準の行送りで本文の板に入る 8 行)。 行間の設定で行送りが伸びた時は、 板に入る行数 (<see cref="EventLayout.BodyLinesFit"/>) まで減らす
        /// (<see cref="LinesPerPage"/>・2026-11-16)。 文は切り捨てず、 頁が増える。</summary>
        public const int TextLines = 8;

        /// <summary>行送り lineH の時の 1 頁の本文の行数 (<see cref="TextLines"/> と板に入る行数の小さい方・最低 1)。</summary>
        public static int LinesPerPage(int lineH) => Math.Max(1, Math.Min(TextLines, EventLayout.BodyLinesFit(lineH)));

        public sealed class Plan
        {
            /// <summary>頁ごとの行 (折り返し済み)。 最低 1 頁。 最後の頁に選択肢を出す。</summary>
            public List<List<string>> pages;
            public int Count => pages.Count;
            public bool IsLast(int page) => page >= pages.Count - 1;
            /// <summary>頁 page の行。</summary>
            public List<string> Lines(int page) => pages[Clamp(page)];
            /// <summary>頁の位置 (「2/4」)。 1 頁しかない時は null。</summary>
            public string Mark(int page) => pages.Count > 1 ? (Clamp(page) + 1) + "/" + pages.Count : null;
            public int Clamp(int page) => page < 0 ? 0 : page >= pages.Count ? pages.Count - 1 : page;
        }

        /// <summary>導入の頁割り (lineH = 行送り・<see cref="Game2DJpFont.LineH"/>)。</summary>
        public static Plan Intro(string flavor, Func<char, int> advance, int lineH) => Split(flavor, advance, lineH);

        /// <summary>結果 (結果の行と選んだ後の文) の頁割り。</summary>
        public static Plan Result(string text, Func<char, int> advance, int lineH) => Split(text, advance, lineH);

        private static Plan Split(string text, Func<char, int> advance, int lineH)
        {
            int n = LinesPerPage(lineH);
            return new Plan { pages = JpTextLayout.Pages(text, EventLayout.BodyW, n, n, advance) };
        }
    }
}
