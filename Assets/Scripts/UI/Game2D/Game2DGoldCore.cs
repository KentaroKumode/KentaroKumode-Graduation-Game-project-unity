using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// GOLD の入れ物の金貨の粒の計算 (map-prototype.html の <c>GoldBurstCore</c> を 1:1 で移したもの)。 画像は上の行が先頭の RGBA (<see cref="Img"/>)。
    /// 画像ごとの値 (コインの大きさ・位置) は書かない: コインの大きさは層 3 の画像から推定し、 絵と色は基本スプライト (coin_gui.png) から取り、
    /// 位置は領域から間引いて決める。 乱数は種つきの xorshift32 (HTML と同じ式で同じ結果)。
    /// </summary>
    public static class Game2DGoldCore
    {
        /// <summary>上の行が先頭の RGBA 画像。</summary>
        public sealed class Img
        {
            public int w, h; public byte[] d;       // d = w*h*4 (r,g,b,a)
            public static Img From(Texture2D t)
            {
                var px = t.GetPixels32(); var o = new Img { w = t.width, h = t.height, d = new byte[t.width * t.height * 4] };
                for (int y = 0; y < o.h; y++)
                    for (int x = 0; x < o.w; x++)
                    {
                        var c = px[(o.h - 1 - y) * o.w + x]; int i = (y * o.w + x) * 4;
                        o.d[i] = c.r; o.d[i + 1] = c.g; o.d[i + 2] = c.b; o.d[i + 3] = c.a;
                    }
                return o;
            }
            public float Luma(int i) => 0.299f * d[i * 4] + 0.587f * d[i * 4 + 1] + 0.114f * d[i * 4 + 2];
            public bool Opaque(int i) => d[i * 4 + 3] > 0;
        }

        public sealed class Rng
        {
            private uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }
            public float Next() { uint x = _s; x ^= x << 13; x ^= x >> 17; x ^= x << 5; _s = x; return x / 4294967296f; }
        }

        public struct Coin { public float radius, threshold; }

        private static int Otsu(List<float> values)
        {
            double best = 0; int bt = 128;
            for (int t = 1; t < 255; t++)
            {
                int n0 = 0, n1 = 0; double s0 = 0, s1 = 0;
                foreach (var v in values) { if (v < t) { n0++; s0 += v; } else { n1++; s1 += v; } }
                if (n0 < 5 || n1 < 5) continue;
                double d = s0 / n0 - s1 / n1, sc = (double)n0 * n1 * d * d;
                if (sc > best) { best = sc; bt = t; }
            }
            return bt;
        }

        /// <summary>コイン 1 枚の大きさ: 画像の「明るい面」の連結成分のうち、 大きい方の半分の面積の中央値から半径を出す (輪郭で割れた小片は数えない)。</summary>
        public static Coin EstimateCoin(Img im)
        {
            int w = im.w, h = im.h; var vals = new List<float>();
            for (int i = 0; i < w * h; i++) if (im.Opaque(i)) vals.Add(im.Luma(i));
            int t = Otsu(vals);
            var face = new bool[w * h]; var seen = new bool[w * h]; var areas = new List<int>();
            for (int i = 0; i < w * h; i++) face[i] = im.Opaque(i) && im.Luma(i) >= t;
            var st = new Stack<int>();
            for (int i = 0; i < w * h; i++)
            {
                if (!face[i] || seen[i]) continue;
                st.Push(i); seen[i] = true; int n = 0;
                while (st.Count > 0)
                {
                    int c = st.Pop(); n++; int x = c % w, y = c / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx; if (face[j] && !seen[j]) { seen[j] = true; st.Push(j); }
                    }
                }
                areas.Add(n);
            }
            areas.Sort((a, b) => b.CompareTo(a));
            int take = Mathf.Max(1, areas.Count >> 1);
            return new Coin { radius = Mathf.Sqrt(areas[take >> 1] / Mathf.PI), threshold = t };
        }

        /// <summary>領域 (mask) から、 コインの直径に近い間隔で点を間引く (ランダムな順に見て、 近すぎる点は捨てる)。</summary>
        public static List<Vector2Int> PickPoints(bool[] mask, int w, int h, float minDist, Rng rng)
        {
            var idx = new List<int>();
            for (int i = 0; i < w * h; i++) if (mask[i]) idx.Add(i);
            for (int i = idx.Count - 1; i > 0; i--) { int j = (int)(rng.Next() * (i + 1)); int tmp = idx[i]; idx[i] = idx[j]; idx[j] = tmp; }
            var pts = new List<Vector2Int>(); float d2 = minDist * minDist;
            foreach (int i in idx)
            {
                int x = i % w, y = i / w; bool ok = true;
                foreach (var p in pts) { float dx = p.x - x, dy = p.y - y; if (dx * dx + dy * dy < d2) { ok = false; break; } }
                if (ok) pts.Add(new Vector2Int(x, y));
            }
            return pts;
        }

        public static Vector3 SpriteMean(Img sp)
        {
            int n = 0; float r = 0, g = 0, b = 0;
            for (int i = 0; i < sp.w * sp.h; i++) if (sp.Opaque(i)) { n++; r += sp.d[i * 4]; g += sp.d[i * 4 + 1]; b += sp.d[i * 4 + 2]; }
            n = Mathf.Max(1, n); return new Vector3(r / n, g / n, b / n);
        }

        /// <summary>側面の基本色: スプライトの不透明な画素のうち、 輝度が平均以上で最も彩度が高い色 (金色そのもの)。</summary>
        public static Vector3 SpriteGold(Img sp)
        {
            float ml = 0; int n = 0;
            for (int i = 0; i < sp.w * sp.h; i++) if (sp.Opaque(i)) { ml += sp.Luma(i); n++; }
            ml /= Mathf.Max(1, n);
            Vector3? best = null; float bs = -1;
            for (int i = 0; i < sp.w * sp.h; i++)
            {
                if (!sp.Opaque(i) || sp.Luma(i) < ml) continue;
                float r = sp.d[i * 4], g = sp.d[i * 4 + 1], b = sp.d[i * 4 + 2], mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
                float s = (mx - mn) / Mathf.Max(1f, mx);
                if (s > bs) { bs = s; best = new Vector3(r, g, b); }
            }
            return best ?? new Vector3(200, 160, 60);
        }

        private static float JsRound(float v) => Mathf.Floor(v + 0.5f);
        private static float Cc(float v) => Mathf.Max(0, Mathf.Min(255, JsRound(v)));

        public struct CoinPx { public int dx, dy; public Color32 col; }

        /// <summary>コイン 1 枚の、 今この瞬間の絵: 基本スプライトを scale 倍して、 向き theta・開き wd (1 = 正面 / 0 = 真横) で回している楕円に写す。
        /// back = 裏面 (左右反転・少し暗く)。 thick = コインの厚さ [画素] (傾くと側面が見える)。 sat = 彩度の倍率。 contrast = 色の中心 (mean) からの離れ具合。
        /// 戻り値は後ろに書いたものが手前。</summary>
        public static List<CoinPx> CoinFrame(Img sp, Vector3 mean, Vector3 gold, float scale, float wd, float theta, bool back, float contrast, float thick, float lean, float sat)
        {
            int sw = sp.w, sh = sp.h;
            int ri = Mathf.CeilToInt(Mathf.Max(sw, sh) * scale / 2f + thick) + 1;
            float ct = Mathf.Cos(-theta), st = Mathf.Sin(-theta), ux = Mathf.Cos(theta), uy = Mathf.Sin(theta), cx = (sw - 1) / 2f, cy = (sh - 1) / 2f;
            Vector3 Adj0(Vector3 c, float k) => new Vector3(Cc(mean.x + (c.x - mean.x) * k), Cc(mean.y + (c.y - mean.y) * k), Cc(mean.z + (c.z - mean.z) * k));
            Vector3 Adj(Vector3 c, float k)
            {
                var a = Adj0(c, k); float l = 0.299f * a.x + 0.587f * a.y + 0.114f * a.z;
                return new Vector3(Cc(l + (a.x - l) * sat), Cc(l + (a.y - l) * sat), Cc(l + (a.z - l) * sat));
            }
            var face = new List<(int dx, int dy, Vector3 col)>();
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float u = dx * ct - dy * st, v = dx * st + dy * ct;
                    int sx = (int)JsRound(cx + (back ? -1 : 1) * u / (wd * scale)), sy = (int)JsRound(cy + v / scale);
                    if (sx < 0 || sy < 0 || sx >= sw || sy >= sh) continue;
                    int i = sy * sw + sx; if (sp.d[i * 4 + 3] == 0) continue;
                    var col = new Vector3(sp.d[i * 4], sp.d[i * 4 + 1], sp.d[i * 4 + 2]);
                    if (back) col *= 0.85f;
                    face.Add((dx, dy, Adj(col, contrast)));
                }
            var res = new List<CoinPx>();
            float o = thick / 2f * lean, span = 2f * Mathf.Abs(o); int K = Mathf.CeilToInt(span); float sgn = o >= 0 ? 1 : -1;
            void Put((int dx, int dy, Vector3 col) f, float off, Vector3 col)
                => res.Add(new CoinPx { dx = (int)JsRound(f.dx + ux * off), dy = (int)JsRound(f.dy + uy * off), col = new Color32((byte)col.x, (byte)col.y, (byte)col.z, 255) });
            for (int k = K; k >= 1; k--)                       // 側面 (奥から手前へ)
            {
                float off = o - sgn * Mathf.Min(span, k), shade = k == K ? 0.5f : 0.62f; var rim = Adj(gold * shade, contrast);
                foreach (var f in face) Put(f, off, rim);
            }
            foreach (var f in face) Put(f, o, f.col);          // 見えている面
            return res;
        }
    }
}
