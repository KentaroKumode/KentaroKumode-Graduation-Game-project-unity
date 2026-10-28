using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// HP・物資のバー (2026-10-12 設計・docs/design/gui-bar-unit.html / map-prototype.html の <c>BARS</c>)。
    ///
    /// <para>素材は左右のパーツ (<c>hp_bar_*.png</c> / <c>supply_bar_*.png</c>・256×40) を行ごとに重ねた全長 256 の板。
    /// <b>左 44 列 ＋ タイル 40 列の繰り返し ＋ 右 30 列</b>の 3 分割で長さを変える (拡大しない)。
    /// 板の窓 (絵の透明な範囲) に、 5 画素の目盛りを 6 画素ごとに並べて割合ぶん点灯させる。
    /// 左のアイコン (緑のマーカー) は点灯色で染める。</para>
    ///
    /// <para>テクスチャは割合・長さが変わった時だけ作り直す。</para>
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class Game2DBar : MonoBehaviour
    {
        public const int LeftW = 44, TileX = 100, TileW = 40, RightW = 30, Full = 256, BarH = 19;
        private const int Seg = 5, Step = 6;

        [Tooltip("左のパーツ (isReadable)")] public Texture2D leftTex;
        [Tooltip("右のパーツ (isReadable)")] public Texture2D rightTex;
        [Tooltip("素材の中でこのバーが始まる行 (HP = 0 / 物資 = 21。 画像の上から)")] public int rowY;
        public Color lit = new Color32(240, 92, 52, 255);
        public Color off = new Color32(58, 50, 46, 255);

        /// <summary>全長 (画素)。 <see cref="LeftW"/> + <see cref="RightW"/> 以上、 <see cref="Full"/> 以下。</summary>
        [Range(LeftW + RightW, Full)] public int length = Full;
        [Range(0f, 1f)] public float value = 1f;

        private RawImage _img;
        private Texture2D _out;
        private Color32[] _frame;          // 合成した全長 256×19 (上が先頭行)
        private int _wx0 = -1, _wx1, _wy0, _wy1;   // 窓
        private int _drawnLen = -1, _drawnLit = -1;
        private float _drawnValue = -1f;

        private void Awake() { _img = GetComponent<RawImage>(); }

        private void OnEnable() { _drawnLen = -1; Refresh(); }

        private void OnDestroy() { if (_out != null) Destroy(_out); }

        /// <summary>割合と長さを入れる。 変わっていなければ何もしない。</summary>
        public void Set(float v, int len)
        {
            value = Mathf.Clamp01(v);
            length = Mathf.Clamp(len, LeftW + RightW, Full);
            Refresh();
        }

        public void Refresh()
        {
            if (_img == null) _img = GetComponent<RawImage>();
            if (leftTex == null || rightTex == null) return;
            if (_frame == null) BuildFrame();
            if (_frame == null) return;

            int n = SegmentCount(length);
            int litCount = Mathf.RoundToInt(value * n);
            if (length == _drawnLen && litCount == _drawnLit && _out != null) return;
            _drawnLen = length; _drawnLit = litCount;
            Draw(length, n, litCount);
        }

        private int SegmentCount(int len)
        {
            int iw = len - (Full - (_wx1 - _wx0 + 1));
            return Mathf.Max(1, (iw + 1) / Step);
        }

        /// <summary>左右のパーツを重ねて全長 256×19 の板を作り、 窓を読み取り、 マーカーを点灯色へ染める準備をする。</summary>
        private void BuildFrame()
        {
            var l = leftTex.GetPixels32(); var r = rightTex.GetPixels32();
            int h = leftTex.height;
            _frame = new Color32[Full * BarH];
            for (int y = 0; y < BarH; y++)
                for (int x = 0; x < Full; x++)
                {
                    int src = (h - 1 - (rowY + y)) * Full + x;   // 画像は下から並ぶので上下を反転して読む
                    var a = l[src]; var b = r[src];
                    _frame[y * Full + x] = b.a > 0 ? b : a;
                }

            // 窓 = 絵の透明な範囲 (座標は書かず、 絵から読む): 列 120 で縦の範囲、 その中段で横の範囲
            bool Tr(int x, int y) => _frame[y * Full + x].a == 0;
            int y0 = -1, y1 = -1;
            for (int y = 1; y < BarH - 1; y++) if (Tr(120, y)) { if (y0 < 0) y0 = y; y1 = y; }
            int x0 = -1, x1 = -1, ym = (y0 + y1) >> 1;
            for (int x = 28; x < 242; x++) if (Tr(x, ym)) { if (x0 < 0) x0 = x; x1 = x; }
            _wy0 = y0; _wy1 = y1; _wx0 = x0; _wx1 = x1;
            if (y0 < 0 || x0 < 0) { Debug.LogWarning("[Game2DBar] 窓が読み取れない: " + name); _frame = null; }
        }

        private void Draw(int len, int segs, int litCount)
        {
            if (_out == null || _out.width != len)
            {
                if (_out != null) Destroy(_out);
                _out = new Texture2D(len, BarH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DBar" };
            }
            var px = new Color32[len * BarH];
            int midW = len - LeftW - RightW;
            Color32 litC = lit, offC = off;
            for (int y = 0; y < BarH; y++)
            {
                for (int x = 0; x < LeftW; x++) px[y * len + x] = Tinted(_frame[y * Full + x], litC);
                for (int o = 0; o < midW; o++) px[y * len + LeftW + o] = Tinted(_frame[y * Full + TileX + (o % TileW)], litC);
                for (int x = 0; x < RightW; x++) px[y * len + len - RightW + x] = Tinted(_frame[y * Full + (Full - RightW) + x], litC);
            }
            // 窓: 目盛り (5 画素・6 画素ごと)。 窓は左パーツの範囲にあるので x はそのまま
            int ix = _wx0, ih = _wy1 - _wy0 + 1;
            for (int i = 0; i < segs; i++)
            {
                var c = i < litCount ? litC : offC;
                for (int dx = 0; dx < Seg; dx++)
                    for (int dy = 0; dy < ih; dy++)
                    {
                        int x = ix + i * Step + dx;
                        if (x >= len) continue;
                        px[(_wy0 + dy) * len + x] = c;
                    }
            }
            // 画像は下から並ぶので、 先頭行 (上) を最後に置く
            var flipped = new Color32[len * BarH];
            for (int y = 0; y < BarH; y++)
                System.Array.Copy(px, y * len, flipped, (BarH - 1 - y) * len, len);
            _out.SetPixels32(flipped);
            _out.Apply(false);
            _img.texture = _out;
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(len, BarH);
        }

        /// <summary>左のアイコンの緑のマーカーを点灯色へ染める。</summary>
        private static Color32 Tinted(Color32 c, Color32 litC)
        {
            if (c.a > 0 && c.g > 200 && c.r < 90 && c.b < 90) return new Color32(litC.r, litC.g, litC.b, c.a);
            return c;
        }
    }
}
