using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 日本語のドット字 (mihiPixelmoji を 16 ドットで焼いたもの・2026-10-31)。 液晶の GUI はテクスチャへ 1 ドットずつ打つので、
    /// TextMesh Pro の SDF ではなく元のドットをそのまま打つ。 焼くのは Tools/bake_jpfont.py (出力 Resources/Fonts/mihi16.bytes)。
    /// 1 行 = 16 ドット (基線は上から 14)。 収録の無い字は送り幅 8 の空白になる。
    /// </summary>
    public static class Game2DJpFont
    {
        public const int Cell = 16;
        /// <summary>行送り (字の高さ 16 + 行間 1)。</summary>
        public const int LineH = 17;

        private struct Glyph { public byte adv; public ushort[] rows; }
        private static Dictionary<int, Glyph> _map;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _map = null; }

        private static Dictionary<int, Glyph> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<int, Glyph>();
                var ta = Resources.Load<TextAsset>("Fonts/mihi16");
                if (ta == null) { Debug.LogError("[Game2DJpFont] Resources/Fonts/mihi16.bytes が無い (Tools/bake_jpfont.py で焼く)"); return _map; }
                var b = ta.bytes;
                if (b.Length < 8 || b[0] != 'M' || b[1] != 'J' || b[2] != 'F' || b[3] != '1') { Debug.LogError("[Game2DJpFont] 形式が違う"); return _map; }
                int n = System.BitConverter.ToInt32(b, 4), o = 8;
                for (int i = 0; i < n && o + 37 <= b.Length; i++)
                {
                    int cp = (int)System.BitConverter.ToUInt32(b, o); byte adv = b[o + 4]; o += 5;
                    var rows = new ushort[Cell];
                    for (int r = 0; r < Cell; r++) { rows[r] = System.BitConverter.ToUInt16(b, o); o += 2; }
                    _map[cp] = new Glyph { adv = adv, rows = rows };
                }
                return _map;
            }
        }

        public static int Advance(char ch) => Map.TryGetValue(ch, out var g) ? g.adv : 8;

        public static int Width(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0; foreach (var ch in s) w += Advance(ch);
            return w;
        }

        /// <summary>1 行を描く (改行しない)。 戻り値 = 描いた幅。</summary>
        public static int Draw(KitPix p, string s, int x, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int cx = x;
            foreach (var ch in s)
            {
                if (Map.TryGetValue(ch, out var g))
                {
                    for (int r = 0; r < Cell; r++)
                    {
                        int bits = g.rows[r]; if (bits == 0) continue;
                        for (int i = 0; i < Cell; i++) if ((bits >> (15 - i) & 1) != 0) p.Set(cx + i, y + r, c);
                    }
                    cx += g.adv;
                }
                else cx += 8;
            }
            return cx - x;
        }

        /// <summary>幅 maxW に収まるよう 1 字ずつ折り返して描く (改行文字でも改行)。 maxLines を超えた分は描かない。 戻り値 = 行数。</summary>
        public static int Wrap(KitPix p, string s, int x, int y, int maxW, Color32 c, int maxLines = 99)
        {
            var lines = Lines(s, maxW);
            int n = Mathf.Min(lines.Count, maxLines);
            for (int i = 0; i < n; i++) Draw(p, lines[i], x, y + i * LineH, c);
            return n;
        }

        /// <summary>折り返した行の一覧 (行頭の句読点は前の行へ寄せない簡易版)。</summary>
        public static List<string> Lines(string s, int maxW)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            var cur = new System.Text.StringBuilder(); int w = 0;
            foreach (var ch in s)
            {
                if (ch == '\n') { lines.Add(cur.ToString()); cur.Clear(); w = 0; continue; }
                int a = Advance(ch);
                if (w + a > maxW && cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); w = 0; if (ch == ' ' || ch == '　') continue; }
                cur.Append(ch); w += a;
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            return lines;
        }

        /// <summary>幅に収まらなければ末尾を「…」にして詰める。</summary>
        public static string Clip(string s, int maxW)
        {
            if (string.IsNullOrEmpty(s) || Width(s) <= maxW) return s;
            int ell = Advance('…'), w = 0; var sb = new System.Text.StringBuilder();
            foreach (var ch in s) { int a = Advance(ch); if (w + a + ell > maxW) break; sb.Append(ch); w += a; }
            return sb.Append('…').ToString();
        }
    }
}
