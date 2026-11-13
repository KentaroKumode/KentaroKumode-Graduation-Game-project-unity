using System;
using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// 持ち物の画面 (<see cref="InventoryGridView"/>) の寸法の式 (2026-11-13)。 窓の中の場所・升目の並べ方・上下の送り・
    /// 矢印でのフォーカスの動き・吹き出しの置き場・詳細の欄の頁割りを<b>この 1 か所</b>で決める。
    /// <b>UnityEngine に依存しない</b> ── 検算 Tools/inventory_check がこのファイルをそのままコンパイルして、 品の数 0〜200 で
    /// 「升目・見出し・詳細の欄・吹き出しが 480×270 の中に収まる」「全部の品の詳細が切り捨てなく読める」を数える。
    /// 単位は 480×270 の格子の dot。 座標は窓の左上が原点 (窓は格子の真ん中に置く・<see cref="WindowX"/>)。 字の行送りは呼ぶ側が渡す。
    ///
    /// <para>窓の中 (幅 472・高さ 266):
    /// <code>
    ///  題「持ち物」 ………………………………… HP・物資・所持金
    ///  ──────────────────────────────
    ///  ┌ 升目の欄 (幅 258) ──────┐ ┌ 詳細の欄 (幅 176) ┐
    ///  │ 秘宝 12 ──────────── ▮│ │ 品名              │
    ///  │ □□□□□□□□□□□□  ▮│ │ 秘宝・金  軸 会心 │
    ///  │ □□□□□□□□□□□□  │ │                   │
    ///  │ 武器 2 ─────────────│ │ 効果の説明文      │
    ///  │ □□                      │ │                   │
    ///  │ 消耗品 3 …              │ │ フレーバー文 1/2 │
    ///  └──────────────┘ └─────────┘
    ///  足元 (操作の案内・トースト)
    ///                               [使う] [ステータス] [閉じる]
    /// </code></para>
    /// </summary>
    public static class InventoryLayout
    {
        /// <summary>窓の幅・高さ (高さは日本語の窓の上限 <see cref="ModalJpMetrics.MaxH"/>)。</summary>
        public const int W = 472, H = ModalJpMetrics.MaxH;
        /// <summary>GUI の格子 (Game2DLayout と同じ値・検算が使う)。</summary>
        public const int ScreenW = 480, ScreenH = 270;
        /// <summary>窓の左上 (格子の中・Game2DModal が真ん中に置く)。</summary>
        public const int WindowX = (ScreenW - W) / 2, WindowY = (ScreenH - H) / 2;

        /// <summary>升目 1 つの大きさ (枠を含む 18)・内側 (アイコン 16)・並びの間隔 (20 = 間 2)。</summary>
        public const int Cell = 18, CellInner = 16, Pitch = 20;
        /// <summary>詳細の欄の幅・升目の欄との間・送りの棒の幅。</summary>
        public const int DetailW = 176, PaneGap = 6, BarW = 3;
        /// <summary>詳細の欄・吹き出しの内側の余白。</summary>
        public const int Pad = 4;
        /// <summary>吹き出しの幅の上限 (枠を含む)。</summary>
        public const int TipMaxW = 184;

        public struct Box
        {
            public int x, y, w, h;
            public Box(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; }
            public int Right => x + w;
            public int Bottom => y + h;
            public bool Contains(int px, int py) => px >= x && px < x + w && py >= y && py < y + h;
            public bool Inside(Box o) => x >= o.x && y >= o.y && Right <= o.Right && Bottom <= o.Bottom;
            public bool Overlaps(Box o) => x < o.Right && o.x < Right && y < o.Bottom && o.y < Bottom;
            public override string ToString() => "(" + x + "," + y + " " + w + "×" + h + ")";
        }

        // ── 窓の中の場所 ──

        /// <summary>中身の場所 (題と線の下から、 足元の 2 dot 上まで)。 式は日本語の窓 (<see cref="ModalJpMetrics"/>・Game2DModal.RedrawJp) と同じ。</summary>
        public static Box Content(int lineH)
        {
            int y = ModalJpMetrics.Inset - 4 + ModalJpMetrics.TitleH + ModalJpMetrics.RuleH;
            int btnY = H - ModalJpMetrics.Inset - ModalJpMetrics.BtnH, footY = btnY - 4 - lineH, bottom = footY - 2;
            return new Box(ModalJpMetrics.Inset, y, W - 2 * ModalJpMetrics.Inset, bottom - y);
        }

        /// <summary>升目の欄 (左)。 右端の <see cref="BarW"/> は送りの棒。</summary>
        public static Box Grid(int lineH) { var c = Content(lineH); return new Box(c.x, c.y, c.w - DetailW - PaneGap, c.h); }

        /// <summary>詳細の欄 (右)。</summary>
        public static Box Detail(int lineH) { var c = Content(lineH); return new Box(c.Right - DetailW, c.y, DetailW, c.h); }

        /// <summary>1 段に並ぶ升目の数 (送りの棒と 2 dot の間を除いた幅に入る数)。</summary>
        public static int Cols(int lineH) => Math.Max(1, (Grid(lineH).w - BarW - 2 + (Pitch - Cell)) / Pitch);

        /// <summary>見出しの行の高さ。</summary>
        public static int HeadH(int lineH) => lineH + 2;

        // ── 行 (見出しと升目の段) ──

        /// <summary>升目の欄の 1 行: 見出し (section の名前と数) か、 升目の段 (flat の first から count 個)。</summary>
        public struct Line
        {
            public bool heading; public int section, first, count;
            public int Height(int lineH) => heading ? HeadH(lineH) : Pitch;
        }

        /// <summary>区切りごとの品の数から行を作る。 空の区切りは見出しも出さない。 flat の並び = 区切りの順に詰めたもの。</summary>
        public static List<Line> BuildLines(IList<int> sectionCounts, int cols)
        {
            var l = new List<Line>(); int flat = 0;
            for (int s = 0; s < sectionCounts.Count; s++)
            {
                int n = sectionCounts[s]; if (n <= 0) continue;
                l.Add(new Line { heading = true, section = s, first = flat, count = n });
                for (int i = 0; i < n; i += cols) l.Add(new Line { section = s, first = flat + i, count = Math.Min(cols, n - i) });
                flat += n;
            }
            return l;
        }

        /// <summary>top 行目から、 高さ h に入る行の数 (最低 1 行・行が無ければ 0)。</summary>
        public static int VisibleCount(List<Line> lines, int top, int h, int lineH)
        {
            int y = 0, n = 0;
            for (int i = top; i < lines.Count; i++) { y += lines[i].Height(lineH); if (y > h && n > 0) break; n++; if (y > h) break; }
            return n;
        }

        /// <summary>送りの上限 (最後の行が欄の下端に来る top)。</summary>
        public static int MaxTop(List<Line> lines, int h, int lineH)
        {
            int y = 0;
            for (int i = lines.Count - 1; i >= 0; i--) { y += lines[i].Height(lineH); if (y > h) return i + 1; }
            return 0;
        }

        public static int ClampTop(List<Line> lines, int top, int h, int lineH) => Math.Max(0, Math.Min(top, MaxTop(lines, h, lineH)));

        /// <summary>flat 番目の品が入っている升目の段の行の番号 (無ければ -1)。</summary>
        public static int LineOf(List<Line> lines, int flat)
        {
            for (int i = 0; i < lines.Count; i++) if (!lines[i].heading && flat >= lines[i].first && flat < lines[i].first + lines[i].count) return i;
            return -1;
        }

        /// <summary>flat 番目の品が見えるように top を動かす。 区切りの最初の段なら見出しも見せる。</summary>
        public static int EnsureVisible(List<Line> lines, int top, int flat, int h, int lineH)
        {
            int li = LineOf(lines, flat); if (li < 0) return ClampTop(lines, top, h, lineH);
            int want = li > 0 && lines[li - 1].heading ? li - 1 : li;
            if (want < top) top = want;
            while (top < li && !(li < top + VisibleCount(lines, top, h, lineH))) top++;
            return ClampTop(lines, top, h, lineH);
        }

        /// <summary>升目の欄の中で、 top から見える升目の位置 (窓の座標)。 flat の番号と箱の組。</summary>
        public static List<KeyValuePair<int, Box>> CellBoxes(List<Line> lines, int top, Box grid, int lineH)
        {
            var l = new List<KeyValuePair<int, Box>>();
            int y = grid.y, n = VisibleCount(lines, top, grid.h, lineH);
            for (int i = top; i < top + n; i++)
            {
                var ln = lines[i];
                if (!ln.heading) for (int k = 0; k < ln.count; k++) l.Add(new KeyValuePair<int, Box>(ln.first + k, new Box(grid.x + 1 + k * Pitch, y + 1, Cell, Cell)));
                y += ln.Height(lineH);
            }
            return l;
        }

        /// <summary>
        /// 矢印でフォーカスを動かす。 dx = ←→ (並びの順に 1 つ・区切りをまたぐ)、 dy = ↑↓ (同じ列の上下の段・区切りをまたぐ時は
        /// 隣の区切りの最後/最初の段の、 同じ列か一番右)。 端では止まる。 フォーカスが無い (-1) 時は最初の品。 品が無ければ -1。
        /// </summary>
        public static int MoveFocus(List<Line> lines, int focus, int dx, int dy, int total)
        {
            if (total <= 0) return -1;
            if (focus < 0 || focus >= total) return 0;
            if (dx != 0) return Math.Max(0, Math.Min(total - 1, focus + dx));
            if (dy == 0) return focus;
            int li = LineOf(lines, focus); if (li < 0) return focus;
            int col = focus - lines[li].first;
            for (int i = li + Math.Sign(dy); i >= 0 && i < lines.Count; i += Math.Sign(dy))
                if (!lines[i].heading) return lines[i].first + Math.Min(col, lines[i].count - 1);
            return focus;
        }

        // ── 吹き出し ──

        /// <summary>吹き出しを置ける場所 (窓の内側・枠の 2 dot 内側)。</summary>
        public static Box TipBounds => new Box(2, 2, W - 4, H - 4);

        /// <summary>吹き出しの文の折り返しの幅。</summary>
        public const int TipTextW = TipMaxW - 2 * Pad;

        /// <summary>吹き出しの大きさ: 行のいちばん長い幅 + 余白 / 行数 × 行送り + 余白。</summary>
        public static void TipSize(IList<string> lines, Func<char, int> advance, int lineH, out int w, out int h)
        {
            int mw = 0;
            foreach (var s in lines) { int x = 0; foreach (var ch in s) x += advance(ch); if (x > mw) mw = x; }
            w = Math.Min(TipMaxW, mw + 2 * Pad); h = lines.Count * lineH + 2 * Pad - 1;
        }

        /// <summary>吹き出しの置き場: 升目の右 (入らなければ左)、 上端は升目の上端にそろえ、 窓の内側 (<see cref="TipBounds"/>) へ寄せる。</summary>
        public static Box TipBox(Box anchor, int w, int h)
        {
            var b = TipBounds;
            int x = anchor.Right + 3;
            if (x + w > b.Right) x = anchor.x - 3 - w;
            x = Math.Max(b.x, Math.Min(x, b.Right - w));
            int y = Math.Max(b.y, Math.Min(anchor.y, b.Bottom - h));
            return new Box(x, y, w, h);
        }

        // ── 詳細の欄 ──

        /// <summary>詳細の欄の文の折り返しの幅。</summary>
        public static int DetailTextW => DetailW - 2 * Pad;

        /// <summary>詳細の欄の 1 頁の行数 (下の 1 行は頁の位置「1/2」に空ける)。</summary>
        public static int DetailPerPage(int lineH) => PerPage(Detail(lineH).h, lineH);

        /// <summary>高さ h の詳細の欄の 1 頁の行数 (上下の余白と、 頁の位置の 1 行を除く)。</summary>
        public static int PerPage(int h, int lineH) => Math.Max(1, (h - 2 * Pad - lineH) / lineH);

        /// <summary>行を頁に割る (1 頁 perPage 行)。 字のある行は捨てない。 頁の頭に来た空行 (区切り) だけは落とす (空行で始まる頁を作らない)。 行が無くても 1 頁。</summary>
        public static List<List<T>> Pages<T>(List<T> lines, int perPage, Func<T, bool> isBlank)
        {
            var pages = new List<List<T>>(); var cur = new List<T>();
            foreach (var x in lines)
            {
                if (cur.Count >= perPage) { if (isBlank(x)) continue; pages.Add(cur); cur = new List<T>(); }
                cur.Add(x);
            }
            pages.Add(cur);
            return pages;
        }
    }
}
