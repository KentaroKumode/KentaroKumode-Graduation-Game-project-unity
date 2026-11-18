using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 外枠の GUI を 960×540 の RT (透明な地) へ描く専用のカメラと、 その RT を液晶の上に重ねる板 (<c>Game2D/GuiPresent</c>) をつなぐ (2026-10-19)。
    /// GUI を一度 RT にするのは、 液晶へ落ちる影 (GUI の輪郭から作る) と、 上が明るく下が暗い色調を付けるため (map-prototype.html の「GUI の影・にじみ・ビネット」)。
    /// RT は GUI の 1 dot = 2 画素 (液晶の RT と同じ細かさ) なので、 画面へは整数倍で最近傍に引き伸ばされ、 ドット絵の密度は変わらない。
    ///
    /// <para><b>ボスの大技を GUI にも掛ける (2026-11-14・作者の指定)</b>: 揺れ・暗転・赤いフィルター・白飛びを、 GUI の板にも掛ける
    /// (<see cref="SetBossFx"/>・値は <c>Battle.BossFx</c> の見本どおりの値)。 強さは画素ごとに、 液晶が見えている所 (GUI に覆われていない所) に
    /// 接している画素で 1.0、 離れるほど滑らかに落ちる (<see cref="HalfDots"/> ドットごとに半分)。 重みは GUI の RT のアルファから距離を測って作る
    /// (<see cref="RequestWeightMap"/>・大技の区間が始まる時に 1 回・480×270)。 乱れ (帯のずれ) と割れ (破片)・効果の絵そのものは GUI に掛けない。
    /// 値が 0 の時は、 シェーダは今までと同じ計算だけを通る (_FxOn = 0)。</para>
    ///
    /// <para><b>GUI を液晶の中身として合成する (2026-11-16・作者の決定「外の部品はもう無い」・<see cref="InLcd"/> = true が既定)</b>:
    /// 提示の板 (<c>Game2D/GuiPresent</c>・影と上下の色調) は使わず、 この RT をグローバル (<c>_Game2DGuiTex</c>) で液晶のシェーダ (<c>Game2D/LcdScreen</c>) に渡す。
    /// 液晶のシェーダが湾曲した後の同じ座標で中身に重ね、 効果を全部掛ける。 残像・ブルーム・自動露出の元の絵には <see cref="Game2DLcdGhost"/> が重ねる。
    /// 液晶のシェーダより先に GUI を描くよう、 GUI のカメラの描く順を液晶を写すカメラ (<see cref="Game2DScreen.screenCamera"/>) の前へ回す。
    /// ボスの大技の揺れ・暗転・赤・白は、 液晶のシェーダが GUI 全体に同じ強さで掛ける (距離の重み <see cref="RequestWeightMap"/> は旧い提示の板の時だけ使う)。
    /// 見比べ用に QA の口 <c>QaBattle.GuiInLcd(false)</c> で旧い提示の板へ戻せる。</para>
    /// </summary>
    public class Game2DGuiRt : MonoBehaviour
    {
        public Camera guiCamera;
        public RawImage present;
        public Material presentMaterial;
        public const int Width = 960, Height = 540;

        /// <summary>GUI に掛ける強さが半分になる距離 (dot)。 窓 (中央の戦闘の窓) に接した画素で 1.0 → HalfDots で 0.5 → 2 倍で 0.25。
        /// 32 (2026-11-14・60 から絞った): 左右のパネル 1 枚 (108 dot) の奥の端で約 0.10・上のバー (約 57 dot) の上端で約 0.30・遠い隅 (約 125 dot) で約 0.07。 調整はここだけ。</summary>
        public const float HalfDots = 32f;
        /// <summary>窓を求める時に塞ぐ隙間 (dot): この 2 倍までの細い隙間 (パネル同士・パネルと画面の端) は窓に繋がらない。</summary>
        public const int GapCloseDots = 4;
        /// <summary>重みを測る格子 (GUI の 1 dot = 1 画素)。</summary>
        private const int MapW = 480, MapH = 270;

        public static Game2DGuiRt Current { get; private set; }

        /// <summary>GUI を液晶の中身として合成するか (true = 既定・2026-11-16 / false = 旧い提示の板で液晶の上に重ねる・見比べ用)。</summary>
        public static bool InLcd = DefaultInLcd;
        public const bool DefaultInLcd = true;
        /// <summary>GUI の RT (液晶の中身と同じ 960×540)。</summary>
        public RenderTexture Texture => _rt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { InLcd = DefaultInLcd; }

        private static readonly int GuiTexId = Shader.PropertyToID("_Game2DGuiTex"), GuiOnId = Shader.PropertyToID("_Game2DGuiOn"),
            GFxOnId = Shader.PropertyToID("_Game2DGuiFxOn"), GFxDarkId = Shader.PropertyToID("_Game2DGuiFxDark"), GFxRedId = Shader.PropertyToID("_Game2DGuiFxRed"),
            GFxWhiteId = Shader.PropertyToID("_Game2DGuiFxWhite"), GFxShakeId = Shader.PropertyToID("_Game2DGuiFxShake");
        private float _camDepth0 = float.NaN; private Camera _screenCam;

        /// <summary>合成の形を当てる (毎フレーム・カメラが描く前): 提示の板の出し入れ・液晶のシェーダへ渡す GUI・GUI のカメラの描く順。</summary>
        private void Update()
        {
            bool on = InLcd && _rt != null;
            if (present != null && present.enabled == on) present.enabled = !on;
            Shader.SetGlobalTexture(GuiTexId, _rt);
            Shader.SetGlobalFloat(GuiOnId, on ? 1f : 0f);
            if (guiCamera == null) return;
            if (float.IsNaN(_camDepth0)) _camDepth0 = guiCamera.depth;
            float want = _camDepth0;
            if (on)
            {
                if (_screenCam == null) { var sc = FindObjectOfType<Game2DScreen>(); _screenCam = sc != null ? sc.screenCamera : null; }
                if (_screenCam != null) want = Mathf.Min(_camDepth0, _screenCam.depth - 1f);      // 液晶を写すカメラより先に描く (同じフレームの GUI を液晶のシェーダが読む)
            }
            if (guiCamera.depth != want) guiCamera.depth = want;
        }

        private RenderTexture _rt, _small;
        private Material _mat;
        private Texture2D _weight;
        private bool _weightPending;
        private float[] _weightData;

        private static readonly int FxOnId = Shader.PropertyToID("_FxOn"), FxDarkId = Shader.PropertyToID("_FxDark"), FxRedId = Shader.PropertyToID("_FxRed"),
            FxWhiteId = Shader.PropertyToID("_FxWhite"), FxShakeId = Shader.PropertyToID("_FxShake"), FxWeightId = Shader.PropertyToID("_FxWeight");

        /// <summary>いま GUI に掛けている値 (確認用・QaBattle.Status が読む)。</summary>
        public static float FxDark = 1f, FxRed, FxWhite; public static Vector2 FxShake; public static bool FxOn, FxWeightReady;

        private void OnEnable()
        {
            Current = this;
            if (_rt == null)
            {
                _rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DGuiRt", useMipMap = false };
                _rt.Create();
            }
            if (guiCamera != null)
            {
                guiCamera.targetTexture = _rt;
                guiCamera.clearFlags = CameraClearFlags.SolidColor;
                guiCamera.backgroundColor = new Color(0, 0, 0, 0);
            }
            // RT は実行時に作るものなので、 アセットのマテリアルへは書かず (保存されてしまう)、 複製に入れる
            if (presentMaterial != null && present != null)
            {
                if (_mat == null) _mat = new Material(presentMaterial) { name = "Game2DGuiPresent (runtime)" };
                _mat.SetTexture("_GuiTex", _rt); _mat.SetVector("_RtSize", new Vector4(Width, Height, 0, 0));
                present.material = _mat;
            }
            if (present != null) present.texture = _rt;
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(GuiOnId, 0f);
            if (present != null) present.enabled = true;
            if (guiCamera != null && !float.IsNaN(_camDepth0)) guiCamera.depth = _camDepth0;
            if (guiCamera != null && guiCamera.targetTexture == _rt) guiCamera.targetTexture = null;
            if (Current == this) Current = null;
        }

        private void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_small != null) { _small.Release(); Destroy(_small); }
            if (_mat != null) Destroy(_mat);
            if (_weight != null) Destroy(_weight);
        }

        // =====================================================================
        //  ボスの大技を GUI にも掛ける (2026-11-14)
        // =====================================================================

        /// <summary>このフレームに GUI へ掛ける値。 on = false なら何も掛けない (シェーダは今までの計算だけ)。
        /// dark = 暗幕の倍率 (1 = そのまま・0 = 真っ黒)・red = 赤いフィルターの a・white = 白飛びの強さ (0〜1) と色・shake = 揺れ (dot)。</summary>
        public static void SetBossFx(bool on, float dark, float red, float white, Color whiteColor, Vector2 shake)
        {
            FxOn = on; FxDark = on ? dark : 1f; FxRed = on ? red : 0f; FxWhite = on ? white : 0f; FxShake = on ? shake : Vector2.zero;
            // 液晶の中身として合成する時 (InLcd): 液晶のシェーダが GUI 全体に同じ強さで掛ける (グローバル)
            Shader.SetGlobalFloat(GFxOnId, on ? 1f : 0f);
            Shader.SetGlobalFloat(GFxDarkId, FxDark); Shader.SetGlobalFloat(GFxRedId, FxRed);
            Shader.SetGlobalVector(GFxWhiteId, new Vector4(whiteColor.r, whiteColor.g, whiteColor.b, FxWhite));
            Shader.SetGlobalVector(GFxShakeId, new Vector4(FxShake.x, FxShake.y, 0, 0));
            var g = Current; if (g == null || g._mat == null) return;
            if (!on || g._weight == null) { g._mat.SetFloat(FxOnId, 0f); return; }      // 重みがまだ無い = 掛けない
            g._mat.SetFloat(FxOnId, 1f);
            g._mat.SetFloat(FxDarkId, dark); g._mat.SetFloat(FxRedId, red);
            g._mat.SetVector(FxWhiteId, new Vector4(whiteColor.r, whiteColor.g, whiteColor.b, white));
            g._mat.SetVector(FxShakeId, new Vector4(shake.x, shake.y, 0, 0));
        }

        /// <summary>GUI の RT のアルファから、 画素ごとの強さ (液晶に接した画素 1.0 → <see cref="HalfDots"/> で半分) を作る。 非同期で読む (呼ぶのは大技の区間の始まり)。</summary>
        public static void RequestWeightMap()
        {
            var g = Current; if (g == null || g._rt == null || g._weightPending) return;
            if (!SystemInfo.supportsAsyncGPUReadback) return;
            if (g._small == null) { g._small = new RenderTexture(MapW, MapH, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "Game2DGuiRtSmall" }; g._small.Create(); }
            Graphics.Blit(g._rt, g._small);      // 1 dot = 1 画素 (点で間引く)
            g._weightPending = true;
            UnityEngine.Rendering.AsyncGPUReadback.Request(g._small, 0, TextureFormat.RGBA32, g.OnWeightReadback);
        }

        private void OnWeightReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req)
        {
            _weightPending = false;
            if (req.hasError || this == null) return;
            var px = req.GetData<Color32>();
            if (px.Length != MapW * MapH) return;
            var w = BuildWeights(px); _weightData = w;
            if (_weight == null) _weight = new Texture2D(MapW, MapH, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Game2DGuiFxWeight" };
            _weight.SetPixelData(w, 0); _weight.Apply(false);
            if (_mat != null) _mat.SetTexture(FxWeightId, _weight);
            FxWeightReady = true;
        }

        /// <summary>重みを作る (2026-11-14 作り直し)。 距離の源は<b>中央の戦闘の窓だけ</b>:
        /// ① GUI のアルファ ≥ 0.5 の画素を「覆われている」とする → ② 覆いを <see cref="GapCloseDots"/> ドット閉じる (膨らませて縮める) ──
        ///    パネル同士の隙間・パネルと画面の端の細い隙間を塞ぎ、 窓と外側が繋がらないようにする (右下のパネルが食い込む L 字のような広い形はそのまま残る)
        /// → ③ 覆われていない画素のうち、 画面の中心に一番近い画素から 4 方向に繋がっている所 = 窓 → ④ 窓からの距離 (8 方向・斜め √2 の 2 回走査)
        /// → ⑤ 窓の画素 1・それ以外 2^(−(距離 − 1) / HalfDots)。 旧い作り (覆われていない画素を全部源にした) は、 パネルの外側の縁や隙間も源になり、
        /// パネルの四方の縁が全部強く光った。 並びは読み戻しのまま (下の行から = テクスチャと同じ)。</summary>
        private static float[] BuildWeights(Unity.Collections.NativeArray<Color32> px)
        {
            int n = MapW * MapH;
            var cov = new bool[n];
            for (int i = 0; i < n; i++) cov[i] = px[i].a >= 128;
            var closed = Erode(Dilate(cov, GapCloseDots), GapCloseDots);
            for (int i = 0; i < n; i++) closed[i] |= cov[i];
            // 画面の中心に一番近い、 覆われていない画素
            int seed = -1; float best = float.MaxValue;
            for (int y = 0; y < MapH; y++) for (int x = 0; x < MapW; x++)
                {
                    int i = y * MapW + x; if (closed[i]) continue;
                    float dd = (x - MapW / 2f) * (x - MapW / 2f) + (y - MapH / 2f) * (y - MapH / 2f);
                    if (dd < best) { best = dd; seed = i; }
                }
            var win = new bool[n];
            if (seed >= 0)
            {
                var q = new System.Collections.Generic.Queue<int>(); q.Enqueue(seed); win[seed] = true;
                while (q.Count > 0)
                {
                    int i = q.Dequeue(), x = i % MapW, y = i / MapW;
                    if (x > 0 && !win[i - 1] && !closed[i - 1]) { win[i - 1] = true; q.Enqueue(i - 1); }
                    if (x < MapW - 1 && !win[i + 1] && !closed[i + 1]) { win[i + 1] = true; q.Enqueue(i + 1); }
                    if (y > 0 && !win[i - MapW] && !closed[i - MapW]) { win[i - MapW] = true; q.Enqueue(i - MapW); }
                    if (y < MapH - 1 && !win[i + MapW] && !closed[i + MapW]) { win[i + MapW] = true; q.Enqueue(i + MapW); }
                }
            }
            const float D1 = 1f, D2 = 1.41421356f, Inf = 1e9f;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = win[i] ? 0f : Inf;
            for (int y = 0; y < MapH; y++)
                for (int x = 0; x < MapW; x++)
                {
                    int i = y * MapW + x; float v = d[i]; if (v == 0f) continue;
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + D1);
                    if (y > 0) { v = Mathf.Min(v, d[i - MapW] + D1); if (x > 0) v = Mathf.Min(v, d[i - MapW - 1] + D2); if (x < MapW - 1) v = Mathf.Min(v, d[i - MapW + 1] + D2); }
                    d[i] = v;
                }
            for (int y = MapH - 1; y >= 0; y--)
                for (int x = MapW - 1; x >= 0; x--)
                {
                    int i = y * MapW + x; float v = d[i]; if (v == 0f) continue;
                    if (x < MapW - 1) v = Mathf.Min(v, d[i + 1] + D1);
                    if (y < MapH - 1) { v = Mathf.Min(v, d[i + MapW] + D1); if (x < MapW - 1) v = Mathf.Min(v, d[i + MapW + 1] + D2); if (x > 0) v = Mathf.Min(v, d[i + MapW - 1] + D2); }
                    d[i] = v;
                }
            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = d[i] == 0f ? 1f : d[i] >= Inf ? 0f : Mathf.Pow(2f, -(d[i] - 1f) / HalfDots);
            return w;
        }

        /// <summary>真の画素を r ドット (チェビシェフ = 正方形) 膨らませる (横・縦の 2 回)。</summary>
        private static bool[] Dilate(bool[] m, int r) => Box(m, r, true);
        /// <summary>真の画素を r ドット縮める (画面の外は真とみなす = 端で縮まない)。</summary>
        private static bool[] Erode(bool[] m, int r) => Box(m, r, false);
        private static bool[] Box(bool[] m, int r, bool any)
        {
            var t = new bool[m.Length]; var o = new bool[m.Length];
            for (int y = 0; y < MapH; y++) for (int x = 0; x < MapW; x++)
                {
                    bool v = !any;
                    for (int k = -r; k <= r; k++) { int xx = x + k; bool s = xx < 0 || xx >= MapW ? !any : m[y * MapW + xx]; if (any ? s : !s) { v = any; break; } }
                    t[y * MapW + x] = v;
                }
            for (int y = 0; y < MapH; y++) for (int x = 0; x < MapW; x++)
                {
                    bool v = !any;
                    for (int k = -r; k <= r; k++) { int yy = y + k; bool s = yy < 0 || yy >= MapH ? !any : t[yy * MapW + x]; if (any ? s : !s) { v = any; break; } }
                    o[y * MapW + x] = v;
                }
            return o;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[実機の確認用] 今の重みの地図を PNG (480×270・白 = 1・黒 = 0・上の行が画面の上) で書き出す。 地図がまだ無ければ false。</summary>
        public static bool QaDumpWeight(string path)
        {
            var g = Current; if (g == null || g._weightData == null) return false;
            var t = new Texture2D(MapW, MapH, TextureFormat.RGBA32, false);
            var c = new Color32[MapW * MapH];
            for (int i = 0; i < c.Length; i++) { byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(g._weightData[i]) * 255f); c[i] = new Color32(v, v, v, 255); }
            t.SetPixels32(c); t.Apply(false);
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Destroy(t);
            return true;
        }
#endif
    }
}
