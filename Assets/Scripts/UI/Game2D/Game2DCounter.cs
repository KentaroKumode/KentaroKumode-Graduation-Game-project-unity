using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// アナログカウンター (3 桁・2026-10-23)。 正本は docs/design/gui-counter.html。
    /// 0 が一番下・9 が一番上の連番シート (<c>counter_strip.png</c>・8×90) を、 桁ごとの回転ドラムとして上下に動かす。
    /// 値は連続で、 桁上がりの瞬間だけ上の桁も一緒に回る。 遠いほど速く回る。 窓は字の高さ (8 行) ぶんだけ (HTML の上下 3 行の「隣の字の端」は見えない)。 光の当たり方 (HTML の <c>LIGHT</c>・<c>drawLight</c>) は、 窓の中央 8 行ぶんを移した。
    /// </summary>
    public class Game2DCounter
    {
        public const int Digits = 3, CellW = 8, GlyphH = 8, StripH = 90, Gap = 1, TotalW = Digits * CellW + (Digits - 1) * Gap;   // 桁の間は 1 画素あけ (枠の絵の 3 つの窓に合わせる)
        // 元シート上の、 各数字 (0〜9) の字の上端の行 (10 は 9 の上に続く 0)
        private static readonly int[] GlyphTop = { 81, 72, 63, 54, 45, 36, 27, 18, 9, 0, 81 - StripH };
        private const float Speed = 8f;

        // ── 光の当たり方 (gui-counter.html の LIGHT / lightMaps / drawLight) ──
        // 窓の中心を光源にした楕円の場 f を、 Bayer の揺らぎで 3 段 (中央 / 中間 / 外) に刻む。 HTML の窓は 14 行 (字 8 行 + 上下 3 行) で、 字の 8 行はその中央 (MARGIN = 3 行目から)。
        private const int Margin = 3, WinH = GlyphH + Margin * 2;
        private const float CY = 6.5f, RX = 4.6f, RY = 6.0f, Jitter = 0.10f, XW = 0.22f, HiAt = 0.60f, MidAt = 0.28f, DimAt = 0.20f, DimHalfAt = 0.36f, EdgeDark = 0.5f;
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        private static readonly Color32 DimTint = new Color32(100, 116, 108, 255);   // 隣の字の色 = 元の色 × この比
        private static readonly int[] LitAdd = { (64 - 29) * 2, (67 - 29) * 2, (58 - 32) * 2 };           // 地の中央の明るさ = 地色 + この足し分
        private static readonly float[] LitMap = new float[CellW * GlyphH];             // 地を明るくする強さ (0 / 0.5 / 1)
        private static readonly float[] DimMap = new float[CellW * GlyphH];
        private static readonly float[] GlyphK = new float[CellW * GlyphH];            // 字の明るさ (光の場に連続でそろえる。 中央 1.0 → 上下の縁ほど暗く)
        private const float GlyphMin = 0.42f;             // 字を暗く・端を黒へ寄せる強さ (0 / 0.5 / 1)

        static Game2DCounter()
        {
            for (int y = 0; y < GlyphH; y++)
                for (int x = 0; x < CellW; x++)
                {
                    int yy = y + Margin;
                    float dx = (x - (CellW - 1) / 2f) / RX, dy = (yy - CY) / RY;
                    float f = 1f - Mathf.Sqrt(dx * dx * XW + dy * dy);
                    float v = f + (Bayer[(yy & 3) * 4 + (x & 3)] / 15f - 0.5f) * Jitter;
                    LitMap[y * CellW + x] = v > HiAt ? 1f : v > MidAt ? 0.5f : 0f;
                    DimMap[y * CellW + x] = v < DimAt ? 1f : v < DimHalfAt ? 0.5f : 0f;
                    GlyphK[y * CellW + x] = Mathf.Lerp(GlyphMin, 1f, Mathf.Clamp01(f / HiAt));      // 字は揺らぎ (Bayer) を入れない: 入れると 1 行の中で明暗が交互にならんでノイズに見える
                }
        }

        private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        /// <summary>1 画素に光を当てる (順は HTML と同じ: 隣の字の色へ寄せる → 端を黒へ → 地を明るくする)。</summary>
        private Color32 Lit(Color32 c, int x, int y)
        {
            float lv = LitMap[y * CellW + x], dm = DimMap[y * CellW + x];
            bool isBg = c.r == _bg.r && c.g == _bg.g && c.b == _bg.b;
            float r = c.r, g = c.g, b = c.b;
            if (dm > 0 && isBg)
            {
                // 字は 元の色 × DimTint、 地は地色のまま。 それを dm の濃さで重ね、 さらに端を黒へ (dm × EdgeDark)
                float tr = isBg ? _bg.r : c.r * DimTint.r / 255f, tg = isBg ? _bg.g : c.g * DimTint.g / 255f, tb = isBg ? _bg.b : c.b * DimTint.b / 255f;
                r = tr * dm + r * (1 - dm); g = tg * dm + g * (1 - dm); b = tb * dm + b * (1 - dm);
                float k = 1f - EdgeDark * dm; r *= k; g *= k; b *= k;
            }
            if (!isBg) { float gk = GlyphK[y * CellW + x]; r *= gk; g *= gk; b *= gk; }       // 字も地と同じ光の場で、 縁へ行くほど暗く
            if (isBg && lv > 0) { r += LitAdd[0] * lv; g += LitAdd[1] * lv; b += LitAdd[2] * lv; }
            return new Color32(B(r), B(g), B(b), 255);
        }

        private readonly Color32[] _strip;      // 上の行が先頭 (0..89)
        private readonly Color32 _bg;
        private readonly Texture2D _tex;
        private readonly Color32[] _px = new Color32[TotalW * GlyphH];      // 桁の間の列は透明のまま (枠の絵が見える)
        private float _v, _target; private int _drawn = int.MinValue;

        private int _lastWhole = -1;
        public Texture2D Texture => _tex;
        public int Width => TotalW;

        public Game2DCounter(Texture2D strip, Color32 bg)
        {
            var src = strip.GetPixels32();                               // 下の行が先頭
            _strip = new Color32[CellW * StripH];
            for (int y = 0; y < StripH; y++) System.Array.Copy(src, (StripH - 1 - y) * CellW, _strip, y * CellW, CellW);
            _bg = bg;
            _tex = new Texture2D(TotalW, GlyphH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DCounter" };
        }

        public void Destroy() { if (_tex != null) Object.Destroy(_tex); }

        /// <summary>目標の値を渡す。 表示は目標へ向かって回る (最初の 1 回は止めずに合わせる)。</summary>
        public void Tick(int value, float dt, bool snap = false)
        {
            float max = Mathf.Pow(10, Digits);
            _target = Mathf.Clamp(value, 0, (int)max - 1);
            if (snap) _v = _target;
            float d = _target - _v;
            float rate = Mathf.Max(Speed, Mathf.Abs(d) * 6f);
            _v += Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), rate * dt);
            Redraw();
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>桁 i (0 = 1 の位) の位置 0〜10。 下の桁が 9 → 0 に回る間だけ、 上の桁も一緒に回る。</summary>
        private static float DrumPos(float val, int i)
        {
            float t = val / Mathf.Pow(10, i), fl = Mathf.Floor(t), fr = t - fl;
            float w = 0.35f / Mathf.Pow(10, i);
            return ((int)fl % 10) + Smooth((fr - (1f - w)) / w);
        }

        private void Redraw()
        {
            int key = Mathf.RoundToInt(_v * 1000f);
            if (key == _drawn) return;
            _drawn = key;
            int whole = Mathf.FloorToInt(_v); if (whole != _lastWhole) { if (_lastWhole >= 0) Game2DSfx.Play("count_tick", Random.Range(0.6f, 0.85f), Random.Range(0.93f, 1.08f)); _lastWhole = whole; }
            for (int k = 0; k < Digits; k++)
            {
                int i = Digits - 1 - k;                                   // 左の桁ほど上の位
                float p = DrumPos(_v, i);
                int fl = Mathf.Min(9, Mathf.FloorToInt(p)); float fr = p - fl;
                int top = Mathf.RoundToInt(GlyphTop[fl] + (GlyphTop[fl + 1] - GlyphTop[fl]) * fr);
                for (int y = 0; y < GlyphH; y++)
                {
                    int row = (((top + y) % StripH) + StripH) % StripH;
                    for (int x = 0; x < CellW; x++)
                    {
                        var c = _strip[row * CellW + x];
                        if (c.a < 255) { float a = c.a / 255f; c = new Color32((byte)(c.r * a + _bg.r * (1 - a)), (byte)(c.g * a + _bg.g * (1 - a)), (byte)(c.b * a + _bg.b * (1 - a)), 255); }
                        _px[(GlyphH - 1 - y) * TotalW + k * (CellW + Gap) + x] = Lit(c, x, y);      // テクスチャは下の行が先頭
                    }
                }
            }
            _tex.SetPixels32(_px); _tex.Apply(false);
        }
    }
}
