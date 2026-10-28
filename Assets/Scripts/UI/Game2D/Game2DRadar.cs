using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 魔石のレーダー (2026-10-27 刷新)。 資源バー (bar_layer1) の右にある 51×51 の液晶 (左上 313,213) の中央へ、 気配の円を 1 つ描く。
    /// 外枠・液晶の地は bar_layer1 に描かれているので、 ここは液晶の上に重ねる透明な 51×51 の絵 (画素そのまま・拡大しない) だけを作る。 素材の絵は使わない。
    /// <para>円は 1 ドット幅・半径 19。 参考 (HighFleet のレーダー) の落ち着いた色味に合わせて、 原色は使わない。
    /// 距離 = 色と線のブレの強さ: 反応なし = 黄 + 小さいブレ / 遠距離 = 橙 + 中くらいのブレ / 近接 = 赤 + 大きいブレ / 察知されている = 赤 + 激しいブレ。
    /// 方位 = ブレる方向 (斜めの 4 つの 90° の扇: 0 = 右下 / 1 = 左下 / 2 = 左上 / 3 = 右上。 円の半径がその扇の中でだけ大きく揺れる)。 扇の外と反応なしの時も、 小さく揺れる (参考の円の線のゆらぎ)。
    /// ブレは 1 秒に 8 回だけ引き直す (8 コマ/秒・間のフレームは絵を変えない = 毎フレームちらつかせない)。</para>
    /// </summary>
    public class Game2DRadar : MonoBehaviour
    {
        public const int Size = 51, C = 25, Cut = 5;
        public const float Radius = 19f;

        [Tooltip("出力を映す RawImage (51×51)")] public RawImage image;
        [Tooltip("光る線のまわりに、 光のにじみ (ブルーム) を付ける")] public bool bloom = true;
        private const float BloomGain = 0.7f, BaseGlow = 0.12f;      // 全体の強さ / ふつうの円の線の発光 (探知した方向の強調の 12%)

        /// <summary>レベル別 (0 = 反応なし / 1 遠い / 2 かなり近い / 3 察知) の色・扇の中のブレの大きさ (円の半径がずれる最大の画素数)・ブレを引き直す間隔 (秒)。 色: 反応なし = 黄 (既定) / 遠距離 = 橙 / 近接 = 赤 / 察知 (発見) = 赤。 どれも参考 (HighFleet) の落ち着いた色味に合わせて、 原色にはしない。</summary>
        private static readonly Color32[] Col = { new Color32(176, 140, 64, 255), new Color32(178, 98, 44, 255), new Color32(150, 50, 40, 255), new Color32(150, 50, 40, 255) };
        private static readonly float[] Amp = { 1.2f, 2.4f, 4.4f, 6.2f };
        private static readonly float[] Wide = { 2.5f, 2.5f, 2.2f, 1.9f };       // ノイズの山の幅 (角度の区切り Bins 何個ぶんか): 広いほど、 線が切れにくい
        private static readonly float[] Fine = { 0f, 0.12f, 0.28f, 0.4f };      // 細かいギザギザの混ぜる割合: 上位ほど荒く、 激しいノイズ感にする
        private static readonly float[] Step = { 0.125f, 0.125f, 0.125f, 0.125f };      // 8 コマ/秒 (全レベル共通。 ブレの大きさで距離を示す)
        private const float BaseAmp = 1.2f;           // 扇の外 (反応していない所) のブレの大きさ。 ごく小さい
        private const int Bins = 180;                 // 円を 2° ごとに区切ってブレを持たせる

        private Texture2D _tex;
        private Color32[] _out;
        private bool[] _inside;
        private float[] _dist, _wt, _sw;               // 画素ごとの、 中心からの距離・ブレの大きさ (画素)・方位の扇の重み (扇の真ん中 1 → 端 0)
        private int[] _bin;
        private float[] _off = new float[Bins];       // 角度ごとの半径のずれ (-1..1)。 Step の間隔ごとに 1 回だけ引き直す
        private float _next; private int _wtLevel = -1, _wtBearing = -2;
        private System.Random _rng = new System.Random(7);
        private int _level = 0, _bearing = -1;
        private RawImage _glowImg; private Texture2D _glowTex; private Material _glowMat; private Color32[] _glowOut;      // ブルームは別の絵 (加算合成) にする: 線の絵の下に重ねる
        private float[] _em;                          // 画素ごとの発光の強さ (ブルームの元): 探知した方向の弧・針・先端は強く、 ふつうの円の線はごく弱く
        private int _drawLv = -1, _drawBr = -2;       // 最後に描いた時のレベルと方位 (変わった時だけ描き直す)

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>確認用: 0 以上にすると、 ゲームの魔石の読みの代わりに、 このレベルと方位で描く (エディタ専用・既定は無効)。</summary>
        public static int DebugLevel = -1, DebugBearing = 0;
#endif

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex); if (_glowTex != null) Destroy(_glowTex); if (_glowMat != null) Destroy(_glowMat);
            if (_glowImg != null) Destroy(_glowImg.gameObject);
        }

        /// <summary>魔石の読み。 level 0 = 反応なし / 1 = 遠い / 2 = かなり近い / 3 = 察知されている。 bearing = 斜めの 4 方向 (0 = 右下 / 1 = 左下 / 2 = 左上 / 3 = 右上。 画面の下 = 南なので、 0 = 南東 / 1 = 南西 / 2 = 北西 / 3 = 北東)。</summary>
        public void SetStone(int level, int bearing4)
        {
            if (level > _level && level >= 1 && level <= 3) Game2DSfx.Play("radar_" + level);
            _level = level; _bearing = level > 0 ? bearing4 : -1;
        }

        /// <summary>液晶の画面の内側か (四隅は斜めに欠けている)。</summary>
        private static bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return false;
            int ax = Mathf.Abs(x - C), ay = Mathf.Abs(y - C);
            return (C - ax) + (C - ay) >= Cut;
        }

        /// <summary>方位の扇 (90°) の中での、 線のブレの掛かり方。 扇の真ん中で 1・端で 0 (扇の外はブレない)。</summary>
        private static float SectorWeight(float angDeg, int bearing)
        {
            float centre = bearing * 90f + 45f, d = Mathf.Abs(Mathf.DeltaAngle(angDeg, centre));
            return d >= 45f ? 0f : Mathf.Pow(Mathf.Cos(d / 45f * Mathf.PI * 0.5f), 0.6f);
        }

        private void Update()
        {
            if (image == null) return;
            if (_tex == null)
            {
                int n = Size * Size; _inside = new bool[n]; _dist = new float[n]; _wt = new float[n]; _sw = new float[n]; _em = new float[n]; _bin = new int[n]; _out = new Color32[n];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x; _inside[i] = Inside(x, y);
                        float dx = x - C, dy = y - C; _dist[i] = Mathf.Sqrt(dx * dx + dy * dy);
                        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; if (ang < 0f) ang += 360f;      // 東 0° → 南 90° (画面の下) → 西 180° → 北 270°
                        _bin[i] = Mathf.Clamp((int)(ang / 360f * Bins), 0, Bins - 1);
                    }
                _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DRadar" };
                image.texture = _tex;
            }
            int level = _level, bearing = _bearing;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugLevel >= 0) { level = DebugLevel; bearing = DebugLevel > 0 ? DebugBearing : -1; }
#endif
            int lv = Mathf.Clamp(level, 0, 3); int br = lv > 0 ? bearing : -1;
            bool stepped = false;
            if (Time.unscaledTime >= _next)                                                        // ブレは、 この間隔ごとに 1 回だけ変える (間の毎フレームは、 絵を変えない)
            {
                stepped = true; _next = Time.unscaledTime + Step[lv];
                var raw = new float[Bins]; for (int k = 0; k < Bins; k++) raw[k] = (float)_rng.NextDouble() * 2f - 1f;
                var coarse = BlurCircular(raw, Wide[lv]);                                                                // 山の幅の広いノイズ (線が切れにくい)
                float f = Fine[lv];
                for (int k = 0; k < Bins; k++)
                {
                    float fresh = coarse[k] * (1f - f) + raw[(k + 1) % Bins] * 0.0f + (raw[k] * 0.6f + raw[(k + Bins - 1) % Bins] * 0.2f + raw[(k + 1) % Bins] * 0.2f) * f;      // 細かいギザギザを、 少しだけ混ぜる
                    _off[k] = Mathf.Lerp(_off[k], Mathf.Clamp(fresh, -1f, 1f), 0.7f);                                          // 前の形を 3 割残して、 全部が一度に入れ替わらないようにする
                }
            }
            if (lv != _wtLevel || br != _wtBearing)                                                // 扇の重み (大きさ) は、 レベルか方位が変わった時だけ引き直す
            {
                _wtLevel = lv; _wtBearing = br;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x; float ang = (_bin[i] + 0.5f) * 360f / Bins;
                        _sw[i] = br >= 0 ? SectorWeight(ang, br) : 0f;
                        _wt[i] = BaseAmp + (Amp[lv] - BaseAmp) * _sw[i];      // 画素ごとのブレの大きさ (画素)
                    }
            }
            SyncGlow();
            if (!stepped && lv == _drawLv && br == _drawBr) return;                               // 絵が変わらない間は、 描き直さない
            _drawLv = lv; _drawBr = br; System.Array.Clear(_em, 0, _em.Length);
            var c = Col[lv]; float ringA = lv == 0 ? 0.85f : 1f;
            var hi = Vivid(c, 0.92f, 1.22f); var hot = Vivid(c, 0.78f, 99f);      // 強調の色 = 線の色の彩度を上げて、 少しだけ明るくしたもの (明度だけを上げない・白っぽくしない)。 先端の熱い点は、 さらに明るく
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x; Color32 px = new Color32(0, 0, 0, 0);
                    if (_inside[i])
                    {
                        float off = _off[_bin[i]] * _wt[i];
                        float slope = Mathf.Max(Mathf.Abs(_off[_bin[i]] - _off[(_bin[i] + Bins - 1) % Bins]), Mathf.Abs(_off[(_bin[i] + 1) % Bins] - _off[_bin[i]])) * _wt[i];       // ずれが急な所は、 線を太らせて、 切れないようにする
                        float diff = _dist[i] - (Radius + off), ext = 0.5f * slope;
                        bool on = diff >= -(0.55f + ext) && diff < 0.45f + ext;      // 幅 1 の帯を、 0.05 だけ内側へ寄せる (上下左右の中央の画素が、 ちょうど境で欠けないように)
                        bool inner = lv > 0 && _sw[i] >= 0.3f && diff >= -(1.55f + ext) && diff < -(0.55f + ext);      // 探知した方向の扇では、 線の内側へ 1 ドット足して、 2 ドット幅にする
                        bool inSector = lv > 0 && _sw[i] >= 0.3f;
                        if (on) px = inSector ? hi : new Color32(c.r, c.g, c.b, (byte)Mathf.Round(ringA * 255f));      // 扇の中の線は、 明るい色 (探知方向の強調)
                        else if (inner) px = hi;
                        if (on || inner) _em[(Size - 1 - y) * Size + x] = inSector ? 1f : BaseGlow;
                    }
                    _out[(Size - 1 - y) * Size + x] = px;      // テクスチャは下の行が先頭
                }
            // 探知した方向の針 (中心 → 扇の真ん中・円の少し手前): 中心ほど暗く、 円へ近いほど明るい。 先端に熱い点 (3 ドット)
            if (lv > 0 && br >= 0)
            {
                float na = (br * 90f + 45f) * Mathf.Deg2Rad, ncx = Mathf.Cos(na), nsy = Mathf.Sin(na);
                float r0 = 2f, r1 = Radius - 2.5f; int count = Mathf.CeilToInt((r1 - r0) * 2f) + 1; int lx = int.MinValue, ly = int.MinValue, tx = 0, ty = 0; int idx = 0;
                var pts = new System.Collections.Generic.List<Vector2Int>();
                for (int k = 0; k < count; k++)
                {
                    float r = Mathf.Lerp(r0, r1, k / (float)(count - 1)); int px = Mathf.RoundToInt(C + r * ncx), py = Mathf.RoundToInt(C + r * nsy);
                    if (px == lx && py == ly) continue; lx = px; ly = py; pts.Add(new Vector2Int(px, py));
                }
                for (idx = 0; idx < pts.Count; idx++)
                {
                    float t = pts.Count > 1 ? idx / (float)(pts.Count - 1) : 1f; var q = pts[idx];
                    if (!Inside(q.x, q.y)) continue;
                    float k2 = t * 0.9f;                                                                                  // 線の色 (暗くした) → 強調の色 (彩度を上げたもの) へ、 中心から先へ
                    _out[(Size - 1 - q.y) * Size + q.x] = new Color32((byte)Mathf.Lerp(c.r * 0.55f, hi.r, k2), (byte)Mathf.Lerp(c.g * 0.55f, hi.g, k2), (byte)Mathf.Lerp(c.b * 0.55f, hi.b, k2), 255);
                    _em[(Size - 1 - q.y) * Size + q.x] = 0.35f + 0.45f * t;                                                // 針も、 先ほど強く光る
                }
                var tip = pts[pts.Count - 1]; tx = tip.x; ty = tip.y; int ox = ncx >= 0 ? 1 : -1, oy = nsy >= 0 ? 1 : -1;       // 先端の熱い点: 先と、 外向きの縦・横の 1 つずつ
                foreach (var q in new[] { new Vector2Int(tx, ty), new Vector2Int(tx + ox, ty), new Vector2Int(tx, ty + oy) })
                    if (Inside(q.x, q.y)) { _out[(Size - 1 - q.y) * Size + q.x] = hot; _em[(Size - 1 - q.y) * Size + q.x] = 1.4f; }
            }
            if (bloom) ApplyBloom();
            _tex.SetPixels32(_out); _tex.Apply(false);
        }

        /// <summary>ブルーム用の絵 (線の絵の 1 つ下・同じ位置と大きさ・加算合成) を用意する。 加算のシェーダが無ければ、 ブルームは出さない。</summary>
        private void SyncGlow()
        {
            if (!bloom) { if (_glowImg != null) _glowImg.gameObject.SetActive(false); return; }
            if (_glowImg == null)
            {
                var sh = Shader.Find("Game2D/UIAdditive"); if (sh == null) { bloom = false; return; }
                _glowMat = new Material(sh) { name = "Game2DRadarGlow (runtime)" };
                _glowTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DRadarGlow" };
                _glowOut = new Color32[Size * Size];
                var go = new GameObject("RadarGlow", typeof(RectTransform), typeof(RawImage)); go.layer = gameObject.layer;
                go.transform.SetParent(transform.parent, false);
                _glowImg = go.GetComponent<RawImage>(); _glowImg.raycastTarget = false; _glowImg.material = _glowMat; _glowImg.texture = _glowTex;
            }
            var me = (RectTransform)transform; var gr = (RectTransform)_glowImg.transform;
            gr.anchorMin = me.anchorMin; gr.anchorMax = me.anchorMax; gr.pivot = me.pivot; gr.anchoredPosition = me.anchoredPosition; gr.sizeDelta = me.sizeDelta; gr.localScale = me.localScale;
            _glowImg.transform.SetSiblingIndex(Mathf.Max(0, me.GetSiblingIndex() - 0));      // 線の絵と同じ親の中で、 線の絵より下 (先に描く)
            if (_glowImg.transform.GetSiblingIndex() > me.GetSiblingIndex()) _glowImg.transform.SetSiblingIndex(me.GetSiblingIndex());
            _glowImg.gameObject.SetActive(me.gameObject.activeInHierarchy);
        }

        /// <summary>色の彩度を sat 以上に上げて、 明るさを vmul 倍 (最大 1) にする。 色相は変えない。 明度だけを上げて白っぽくするのを避ける。</summary>
        private static Color32 Vivid(Color32 c, float sat, float vmul)
        {
            Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out float h, out float sv, out float v);
            var o = Color.HSVToRGB(h, Mathf.Max(sv, sat), Mathf.Min(1f, v * vmul));
            return new Color32((byte)Mathf.Round(o.r * 255f), (byte)Mathf.Round(o.g * 255f), (byte)Mathf.Round(o.b * 255f), 255);
        }

        /// <summary>角度の輪 (Bins 個) の上の、 ガウスぼかし。 ぼかした後も、 元と同じばらつきの大きさに戻す (山を広くしても、 揺れの大きさは落とさない)。</summary>
        private static float[] BlurCircular(float[] src, float sigma)
        {
            var k = Kernel(sigma); int r = k.Length / 2; var o = new float[Bins]; float m = 0f;
            for (int i = 0; i < Bins; i++) { float a = 0f; for (int j = -r; j <= r; j++) a += src[(i + j + Bins * 4) % Bins] * k[j + r]; o[i] = a; }
            double v = 0; for (int i = 0; i < Bins; i++) v += o[i] * o[i]; float std = Mathf.Sqrt((float)(v / Bins)); float w = 0.577f / Mathf.Max(0.01f, std);      // 一様乱数 (-1..1) の標準偏差 0.577 にそろえる
            for (int i = 0; i < Bins; i++) { o[i] *= w; m = Mathf.Max(m, Mathf.Abs(o[i])); }
            return o;
        }

        private static float[] Kernel(float sigma)
        {
            int r = Mathf.CeilToInt(sigma * 3f); var k = new float[2 * r + 1]; float sum = 0f;
            for (int i = -r; i <= r; i++) { k[i + r] = Mathf.Exp(-(i * i) / (2f * sigma * sigma)); sum += k[i + r]; }
            for (int i = 0; i < k.Length; i++) k[i] /= sum; return k;
        }

        private static float[] Blur(float[] src, float[] k)
        {
            int r = k.Length / 2; var tmp = new float[Size * Size]; var dst = new float[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) { float a = 0f; for (int j = -r; j <= r; j++) { int xx = x + j; if (xx >= 0 && xx < Size) a += src[y * Size + xx] * k[j + r]; } tmp[y * Size + x] = a; }
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) { float a = 0f; for (int j = -r; j <= r; j++) { int yy = y + j; if (yy >= 0 && yy < Size) a += tmp[yy * Size + x] * k[j + r]; } dst[y * Size + x] = a; }
            return dst;
        }

        private static readonly float[][] BloomKernels = { Kernel(0.7f), Kernel(1.6f), Kernel(3.2f) };
        private static readonly float[] BloomWeights = { 0.62f, 0.3f, 0.08f };      // 近くを強く、 遠くの裾はごく弱く (太い光の帯にしない)

        /// <summary>
        /// ブルーム (光のにじみ): 光る画素の色を、 線形の光として 3 つの大きさ (σ 0.7 / 1.6 / 3.2) でぼかして重ね (近くは強く、 遠くへ向けて弱く)、 線の外側の透明な画素へ、
        /// 別の絵 (加算合成) に書く。 足し算なので、 下の液晶の地のディザの粒は、 潰れずに透けて残る。 線そのものの色は変えない。 液晶の外 (四隅の欠け) には出さない。
        /// </summary>
        private void ApplyBloom()
        {
            int n = Size * Size; var e = new float[3][]; for (int k = 0; k < 3; k++) e[k] = new float[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = _out[(Size - 1 - y) * Size + x]; if (p.a == 0) continue; float a = p.a / 255f * _em[(Size - 1 - y) * Size + x]; int i = y * Size + x;
                    e[0][i] = Mathf.Pow(p.r / 255f, 2.2f) * a; e[1][i] = Mathf.Pow(p.g / 255f, 2.2f) * a; e[2][i] = Mathf.Pow(p.b / 255f, 2.2f) * a;
                }
            var g = new float[3][];
            for (int k = 0; k < 3; k++)
            {
                g[k] = new float[n];
                for (int s = 0; s < BloomKernels.Length; s++) { var b = Blur(e[k], BloomKernels[s]); for (int i = 0; i < n; i++) g[k][i] += b[i] * BloomWeights[s]; }
            }
            if (_glowImg == null) return;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x, o = (Size - 1 - y) * Size + x; _glowOut[o] = new Color32(0, 0, 0, 255);      // 足す光なので、 光のない所は黒 (0 を足す)
                    if (!_inside[i]) continue;                                                                      // 液晶の外 (四隅の欠け) には出さない
                    float r = g[0][i] * BloomGain, gg = g[1][i] * BloomGain, b = g[2][i] * BloomGain;
                    if (Mathf.Max(r, Mathf.Max(gg, b)) < 0.0015f) continue;
                    _glowOut[o] = new Color32((byte)Mathf.Round(Mathf.Pow(Mathf.Clamp01(r), 1f / 2.2f) * 255f), (byte)Mathf.Round(Mathf.Pow(Mathf.Clamp01(gg), 1f / 2.2f) * 255f),
                                              (byte)Mathf.Round(Mathf.Pow(Mathf.Clamp01(b), 1f / 2.2f) * 255f), 255);      // 光の色と明るさ (sRGB)
                }
            _glowTex.SetPixels32(_glowOut); _glowTex.Apply(false);
        }
    }
}
