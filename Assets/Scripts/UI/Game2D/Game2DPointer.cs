using UnityEngine;

namespace UI.Game2D
{
    /// <summary>マウスの位置 → 480×270 の格子の dot (左上が原点・y 下向き)。 <b>押す所は全部ここを通る</b> (2026-11-16)。</summary>
    public static class Game2DPointer
    {
        /// <summary>マウスの位置 → GUI の部品 (計器盤・帯・窓・手引き) の dot。 GUI を液晶の中身として合成している間 (<see cref="Game2DGuiRt.InLcd"/>・既定) は、
        /// GUI も液晶と同じに湾曲して見えるので、 湾曲を戻した dot (<see cref="LcdDot(Transform, Vector2)"/>) を返す。 旧い提示の板の時は画面の位置そのまま。
        /// 絵の外 (四隅の楔) を指している時は false。</summary>
        public static bool TryDot(Game2DScreen screen, out Vector2 dot)
        {
            if (!TryScreenDot(screen, out dot)) return false;
            if (Game2DGuiRt.InLcd) dot = LcdDot(screen.lcdQuad, dot);
            return InGrid(dot);
        }

        /// <summary>マウスの位置 → 液晶の中身 (地図・戦闘・イベントの画面) の dot。 いつも湾曲を戻す (GUI の合成の形に関係なく、 中身は湾曲している)。 絵の外なら false。</summary>
        public static bool TryLcdDot(Game2DScreen screen, out Vector2 dot)
        {
            if (!TryScreenDot(screen, out dot)) return false;
            dot = LcdDot(screen.lcdQuad, dot);
            return InGrid(dot);
        }

        /// <summary>マウスの位置 → 画面の上の格子の dot (湾曲を戻さない)。</summary>
        private static bool TryScreenDot(Game2DScreen screen, out Vector2 dot)
        {
            dot = default;
            if (screen == null) return false;
            var mp = Input.mousePosition;
            if (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height) return false;
            float ppd = screen.PixelsPerGuiDot;
            dot = new Vector2((mp.x - Screen.width / 2f) / ppd + Game2DLayout.Width / 2f, Game2DLayout.Height / 2f - (mp.y - Screen.height / 2f) / ppd);
            return InGrid(dot);
        }

        private static bool InGrid(Vector2 d) => d.x >= 0 && d.y >= 0 && d.x <= Game2DLayout.Width && d.y <= Game2DLayout.Height;

        /// <summary>GUI の格子の dot → 液晶の絵 (480×270) の dot。 液晶は樽型に湾曲している (Game2D/LcdScreen の _Curve・_CurveFit) ので、
        /// 見えている点が絵のどこを映しているかをシェーダと同じ式で求める。 <b>式の置き場はここ 1 か所</b> (2026-11-16 にまとめた:
        /// 地図 Game2DMapView.ScreenToMapDot・戦闘 Game2DBattleLcd.LcdDot・イベントの画面 Game2DEventScreen がここを呼ぶ)。
        /// lcdQuad = 液晶の Quad (<see cref="Game2DScreen.lcdQuad"/>・湾曲の値をその材質から読む)。 絵の外 (f &gt; 0 の時の四隅) を指すと 0〜480 / 0〜270 の外の値が返る。</summary>
        public static Vector2 LcdDot(Transform lcdQuad, Vector2 d)
        {
            float k = 0f, fit = 0f;
            if (lcdQuad != null)
            {
                var mr = lcdQuad.GetComponent<MeshRenderer>(); var m = mr != null ? mr.sharedMaterial : null;
                if (m != null && m.HasProperty("_Curve")) k = m.GetFloat("_Curve");
                if (m != null && m.HasProperty("_CurveFit")) fit = m.GetFloat("_CurveFit");
            }
            return LcdDot(d, k, fit);
        }

        /// <summary>逆向き: 液晶の絵 (480×270) の dot → GUI の格子の dot (その絵の点が画面のどこに見えるか)。 GUI の層 (湾曲しない) で、 液晶の中の部品を囲む・指す時に使う
        /// (手引きの強調の枠など)。 式は <see cref="LcdDot(Vector2, float, float)"/> の逆を不動点の繰り返しで解く (k ≤ 0.2 なら 12 回で 0.01 dot 以下に収まる)。</summary>
        public static Vector2 ScreenDotOfLcd(Transform lcdQuad, Vector2 p)
        {
            float k = 0f, fit = 0f;
            if (lcdQuad != null)
            {
                var mr = lcdQuad.GetComponent<MeshRenderer>(); var m = mr != null ? mr.sharedMaterial : null;
                if (m != null && m.HasProperty("_Curve")) k = m.GetFloat("_Curve");
                if (m != null && m.HasProperty("_CurveFit")) fit = m.GetFloat("_CurveFit");
            }
            if (k <= 0f) return p;
            const float W = Game2DLayout.Width, H = Game2DLayout.Height;
            float sx = p.x / W * 2f - 1f, sy = (1f - p.y / H) * 2f - 1f, D = 1f + k * (2f - Mathf.Clamp01(fit));
            float cx = sx, cy = sy;
            for (int i = 0; i < 12; i++) { float r2 = cx * cx + cy * cy; cx = sx * D / (1f + k * r2); cy = sy * D / (1f + k * r2); }
            return new Vector2((cx * 0.5f + 0.5f) * W, (1f - (cy * 0.5f + 0.5f)) * H);
        }

        /// <summary>湾曲の式 (シェーダ Game2D/LcdScreen の frag の頭と同じ): 元 = 中心 × (1 + k·r²) / (1 + k·(2 − fit))。 c・r² は縦横とも −1〜1 に正規化した座標。
        /// fit = 0: 四隅を絵の端に合わせる (見える範囲は長方形・既定) / 1: 縁の中央を絵の端に合わせる (絵の外形が樽型・試作)。</summary>
        public static Vector2 LcdDot(Vector2 d, float k, float fit)
        {
            if (k <= 0f) return d;
            const float W = Game2DLayout.Width, H = Game2DLayout.Height;
            float cx = d.x / W * 2f - 1f, cy = (1f - d.y / H) * 2f - 1f;
            float f = (1f + k * (cx * cx + cy * cy)) / (1f + k * (2f - Mathf.Clamp01(fit)));
            return new Vector2((cx * f * 0.5f + 0.5f) * W, (1f - (cy * f * 0.5f + 0.5f)) * H);
        }
    }
}
