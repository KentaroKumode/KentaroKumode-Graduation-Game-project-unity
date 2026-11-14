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
    /// </summary>
    public class Game2DGuiRt : MonoBehaviour
    {
        public Camera guiCamera;
        public RawImage present;
        public Material presentMaterial;
        public const int Width = 960, Height = 540;

        /// <summary>GUI に掛ける強さが半分になる距離 (dot)。 液晶に接した画素で 1.0 → HalfDots で 0.5 → 2 倍で 0.25。
        /// 初期値 60: 左右のパネル 1 枚の幅 (108 dot) の奥の端で約 0.29 (作者の目安「パネル 1 枚の中で 1.0 → 0.3」)。 調整はここだけ。</summary>
        public const float HalfDots = 60f;
        /// <summary>重みを測る格子 (GUI の 1 dot = 1 画素)。</summary>
        private const int MapW = 480, MapH = 270;

        public static Game2DGuiRt Current { get; private set; }

        private RenderTexture _rt, _small;
        private Material _mat;
        private Texture2D _weight;
        private bool _weightPending;

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
            var w = BuildWeights(px);
            if (_weight == null) _weight = new Texture2D(MapW, MapH, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Game2DGuiFxWeight" };
            _weight.SetPixelData(w, 0); _weight.Apply(false);
            if (_mat != null) _mat.SetTexture(FxWeightId, _weight);
            FxWeightReady = true;
        }

        /// <summary>液晶が見えている画素 (GUI のアルファ &lt; 0.5) からの距離 (8 方向・斜め √2 の 2 回走査) で重みを作る。
        /// GUI の画素: 2^(−(距離 − 1) / HalfDots) ・液晶の画素: 1 (GUI が無いので使われない)。 並びは読み戻しのまま (下の行から = テクスチャと同じ)。</summary>
        private static float[] BuildWeights(Unity.Collections.NativeArray<Color32> px)
        {
            const float D1 = 1f, D2 = 1.41421356f, Inf = 1e9f;
            var d = new float[MapW * MapH];
            for (int i = 0; i < d.Length; i++) d[i] = px[i].a < 128 ? 0f : Inf;
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
            var w = new float[d.Length];
            for (int i = 0; i < d.Length; i++) w[i] = d[i] == 0f ? 1f : d[i] >= Inf ? 0f : Mathf.Pow(2f, -(d[i] - 1f) / HalfDots);
            return w;
        }
    }
}
