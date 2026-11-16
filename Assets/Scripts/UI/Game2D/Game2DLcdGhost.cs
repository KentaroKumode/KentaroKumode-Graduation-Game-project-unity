using UI.Lcd;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 液晶の残像 (液晶の応答の遅れ) の「前のフレーム」と、 発光のにじみ (ブルーム) の下処理を用意する。
    /// フレームの頭 (カメラが描く前) の液晶 RT は、 まだ前のフレームの絵なので、 それを別の RT へ写して液晶の Quad の
    /// <c>_PrevTex</c> に渡す。 重ね方 (lighten) は Quad のシェーダ (Game2D/LcdScreen) が行う。
    ///
    /// <para><b>自動露出</b>: 画面が明るすぎる時 (ブルームで眩しい時) に、 液晶全体の明るさを自動で下げる。
    /// 前のフレームの RT にミップマップを作り、 最も小さい階層 (15×8) を非同期に読んで、 絵の平均の明るさ (効果を掛ける前の値) を出す。
    /// 目標より明るいほど <c>_Exposure</c> を 1 より下げ、 暗いと少しだけ上げる (上限・下限あり)。 下げるのは速く (attack)、 戻すのはゆっくり (release)。
    /// シェーダは <c>_Exposure</c> を、 最終の明るさと、 ブルームの強さ (2 乗) に掛ける。</para>
    ///
    /// <para>2026-11-02 の効果の絞り込みで、 ここにあった画素の格子の細かさ・ドットの芯の掛け具合は外した (git の履歴に残る)。
    /// ブルームと自動露出は同日に戻した。</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Game2DLcdGhost : MonoBehaviour
    {
        public LcdScreen lcd;
        public Renderer surface;

        [Header("ブルーム (なめらかなにじみ)")]
        [Tooltip("Hidden/Game2DBloom (ビルドに含めるため参照を持つ)")] public Shader bloomShader;
        [Range(0f, 4f), Tooltip("タイトル・職業選択での光のにじみの倍率 (マテリアルの _Bloom と _Halo に掛ける)。 航行は暗い地に明るい細い線なので、 にじみが強く出て光って見える。 タイトルは絵が中間の明るさなので、 同じ値だとほとんど光らない")]
        public float titleBloomScale = 2f;

        [Header("自動露出 (2026-11-17〜 止めた・AutoExposureOn = false の間は効かない)")]
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

        // ── ブルームの形 (2026-11-17・作者「昼の画面は周りも明るいのにブルームが強い・周りが明るい所は抑えたい」) ──
        //   <b>既定 = 形 2 (周りより明るい分だけ光る)・k 2.0・thr 0.08・広さ 1/16</b> (2026-11-17・作者の決定: 実機の撮り比べで、 昼の砂地のにじみが減り、 暗い場面は今のまま)。
        //   形 0・1 は見比べの口として残す (QaBattle.BloomMode / BloomSuppress / BloomRelative)。 既定の置き場はここの Default* (起動時もこの値に戻す)。
        /// <summary>0 = 旧い形 (絶対のしきい値 _BloomThr) / 1 = 周りが明るい所ほどブルームを抑える (抑え率 = 1 − smoothstep(<see cref="SupLo"/>, <see cref="SupHi"/>, 周り)) /
        /// 2 = 周りより明るい分だけ光る (しきい値 = <see cref="RelThr"/> + <see cref="RelK"/> × 周り・既定)。</summary>
        public static int BloomMode = DefaultBloomMode;
        /// <summary>周りの明るさを測る広さ: 0 = 1/4 (細かい・約 4 dot の塊をぼかす) / 1 = 1/8 / 2 = 1/16 (広い・約 16 dot の塊をぼかす・既定)。</summary>
        public static int SurLevel = DefaultSurLevel;
        /// <summary>形 1 のしきい値 (周りの輝度・液晶の RT の値・リニア): ここから下げ始め、 <see cref="SupHi"/> で消える。</summary>
        public static float SupLo = DefaultSupLo, SupHi = DefaultSupHi;
        /// <summary>形 2 の値: しきい値 = RelThr + RelK × 周り。</summary>
        public static float RelK = DefaultRelK, RelThr = DefaultRelThr;
        public const int DefaultBloomMode = 2, DefaultSurLevel = 2;
        public const float DefaultSupLo = 0.10f, DefaultSupHi = 0.35f, DefaultRelK = 2.0f, DefaultRelThr = 0.08f;

        /// <summary>自動露出を効かせるか (2026-11-17〜 false・作者の決定: ブルームを周りとの比較で出すので、 絵全体の明るさは変えない)。 false の間は _Exposure = 1 に固定し、
        /// 明るさを測る縮小 (GenerateMips) と読み戻し (AsyncGPUReadback) もしない。 仕組み (Meter) は戻せるように残す。 _Exposure を読むのは液晶のシェーダ (Game2D/LcdScreen) だけ。</summary>
        public const bool AutoExposureOn = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBloomTrial() { BloomMode = DefaultBloomMode; SurLevel = DefaultSurLevel; SupLo = DefaultSupLo; SupHi = DefaultSupHi; RelK = DefaultRelK; RelThr = DefaultRelThr; }

        private RenderTexture _lumA, _lumTmpA, _lumB, _lumTmpB, _lumC, _lumTmpC;
        private static readonly int SurTexId = Shader.PropertyToID("_SurTex"), RelModeId = Shader.PropertyToID("_RelMode"), SupLoId = Shader.PropertyToID("_SupLo"),
            SupHiId = Shader.PropertyToID("_SupHi"), RelKId = Shader.PropertyToID("_RelK"), RelThrId = Shader.PropertyToID("_RelThr");
        private Material _bloomMat;
        private MaterialPropertyBlock _mpb;
        private static readonly int BloomTexId = Shader.PropertyToID("_BloomTex"), BloomTexBId = Shader.PropertyToID("_BloomTexB");
        private static readonly int ThrId = Shader.PropertyToID("_Thr"), PowId = Shader.PropertyToID("_Pow"), DirId = Shader.PropertyToID("_Dir");
        private static readonly int PrevId = Shader.PropertyToID("_PrevTex"), BloomId = Shader.PropertyToID("_Bloom"), HaloId = Shader.PropertyToID("_Halo");

        private void OnDestroy()
        {
            foreach (var rt in new[] { _prev, _bloomA, _bloomTmpA, _bloomB, _bloomTmpB, _lumA, _lumTmpA, _lumB, _lumTmpB, _lumC, _lumTmpC }) if (rt != null) { rt.Release(); Destroy(rt); }
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
            float softness = mat.GetFloat("_BloomSoft");      // ぼかしの広がり (マテリアルで調整する)
            _bloomMat.SetFloat(ThrId, mat.GetFloat("_BloomThr")); _bloomMat.SetFloat(PowId, mat.GetFloat("_BloomPow"));
            if (BloomMode == 1 || BloomMode == 2)
            {
                // 周りの明るさ: 輝度の 4×4 平均 (1/4) → ぼかす → 1/8 → ぼかす → 1/16 → ぼかす。 使う段は SurLevel。 段を足した分だけ下処理が増える (小さい絵の Blit 9 回)
                if (_lumA == null || _lumA.width != w || _lumA.height != h)
                {
                    foreach (var rt in new[] { _lumA, _lumTmpA, _lumB, _lumTmpB, _lumC, _lumTmpC }) if (rt != null) { rt.Release(); Destroy(rt); }
                    _lumA = NewSmall(w, h, "Game2DLumA"); _lumTmpA = NewSmall(w, h, "Game2DLumTmpA");
                    _lumB = NewSmall(Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), "Game2DLumB"); _lumTmpB = NewSmall(Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), "Game2DLumTmpB");
                    _lumC = NewSmall(Mathf.Max(1, w / 4), Mathf.Max(1, h / 4), "Game2DLumC"); _lumTmpC = NewSmall(Mathf.Max(1, w / 4), Mathf.Max(1, h / 4), "Game2DLumTmpC");
                }
                Graphics.Blit(_prev, _lumA, _bloomMat, 5);
                Blur(_lumA, _lumTmpA, softness);
                RenderTexture sur = _lumA;
                if (SurLevel >= 1) { Graphics.Blit(_lumA, _lumB, _bloomMat, 3); Blur(_lumB, _lumTmpB, softness); sur = _lumB; }
                if (SurLevel >= 2) { Graphics.Blit(_lumB, _lumC, _bloomMat, 3); Blur(_lumC, _lumTmpC, softness); sur = _lumC; }
                _bloomMat.SetTexture(SurTexId, sur); _bloomMat.SetFloat(RelModeId, BloomMode);
                _bloomMat.SetFloat(SupLoId, SupLo); _bloomMat.SetFloat(SupHiId, SupHi); _bloomMat.SetFloat(RelKId, RelK); _bloomMat.SetFloat(RelThrId, RelThr);
                Graphics.Blit(_prev, _bloomA, _bloomMat, 6);                               // 周りの明るさを効かせた取り出し・4×4 を 1 つに
            }
            else Graphics.Blit(_prev, _bloomA, _bloomMat, 0);                                   // 明るい所だけ・4×4 を 1 つに
            _bloomMat.SetVector(DirId, new Vector2(softness, 0)); Graphics.Blit(_bloomA, _bloomTmpA, _bloomMat, 1);
            _bloomMat.SetVector(DirId, new Vector2(0, softness)); Graphics.Blit(_bloomTmpA, _bloomA, _bloomMat, 1);
            Graphics.Blit(_bloomA, _bloomB, _bloomMat, 3);                                 // もう 1 段小さく (広いやわらかな光)
            _bloomMat.SetVector(DirId, new Vector2(softness, 0)); Graphics.Blit(_bloomB, _bloomTmpB, _bloomMat, 1);
            _bloomMat.SetVector(DirId, new Vector2(0, softness)); Graphics.Blit(_bloomTmpB, _bloomB, _bloomMat, 1);
        }

        /// <summary>横・縦のガウスぼかし (tmp を経て rt へ戻す)。</summary>
        private void Blur(RenderTexture rt, RenderTexture tmp, float softness)
        {
            _bloomMat.SetVector(DirId, new Vector2(softness, 0)); Graphics.Blit(rt, tmp, _bloomMat, 1);
            _bloomMat.SetVector(DirId, new Vector2(0, softness)); Graphics.Blit(tmp, rt, _bloomMat, 1);
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
            // GUI を液晶の中身として合成する時 (2026-11-16・Game2DGuiRt.InLcd): 残像・ブルーム・自動露出の元の絵にも GUI を重ねる (計器も液晶の明るさの一部)。
            //   ここは前のフレームの中身なので、 GUI の RT も前のフレームの物 (同じフレームどうし)。 重ね方は液晶のシェーダの GuiOver と同じ (Hidden/Game2DBloom の Pass 4)
            var gui = Game2DGuiRt.InLcd && Game2DGuiRt.Current != null ? Game2DGuiRt.Current.Texture : null;
            if (gui != null && bloomShader != null)
            {
                if (_bloomMat == null) _bloomMat = new Material(bloomShader) { hideFlags = HideFlags.HideAndDontSave };
                Graphics.Blit(gui, _prev, _bloomMat, 4);
            }
            BuildBloom();
            if (!AutoExposureOn) { MeasuredLuma = -1f; Exposure = 1f; }      // 自動露出は止めた (2026-11-17・どの形でも 1 に固定・測らない)
#pragma warning disable CS0162      // AutoExposureOn は定数 (戻す時のために残す)
            else if (autoExposure) Meter(); else { MeasuredLuma = -1f; Exposure = Mathf.MoveTowards(Exposure, 1f, Time.unscaledDeltaTime * 2f); }
#pragma warning restore CS0162
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            surface.GetPropertyBlock(_mpb);
            _mpb.SetTexture(PrevId, _prev);
            _mpb.SetFloat(ExposureId, Exposure);
            if (_bloomA != null) { _mpb.SetTexture(BloomTexId, _bloomA); _mpb.SetTexture(BloomTexBId, _bloomB); }
            // タイトル・職業選択は絵が中間の明るさなので、 光のにじみを強める
            var gm = GameLoop.GameManager.Instance;
            bool title = gm != null && gm.CurrentPhase == GameLoop.GameManager.GamePhase.Title;
            var lcdMat = surface.sharedMaterial;
            _mpb.SetFloat(BloomId, lcdMat.GetFloat(BloomId) * (title ? titleBloomScale : 1f));
            _mpb.SetFloat(HaloId, lcdMat.GetFloat(HaloId) * (title ? titleBloomScale : 1f));
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
