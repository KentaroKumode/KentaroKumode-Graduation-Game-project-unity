using UnityEngine;

namespace Battle.Visual
{
    /// <summary>
    /// カメラ全体をドット絵化する OnRenderImage ポストフィルタ。
    /// 単純な downscale → Point upscale により VFX を含む全描画をピクセル化する。
    ///
    /// 想定用途: VFX 試打シーン ([CombatVFXTester]) でアセット VFX のドット感を即座に確認。
    /// 本番では LCD パイプラインを通すが、 単体検証としてはこれだけで十分。
    ///
    /// 配置: 表示用カメラに 1 つアタッチするだけ。
    /// ホットキー P でトグル可能 (toggleKey で変更)。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class PixelPostFilter : MonoBehaviour
    {
        [Header("ピクセル化")]
        [Tooltip("ON で有効化。 OFF で素のカメラ出力を表示。")]
        public bool enablePixelize = true;
        [Tooltip("ターゲットの縦解像度 (ドット数)。 横はアスペクト比から自動算出。")]
        [Range(90, 1080)] public int pixelHeight = 270;
        [Tooltip("ダウンサンプル時のフィルタ。 Bilinear で柔らかく、 Point でハードに。")]
        public FilterMode downsampleFilter = FilterMode.Bilinear;

        [Header("カラー量子化 (任意)")]
        [Tooltip("ON で色を N 段階にスナップ (パレット風) する。")]
        public bool quantizeColor;
        [Range(2, 32)] public int colorLevels = 8;

        [Header("ホットキー")]
        public KeyCode toggleKey = KeyCode.P;
        public KeyCode quantizeToggleKey = KeyCode.O;

        private Material _quantizeMat;
        private Shader _quantizeShader;

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
                enablePixelize = !enablePixelize;
            if (quantizeToggleKey != KeyCode.None && Input.GetKeyDown(quantizeToggleKey))
                quantizeColor = !quantizeColor;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!enablePixelize)
            {
                Graphics.Blit(src, dst);
                return;
            }

            int srcH = src.height;
            int srcW = src.width;
            int targetH = Mathf.Clamp(pixelHeight, 16, srcH);
            int targetW = Mathf.Max(16, Mathf.RoundToInt(targetH * (float)srcW / srcH));

            var low = RenderTexture.GetTemporary(targetW, targetH, 0, src.format);
            low.filterMode = FilterMode.Point;
            low.wrapMode = TextureWrapMode.Clamp;

            // src を一度バイリニアで縮小 (柔らかさ) → low に書き込み (Point でサンプル)
            var prevSrcFilter = src.filterMode;
            src.filterMode = downsampleFilter;
            Graphics.Blit(src, low);
            src.filterMode = prevSrcFilter;

            if (quantizeColor && EnsureQuantizeMaterial())
            {
                _quantizeMat.SetFloat("_Levels", Mathf.Max(2, colorLevels));
                Graphics.Blit(low, dst, _quantizeMat);
            }
            else
            {
                // low → dst を Point で拡大 (元のドットがそのまま大きくなる)
                Graphics.Blit(low, dst);
            }

            RenderTexture.ReleaseTemporary(low);
        }

        private bool EnsureQuantizeMaterial()
        {
            if (_quantizeMat != null) return true;
            if (_quantizeShader == null) _quantizeShader = Shader.Find("Hidden/Battle/PixelQuantize");
            if (_quantizeShader == null) return false;
            _quantizeMat = new Material(_quantizeShader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private void OnDestroy()
        {
            if (_quantizeMat != null)
            {
                if (Application.isPlaying) Destroy(_quantizeMat);
                else DestroyImmediate(_quantizeMat);
                _quantizeMat = null;
            }
        }

        private void OnGUI()
        {
            if (!enablePixelize && !quantizeColor) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            var rect = new Rect(10, Screen.height - 24, 800, 22);
            string msg = $"Pixel: {(enablePixelize ? $"ON ({pixelHeight}px)" : "OFF")}  " +
                         $"Quantize: {(quantizeColor ? $"ON (Lv {colorLevels})" : "OFF")}  [P]/[O] toggle";
            GUI.Label(rect, msg, style);
        }
    }
}
