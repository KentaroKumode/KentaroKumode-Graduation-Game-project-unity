using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 液晶の GUI のドット字。 液晶の GUI はテクスチャへ 1 ドットずつ打つので、 TextMesh Pro の SDF ではなく元のドットをそのまま打つ。
    /// どのフォントかは <see cref="GameFont"/> が決める (2026-11-04〜 x8y12pxDenkiChip・12 ドット)。 焼くのは Tools/bake_jpfont.py
    /// (出力 Resources/<see cref="GameFont.DotPath"/>.bytes)。 字の高さはファイルが持つ (<see cref="Cell"/>)。
    /// 収録の無い字は送り幅 = 字の高さの半分の空白になる。
    /// </summary>
    public static class Game2DJpFont
    {
        /// <summary>字の高さ (ドット)。 焼いたファイルから読む (読めない時は <see cref="GameFont.DotPx"/>)。</summary>
        public static int Cell { get { var _ = Map; return _cell; } }
        /// <summary>行送り (字の高さ + 行間 1)。</summary>
        public static int LineH => Cell + 1;

        private struct Glyph { public byte adv; public ushort[] rows; }
        private static Dictionary<int, Glyph> _map;
        private static int _cell = GameFont.DotPx;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _map = null; _cell = GameFont.DotPx; }

        private static Dictionary<int, Glyph> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<int, Glyph>();
                var ta = Resources.Load<TextAsset>(GameFont.DotPath);
                if (ta == null) { Debug.LogError("[Game2DJpFont] Resources/" + GameFont.DotPath + ".bytes が無い (Tools/bake_jpfont.py で焼く)"); return _map; }
                var b = ta.bytes;
                if (b.Length < 9 || b[0] != 'M' || b[1] != 'J' || b[2] != 'F' || b[3] != '2') { Debug.LogError("[Game2DJpFont] 形式が違う (Tools/bake_jpfont.py で焼き直す)"); return _map; }
                _cell = b[4];
                int n = System.BitConverter.ToInt32(b, 5), o = 9, rec = 5 + 2 * _cell;
                for (int i = 0; i < n && o + rec <= b.Length; i++)
                {
                    int cp = (int)System.BitConverter.ToUInt32(b, o); byte adv = b[o + 4]; o += 5;
                    var rows = new ushort[_cell];
                    for (int r = 0; r < _cell; r++) { rows[r] = System.BitConverter.ToUInt16(b, o); o += 2; }
                    _map[cp] = new Glyph { adv = adv, rows = rows };
                }
                return _map;
            }
        }

        public static int Advance(char ch) => Map.TryGetValue(ch, out var g) ? g.adv : _cell / 2;

        public static int Width(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0; foreach (var ch in s) w += Advance(ch);
            return w;
        }

        /// <summary>1 行を描く (改行しない)。 戻り値 = 描いた幅。</summary>
        public static int Draw(KitPix p, string s, int x, int y, Color32 c) => Draw((px, py) => p.Set(px, py, c), s, x, y);

        /// <summary>1 行を描く。 点は put(x, y) で打つ (KitPix 以外の作図面 ── 戦闘の演出・地図 ── から使う・2026-11-11)。 戻り値 = 描いた幅。</summary>
        public static int Draw(System.Action<int, int> put, string s, int x, int y)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int cx = x;
            foreach (var ch in s)
            {
                if (Map.TryGetValue(ch, out var g))
                {
                    for (int r = 0; r < g.rows.Length; r++)
                    {
                        int bits = g.rows[r]; if (bits == 0) continue;
                        for (int i = 0; i < 16; i++) if ((bits >> (15 - i) & 1) != 0) put(cx + i, y + r);
                    }
                    cx += g.adv;
                }
                else cx += _cell / 2;
            }
            return cx - x;
        }

        /// <summary>大きい字の幅: 元の 1 ドットを k×k の点で打ち、 字の間に track ドットを空ける (<see cref="DrawLarge"/>)。</summary>
        public static int WidthLarge(string s, int k, int track)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return Width(s) * k + track * (s.Length - 1);
        }

        /// <summary>大きい字を描く (層の名前の帯・issue #111)。 同じドット字の 1 ドットを k×k の点としてテクスチャへ打つ
        /// ── Transform の拡大 (localScale) ではなく、 打つ点そのものが大きい別の大きさの字。 字の間に track ドットを空ける。 戻り値 = 描いた幅。</summary>
        public static int DrawLarge(KitPix p, string s, int x, int y, int k, int track, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int cx = x;
            for (int n = 0; n < s.Length; n++)
            {
                int ox = cx;
                Draw((px, py) => { for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) p.Set(ox + (px - ox) * k + i, y + (py - y) * k + j, c); }, s[n].ToString(), ox, y);
                cx += Advance(s[n]) * k + (n < s.Length - 1 ? track : 0);
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
