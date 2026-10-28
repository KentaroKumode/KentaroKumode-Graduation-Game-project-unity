using System;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>色 (0..255 の実数)。 HTML の [r, g, b] / "#rrggbb"。</summary>
    public struct Col
    {
        public float r, g, b;
        public Col(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
        public static Col Hex(string h) => new Col(Convert.ToInt32(h.Substring(1, 2), 16), Convert.ToInt32(h.Substring(3, 2), 16), Convert.ToInt32(h.Substring(5, 2), 16));
    }

    /// <summary>絵 (上の行が先頭の RGBA)。 武器・背景を 1 dot ずつ読むための配列。</summary>
    public sealed class BSprite
    {
        public int w, h; public byte[] d;
        /// <summary>左右を鏡写しにした複製 (斧の刃を、 時計回りに振る時に先へ来る側へ向ける)。</summary>
        public BSprite Mirrored()
        {
            var m = (BSprite)MemberwiseClone(); m.d = new byte[d.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) Array.Copy(d, (y * w + x) * 4, m.d, (y * w + (w - 1 - x)) * 4, 4);
            return m;
        }

        public BSprite(Texture2D t)
        {
            w = t.width; h = t.height; d = new byte[w * h * 4];
            var px = t.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[(h - 1 - y) * w + x]; int i = (y * w + x) * 4;
                    d[i] = c.r; d[i + 1] = c.g; d[i + 2] = c.b; d[i + 3] = c.a;
                }
        }
    }

    /// <summary>
    /// 戦闘演出の作図面 (480×270 の dot)。 正本は docs/design/battle-prototype.src.html の <c>putRGB / rect / line / ellipse / dglow / ditherRect / blit</c>。
    /// 絵は 1 dot ずつ打つ (補間・拡縮・アンチエイリアスを使わない)。 画面全体に掛かるブルーム・格子・暗がりは持たない (Unity の液晶のシェーダ側が掛ける)。
    /// </summary>
    public sealed class BattleCanvas
    {
        public const int W = 480, H = 270;
        public readonly uint[] FB = new uint[W * H];      // (255 &lt;&lt; 24) | (b &lt;&lt; 16) | (g &lt;&lt; 8) | r
        public static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        public int clipY0 = -1, clipY1 = -1;               // この行の範囲 [y0, y1) だけに描く (-1 = 全面)
        public float glowA = -1f;                          // dglow の濃さ (負 = 既定の 0.5)

        public static uint Pack(int r, int g, int b) => (255u << 24) | ((uint)b << 16) | ((uint)g << 8) | (uint)r;

        public void Put(int x, int y, Col c, float a = 1f, bool add = false) => PutRgb(x, y, c.r, c.g, c.b, a, add);

        public void PutRgb(int x, int y, float cr, float cg, float cb, float a, bool add)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            if (clipY0 >= 0 && (y < clipY0 || y >= clipY1)) return;
            int i = y * W + x;
            if (!add && a >= 1f) { FB[i] = Pack((int)cr, (int)cg, (int)cb); return; }
            uint d = FB[i]; float dr = d & 255, dg = (d >> 8) & 255, db = (d >> 16) & 255, r, g, b;
            if (add) { r = Mathf.Min(255, dr + cr * a); g = Mathf.Min(255, dg + cg * a); b = Mathf.Min(255, db + cb * a); }
            else { r = dr + (cr - dr) * a; g = dg + (cg - dg) * a; b = db + (cb - db) * a; }
            FB[i] = Pack((int)r, (int)g, (int)b);
        }

        public void Rect(int x, int y, int w, int h, Col c, float a = 1f, bool add = false)
        {
            if (a >= 1f && !add)
            {
                uint pk = Pack((int)c.r, (int)c.g, (int)c.b); int xa = Mathf.Max(0, x), xb = Mathf.Min(W, x + w);
                int ya = Mathf.Max(Mathf.Max(0, y), clipY0 >= 0 ? clipY0 : 0), yb = Mathf.Min(Mathf.Min(H, y + h), clipY0 >= 0 ? clipY1 : H);
                if (xb > xa) for (int j = ya; j < yb; j++) for (int i = xa; i < xb; i++) FB[j * W + i] = pk;
                return;
            }
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) PutRgb(x + i, y + j, c.r, c.g, c.b, a, add);
        }

        /// <summary>太さ w の整数の線 (ブレゼンハム)。</summary>
        public void Line(float fx0, float fy0, float fx1, float fy1, Col c, int w = 1, float a = 1f, bool add = false)
        {
            int x0 = Mathf.RoundToInt(fx0), y0 = Mathf.RoundToInt(fy0), x1 = Mathf.RoundToInt(fx1), y1 = Mathf.RoundToInt(fy1);
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy, h = w >> 1;
            for (int n = 0; n < 2000; n++)
            {
                for (int j = 0; j < w; j++) for (int i = 0; i < w; i++) Put(x0 + i - h, y0 + j - h, c, a, add);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err; if (e2 >= dy) { err += dy; x0 += sx; } if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>輪郭 (整数の dot だけ)。</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, Col c, float a = 1f, bool add = false)
        {
            if (rx < 1 || ry < 1) return;
            int n = Mathf.Max(24, Mathf.CeilToInt(10 * Mathf.Max(rx, ry))), lx = int.MaxValue, ly = int.MaxValue;
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)n * 6.2832f; int x = Mathf.RoundToInt(cx + Mathf.Cos(t) * rx), y = Mathf.RoundToInt(cy + Mathf.Sin(t) * ry);
                if (x == lx && y == ly) continue; lx = x; ly = y; Put(x, y, c, a, add);
            }
        }

        /// <summary>網点の光 (ベイヤー配列で濃さを dot の有無に変える): 滑らかなグラデーションを使わない。</summary>
        public void Dglow(float cx, float cy, float rx, float ry, Col c, float strength, bool add)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + rx)), y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry, d = Mathf.Sqrt(dx * dx + dy * dy); if (d >= 1) continue;
                    float lv = (1 - d) * (1 - d * 0.5f) * strength * 16f;
                    if (lv > Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) Put(x, y, c, glowA < 0 ? 0.5f : glowA, add);
                }
        }

        /// <summary>上下 2 色の網点の遷移。</summary>
        public void DitherRect(int x, int y, int w, int h, Col top, Col bot)
        {
            for (int j = 0; j < h; j++)
            {
                float t = (j + 0.5f) / h * 16f;
                for (int i = 0; i < w; i++) Put(x + i, y + j, t > Bayer[((y + j) & 3) * 4 + ((x + i) & 3)] ? bot : top, 1f);
            }
        }

        public struct BlitOpt
        {
            public float pvx, pvy, sc, a, yel, sy, bend; public bool hot, flip, add, hasPv;
            public static BlitOpt Default => new BlitOpt { sc = 1, a = 1, yel = -1, sy = 1 };
        }

        /// <summary>武器の絵を、 回転軸 (pivot) まわりに回して 1 dot ずつ打つ。 最近傍だけ。 yel: 黄色で塗る高さ (0..1・負 = 塗らない) / hot: 白熱。</summary>
        public void Blit(BSprite s, float px, float py, float rot, BlitOpt o)
        {
            if (s == null) return;
            float pvx = o.hasPv ? o.pvx : s.w / 2f, pvy = o.hasPv ? o.pvy : s.h / 2f;
            float sc = o.sc <= 0 ? 1 : o.sc, th = rot * Mathf.PI / 180f, cs = Mathf.Cos(th), sn = Mathf.Sin(th), a = o.a;
            float yelY = (1 - (o.yel < 0 ? 0 : o.yel)) * s.h;
            float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
            for (int q = 0; q < 4; q++)
            {
                float qx = (q & 1) * s.w, qy = (q >> 1) * s.h, vx = (qx - pvx) * sc, vy = (qy - pvy) * sc, rx = vx * cs - vy * sn, ry = vx * sn + vy * cs;
                x0 = Mathf.Min(x0, Mathf.Min(rx, -rx)); x1 = Mathf.Max(x1, Mathf.Max(rx, -rx)); y0 = Mathf.Min(y0, ry); y1 = Mathf.Max(y1, ry);
            }
            float fl = o.flip ? -1 : 1, sy_ = o.sy <= 0 ? 1 : o.sy, bend = o.bend;
            y0 *= sy_; y1 *= sy_;
            float pad = Mathf.Ceil(Mathf.Abs(bend) * sc * 0.8f) + 6; x0 -= pad; x1 += pad; y0 -= pad; y1 += pad;      // たわみ・拡縮ではみ出す分 (剣先が欠けない)
            int ox = Mathf.RoundToInt(px), oy = Mathf.RoundToInt(py);
            for (int y = Mathf.FloorToInt(y0) - 1; y <= Mathf.CeilToInt(y1) + 1; y++)
                for (int x = Mathf.FloorToInt(x0) - 1; x <= Mathf.CeilToInt(x1) + 1; x++)
                {
                    float xx = x * fl, yv = y / sy_, su = (xx * cs + yv * sn) / sc, sv = (-xx * sn + yv * cs) / sc;
                    if (bend != 0)
                    {
                        float al = (su - sv) * 0.7071f, pe = (su + sv) * 0.7071f, t = Mathf.Max(0, al) / 26f, pe2 = pe - bend * t * (0.45f + 0.55f * t);
                        su = (al + pe2) * 0.7071f; sv = (pe2 - al) * 0.7071f;      // 軸 (握り → 刃先) に沿って、 先へ行くほど 2 乗で横へたわませる
                    }
                    int sx = Mathf.FloorToInt(su + pvx), sy = Mathf.FloorToInt(sv + pvy);
                    if (sx < 0 || sy < 0 || sx >= s.w || sy >= s.h) continue;
                    int i = (sy * s.w + sx) * 4; if (s.d[i + 3] < 128) continue;
                    float cr = s.d[i], cg = s.d[i + 1], cb = s.d[i + 2];
                    if (o.yel >= 0 && sy >= yelY)
                    {
                        float l = (cr + cg + cb) / 765f;
                        if (o.hot) { cr = 255; cg = 248; cb = 224; } else { cr = 255; cg = Mathf.Round(222 + 26 * l); cb = Mathf.Round(150 + 70 * l); }
                    }
                    PutRgb(ox + x, oy + y, cr, cg, cb, a, o.add);
                }
        }
    }
}
