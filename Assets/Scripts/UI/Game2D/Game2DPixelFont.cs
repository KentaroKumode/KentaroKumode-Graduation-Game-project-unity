using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 数字・英大文字の小さなドット字体 (GUI の格子用)。 画像素材を持ち込まず、 必要な時にテクスチャへ焼く。
    /// 数字は 3×5 (読める最小)、 数字の大きい版は 5×7。 色は呼び出し側が決める。
    /// </summary>
    public static class Game2DPixelFont
    {
        // 3×5: 15 文字 = 行ごとに 3 文字
        private static readonly string[] Small = {
            "111101101101111", // 0
            "010110010010111", // 1
            "110001010100111", // 2
            "110001010001110", // 3
            "101101111001001", // 4
            "111100110001110", // 5
            "011100111101111", // 6
            "111001010010010", // 7
            "111101111101111", // 8
            "111101111001110", // 9
        };

        /// <summary>数字を 3×5 で並べたテクスチャ (字間 1・背景は透明)。 点灯色 <paramref name="color"/>。</summary>
        public static Texture2D RenderSmall(string digits, Color color)
        {
            int n = digits.Length;
            var tex = NewTex(Mathf.Max(1, n * 4 - 1), 5);
            for (int i = 0; i < n; i++)
            {
                string g;
                if (digits[i] == '-') g = "000000111000000";           // マイナス (行き先の見込み "-10" 用)
                else { int d = digits[i] - '0'; if (d < 0 || d > 9) continue; g = Small[d]; }
                for (int k = 0; k < 15; k++)
                    if (g[k] == '1') tex.SetPixel(i * 4 + k % 3, 4 - k / 3, color);
            }
            tex.Apply(false);
            return tex;
        }

        private static Texture2D NewTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color32[w * h];
            tex.SetPixels32(clear);
            return tex;
        }
    }
}
