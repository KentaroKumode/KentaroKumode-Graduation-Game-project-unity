using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>読み出した絵 (上の行が先頭)。</summary>
    public sealed class KitSrc
    {
        public int w, h; public Color32[] px;
        /// <summary>時計回りに 90° × quarter 回した複製 (丸ボタンの下向きの矢印を、 左・右・上向きにする)。</summary>
        public KitSrc Rot(int quarter)
        {
            quarter = ((quarter % 4) + 4) % 4; var c = this;
            for (int q = 0; q < quarter; q++)
            {
                var n = new KitSrc { w = c.h, h = c.w, px = new Color32[c.px.Length] };
                for (int y = 0; y < c.h; y++) for (int x = 0; x < c.w; x++) n.px[x * n.w + (c.h - 1 - y)] = c.px[y * c.w + x];
                c = n;
            }
            return c;
        }

        public static KitSrc From(Texture2D t)
        {
            if (t == null) return null;
            var s = new KitSrc { w = t.width, h = t.height, px = new Color32[t.width * t.height] };
            var p = t.GetPixels32();
            for (int y = 0; y < s.h; y++) System.Array.Copy(p, (s.h - 1 - y) * s.w, s.px, y * s.w, s.w);
            return s;
        }
    }

    public static class Game2DKit
    {
        public static readonly Color32 Ink = new Color32(0xd9, 0xd4, 0xc4, 255), Dim = new Color32(0x8b, 0x7a, 0x60, 255), Faint = new Color32(0x5a, 0x55, 0x48, 255),
            Dark = new Color32(0x05, 0x03, 0x02, 255), Off = new Color32(0x2a, 0x22, 0x18, 255), Well = new Color32(0x1c, 0x22, 0x1e, 255),
            Amber = new Color32(0xe0, 0xa0, 0x30, 255), Red = new Color32(0xe0, 0x60, 0x3c, 255), Blue = new Color32(0x4a, 0x86, 0xd8, 255), Gold = new Color32(0xd8, 0xb4, 0x3a, 255),
            Green = new Color32(0x6c, 0xc0, 0x6c, 255), Ivory = new Color32(0xe8, 0xdc, 0xc0, 255);

        private static KitSrc _frame, _btn, _go, _die, _row, _tab, _chip, _banner, _slot, _lampOn, _lampOff, _round, _dial, _gauge, _atlas; private static KitSrc[] _dice = new KitSrc[6];
        private static string[] _iconNames = new string[0]; private static bool _init;
        /// <summary>true の間は、 生成した絵を一切使わず、 長方形 + 文字だけで描く (仮置きの見た目の確認用・2026-10-24)。 絵を戻す時は false。</summary>
        public const bool Plain = true;
        public static void Reset() { _init = false; }
        private static void Init()
        {
            if (_init) return; _init = true;
            if (Plain) return;
            var a = Game2DKitAssets.Instance; if (a == null) return;
            _frame = KitSrc.From(a.frameTex); _btn = KitSrc.From(a.btnTex); _go = KitSrc.From(a.goTex); _die = KitSrc.From(a.dieTex);
            _row = KitSrc.From(a.rowTex); _tab = KitSrc.From(a.tabTex); _chip = KitSrc.From(a.chipTex); _banner = KitSrc.From(a.bannerTex); _slot = KitSrc.From(a.slotTex);
            _lampOn = KitSrc.From(a.lampOnTex); _lampOff = KitSrc.From(a.lampOffTex); _round = KitSrc.From(a.roundTex); _dial = KitSrc.From(a.dialTex); _gauge = KitSrc.From(a.gaugeTex);
            _atlas = KitSrc.From(a.iconAtlas); _iconNames = string.IsNullOrEmpty(a.iconNames) ? new string[0] : a.iconNames.Split(',');
            for (int i = 0; i < 6; i++) _dice[i] = a.diceTex != null && i < a.diceTex.Length ? KitSrc.From(a.diceTex[i]) : null;
        }
        public static KitSrc Frame { get { Init(); return _frame; } }
        public static KitSrc Btn { get { Init(); return _btn; } }
        public static KitSrc Go { get { Init(); return _go; } }
        public static KitSrc Die { get { Init(); return _die; } }
        public static KitSrc Row { get { Init(); return _row; } }
        public static KitSrc Tab { get { Init(); return _tab; } }
        public static KitSrc Chip { get { Init(); return _chip; } }
        public static KitSrc Banner { get { Init(); return _banner; } }
        public static KitSrc Slot { get { Init(); return _slot; } }
        public static KitSrc LampOn { get { Init(); return _lampOn; } }
        public static KitSrc LampOff { get { Init(); return _lampOff; } }
        public static KitSrc Round { get { Init(); return _round; } }
        public static KitSrc Dial { get { Init(); return _dial; } }
        public static KitSrc Gauge { get { Init(); return _gauge; } }
        public static KitSrc DieFace(int v) { Init(); return v >= 1 && v <= 6 ? _dice[v - 1] : null; }
        /// <summary>アイコンのアトラスの中の位置 (見つからなければ -1)。</summary>
        public static int IconIndex(string name) { Init(); if (_atlas == null || name == null) return -1; return System.Array.IndexOf(_iconNames, name); }
        public static KitSrc Atlas { get { Init(); return _atlas; } }

        // 3×5 の字に無い記号
        private static readonly Dictionary<char, string> Extra = new Dictionary<char, string>
        {
            {'<',"001010100010001"},{'>',"100010001010100"},{'=',"000111000111000"},{'_',"000000000000111"},{'?',"110001010000010"},{'!',"010010010000010"},
            {'(',"010100100100010"},{')',"010001001001010"},{'%',"101001010100101"},{',',"000000000010100"},{';',"000010000010100"},{'[',"110100100100110"},
            {']',"011001001001011"},{'*',"000101010101000"},{'#',"101111101111101"},{'\'',"010010000000000"},{'"',"101101000000000"},{'~',"000011110000000"},{'$',"011110011011110"},
        };

        public static bool TryGlyph(char ch, out string g)
        {
            ch = char.ToUpperInvariant(ch);
            return Game2DMapView.F35.TryGetValue(ch, out g) || Extra.TryGetValue(ch, out g);
        }

        public static int TextWidth(string s, int scale = 1) => s.Length == 0 ? 0 : s.Length * 4 * scale - scale;
    }

    /// <summary>1 枚のテクスチャへ dot を打つ道具 (上が先頭の座標)。</summary>
    public sealed class KitPix
    {
        public readonly int w, h; public readonly Color32[] px;
        public KitPix(int w, int h) { this.w = w; this.h = h; px = new Color32[w * h]; }

        public void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < w && y < h) px[(h - 1 - y) * w + x] = c; }
        public void Rect(int x, int y, int rw, int rh, Color32 c) { for (int j = 0; j < rh; j++) for (int i = 0; i < rw; i++) Set(x + i, y + j, c); }
        public void Box(int x, int y, int rw, int rh, Color32 fill, Color32 edge) { Rect(x, y, rw, rh, edge); Rect(x + 1, y + 1, rw - 2, rh - 2, fill); }

        public void Text(string s, int x, int y, Color32 c, int scale = 1)
        {
            for (int k = 0; k < s.Length; k++)
            {
                if (!Game2DKit.TryGlyph(s[k], out var g)) continue;
                for (int i = 0; i < 15; i++) if (g[i] == '1') Rect(x + k * 4 * scale + (i % 3) * scale, y + (i / 3) * scale, scale, scale, c);
            }
        }
        public void TextC(string s, RectInt r, Color32 c, int scale = 1) { Text(s, r.x + (r.width - Game2DKit.TextWidth(s, scale)) / 2, r.y + (r.height - 5 * scale) / 2, c, scale); }
        public void TextR(string s, int right, int y, Color32 c, int scale = 1) { Text(s, right - Game2DKit.TextWidth(s, scale), y, c, scale); }

        /// <summary>幅 maxW (dot) に収まるよう、 空白で折り返して描く。 戻り値 = 描いた行数。</summary>
        public int Wrap(string s, int x, int y, int maxW, Color32 c, int lineH = 8)
        {
            int per = Mathf.Max(1, (maxW + 1) / 4), lines = 0; var words = s.Split(' '); var cur = "";
            foreach (var wd in words)
            {
                if (cur.Length > 0 && cur.Length + 1 + wd.Length > per) { Text(cur, x, y + lines * lineH, c); lines++; cur = wd; }
                else cur = cur.Length == 0 ? wd : cur + " " + wd;
            }
            if (cur.Length > 0) { Text(cur, x, y + lines * lineH, c); lines++; }
            return lines;
        }

        /// <summary>行・タブ・計器の台 (暗い金属の板): 縁・上の照り・下の影つき。 mul = 明るさ (1 = 標準)。</summary>
        public void Plate(int x, int y, int rw, int rh, float mul = 1f)
        {
            Color32 F(int r, int g, int b) => new Color32((byte)Mathf.Min(255, r * mul), (byte)Mathf.Min(255, g * mul), (byte)Mathf.Min(255, b * mul), 255);
            Rect(x, y, rw, rh, Game2DKit.Dark);
            Rect(x + 1, y + 1, rw - 2, rh - 2, F(0x2c, 0x34, 0x30));
            Rect(x + 1, y + 1, rw - 2, 1, F(0x4a, 0x54, 0x4c)); Rect(x + 1, y + rh - 2, rw - 2, 1, F(0x1c, 0x22, 0x1e));
        }

        /// <summary>絵を (x, y) へ tw×th に最近傍で合わせて打つ (透明は飛ばす)。 色・明るさは変えない。</summary>
        public void Sprite(KitSrc s, int x, int y, int tw, int th)
        {
            if (s == null) { Box(x, y, tw, th, Game2DKit.Off, Game2DKit.Dark); return; }
            for (int j = 0; j < th; j++)
                for (int i = 0; i < tw; i++)
                {
                    var c = s.px[(j * s.h / th) * s.w + (i * s.w / tw)]; if (c.a < 128) continue;
                    Set(x + i, y + j, new Color32(c.r, c.g, c.b, 255));
                }
        }

        private void Put(int x, int y, Color32 c) { if (c.a >= 128) Set(x, y, new Color32(c.r, c.g, c.b, 255)); }

        /// <summary>枠の絵を 9 分割して、 w×h いっぱいに敷く (角はそのまま・辺はタイル・中は center)。</summary>
        public void NineSlice(KitSrc s, int b, Color32 center, int ox = 0, int oy = 0, int fw = -1, int fh = -1)
        {
            if (fw < 0) fw = w; if (fh < 0) fh = h;
            if (s == null) { Box(ox, oy, fw, fh, center, new Color32(0x6a, 0x56, 0x38, 255)); return; }
            Rect(ox, oy, fw, fh, center);
            int mw = s.w - 2 * b, mh = s.h - 2 * b;
            for (int x = b; x < fw - b; x++)
                for (int j = 0; j < b; j++)
                {
                    int sx = b + ((x - b) % mw);
                    Put(ox + x, oy + j, s.px[j * s.w + sx]); Put(ox + x, oy + fh - b + j, s.px[(s.h - b + j) * s.w + sx]);
                }
            for (int y = b; y < fh - b; y++)
                for (int i = 0; i < b; i++)
                {
                    int sy = b + ((y - b) % mh);
                    Put(ox + i, oy + y, s.px[sy * s.w + i]); Put(ox + fw - b + i, oy + y, s.px[sy * s.w + s.w - b + i]);
                }
            for (int j = 0; j < b; j++)
                for (int i = 0; i < b; i++)
                {
                    Put(ox + i, oy + j, s.px[j * s.w + i]); Put(ox + fw - b + i, oy + j, s.px[j * s.w + s.w - b + i]);
                    Put(ox + i, oy + fh - b + j, s.px[(s.h - b + j) * s.w + i]); Put(ox + fw - b + i, oy + fh - b + j, s.px[(s.h - b + j) * s.w + s.w - b + i]);
                }
        }

        /// <summary>
        /// アイコン: PixelLab で作った絵 (アトラス) にあればそれを size×size に最近傍で合わせて、 色を変えずに打つ。 無ければ地図のノードの絵 (17×17・1 色) を col で塗る。
        /// </summary>
        public void AnyIcon(string name, int x, int y, int size, Color32 col)
        {
            if (Game2DKit.Plain)          // 長方形 + 名前の頭文字
            {
                Box(x, y, size, size, Game2DKit.Off, col);
                if (!string.IsNullOrEmpty(name)) { string t = name.Substring(0, Mathf.Min(name.Length, size >= 16 ? 3 : 2)); Text(t, x + (size - Game2DKit.TextWidth(t)) / 2, y + (size - 5) / 2, col); }
                return;
            }
            int k = Game2DKit.IconIndex(name);
            if (k < 0) { Icon(name, x, y, col, 1); return; }          // 生成した絵が無い名前だけ、 地図のノードの絵を col で塗る
            var a = Game2DKit.Atlas; int cols = a.w / 48, cx = (k % cols) * 48, cy = (k / cols) * 48;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    var c = a.px[(cy + j * 48 / size) * a.w + cx + i * 48 / size]; if (c.a < 128) continue;
                    Set(x + i, y + j, new Color32(c.r, c.g, c.b, 255));          // 生成した絵の色のまま
                }
        }

        /// <summary>ゲージ: 生成した帯の絵を、 そのまま台として w×h に敷き、 frac ぶんの塗り (fill の色) を台の内側へ上から描く。 絵の色は変えない。</summary>
        public void GaugeBar(KitSrc g, int x, int y, int bw, int bh, float frac, Color32 fill)
        {
            frac = Mathf.Clamp01(frac);
            if (g == null) { Box(x, y, bw, bh, Game2DKit.Off, Game2DKit.Dark); Rect(x + 1, y + 1, Mathf.RoundToInt((bw - 2) * frac), bh - 2, fill); return; }
            int th = Mathf.Max(8, g.h * bw / g.w), oy = y - (th - bh) / 2;      // 縦横の比は絵のまま
            Sprite(g, x, oy, bw, th);
            int bandTop = oy + 7 * th / g.h + 1, bandH = Mathf.Max(2, 11 * th / g.h - 2);      // 絵の中の帯の位置 (絵の 7〜18 行目)
            Rect(x + 3, bandTop, Mathf.RoundToInt((bw - 6) * frac), bandH, fill);
        }

        /// <summary>ノードのアイコン (17×17・'1' の所を col で塗る)。 scale 倍 (整数)。</summary>
        public void Icon(string name, int x, int y, Color32 col, int scale = 1)
        {
            var rows = Game2DMapView.IconRows(name); if (rows == null) return;
            for (int j = 0; j < rows.Length; j++) for (int i = 0; i < rows[j].Length; i++) if (rows[j][i] == '1') Rect(x + i * scale, y + j * scale, scale, scale, col);
        }

        /// <summary>小さい絵 (文字列の行) を col で塗る。</summary>
        public void Glyph(string[] rows, int x, int y, Color32 col, int scale = 1)
        {
            for (int j = 0; j < rows.Length; j++) for (int i = 0; i < rows[j].Length; i++) if (rows[j][i] == '1') Rect(x + i * scale, y + j * scale, scale, scale, col);
        }

        public Texture2D Apply(Texture2D t) { t.SetPixels32(px); t.Apply(false); return t; }
    }
}
