using UI.Lcd;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 液晶の残像 (map-prototype.html の <c>LFX.ghost</c>・液晶の応答の遅れ) の「前のフレーム」を用意する。
    /// フレームの頭 (カメラが描く前) の液晶 RT は、 まだ前のフレームの絵なので、 それを別の RT へ写して液晶の Quad の
    /// <c>_PrevTex</c> に渡す。 重ね方 (lighten) は Quad のシェーダ (Game2D/LcdScreen) が行う。
    ///
    /// <para><b>自動露出</b> (2026-10-19): 画面が明るすぎる時 (ブルームで眩しい時) に、 液晶全体の明るさを自動で下げる。
    /// 前のフレームの RT にミップマップを作り、 最も小さい階層 (15×8) を非同期に読んで、 絵の平均の明るさ (効果を掛ける前の値) を出す。
    /// 目標より明るいほど <c>_Exposure</c> を 1 より下げ、 暗いと少しだけ上げる (上限・下限あり)。 下げるのは速く (attack)、 戻すのはゆっくり (release)。
    /// シェーダは <c>_Exposure</c> を、 最終の明るさと、 ブルームの強さ (2 乗) に掛ける。</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Game2DLcdGhost : MonoBehaviour
    {
        public LcdScreen lcd;
        public Renderer surface;

        [Header("ブルーム (なめらかなにじみ)")]
        [Tooltip("Hidden/Game2DBloom (ビルドに含めるため参照を持つ)")] public Shader bloomShader;
        [Range(0.5f, 4f), Tooltip("ぼかしの広がり (大きいほど柔らかく広い)")] public float softness = 1.6f;

        [Header("自動露出")]
        public bool autoExposure = true;
        [Range(0.05f, 0.9f), Tooltip("目標の平均の明るさ (効果を掛ける前の絵・0..1)。 これより明るいと下げる")] public float target = 0.38f;
        [Range(0f, 1.5f), Tooltip("効きの強さ (0 = 何もしない・1 = 目標との比そのまま)")] public float strength = 0.6f;
        [Range(0.3f, 1f), Tooltip("下げる限界 (明るすぎる絵でもここまで)")] public float minExposure = 0.72f;
        [Range(1f, 1.5f), Tooltip("上げる限界 (暗い絵でもここまで)")] public float maxExposure = 1.06f;
        [Tooltip("明るくなった時に下げる速さ (秒・短いほど速い)")] public float attackSec = 0.12f;
        [Tooltip("暗くなった時に戻す速さ (秒・長いほどゆっくり)")] public float releaseSec = 1.1f;

        /// <summary>いまの平均の明るさ (効果を掛ける前・0..1)。 デバッグ・調整用。</summary>
        public float MeasuredLuma { get; private set; } = -1f;
        public float Exposure { get; private set; } = 1f;

        private const int MeterMip = 6;          // 960×540 → 15×8
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private bool _pending;
        private int _frame;

        private RenderTexture _prev, _bloomA, _bloomTmpA, _bloomB, _bloomTmpB;
        private Material _bloomMat;
        private MaterialPropertyBlock _mpb;
        private static readonly int BloomTexId = Shader.PropertyToID("_BloomTex"), BloomTexBId = Shader.PropertyToID("_BloomTexB");
        private static readonly int ThrId = Shader.PropertyToID("_Thr"), PowId = Shader.PropertyToID("_Pow"), DirId = Shader.PropertyToID("_Dir");
        private static readonly int PrevId = Shader.PropertyToID("_PrevTex");

        private void OnDestroy()
        {
            foreach (var rt in new[] { _prev, _bloomA, _bloomTmpA, _bloomB, _bloomTmpB }) if (rt != null) { rt.Release(); Destroy(rt); }
            if (_bloomMat != null) Destroy(_bloomMat);
        }

        private RenderTexture NewSmall(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };
            rt.Create();
            return rt;
        }

        /// <summary>ブルームの下処理: 明るい所を取り出して縮小 (1/4・1/8) し、 ぼかす。 Quad のシェーダが双一次補間で引き伸ばして使う = 輪郭がなめらか。</summary>
        private void BuildBloom()
        {
            if (bloomShader == null) return;
            if (_bloomMat == null) _bloomMat = new Material(bloomShader) { hideFlags = HideFlags.HideAndDontSave };
            int w = _prev.width / 4, h = _prev.height / 4;
            if (_bloomA == null || _bloomA.width != w || _bloomA.height != h)
            {
                foreach (var rt in new[] { _bloomA, _bloomTmpA, _bloomB, _bloomTmpB }) if (rt != null) { rt.Release(); Destroy(rt); }
                _bloomA = NewSmall(w, h, "Game2DBloomA"); _bloomTmpA = NewSmall(w, h, "Game2DBloomTmpA");
                _bloomB = NewSmall(w / 2, h / 2, "Game2DBloomB"); _bloomTmpB = NewSmall(w / 2, h / 2, "Game2DBloomTmpB");
            }
            var mat = surface.sharedMaterial;
            _bloomMat.SetFloat(ThrId, mat.GetFloat("_BloomThr")); _bloomMat.SetFloat(PowId, mat.GetFloat("_BloomPow"));
            Graphics.Blit(_prev, _bloomA, _bloomMat, 0);                                   // 明るい所だけ・4×4 を 1 つに
            _bloomMat.SetVector(DirId, new Vector2(softness, 0)); Graphics.Blit(_bloomA, _bloomTmpA, _bloomMat, 1);
            _bloomMat.SetVector(DirId, new Vector2(0, softness)); Graphics.Blit(_bloomTmpA, _bloomA, _bloomMat, 1);
            Graphics.Blit(_bloomA, _bloomB, _bloomMat, 3);                                 // もう 1 段小さく (広いやわらかな光)
            _bloomMat.SetVector(DirId, new Vector2(softness, 0)); Graphics.Blit(_bloomB, _bloomTmpB, _bloomMat, 1);
            _bloomMat.SetVector(DirId, new Vector2(0, softness)); Graphics.Blit(_bloomTmpB, _bloomB, _bloomMat, 1);
        }

        private void Update()
        {
            if (lcd == null || lcd.Texture == null || surface == null) return;
            var src = lcd.Texture;
            if (_prev == null || _prev.width != src.width || _prev.height != src.height)
            {
                if (_prev != null) { _prev.Release(); Destroy(_prev); }
                _prev = new RenderTexture(src.width, src.height, 0, src.format) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DLcdPrev", useMipMap = true, autoGenerateMips = false };
                _prev.Create();
            }
            Graphics.Blit(src, _prev);
            BuildBloom();
            if (autoExposure) Meter(); else { MeasuredLuma = -1f; Exposure = Mathf.MoveTowards(Exposure, 1f, Time.unscaledDeltaTime * 2f); }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            surface.GetPropertyBlock(_mpb);
            _mpb.SetTexture(PrevId, _prev);
            if (_bloomA != null) { _mpb.SetTexture(BloomTexId, _bloomA); _mpb.SetTexture(BloomTexBId, _bloomB); }
            _mpb.SetFloat(ExposureId, Exposure);
            surface.SetPropertyBlock(_mpb);
        }

        /// <summary>平均の明るさを (数フレームおきに・非同期で) 測り、 目標との比から露出を決めてなめらかに追う。</summary>
        private void Meter()
        {
            if (!_pending && (++_frame % 3) == 0)
            {
                _pending = true;
                _prev.GenerateMips();
                UnityEngine.Rendering.AsyncGPUReadback.Request(_prev, MeterMip, TextureFormat.RGBA32, OnReadback);
            }
            float want = 1f;
            if (MeasuredLuma >= 0f) want = Mathf.Clamp(Mathf.Pow(target / (MeasuredLuma + 0.02f), strength), minExposure, maxExposure);
            // 下げる (明るくなった) 時は速く、 戻す時はゆっくり
            float sec = want < Exposure ? attackSec : releaseSec;
            Exposure = Mathf.Lerp(Exposure, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(sec, 0.01f)));
        }

        private void OnReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req)
        {
            _pending = false;
            if (req.hasError) return;
            var px = req.GetData<Color32>();
            if (px.Length == 0) return;
            double sum = 0;
            for (int i = 0; i < px.Length; i++) sum += (0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b) / 255.0;
            MeasuredLuma = (float)(sum / px.Length);
        }
    }
}
