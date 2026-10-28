using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の液晶の 1 画面 (480×270 の dot)。 正本は docs/design/map-prototype.html の「液晶の表示」(<c>LC</c> / <c>INK</c> / <c>PALETTES</c> /
    /// <c>F35</c> / <c>ICON9</c> と <c>ltext / glyph / lline / lellipse / lfill / lcirc / bracket</c>)。
    ///
    /// <para>このファイルは<b>描くための道具</b>だけ (色・字・絵・線・円・塗り)。 地図の作図は <c>Plot</c>、 天候・照明・偵察は <c>Fx</c>、
    /// 操作は <c>Input</c> の部分クラスにある。 すべて 1 dot 単位で打つ (線の描画機能・アンチエイリアスは使わない)。
    /// 半透明を使うのは HTML と同じ「照明の範囲」「魔石の範囲」の淡い塗りだけ。</para>
    /// </summary>
    public partial class Game2DMapView
    {
        public const int W = 480, H = 270;
        /// <summary>文字・記号まわりの dot の大きさ (HTML の PXS。 480×270 の画面では 2)。</summary>
        public const int Pxs = 2;

        private Color32[] _buf;
        private Texture2D _tex;

        // ── 液晶の配色 (層ごとに地の色だけ変え、 線・文字のインクは共通) ──
        private struct Lc
        {
            public Color32 bg, lite, shadow, landE, fill2, grid;
            public Color32 frameHi, dim, mid, hi, hot, red, redDim, warn;
        }

        private static Color32 Hex(string h) => new Color32(System.Convert.ToByte(h.Substring(1, 2), 16), System.Convert.ToByte(h.Substring(3, 2), 16), System.Convert.ToByte(h.Substring(5, 2), 16), 255);

        private static Lc Layer(string dark, string lite, string land, string landE, string ring) => new Lc
        {
            bg = Hex(dark), lite = Hex(lite), fill2 = Hex(land), landE = Hex(landE), grid = Hex(ring),
            shadow = Hex("#04060a"), frameHi = Hex("#c9c2aa"), dim = Hex("#6f7480"), mid = Hex("#a59f8a"), hi = Hex("#e2dcc6"), hot = Hex("#f2ecd8"),
            red = Hex("#c8553f"), redDim = Hex("#8a3a2c"), warn = Hex("#d9a24a"),
        };

        private static readonly Lc[] Pals =
        {
            default,
            Layer("#0a0e15", "#161e2b", "#1a2433", "#56657a", "#283346"),   // 層 1 紺
            Layer("#0b110d", "#18241c", "#1d2a21", "#526a54", "#263629"),   // 層 2 緑灰
            Layer("#0b1113", "#172225", "#1c282b", "#4f6466", "#26363a"),   // 層 3 灰青
            Layer("#120e0a", "#231b13", "#2a2016", "#7a6446", "#3a2e22"),   // 層 4 茶褐
            Layer("#120b10", "#231721", "#2a1b26", "#7a566e", "#3a2733"),   // 層 5 赤紫
            Layer("#0c0c0e", "#1b1b20", "#222228", "#5e5e6a", "#2e2e36"),   // 層 6 墨
            Layer("#130a0a", "#261515", "#2e1a1a", "#7a4e4e", "#3a2424"),   // 層 7 深緋
        };

        private Lc _lc = Pals[1];

        // 天候・照明・偵察の演出が使うインク (どの層でも共通)
        private static readonly Color32 InkDim = Hex("#6f7480"), InkMid = Hex("#a59f8a"), InkHi = Hex("#e2dcc6"), InkHot = Hex("#f2ecd8"), InkRed = Hex("#c8553f");

        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        // ── 3×5 のドット字 (大文字・数字・記号少し) ──
        internal static readonly System.Collections.Generic.Dictionary<char, string> F35 = new System.Collections.Generic.Dictionary<char, string>
        {
            {'A',"010101111101101"},{'B',"110101110101110"},{'C',"011100100100011"},{'D',"110101101101110"},{'E',"111100110100111"},{'F',"111100110100100"},
            {'G',"011100101101011"},{'H',"101101111101101"},{'I',"111010010010111"},{'J',"001001001101010"},{'K',"101101110101101"},{'L',"100100100100111"},
            {'M',"101111111101101"},{'N',"110101101101101"},{'O',"010101101101010"},{'P',"110101110100100"},{'Q',"010101101110011"},{'R',"110101110101101"},
            {'S',"011100010001110"},{'T',"111010010010010"},{'U',"101101101101111"},{'V',"101101101101010"},{'W',"101101111111101"},{'X',"101101010101101"},
            {'Y',"101101010010010"},{'Z',"111001010100111"},{'0',"111101101101111"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},
            {'4',"101101111001001"},{'5',"111100110001110"},{'6',"011100111101111"},{'7',"111001010010010"},{'8',"111101111101111"},{'9',"111101111001110"},
            {'-',"000000111000000"},{'+',"000010111010000"},{'.',"000000000000010"},{':',"000010000010000"},{'/',"001001010100100"},{' ',"000000000000000"},
        };

        // ── 点のアイコン (17×17・1 dot で描く) ──
        // 旧ノード (Assets/gui/map_icon.png の 32×32) で使っていた項目を、 区別がつく大きさに書き直したもの:
        //   戦闘 = 交差した剣 / エリート = どくろ (赤) / ショップ = 日除けのある店 / 拠点 = 家 / 宝 = 宝石 / ボス = 王冠 / ? = 疑問符 / イベント = 渦 / 休憩 = テント  (罠は存在しない)
        private static readonly string[] IcoBattle = new[] { "11100000000000111", "11110000000001111", "11111000000011111", "01111100000111110", "00111110001111100", "00011111011111000", "00001111111110000", "00000111111100000", "00000011111000000", "00010111111101000", "00011111011111000", "00011110001111000", "00011110001111000", "00111111011111100", "01110000000001110", "11100000000000111", "11000000000000011" };
        private static readonly string[] IcoElite = new[] { "11100000000000111", "11110000100001111", "11111001110011111", "01111101110111110", "00111101110111100", "00011101110111000", "00001101110110000", "00000100100100000", "00000000000000000", "00010000100001000", "00011101110111000", "00011100100111000", "00011110001111000", "00111111011111100", "01110000000001110", "11100000000000111", "11000000000000011" };
        private static readonly string[] IcoShop = new[] { "00000000000000000", "00000001110011100", "00000011011011100", "00000110011111100", "00001100110111100", "00011001100111100", "00110011001101100", "01100110011001110", "11111111111111111", "00000000000000000", "00111111111111100", "00111111100100100", "00111111111111100", "00111111100100100", "00100111100100100", "00100111111111100", "00100111111111100" };
        private static readonly string[] IcoDepot = new[] { "00000000000000000", "00111111111111100", "01111111111111110", "01111111111111110", "11111111111111111", "11111111111111111", "11111111111111111", "01110000000001110", "01110000000001110", "00110111011101100", "00110111011101100", "11111111111111111", "01111111111111110", "01111111111111110", "01111111111111110", "11111111111111111", "11111111111111111" };       // 補給拠点 (新設・フレア / 罠 / 物資を補給できる)
        private static readonly string[] IcoStart = new[] { "01110000000000000", "00100000000000000", "00111111111111100", "00111111111111000", "00111111111110000", "00111111111100000", "00111111111000000", "00111111111100000", "00111111111110000", "00111111111111000", "00111111111111100", "00100000000000000", "00100000000000000", "00100000000000000", "00100000000000000", "00100000000000000", "11111110000000000" };       // 前哨基地 = 旗
        private static readonly string[] IcoTreasure = new[] { "00000000000000000", "00000000000000000", "00101110001110100", "01100100100100110", "11110001110001111", "00000000000000000", "11110011111001111", "01111011111011110", "00111001110011100", "00011101110111000", "00001100100110000", "00000110101100000", "00000010001000000", "00000001010000000", "00000000100000000", "00000000000000000", "00000000000000000" };
        private static readonly string[] IcoExit = new[] { "00000000000011000000000000", "00000001100111100110000000", "00000011001111110011000000", "00000110011100111001100000", "00000000011000011000000000", "00000001000000000010000000", "01110001100100100110001110", "00111000110100101100011100", "01111110110111101101111110", "11001110010011001001110011", "10001111011011011011110001", "00000111000000000011100000", "00011111111000011111111000", "00111111101111110111111100", "01110011100111100111001110", "11000001110011001110000011", "10000000111111111100000001", "10000000011111111000000001", "00000000011111111000000000", "00000000001111110000000000", "00000000001011010000000000", "00000000000011000000000000" };
        private static readonly string[] IcoUnknown = new[] { "00000000000000000", "00111111111110000", "00111111111111000", "00111000000111100", "00111101100011100", "00011111110011100", "00001111111011100", "00000000000011100", "00000001111111100", "00000001111111100", "00000001110000000", "00000000110000000", "00000000010000000", "00000000000000000", "00000001110000000", "00000000110000000", "00000000010000000" };
        private static readonly string[] IcoEvent = new[] { "00000001111110000", "00000011111100000", "00000111000000000", "00001110001000000", "00011100011100000", "00111000111110000", "01110001110111000", "11100011100011100", "01110111011101110", "00111000111000111", "00011101110001110", "00001111100011100", "00000111000111000", "00000010001110000", "00000000011100000", "00000111111000000", "00001111110000000" };
        private static readonly string[] IcoRest = new[] { "00000000000000000", "00000000000000000", "00000010001000000", "00000011111000000", "00000011111000000", "00000111111100000", "00000111111100000", "00001101111110000", "00001101111110000", "00011000111111000", "00011000111111000", "00110000011111100", "00110111011111100", "01100111001111110", "01111111111111110", "11111111111111111", "00000000000000000" };
        /// <summary>画面の見出しなどに使う、 ノードのアイコン (17×17)。 名前: battle / elite / shop / depot / start / treasure / exit / unknown / event / rest。</summary>
        internal static string[] IconRows(string name)
        {
            switch (name)
            {
                case "battle": return IcoBattle; case "elite": return IcoElite; case "shop": return IcoShop; case "depot": return IcoDepot; case "start": return IcoStart;
                case "treasure": return IcoTreasure; case "exit": return IcoExit; case "unknown": return IcoUnknown; case "event": return IcoEvent; case "rest": return IcoRest;
            }
            return null;
        }

        private static readonly string[] IcoBase = IcoTreasure;      // 交換 (旧アトラスに専用の絵が無い)
        private static readonly string[] IcoDone = { "01110", "10001", "10001", "10001", "01110" };       // 既定 (どの種別にも当たらない時)
        private static readonly string[] IcoFoe = { "000010000", "000111000", "000111000", "001111100", "001101100", "011111110", "011111110", "111111111" };
        private static readonly string[] GlyphExcl = { "01110", "11011", "11011", "11011", "11111", "11011", "01110" };     // 位置がバレた印「!」
        private static readonly string[] GlyphX = { "101", "010", "101" };
        private static readonly string[] GlyphPlus = { "010", "111", "010" };

        // =====================================================================
        //  テクスチャ・点・四角
        // =====================================================================

        private void EnsureCanvas()
        {
            if (_tex != null) return;
            _buf = new Color32[W * H];
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DMapView" };
            Fill(_lc.bg); Upload();
        }

        private void Fill(Color32 c) { for (int i = 0; i < _buf.Length; i++) _buf[i] = c; }

        private void Upload() { _tex.SetPixels32(_buf); _tex.Apply(false); }

        private void Put(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            _buf[(H - 1 - y) * W + x] = c;
        }

        /// <summary>下の色と混ぜて置く (照明・魔石の範囲の淡い塗り用)。</summary>
        private void PutA(int x, int y, Color32 c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = (H - 1 - y) * W + x; var d = _buf[i];
            _buf[i] = new Color32((byte)(d.r + (c.r - d.r) * a), (byte)(d.g + (c.g - d.g) * a), (byte)(d.b + (c.b - d.b) * a), 255);
        }

        private void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Put(x + i, y + j, c);
        }

        /// <summary>中心 (x, y)・1 辺 w の四角 (HTML の px)。</summary>
        private void PxC(float x, float y, int w, Color32 c) => Rect(Mathf.RoundToInt(x - w / 2f), Mathf.RoundToInt(y - w / 2f), w, w, c);

        private static Color32 Mix(Color32 a, Color32 b, float t)
            => new Color32((byte)Mathf.Round(a.r + (b.r - a.r) * t), (byte)Mathf.Round(a.g + (b.g - a.g) * t), (byte)Mathf.Round(a.b + (b.b - a.b) * t), 255);

        // =====================================================================
        //  字・絵
        // =====================================================================

        public enum Align { Left, Center, Right }

        /// <summary>3×5 の字 (HTML の ltext)。 1 dot が Pxs dot。 (x, y) は文字の縦の中心。</summary>
        private void Text(string s, float x, float y, Color32 col, Align align = Align.Center)
        {
            s = s.ToUpperInvariant();
            int w = (s.Length * 4 - 1) * Pxs;
            int x0 = Mathf.RoundToInt(align == Align.Left ? x : align == Align.Right ? x - w : x - w / 2f);
            int y0 = Mathf.RoundToInt(y - 2.5f * Pxs);
            foreach (char ch in s)
            {
                if (!F35.TryGetValue(ch, out var g)) g = F35[' '];
                for (int i = 0; i < 15; i++)
                    if (g[i] == '1') Rect(x0 + (i % 3) * Pxs, y0 + (i / 3) * Pxs, Pxs, Pxs, col);
                x0 += 4 * Pxs;
            }
        }

        /// <summary>行の文字列で描いた絵 (HTML の glyph)。 (x, y) は絵の中心、 u は 1 マスの大きさ。</summary>
        private void Glyph(string[] rows, float x, float y, Color32 col, int u = 1)
        {
            int h = rows.Length, w = rows[0].Length;
            int x0 = Mathf.FloorToInt(x - w * u / 2f + 0.001f), y0 = Mathf.FloorToInt(y - h * u / 2f + 0.001f);      // 偶数の絵は半 dot ずれる。 RoundToInt (偶数丸め) だと 1 dot ごとに行ったり来たりするので、 切り捨てで固定する
            for (int r = 0; r < h; r++) for (int c = 0; c < w; c++) if (rows[r][c] == '1') Rect(x0 + c * u, y0 + r * u, u, u, col);
        }

        // =====================================================================
        //  線・円・塗り
        // =====================================================================

        private static bool DashOn(int i, int[] dash)
        {
            if (dash == null || dash.Length == 0) return true;
            int per = 0; foreach (int d in dash) per += d;
            int k = i % per;
            for (int d = 0; d < dash.Length; d++) { if (k < dash[d]) return d % 2 == 0; k -= dash[d]; }
            return true;
        }

        /// <summary>1 dot の線 (HTML の lline・ブレゼンハム)。 dash は {描く, 空ける, ...}。</summary>
        private void Line(Vector2 a, Vector2 b, Color32 col, int[] dash = null)
        {
            int x0 = Mathf.FloorToInt(a.x), y0 = Mathf.FloorToInt(a.y), x1 = Mathf.FloorToInt(b.x), y1 = Mathf.FloorToInt(b.y);
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy, i = 0;
            for (int guard = 0; guard < 4000; guard++)
            {
                if (DashOn(i++, dash)) Put(x0, y0, col);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>楕円の輪郭 (HTML の lellipse): 隣り合う dot が途切れないよう角度を細かく刻む。</summary>
        private void Ellipse(Vector2 c, float ra, float rb, float rot, Color32 col, int[] dash = null)
        {
            int n = Mathf.Max(24, Mathf.CeilToInt(Mathf.PI * 2 * Mathf.Max(ra, rb) * 2));
            float co = Mathf.Cos(rot), si = Mathf.Sin(rot);
            int lx = int.MinValue, ly = int.MinValue, i = 0;
            for (int k = 0; k < n; k++)
            {
                float t = (float)k / n * Mathf.PI * 2, ex = Mathf.Cos(t) * ra, ey = Mathf.Sin(t) * rb;
                int x = Mathf.FloorToInt(c.x + ex * co - ey * si), y = Mathf.FloorToInt(c.y + ex * si + ey * co);
                if (x == lx && y == ly) continue;
                lx = x; ly = y;
                if (DashOn(i++, dash)) Put(x, y, col);
            }
        }

        /// <summary>塗りつぶしの円を、 下の色と混ぜて塗る (HTML の lcirc: 範囲の淡い塗り)。</summary>
        private void Disc(float cx, float cy, float R, Color32 col, float alpha)
        {
            int ix = Mathf.FloorToInt(cx), iy = Mathf.FloorToInt(cy), n = Mathf.CeilToInt(R);
            float r2 = R * R;
            for (int y = -n; y <= n; y++) for (int x = -n; x <= n; x++) if (x * x + y * y <= r2) PutA(ix + x, iy + y, col, alpha);
        }

        /// <summary>四隅のかぎ括弧 (HTML の bracket)。</summary>
        private void Bracket(Vector2 q, float s, Color32 col)
        {
            int L = 2 * Pxs; s *= Pxs;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                int x = Mathf.FloorToInt(q.x + sx * s), y = Mathf.FloorToInt(q.y + sy * s);
                Rect(sx < 0 ? x : x - L + 1, y, L, 1, col);
                Rect(x, sy < 0 ? y : y - L + 1, 1, L, col);
            }
        }
    }
}
